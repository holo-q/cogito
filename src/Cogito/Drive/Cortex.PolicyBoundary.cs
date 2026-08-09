namespace Cogito;

using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Cogito.Grammar;
using Cogito.Induct;
using Ronmamon;

public sealed partial class Cortex
{
    private const string PolicyBoundarySeedCustodyDirectory = "policy-boundary-seeds";
    private const string PolicyBoundarySeedCustodyFile = "seed-custody.ron";
    private readonly record struct RootFundingMemoKey(CortexPolicyID Policy, string RootDirectory, string SourceRunID, ulong QuotaID);
    private readonly Dictionary<RootFundingMemoKey, CortexPolicyTrialQuotaDecision> _rootFundingMemo = new();

    private readonly Dictionary<CortexPolicyID, IPolicyBoundaryDomain> _policyBoundaryDomains = new();

    private bool IsRegisteredPolicyBoundaryCanonicalStateValid(PolicyCanonicalStateID state)
        => _policyBoundaryDomains.TryGetValue(state.Policy, out IPolicyBoundaryDomain? domain)
            && domain.ValidateCanonicalState(in state);

    private static string EncodeCanonicalState(in PolicyCanonicalStateID state)
        => state.Version == 0 ? "" : string.Join(':', state.Policy.Value, (byte)state.Kind,
            state.Version, state.Value.ToString("X16", CultureInfo.InvariantCulture));

    /// Read an encoded canonical-state identity without judging it. An absent
    /// encoding is a valid absence and yields the default state.
    private static bool TryParseCanonicalState(string encoded, out PolicyCanonicalStateID state)
    {
        state = default;
        if (string.IsNullOrEmpty(encoded)) return true;
        string[] parts = encoded.Split(':');
        if (parts.Length != 4
            || !byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte kind)
            || !ushort.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort version)
            || !ulong.TryParse(parts[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong value)) return false;
        state = new PolicyCanonicalStateID(new CortexPolicyID(parts[0]), (PolicyCanonicalStateKinds)kind, version, value);
        return true;
    }

    /// Decode against the domain the caller already holds. The state must belong to
    /// that domain's policy — a state that parses but names another policy is not this
    /// domain's scope, and admitting it would let one policy validate another's custody.
    private static bool TryDecodeCanonicalState(string encoded, IPolicyBoundaryDomain domain, out PolicyCanonicalStateID state)
        => TryParseCanonicalState(encoded, out state)
            && (state.Equals(default)
                || state.Policy.Equals(domain.PolicyID) && domain.ValidateCanonicalState(in state));

    /// Decode when only the encoding is known: the state names its own policy, and the
    /// mounted registry supplies the domain that may judge it.
    private bool TryDecodeCanonicalState(string encoded, out PolicyCanonicalStateID state)
        => TryParseCanonicalState(encoded, out state)
            && (state.Equals(default) || IsRegisteredPolicyBoundaryCanonicalStateValid(state));

    private enum PolicyBoundaryContinuationModes : byte
    {
        RestoreNotAttempted,
        PreserveGuardDenied,
        PreserveHistorical,
    }

    /// Authenticates the loaded child epoch before the runtime-bind callback can
    /// decide whether to restore it.  A rung continuation carries its terminal
    /// receipt forward; only an untouched NotAttempted epoch is eligible for
    /// the idempotent Restore call.
    private PolicyBoundaryContinuationModes AuthenticatePolicyBoundaryContinuationForDomain(
        in PolicyBoundarySeedCustody custody,
        IPolicyBoundaryDomain domain,
        CortexPolicySelectionCauses expectedCause,
        in PolicyCanonicalStateID canonicalState)
    {
        CortexPolicyQuotaDecisionID fundingID = new(custody.fundingID);
        string computedAuditOnlyDigest = custody.ComputeDigest();
        if (!IsAuthenticatedAuditOnlyDigest(custody.custodyDigest)
            || !string.Equals(custody.custodyDigest, computedAuditOnlyDigest, StringComparison.Ordinal))
        {
            Trace.Cortex.Boundary("policy.boundary.continuation-reject",
                $"mode=custody field=custody_digest expected_epoch=source-funding actual_epoch=passed-custody expected={computedAuditOnlyDigest} actual={custody.custodyDigest} funding={fundingID} source_funding_state={custody.canonicalState} successor_execution_state=unreached");
            throw new InvalidDataException($"policy boundary continuation custody digest mismatch expected={computedAuditOnlyDigest} actual={custody.custodyDigest} source_epoch=source-funding passed_epoch=passed-custody");
        }
        string encodedCanonicalState = EncodeCanonicalState(canonicalState);
        if (!IsRegisteredPolicyBoundaryCanonicalStateValid(canonicalState)
            || !string.Equals(encodedCanonicalState, custody.canonicalState, StringComparison.Ordinal))
        {
            Trace.Cortex.Boundary("policy.boundary.continuation-reject",
                $"mode=source-state field=source_state expected_epoch=source-funding actual_epoch=passed-custody expected={custody.canonicalState} actual={encodedCanonicalState} funding={fundingID} source_funding_state={custody.canonicalState} successor_execution_state=unreached");
            throw new InvalidDataException($"policy boundary continuation source-state mismatch expected={custody.canonicalState} actual={encodedCanonicalState} source_epoch=source-funding passed_epoch=passed-custody");
        }
        PolicyState state = GetPolicy(domain.PolicyID);
        if (!TryReadPolicyTrialExecutionReceiptForQuota(domain.PolicyID, fundingID,
                out CortexPolicyTrialExecutionOutcomes outcome,
                out long requestCount, out long guardAdmittedCount,
                out _, out _, out _, out CortexPolicyDecisionReadout executionReadout,
                out CortexPolicyDecisionID executionDecisionID, out ulong executionFingerprint,
                out int executionStep))
        {
            Trace.Cortex.Boundary("policy.boundary.continuation-reject",
                $"mode=receipt field=execution_receipt expected_epoch=source-funding actual_epoch=successor-execution expected=funding-bound authenticated receipt actual=missing funding={fundingID} source_funding_candidate={custody.candidateFingerprint:X16} source_funding_support={custody.sourceSupportDigest:X16} source_funding_revision={custody.candidateRevision} source_funding_state={custody.canonicalState} successor_execution_candidate=unknown successor_execution_support=unknown successor_execution_revision=unknown successor_execution_state=unknown readout={custody.readoutFingerprint:X16} cause={expectedCause}");
            throw new InvalidDataException("policy boundary continuation has no authenticated funding-bound execution receipt");
        }

        if (state.HistoricalTrialExecution.IsPresent)
        {
            if (!TryAuthenticatePolicyTrialQuotaIdentity(state, fundingID, custody.custodyDigest))
            {
                Trace.Cortex.Boundary("policy.boundary.continuation-reject",
                    $"mode=historical funding={fundingID} field=custody_digest expected_epoch=source-funding actual_epoch=successor-execution expected={custody.custodyDigest} actual=unproven source_funding_candidate={custody.candidateFingerprint:X16} source_funding_support={custody.sourceSupportDigest:X16} source_funding_revision={custody.candidateRevision} source_funding_state={custody.canonicalState} successor_execution_candidate=unknown successor_execution_support=unknown successor_execution_revision=unknown successor_execution_state=unknown readout={custody.readoutFingerprint:X16} cause={expectedCause}");
                throw new InvalidDataException($"policy boundary continuation history has no exact durable funding authority funding={fundingID} expected={custody.custodyDigest} actual=unproven source_epoch=source-funding successor_epoch=successor-execution");
            }
            if (!TryReadPolicyTrialExecutionScopeForQuota(domain.PolicyID, fundingID,
                    out PolicyVerifiedScopeEntry executionScope))
            {
                Trace.Cortex.Boundary("policy.boundary.continuation-reject",
                    $"mode=historical funding={fundingID} field=scope expected_epoch=successor-execution actual=missing source_funding_candidate={custody.candidateFingerprint:X16} source_funding_support={custody.sourceSupportDigest:X16} source_funding_revision={custody.candidateRevision} source_funding_state={custody.canonicalState} successor_execution_candidate=unknown successor_execution_support=unknown successor_execution_revision=unknown successor_execution_state=unknown readout={custody.readoutFingerprint:X16} cause={expectedCause}");
                throw new InvalidDataException($"policy boundary continuation history has no durable verified scope funding={fundingID} expected=successor-execution scope actual=missing source_epoch=source-funding successor_epoch=successor-execution");
            }
            PolicyTrialExecutionHistory history = state.HistoricalTrialExecution;
            string? mismatch = !history.QuotaDecisionID.Equals(fundingID) ? "funding_id"
                : history.Cause != expectedCause ? "cause"
                : history.Outcome != CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted ? "outcome"
                : state.SuppressTrialPackets ? "suppression_not_closed"
                : state.ActiveTrialQuotaID.Value != 0 ? "active_funding_not_closed"
                : state.PendingForcedTrialIntent.HasSeed ? "forced_intent_not_closed"
                : executionDecisionID.Value == 0 ? "execution_decision_id"
                : executionStep < 0 ? "execution_step"
                : executionFingerprint != custody.readoutFingerprint ? "execution_readout"
                : executionReadout.SelectionCause != expectedCause ? "execution_cause"
                : expectedCause == CortexPolicySelectionCauses.Launchpad
                    && executionReadout.ReadoutCandidateFingerprint != 0 ? "launchpad_candidate"
                : expectedCause == CortexPolicySelectionCauses.Launchpad
                    && executionReadout.ReadoutCandidateOccurrenceDigest != 0 ? "launchpad_support"
                : executionScope.ReadoutFingerprint != custody.readoutFingerprint ? "scope_readout"
                : null;
            if (mismatch is not null)
            {
                (string expected, string actual) = mismatch switch
                {
                    "funding_id" => (fundingID.ToString(), history.QuotaDecisionID.ToString()),
                    "cause" => (expectedCause.ToString(), history.Cause.ToString()),
                    "outcome" => (CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted.ToString(), history.Outcome.ToString()),
                    "suppression_not_closed" => ("0", state.SuppressTrialPackets ? "1" : "0"),
                    "active_funding_not_closed" => ("0", state.ActiveTrialQuotaID.Value.ToString(CultureInfo.InvariantCulture)),
                    "forced_intent_not_closed" => ("0", state.PendingForcedTrialIntent.HasSeed ? "1" : "0"),
                    "execution_decision_id" => ("nonzero", executionDecisionID.Value.ToString(CultureInfo.InvariantCulture)),
                    "execution_step" => (">=0", executionStep.ToString(CultureInfo.InvariantCulture)),
                    "execution_readout" => (custody.readoutFingerprint.ToString("X16", CultureInfo.InvariantCulture), executionFingerprint.ToString("X16", CultureInfo.InvariantCulture)),
                    "execution_cause" => (expectedCause.ToString(), executionReadout.SelectionCause.ToString()),
                    "launchpad_candidate" => ("0", executionReadout.ReadoutCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture)),
                    "launchpad_support" => ("0", executionReadout.ReadoutCandidateOccurrenceDigest.ToString("X16", CultureInfo.InvariantCulture)),
                    "scope_readout" => ("source-funding readout " + custody.readoutFingerprint.ToString("X16", CultureInfo.InvariantCulture), "successor-execution readout " + executionScope.ReadoutFingerprint.ToString("X16", CultureInfo.InvariantCulture)),
                    _ => ("expected", "actual"),
                };
                Trace.Cortex.Boundary("policy.boundary.continuation-reject",
                    $"mode=historical funding={fundingID} field={mismatch} expected_epoch=source-funding actual_epoch=successor-execution expected={expected} actual={actual} source_funding_candidate={custody.candidateFingerprint:X16} source_funding_support={custody.sourceSupportDigest:X16} source_funding_revision={custody.candidateRevision} source_funding_state={custody.canonicalState} successor_execution_candidate={executionScope.CandidateFingerprint:X16} successor_execution_support={executionScope.OccurrenceDigest:X16} successor_execution_revision={executionScope.Revision.Value} successor_execution_state={EncodeCanonicalState(executionScope.State)} readout={executionFingerprint:X16} cause={history.Cause} outcome={history.Outcome}");
                throw new InvalidDataException($"policy boundary continuation history disagrees with paid custody field={mismatch} expected={expected} actual={actual} source_epoch=source-funding successor_epoch=successor-execution");
            }
            Trace.Cortex.Boundary("policy.boundary.continuation",
                $"mode=historical funding={fundingID} source_funding_candidate={custody.candidateFingerprint:X16} source_funding_support={custody.sourceSupportDigest:X16} source_funding_revision={custody.candidateRevision} source_funding_state={custody.canonicalState} successor_execution_candidate={executionScope.CandidateFingerprint:X16} successor_execution_support={executionScope.OccurrenceDigest:X16} successor_execution_revision={executionScope.Revision.Value} successor_execution_state={EncodeCanonicalState(executionScope.State)} readout={executionFingerprint:X16} cause={history.Cause} outcome={history.Outcome}");
            return PolicyBoundaryContinuationModes.PreserveHistorical;
        }

        bool authenticatedEpoch = TryAuthenticatePaidTrialEpoch(state, domain.PolicyID,
            fundingID, custody.custodyDigest, expectedCause, in canonicalState,
            custody.readoutFingerprint, custody.candidateFingerprint,
            custody.sourceSupportDigest, new GrammarRevisionID(custody.candidateRevision),
            out PolicyVerifiedScopeEntry authenticatedScope);
        if (!authenticatedEpoch || !authenticatedScope.IsValid)
            throw new InvalidDataException("policy boundary continuation has no exact authenticated active source epoch");

        if (outcome == CortexPolicyTrialExecutionOutcomes.NotAttempted
            && requestCount == 0 && guardAdmittedCount == 0)
            return PolicyBoundaryContinuationModes.RestoreNotAttempted;
        if (outcome == CortexPolicyTrialExecutionOutcomes.GuardDenied
            && requestCount > 0 && guardAdmittedCount == 0)
            return PolicyBoundaryContinuationModes.PreserveGuardDenied;
        throw new InvalidDataException($"policy boundary continuation has mismatched execution state outcome={outcome} requests={requestCount} admitted={guardAdmittedCount}");
    }

    private PolicyBoundaryContinuationModes AuthenticatePolicyBoundaryContinuation(
        in PolicyBoundarySeedCustody custody,
        CortexPolicySelectionCauses expectedCause,
        in PolicyCanonicalStateID canonicalState)
        => AuthenticatePolicyBoundaryContinuationForDomain(custody, HomeostatPolicyBoundaryDomain.Instance,
            expectedCause, in canonicalState);

    [RonObject]
    internal partial class PolicyBoundarySeedCustody
    {
        public int schemaVersion = 5;
        public ulong fundingID;
        // The parent lease is the source of authority for a fork.  Child-local
        // trial accounting intentionally does not carry this row, so custody
        // must retain the parent's paid/reused verdict as a receipt.
        public CortexPolicyQuotaDecisions sourceFundingDecision;
        public string sourceRunID = "";
        public ulong sourceDecisionID;
        public long sourceDecisionEventID = -1;
        public long sourceCorroborationEventID = -1;
        public CortexPolicySelectionCauses sourceSelectionCause;
        public ulong sourceSupportDigest;
        // Funding binds the canonical policy fingerprint; the source packet carries
        // the raw candidate fingerprint. Keep both identities in custody.
        public ulong sourceCandidateFingerprint;
        public string policy = "";
        public ulong candidateFingerprint;
        public ulong readoutFingerprint;
        public int fundingStep;
        public int nextStep;
        public ulong candidateRevision;
        public string canonicalState = "";
        // v6 closes the transaction's semantic boundary, not merely its seed bytes.
        // These fields are populated only after the lease is actually Paid.
        public string boundary = "";
        public PolicyBoundaryComparisons comparison;
        public string provenance = "";
        public int sourceStep = -1;
        public string obligation = "";
        // The learner's exact quality receipt is part of the paid authority tuple.
        // Reconstructing 0/0/0 on resume would turn authenticated evidence into a
        // false non-exact readout and silently discard a valid child generation.
        public int readoutCachedContexts;
        public int readoutComparisons;
        public int readoutAgreements;
        public int readoutMisses;
        // Domain-owned proposal custody lets a forced child execute the exact
        // captured repository candidate instead of synthesizing an action from a
        // divergence seed.
        public string domainCandidateCanonical = "";
        public ulong domainCandidateDigest;
        public ulong domainFrontierRevision;
        public string domainFrontierAuthoritySHA256 = "";
        public string coldSeedDigest = "";
        public string checkpointSHA256 = "";
        public string tapeSpanlogSHA256 = "";
        public string curveSHA256 = "";
        public string excursionsSHA256 = "";
        public string custodyDigest = "";

        public byte[] Encode()
        {
            PolicyBoundarySeedCustody document = this;
            byte[] first = RonSerializer.SerializeToUtf8(in document);
            PolicyBoundarySeedCustody restored = RonSerializer.Deserialize<PolicyBoundarySeedCustody>(first);
            byte[] second = RonSerializer.SerializeToUtf8(in restored);
            if (!first.AsSpan().SequenceEqual(second))
                throw new InvalidDataException("policy-boundary seed custody SaveLoadSave drifted");
            return first;
        }

        public string ComputeDigest()
        {
            string canonical = schemaVersion >= 8
                ? string.Join('|', schemaVersion, fundingID, (byte)sourceFundingDecision, sourceRunID, sourceDecisionID, sourceDecisionEventID, sourceCorroborationEventID,
                    (byte)sourceSelectionCause,
                    sourceSupportDigest.ToString("X16", CultureInfo.InvariantCulture), sourceCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture),
                    policy, candidateFingerprint.ToString("X16", CultureInfo.InvariantCulture), fundingStep,
                    readoutFingerprint.ToString("X16", CultureInfo.InvariantCulture), nextStep, candidateRevision, canonicalState,
                    boundary, (byte)comparison, provenance, sourceStep, obligation,
                    readoutCachedContexts, readoutComparisons, readoutAgreements, readoutMisses,
                    domainCandidateCanonical, domainCandidateDigest, domainFrontierRevision, domainFrontierAuthoritySHA256,
                    coldSeedDigest,
                    checkpointSHA256, tapeSpanlogSHA256, curveSHA256, excursionsSHA256, "policy-boundary-seed-custody-v8")
                : schemaVersion >= 7
                ? string.Join('|', schemaVersion, fundingID, (byte)sourceFundingDecision, sourceRunID, sourceDecisionID, sourceDecisionEventID, sourceCorroborationEventID,
                    (byte)sourceSelectionCause,
                    sourceSupportDigest.ToString("X16", CultureInfo.InvariantCulture), sourceCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture),
                    policy, candidateFingerprint.ToString("X16", CultureInfo.InvariantCulture), fundingStep,
                    readoutFingerprint.ToString("X16", CultureInfo.InvariantCulture), nextStep, candidateRevision, canonicalState,
                    boundary, (byte)comparison, provenance, sourceStep, obligation,
                    readoutCachedContexts, readoutComparisons, readoutAgreements, readoutMisses,
                    coldSeedDigest,
                    checkpointSHA256, tapeSpanlogSHA256, curveSHA256, excursionsSHA256, "policy-boundary-seed-custody-v7")
                : schemaVersion >= 6
                ? string.Join('|', schemaVersion, fundingID, (byte)sourceFundingDecision, sourceRunID, sourceDecisionID, sourceDecisionEventID, sourceCorroborationEventID,
                    (byte)sourceSelectionCause,
                    sourceSupportDigest.ToString("X16", CultureInfo.InvariantCulture), sourceCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture),
                    policy, candidateFingerprint.ToString("X16", CultureInfo.InvariantCulture), fundingStep,
                    readoutFingerprint.ToString("X16", CultureInfo.InvariantCulture), nextStep, candidateRevision, canonicalState,
                    boundary, (byte)comparison, provenance, sourceStep, obligation, coldSeedDigest,
                    checkpointSHA256, tapeSpanlogSHA256, curveSHA256, excursionsSHA256, "policy-boundary-seed-custody-v6")
                : schemaVersion >= 5
                ? string.Join('|', schemaVersion, fundingID, (byte)sourceFundingDecision, sourceRunID, sourceDecisionID, sourceDecisionEventID, sourceCorroborationEventID,
                    (byte)sourceSelectionCause,
                    sourceSupportDigest.ToString("X16", CultureInfo.InvariantCulture), sourceCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture),
                    policy, candidateFingerprint.ToString("X16", CultureInfo.InvariantCulture), fundingStep,
                    readoutFingerprint.ToString("X16", CultureInfo.InvariantCulture), nextStep, candidateRevision, coldSeedDigest,
                    checkpointSHA256, tapeSpanlogSHA256, curveSHA256, excursionsSHA256, canonicalState, "policy-boundary-seed-custody-v5")
                : schemaVersion >= 4
                ? string.Join('|', schemaVersion, fundingID, sourceRunID, sourceDecisionID, sourceDecisionEventID, sourceCorroborationEventID,
                    (byte)sourceSelectionCause,
                    sourceSupportDigest.ToString("X16", CultureInfo.InvariantCulture), sourceCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture),
                    policy, candidateFingerprint.ToString("X16", CultureInfo.InvariantCulture), fundingStep,
                    readoutFingerprint.ToString("X16", CultureInfo.InvariantCulture), nextStep, candidateRevision, coldSeedDigest,
                    checkpointSHA256, tapeSpanlogSHA256, curveSHA256, excursionsSHA256, canonicalState, "policy-boundary-seed-custody-v4")
                : schemaVersion >= 3
                ? string.Join('|', schemaVersion, fundingID, sourceRunID, sourceDecisionID, sourceDecisionEventID,
                    sourceSupportDigest.ToString("X16", CultureInfo.InvariantCulture), sourceCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture),
                    policy, candidateFingerprint.ToString("X16", CultureInfo.InvariantCulture), fundingStep,
                    nextStep, candidateRevision, coldSeedDigest, checkpointSHA256, tapeSpanlogSHA256, curveSHA256,
                    excursionsSHA256, "policy-boundary-seed-custody-v2")
                : string.Join('|', schemaVersion, fundingID, sourceRunID, sourceDecisionID, sourceDecisionEventID,
                    sourceSupportDigest.ToString("X16", CultureInfo.InvariantCulture), sourceCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture),
                    policy, candidateFingerprint.ToString("X16", CultureInfo.InvariantCulture), fundingStep,
                    nextStep, candidateRevision, coldSeedDigest, checkpointSHA256, tapeSpanlogSHA256, curveSHA256,
                    excursionsSHA256, "policy-boundary-seed-custody-v1");
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        }

        public void Validate(in CortexPolicyTrialQuotaDecision funding, in CortexForkSeed seed, string expectedSourceRunID, IPolicyBoundaryDomain domain)
        {
            ArgumentNullException.ThrowIfNull(domain);
            if (schemaVersion is not (4 or 5 or 6 or 7 or 8) || fundingID != funding.QuotaDecisionID.Value
                || !domain.PolicyID.Equals(funding.Policy) || sourceRunID != expectedSourceRunID || policy != funding.Policy.Value
                || schemaVersion >= 5 && sourceFundingDecision is not (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused)
                || candidateFingerprint != funding.CandidateFingerprint || fundingStep != funding.QuotaStep
                || nextStep != seed.NextStep || candidateRevision != funding.CandidateRevision.Value
                || domain.CanonicalScopeMode != PolicyCanonicalScopeModes.None && (!TryDecodeCanonicalState(canonicalState, domain, out PolicyCanonicalStateID custodyState)
                    || !domain.ValidateCanonicalState(in custodyState))
                || funding.HasCanonicalState && canonicalState != EncodeCanonicalState(funding.CanonicalState)
                || sourceCorroborationEventID <= 0 || !Enum.IsDefined(sourceSelectionCause)
                || schemaVersion >= 4 && readoutFingerprint != funding.ReadoutFingerprint
                || schemaVersion >= 6 && (!PolicyBoundaryRational.TryParse(boundary, out _)
                    || !Enum.IsDefined(comparison) || comparison == PolicyBoundaryComparisons.Unknown
                    || string.IsNullOrWhiteSpace(provenance) || sourceStep < 0 || string.IsNullOrWhiteSpace(obligation))
                || schemaVersion >= 7 && (readoutComparisons <= 0 || readoutAgreements != readoutComparisons
                    || readoutMisses != 0 || readoutCachedContexts < 0)
                || !domain.ValidateCandidateTransport(domainCandidateCanonical, domainCandidateDigest,
                    domainFrontierRevision, domainFrontierAuthoritySHA256)
                || coldSeedDigest != seed.ColdSeedDigest || checkpointSHA256 != seed.Digests.CheckpointSHA256
                || tapeSpanlogSHA256 != seed.Digests.TapeSpanlogSHA256 || curveSHA256 != seed.Digests.CurveSHA256
                || excursionsSHA256 != seed.Digests.ExcursionsSHA256 || custodyDigest != ComputeDigest())
                throw new InvalidDataException("policy-boundary seed custody does not bind its paid lease");
        }
    }

    [RonObject]
    internal partial class PolicyBoundarySettlementCustody
    {
        public int schemaVersion = 1;
        public ulong fundingID;
        public string sourceRunID = "";
        public int sourceNextStep;
        public string coldSeedDigest = "";
        public string seedAuditOnlyDigest = "";
        public string generationDigest = "";
        public long actualExecutedArmSteps;
        public string settlementDigest = "";

        public string ComputeDigest()
            => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', schemaVersion,
                fundingID, sourceRunID, sourceNextStep, coldSeedDigest, seedAuditOnlyDigest, generationDigest,
                actualExecutedArmSteps, "policy-boundary-settlement-custody-v1"))));

        public byte[] Encode()
        {
            PolicyBoundarySettlementCustody document = this;
            byte[] first = RonSerializer.SerializeToUtf8(in document);
            PolicyBoundarySettlementCustody restored = RonSerializer.Deserialize<PolicyBoundarySettlementCustody>(first);
            byte[] second = RonSerializer.SerializeToUtf8(in restored);
            if (!first.AsSpan().SequenceEqual(second)) throw new InvalidDataException("policy-boundary settlement custody SaveLoadSave drifted");
            return first;
        }
    }

    private string PolicyBoundarySeedCustodyPath(in CortexPolicyTrialQuotaDecision funding)
        => Path.Combine(CurrentRun.Dir, PolicyBoundarySeedCustodyDirectory, funding.QuotaDecisionID.ToString());

    internal string? PersistPolicyBoundarySeed(
        CortexPolicyQuotaDecisionID fundingID,
        CortexPolicyID policy,
        ulong candidateFingerprint,
        int fundingStep,
        GrammarRevisionID candidateRevision,
        CortexForkSeed seed,
        ulong sourceDecisionID,
        long sourceDecisionEventID,
        ulong sourceSupportDigest,
        ulong sourceCandidateFingerprint,
        ulong readoutFingerprint,
        CortexPolicyQuotaDecisions sourceFundingDecision,
        PolicyCanonicalStateID canonicalState = default)
    {
        int cachedContexts = 0;
        int comparisons = 0;
        int agreements = 0;
        int misses = 0;
        if (TryReadBoundarySourceCorroborationReceipt(sourceDecisionID, sourceDecisionEventID, candidateRevision,
                readoutFingerprint, sourceCandidateFingerprint, sourceSupportDigest,
                out CortexPolicyBoundarySourceCorroboration corroboration, out _, canonicalState,
                HomeostatPolicyBoundaryDomain.Instance))
        {
            cachedContexts = corroboration.CachedContexts;
            comparisons = corroboration.Comparisons;
            agreements = corroboration.Agreements;
            misses = corroboration.Misses;
        }
        return PersistPolicyBoundarySeedV6(fundingID, policy, candidateFingerprint, fundingStep, candidateRevision, seed,
            sourceDecisionID, sourceDecisionEventID, sourceSupportDigest, sourceCandidateFingerprint, readoutFingerprint,
            sourceFundingDecision, canonicalState, PolicyBoundaryRational.Zero, PolicyBoundaryComparisons.LessThanOrEqual,
            "legacy-policy-boundary-seed", fundingStep, "", cachedContexts, comparisons, agreements, misses);
    }

    internal string? PersistPolicyBoundarySeedForDomain(
        CortexPolicyQuotaDecisionID fundingID,
        CortexPolicyID policy,
        ulong candidateFingerprint,
        int fundingStep,
        GrammarRevisionID candidateRevision,
        CortexForkSeed seed,
        ulong sourceDecisionID,
        long sourceDecisionEventID,
        ulong sourceSupportDigest,
        ulong sourceCandidateFingerprint,
        ulong readoutFingerprint,
        CortexPolicyQuotaDecisions sourceFundingDecision,
        PolicyCanonicalStateID canonicalState,
        PolicyBoundaryRational boundary,
        PolicyBoundaryComparisons comparison,
        string provenance,
        int sourceStep,
        string obligation,
        string domainCandidateCanonical = "",
        ulong domainCandidateDigest = 0,
        ulong domainFrontierRevision = 0,
        string domainFrontierAuthoritySHA256 = "")
    {
        if (!TryReadBoundarySourceCorroborationReceipt(sourceDecisionID, sourceDecisionEventID, candidateRevision,
                readoutFingerprint, sourceCandidateFingerprint, sourceSupportDigest,
                out CortexPolicyBoundarySourceCorroboration corroboration, out _, canonicalState,
                RequirePolicyBoundaryDomain(policy)))
            return null;
        return PersistPolicyBoundarySeedV6(fundingID, policy, candidateFingerprint, fundingStep, candidateRevision, seed,
            sourceDecisionID, sourceDecisionEventID, sourceSupportDigest, sourceCandidateFingerprint, readoutFingerprint,
            sourceFundingDecision, canonicalState, boundary, comparison, provenance, sourceStep, obligation,
            corroboration.CachedContexts, corroboration.Comparisons, corroboration.Agreements, corroboration.Misses,
            domainCandidateCanonical, domainCandidateDigest, domainFrontierRevision, domainFrontierAuthoritySHA256);
    }

    private string? PersistPolicyBoundarySeedV6(
        CortexPolicyQuotaDecisionID fundingID,
        CortexPolicyID policy,
        ulong candidateFingerprint,
        int fundingStep,
        GrammarRevisionID candidateRevision,
        CortexForkSeed seed,
        ulong sourceDecisionID,
        long sourceDecisionEventID,
        ulong sourceSupportDigest,
        ulong sourceCandidateFingerprint,
        ulong readoutFingerprint,
        CortexPolicyQuotaDecisions sourceFundingDecision,
        PolicyCanonicalStateID canonicalState,
        PolicyBoundaryRational boundary,
        PolicyBoundaryComparisons comparison,
        string provenance,
        int sourceStep,
        string obligation,
        int readoutCachedContexts,
        int readoutComparisons,
        int readoutAgreements,
        int readoutMisses,
        string domainCandidateCanonical = "",
        ulong domainCandidateDigest = 0,
        ulong domainFrontierRevision = 0,
        string domainFrontierAuthoritySHA256 = "")
    {
        IPolicyBoundaryDomain domain = RequirePolicyBoundaryDomain(policy);
        if (!policy.Equals(domain.PolicyID) || sourceDecisionID == 0 || sourceDecisionEventID <= 0
            || sourceSupportDigest == 0 || sourceCandidateFingerprint == 0 || candidateFingerprint == 0 || candidateRevision == GrammarRevisionID.Zero
            || !TryReadBoundarySourceCorroboration(sourceDecisionID, sourceDecisionEventID, candidateRevision,
                readoutFingerprint, sourceCandidateFingerprint, sourceSupportDigest, out TapeEventID sourceCorroborationEventID,
                out CortexPolicySelectionCauses sourceSelectionCause, canonicalState,
                RequirePolicyBoundaryDomain(policy)))
            return null;
        if (!TryReadBoundarySourceCorroborationReceipt(sourceDecisionID, sourceDecisionEventID, candidateRevision,
                readoutFingerprint, sourceCandidateFingerprint, sourceSupportDigest,
                out CortexPolicyBoundarySourceCorroboration sourceCorroboration, out _, canonicalState,
                RequirePolicyBoundaryDomain(policy))
            || sourceCorroboration.CachedContexts != readoutCachedContexts
            || sourceCorroboration.Comparisons != readoutComparisons
            || sourceCorroboration.Agreements != readoutAgreements
            || sourceCorroboration.Misses != readoutMisses)
            return null;
        if (!domain.ValidateCandidateTransport(domainCandidateCanonical, domainCandidateDigest,
                domainFrontierRevision, domainFrontierAuthoritySHA256))
            return null;
        if (sourceFundingDecision is not (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused))
            return null;
        string directory = Path.Combine(CurrentRun.Dir, PolicyBoundarySeedCustodyDirectory, fundingID.ToString());
        string custodyPath = Path.Combine(directory, PolicyBoundarySeedCustodyFile);
        if (File.Exists(custodyPath))
        {
            if (!TryReadPolicyBoundarySeedCustodyDocument(CurrentRun.Dir, Path.GetFileName(CurrentRun.Dir),
                    fundingID.ToString(), out PolicyBoundarySeedCustody existingCustody)
                || !string.IsNullOrWhiteSpace(obligation) && existingCustody.schemaVersion < 6
                || existingCustody.sourceDecisionID != sourceDecisionID
                || existingCustody.sourceDecisionEventID != sourceDecisionEventID
                || existingCustody.sourceCorroborationEventID != sourceCorroborationEventID.Value
                || existingCustody.sourceSelectionCause != sourceSelectionCause
                || existingCustody.sourceSupportDigest != sourceSupportDigest
                || existingCustody.sourceCandidateFingerprint != sourceCandidateFingerprint
                || existingCustody.readoutFingerprint != readoutFingerprint
                || existingCustody.schemaVersion >= 5 && existingCustody.sourceFundingDecision != sourceFundingDecision
                || existingCustody.schemaVersion >= 6 && (existingCustody.boundary != boundary.ToString()
                    || existingCustody.comparison != comparison || existingCustody.provenance != provenance
                    || existingCustody.sourceStep != sourceStep || existingCustody.obligation != obligation)
                || existingCustody.schemaVersion >= 7 && (existingCustody.readoutCachedContexts != readoutCachedContexts
                    || existingCustody.readoutComparisons != readoutComparisons
                    || existingCustody.readoutAgreements != readoutAgreements
                    || existingCustody.readoutMisses != readoutMisses)
                || domainCandidateCanonical.Length != 0 && existingCustody.schemaVersion < 8
                || existingCustody.schemaVersion >= 8 && (existingCustody.domainCandidateCanonical != domainCandidateCanonical
                    || existingCustody.domainCandidateDigest != domainCandidateDigest
                    || existingCustody.domainFrontierRevision != domainFrontierRevision
                    || existingCustody.domainFrontierAuthoritySHA256 != domainFrontierAuthoritySHA256)
                || (canonicalState.Version != 0 && existingCustody.canonicalState != EncodeCanonicalState(canonicalState))
                || existingCustody.policy != policy.Value
                || existingCustody.candidateFingerprint != candidateFingerprint
                || existingCustody.candidateRevision != candidateRevision.Value)
                return null;
            CortexPolicyTrialQuotaDecision existingFunding = new(fundingID, policy, candidateFingerprint,
                fundingStep, 1, 1, 1, 1, CortexPolicyQuotaDecisions.Paid, 1, 0)
            { CandidateRevision = candidateRevision, SeedAuditOnlyDigest = existingCustody.custodyDigest, ReadoutFingerprint = readoutFingerprint };
            if (!TryLoadPolicyBoundarySeed(in existingFunding, out CortexForkSeed existing)) return null;
            return existing.ColdSeedDigest == seed.ColdSeedDigest
                ? existingCustody.custodyDigest : null;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
        seed.WriteRunDirectory(directory);
        PolicyBoundarySeedCustody custody = new()
        {
            schemaVersion = string.IsNullOrWhiteSpace(obligation) ? 5 : domainCandidateCanonical.Length == 0 ? 7 : 8,
            fundingID = fundingID.Value,
            sourceFundingDecision = sourceFundingDecision,
            sourceRunID = Path.GetFileName(CurrentRun.Dir),
            sourceDecisionID = sourceDecisionID,
            sourceDecisionEventID = sourceDecisionEventID,
            sourceCorroborationEventID = sourceCorroborationEventID.Value,
            sourceSelectionCause = sourceSelectionCause,
            sourceSupportDigest = sourceSupportDigest,
            sourceCandidateFingerprint = sourceCandidateFingerprint,
            policy = policy.Value,
            candidateFingerprint = candidateFingerprint,
            readoutFingerprint = readoutFingerprint,
            fundingStep = fundingStep,
            nextStep = seed.NextStep,
            candidateRevision = candidateRevision.Value,
            canonicalState = EncodeCanonicalState(canonicalState),
            boundary = boundary.ToString(),
            comparison = comparison,
            provenance = provenance,
            sourceStep = sourceStep,
            obligation = obligation,
            readoutCachedContexts = readoutCachedContexts,
            readoutComparisons = readoutComparisons,
            readoutAgreements = readoutAgreements,
            readoutMisses = readoutMisses,
            domainCandidateCanonical = domainCandidateCanonical,
            domainCandidateDigest = domainCandidateDigest,
            domainFrontierRevision = domainFrontierRevision,
            domainFrontierAuthoritySHA256 = domainFrontierAuthoritySHA256,
            coldSeedDigest = seed.ColdSeedDigest,
            checkpointSHA256 = seed.Digests.CheckpointSHA256,
            tapeSpanlogSHA256 = seed.Digests.TapeSpanlogSHA256,
            curveSHA256 = seed.Digests.CurveSHA256,
            excursionsSHA256 = seed.Digests.ExcursionsSHA256,
        };
        custody.custodyDigest = custody.ComputeDigest();
        global::Cogito.Run.Open(directory).WriteAtomic(PolicyBoundarySeedCustodyFile, stream => stream.Write(custody.Encode()));
        CortexPolicyTrialQuotaDecision preparedFunding = new(fundingID, policy, candidateFingerprint,
            fundingStep, 1, 1, 1, 1, CortexPolicyQuotaDecisions.Paid, 1, 0)
        { CandidateRevision = candidateRevision, SeedAuditOnlyDigest = custody.custodyDigest, ReadoutFingerprint = readoutFingerprint };
        return TryLoadPolicyBoundarySeed(in preparedFunding, out CortexForkSeed verified)
            && verified.ColdSeedDigest == seed.ColdSeedDigest ? custody.custodyDigest : null;
    }

    internal bool TryReadPolicyBoundarySeedAuditOnlyDigest(CortexPolicyQuotaDecisionID fundingID, out string digest)
    {
        digest = "";
        string parentDirectory = _runtimeRun?.Dir ?? "";
        if (parentDirectory.Length == 0) return false;
        if (!TryReadPolicyBoundarySeedCustodyDocument(parentDirectory, Path.GetFileName(parentDirectory), fundingID.ToString(), out PolicyBoundarySeedCustody custody)) return false;
        digest = custody.custodyDigest;
        return digest.Length == 64;
    }

    internal bool TryLoadPolicyBoundarySeed(in CortexPolicyTrialQuotaDecision funding, out CortexForkSeed seed)
    {
        seed = null!;
        IPolicyBoundaryDomain domain;
        try { domain = RequirePolicyBoundaryDomain(funding.Policy); }
        catch (InvalidDataException) { return false; }
        string directory = PolicyBoundarySeedCustodyPath(in funding);
        string custodyPath = Path.Combine(directory, PolicyBoundarySeedCustodyFile);
        if (!Directory.Exists(directory) || !File.Exists(custodyPath)) return false;
        try
        {
            byte[] bytes = File.ReadAllBytes(custodyPath);
            PolicyBoundarySeedCustody custody = RonSerializer.Deserialize<PolicyBoundarySeedCustody>(bytes);
            if (!bytes.AsSpan().SequenceEqual(custody.Encode())
                || !IsAuthenticatedAuditOnlyDigest(funding.SeedAuditOnlyDigest)
                || !string.Equals(funding.SeedAuditOnlyDigest, custody.custodyDigest, StringComparison.Ordinal)
                || funding.Policy.Equals(domain.PolicyID) && custody.schemaVersion < 5
                || custody.sourceDecisionID == 0 || custody.sourceDecisionEventID <= 0 || custody.sourceCorroborationEventID <= 0 || custody.sourceSupportDigest == 0
                || custody.sourceCandidateFingerprint == 0
                || !TryDecodeCanonicalState(custody.canonicalState, out PolicyCanonicalStateID custodyState)
                || domain.CanonicalScopeMode != PolicyCanonicalScopeModes.None && !domain.ValidateCanonicalState(in custodyState)
                || !TryReadBoundarySourceCorroboration(custody.sourceDecisionID, custody.sourceDecisionEventID,
                    new GrammarRevisionID(custody.candidateRevision), funding.ReadoutFingerprint,
                    custody.sourceCandidateFingerprint, custody.sourceSupportDigest,
                    out TapeEventID loadedCorroborationEventID, out CortexPolicySelectionCauses loadedCause, custodyState, domain)
                || loadedCorroborationEventID.Value != custody.sourceCorroborationEventID
                || loadedCause != custody.sourceSelectionCause
                || custody.schemaVersion >= 6 && (!PolicyBoundaryRational.TryParse(custody.boundary, out _)
                    || !Enum.IsDefined(custody.comparison) || custody.comparison == PolicyBoundaryComparisons.Unknown
                    || string.IsNullOrWhiteSpace(custody.provenance) || custody.sourceStep < 0
                    || string.IsNullOrWhiteSpace(custody.obligation)))
                return false;
            seed = CortexForkSeed.MaterializeRun(directory, checked(funding.QuotaStep + 1));
            custody.Validate(in funding, in seed, Path.GetFileName(CurrentRun.Dir), domain);
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            Trace.Cortex.Boundary("policy.boundary.seed-custody-rejected",
                $"id={funding.QuotaDecisionID} step={funding.QuotaStep} message={error.Message}");
            seed = null!;
            return false;
        }
    }

    private static bool TryReadPolicyBoundarySeedCustodyDocument(string parentDirectory, string parentRunID, string fundingID,
        out PolicyBoundarySeedCustody custody)
    {
        custody = null!;
        string path = Path.Combine(parentDirectory, PolicyBoundarySeedCustodyDirectory, fundingID, PolicyBoundarySeedCustodyFile);
        if (!string.Equals(Path.GetFileName(Path.GetFullPath(parentDirectory)), parentRunID, StringComparison.Ordinal)
            || !File.Exists(path)) return false;
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            custody = RonSerializer.Deserialize<PolicyBoundarySeedCustody>(bytes);
            return bytes.AsSpan().SequenceEqual(custody.Encode())
                && custody.custodyDigest == custody.ComputeDigest()
                && custody.sourceRunID == parentRunID
                && (custody.schemaVersion < 6 || (PolicyBoundaryRational.TryParse(custody.boundary, out _)
                    && Enum.IsDefined(custody.comparison) && custody.comparison != PolicyBoundaryComparisons.Unknown
                    && !string.IsNullOrWhiteSpace(custody.provenance) && custody.sourceStep >= 0
                    && !string.IsNullOrWhiteSpace(custody.obligation)));
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or FormatException)
        {
            custody = null!;
            return false;
        }
    }

    private void WritePolicyBoundarySettlementCustody(
        in CortexPolicyTrialQuotaDecision funding, string generationDigest, long actualExecutedArmSteps)
    {
        _ = RequirePolicyBoundaryDomain(funding.Policy);
        if (!TryReadPolicyBoundarySeedCustodyDocument(CurrentRun.Dir, Path.GetFileName(CurrentRun.Dir),
                funding.QuotaDecisionID.ToString(), out PolicyBoundarySeedCustody seedCustody))
            throw new InvalidDataException($"paid policy trial {funding.QuotaDecisionID} has no seed custody");
        if (!string.Equals(funding.SeedAuditOnlyDigest, seedCustody.custodyDigest, StringComparison.Ordinal))
            throw new InvalidDataException($"paid policy trial {funding.QuotaDecisionID} disagrees with its seed custody digest");
        PolicyBoundarySettlementCustody custody = new()
        {
            fundingID = funding.QuotaDecisionID.Value,
            sourceRunID = seedCustody.sourceRunID,
            sourceNextStep = seedCustody.nextStep,
            coldSeedDigest = seedCustody.coldSeedDigest,
            seedAuditOnlyDigest = seedCustody.custodyDigest,
            generationDigest = generationDigest,
            actualExecutedArmSteps = actualExecutedArmSteps,
        };
        custody.settlementDigest = custody.ComputeDigest();
        string directory = Path.Combine(CurrentRun.Dir, PolicyBoundarySeedCustodyDirectory, funding.QuotaDecisionID.ToString());
        global::Cogito.Run.Open(directory).WriteAtomic("settlement-custody.ron", stream => stream.Write(custody.Encode()));
    }

    private CortexPolicyTrialCompletion SettleAuthenticatedPolicyBoundary(
        in CortexPolicyTrialQuotaDecision funding,
        int[] horizons,
        out PolicyBoundaryForkReceipt receipt,
        bool requireReceipt = true)
    {
        if (!TryReadTerminalHomeostatBoundaryGeneration(funding, funding.ReadoutFingerprint, horizons,
                out long actualExecutedArmSteps, out string generationDigest, out receipt, requireReceipt))
            throw new InvalidDataException($"paid policy trial {funding.QuotaDecisionID} lacks an authenticated terminal generation");
        if (requireReceipt && !receipt.Verified)
            throw new InvalidDataException($"paid policy trial {funding.QuotaDecisionID} terminal generation is not verified");
        WritePolicyBoundarySettlementCustody(in funding, generationDigest, actualExecutedArmSteps);
        CortexPolicyTrialCompletion settlement = CompletePolicyTrial(in funding, actualExecutedArmSteps, null,
            CortexPolicyVerifierOutcomes.Passed, null);
        EnsurePolicyTrialCompletionDurable(in settlement);
        return settlement;
    }

    private bool TryReadPolicyBoundarySettlementCustody(in CortexPolicyTrialQuotaDecision funding,
        out PolicyBoundarySettlementCustody custody)
    {
        custody = null!;
        string path = Path.Combine(CurrentRun.Dir, PolicyBoundarySeedCustodyDirectory,
            funding.QuotaDecisionID.ToString(), "settlement-custody.ron");
        if (!File.Exists(path)) return false;
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            custody = RonSerializer.Deserialize<PolicyBoundarySettlementCustody>(bytes);
            return bytes.AsSpan().SequenceEqual(custody.Encode()) && custody.schemaVersion == 1
                && custody.settlementDigest == custody.ComputeDigest()
                && custody.fundingID == funding.QuotaDecisionID.Value
                && custody.sourceNextStep >= 0
                && TryReadPolicyBoundarySeedCustodyDocument(CurrentRun.Dir, Path.GetFileName(CurrentRun.Dir),
                    funding.QuotaDecisionID.ToString(), out PolicyBoundarySeedCustody seedCustody)
                && custody.sourceRunID == seedCustody.sourceRunID
                && custody.sourceNextStep == seedCustody.nextStep
                && custody.coldSeedDigest == seedCustody.coldSeedDigest
                && custody.seedAuditOnlyDigest == seedCustody.custodyDigest
                && custody.seedAuditOnlyDigest == funding.SeedAuditOnlyDigest;
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or FormatException)
        {
            custody = null!;
            return false;
        }
    }

    private void ValidatePolicyBoundarySeedCustody(in CortexPolicyTrialQuotaDecision funding, IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        string reason = "";
        PolicyBoundarySeedCustody custody = null!;
        PolicyCanonicalStateID custodyState = default;
        TapeEventID validatedCorroborationEventID = default;
        CortexPolicySelectionCauses validatedCause = default;
        if (funding.SeedAuditOnlyDigest.Length != 64) reason = "digest-length";
        else if (!TryReadPolicyBoundarySeedCustodyDocument(CurrentRun.Dir, Path.GetFileName(CurrentRun.Dir),
                funding.QuotaDecisionID.ToString(), out custody))
            reason = "document";
        else if (!string.Equals(funding.SeedAuditOnlyDigest, custody.custodyDigest, StringComparison.Ordinal)) reason = "digest";
        else if (custody.sourceDecisionID == 0 || custody.sourceDecisionEventID <= 0 || custody.sourceCorroborationEventID <= 0 || custody.sourceSupportDigest == 0) reason = "source-ids";
        else if (custody.sourceCandidateFingerprint == 0 || custody.candidateFingerprint == 0 || custody.candidateRevision == 0) reason = "candidate";
        else if (!TryDecodeCanonicalState(custody.canonicalState, out custodyState)) reason = "state";
        else if (!TryReadBoundarySourceCorroboration(custody.sourceDecisionID, custody.sourceDecisionEventID,
                new GrammarRevisionID(custody.candidateRevision), funding.ReadoutFingerprint,
                custody.sourceCandidateFingerprint, custody.sourceSupportDigest,
                out validatedCorroborationEventID, out validatedCause, custodyState, domain)) reason = "corroboration";
        else if (validatedCorroborationEventID.Value != custody.sourceCorroborationEventID) reason = "corroboration-id";
        else if (validatedCause != custody.sourceSelectionCause) reason = "cause";
        else if (custody.schemaVersion >= 6 && (!PolicyBoundaryRational.TryParse(custody.boundary, out _)
            || !Enum.IsDefined(custody.comparison) || custody.comparison == PolicyBoundaryComparisons.Unknown
            || string.IsNullOrWhiteSpace(custody.provenance) || custody.sourceStep < 0
            || string.IsNullOrWhiteSpace(custody.obligation)
            || !_policyBoundaryObligations.TryGetValue(domain.PolicyID, out PolicyBoundaryObligation? boundaryObligation)
            || !boundaryObligation.Identity.Policy.Equals(domain.PolicyID)
            || custody.obligation != boundaryObligation.ID.Value)) reason = "boundary";
        if (reason.Length != 0)
            throw new InvalidDataException($"paid Homeostat policy trial {funding.QuotaDecisionID} has missing or mismatched seed custody ({reason})");
    }

    private readonly Dictionary<CortexPolicyID, (PolicyBoundaryTrainingReceipt Training, PolicyBoundaryMountReceipt Mount)> _mountedPolicyBoundaryLineage = new();
    private HomeostatDestinationHandshakeReceipt? _destinationHandshakeReceipt;
    private HomeostatDestinationHandshakeReceipt? _destinationHandshakeOwnerReceipt;
    private string _destinationHandshakeMountedDigest = "";

    internal readonly record struct PolicyBoundaryAuthorityReceipt(
        string Policy,
        string Obligation,
        string Boundary,
        string ForkReceiptDigest,
        ulong DecisionReadoutFingerprint,
        ulong DecisionReadoutRevision,
        bool Verified,
        string ReceiptDigest,
        PolicyBoundaryForkReceipt ForkReceipt,
        string Feature,
        PolicyBoundaryComparisons Comparison,
        string ParentRunID,
        string SourceChildID,
        string ColdSeedDigest,
        string ConfigReceiptDigest,
        string CheckpointReceiptDigest,
        ulong DestinationDecisionReadoutFingerprint,
        ulong DestinationDecisionReadoutRevision,
        string TrainingParentRunID,
        string TrainingSourceChildID,
        string MountDestinationChildID,
        string TrainingContentDigest,
        string MountReceiptDigest)
    {
        internal string ComputeDigest()
        {
            PolicyBoundaryForkReceipt forkReceipt = ForkReceipt;
            string forkDigest = PolicyBoundaryObligation.ComputeReceiptDigest(in forkReceipt);
            string canonical = string.Join('|', Policy, Obligation, Boundary, ForkReceiptDigest,
                DecisionReadoutFingerprint.ToString("X16"), DecisionReadoutRevision, Verified ? 1 : 0,
                forkDigest, Feature, Comparison, ParentRunID, SourceChildID, ColdSeedDigest,
                ConfigReceiptDigest, CheckpointReceiptDigest, DestinationDecisionReadoutFingerprint.ToString("X16"),
                DestinationDecisionReadoutRevision, TrainingParentRunID, TrainingSourceChildID,
                MountDestinationChildID, TrainingContentDigest, MountReceiptDigest, "policy-boundary-authority-v1");
            return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        }
    }

    /// Read the restored policy-boundary authority without exposing mutable policy state. The receipt is the
    /// checkpoint-backed authority seam used to compare a cold evaluation child with its calibration sibling.
    internal bool TryReadPolicyBoundaryAuthority(CortexPolicyID policy, out PolicyBoundaryAuthorityReceipt authority)
    {
        if (!_policyBoundaryObligations.TryGetValue(policy, out PolicyBoundaryObligation? obligation)
            || obligation.Receipt is not PolicyBoundaryForkReceipt receipt
            || !receipt.Verified
            || obligation.Winner is not PolicyBoundaryCandidate winner
            || receipt.SourceDecisionReadoutFingerprint == 0
            || receipt.SourceDecisionReadoutRevision == 0)
        {
            authority = default;
            return false;
        }
        try { receipt.Validate(RequirePolicyBoundaryDomain(policy)); }
        catch (InvalidDataException)
        {
            authority = default;
            return false;
        }
        string digest = PolicyBoundaryObligation.ComputeReceiptDigest(in receipt);
        bool hasLineage = _mountedPolicyBoundaryLineage.TryGetValue(policy, out var lineage);
        if (hasLineage)
        {
            lineage.Training.Validate(RequirePolicyBoundaryDomain(policy));
            if (lineage.Training.SourceDecisionReadoutFingerprint != receipt.SourceDecisionReadoutFingerprint
                || lineage.Training.SourceDecisionReadoutRevision != receipt.SourceDecisionReadoutRevision
                || !string.Equals(lineage.Training.ForkReceiptDigest, digest, StringComparison.Ordinal))
            {
                authority = default;
                return false;
            }
            lineage.Mount.Validate(in lineage.Training, lineage.Training.ParentRunID,
                lineage.Mount.DestinationChildID, lineage.Training.ColdSeedDigest,
                RequirePolicyBoundaryDomain(policy));
            if (lineage.Mount.DestinationDecisionReadoutFingerprint == 0
                || lineage.Mount.DestinationDecisionReadoutRevision == 0)
            {
                authority = default;
                return false;
            }
        }
        PolicyBoundaryAuthorityReceipt source = new(
            policy.Value,
            obligation.ID.Value,
            receipt.CandidateBoundary.ToString(),
            digest,
            receipt.SourceDecisionReadoutFingerprint,
            receipt.SourceDecisionReadoutRevision,
            true,
            digest,
            receipt,
            obligation.Identity.Feature,
            winner.Comparison,
            hasLineage ? lineage.Training.ParentRunID : "",
            hasLineage ? lineage.Training.SourceChildID : "",
            hasLineage ? lineage.Training.ColdSeedDigest : "",
            hasLineage ? lineage.Training.ConfigReceiptDigest : "",
            hasLineage ? lineage.Training.CheckpointReceiptDigest : "",
            hasLineage ? lineage.Mount.DestinationDecisionReadoutFingerprint : 0,
            hasLineage ? lineage.Mount.DestinationDecisionReadoutRevision : 0,
            hasLineage ? lineage.Training.ParentRunID : "",
            hasLineage ? lineage.Training.SourceChildID : "",
            hasLineage ? lineage.Mount.DestinationChildID : "",
            hasLineage ? lineage.Training.ContentDigest : "",
            hasLineage ? lineage.Mount.ReceiptDigest : "");
        authority = source with
        {
            ReceiptDigest = source.ComputeDigest(),
        };
        return authority.Verified;
    }

    /// Bind the source authority to the actual calibration rail's seed-load receipt. The seed-load receipt is the
    /// owner of parent/child identity, cold seed, persisted config, and checkpoint bytes; callers cannot recombine
    /// those provenance fields with a policy receipt after this seam returns.
    internal bool TryReadPolicyBoundaryAuthority(
        CortexPolicyID policy,
        in CortexForkSeedLoadReceipt sourceSeedLoad,
        out PolicyBoundaryAuthorityReceipt authority)
    {
        if (!sourceSeedLoad.Bound || !sourceSeedLoad.Exact
            || sourceSeedLoad.Role != CortexForkRailRoles.Calibration
            || sourceSeedLoad.ExpectedCheckpointSHA256.Length != 64)
        {
            authority = default;
            return false;
        }
        if (!TryReadPolicyBoundaryAuthority(policy, out PolicyBoundaryAuthorityReceipt baseAuthority))
        {
            authority = default;
            return false;
        }
        authority = baseAuthority with
        {
            ParentRunID = sourceSeedLoad.ParentRunID,
            SourceChildID = sourceSeedLoad.ChildRunID,
            ColdSeedDigest = sourceSeedLoad.ColdSeedDigest,
            ConfigReceiptDigest = sourceSeedLoad.PersistedConfigDigest,
            CheckpointReceiptDigest = sourceSeedLoad.ExpectedCheckpointSHA256,
        };
        authority = authority with { ReceiptDigest = authority.ComputeDigest() };
        return true;
    }

    /// Bind a raw calibration readout after its fork has completed.  The fork runner owns the only
    /// authoritative child identity and seed-load receipt, while this policy seam owns the authority
    /// digest and the final checkpoint provenance.  Keeping this bind here prevents a caller from
    /// assembling a training receipt from free lineage strings.
    internal static PolicyBoundaryAuthorityReceipt BindPolicyBoundaryAuthority(
        in PolicyBoundaryAuthorityReceipt sourceAuthority,
        in CortexForkSeedLoadReceipt sourceSeedLoad,
        string finalCheckpointSHA256)
    {
        if (!sourceAuthority.Verified
            || !string.Equals(sourceAuthority.ReceiptDigest, sourceAuthority.ComputeDigest(), StringComparison.Ordinal))
            throw new InvalidDataException("calibration source authority is not self-verified");
        if (!sourceSeedLoad.Bound || !sourceSeedLoad.Exact
            || sourceSeedLoad.Role != CortexForkRailRoles.Calibration)
            throw new InvalidDataException("calibration source authority lacks an exact calibration seed-load receipt");
        if (finalCheckpointSHA256.Length != 64 || finalCheckpointSHA256.Any(static c => !char.IsAsciiHexDigit(c)))
            throw new InvalidDataException("calibration source authority lacks a valid final checkpoint digest");

        PolicyBoundaryAuthorityReceipt bound = sourceAuthority with
        {
            ParentRunID = sourceSeedLoad.ParentRunID,
            SourceChildID = sourceSeedLoad.ChildRunID,
            ColdSeedDigest = sourceSeedLoad.ColdSeedDigest,
            ConfigReceiptDigest = sourceSeedLoad.PersistedConfigDigest,
            CheckpointReceiptDigest = finalCheckpointSHA256,
        };
        bound = bound with { ReceiptDigest = bound.ComputeDigest() };
        if (string.IsNullOrWhiteSpace(bound.ParentRunID) || string.IsNullOrWhiteSpace(bound.SourceChildID))
            throw new InvalidDataException("calibration source authority lost its parent or child identity");
        return bound;
    }

    /// Mount only the verified boundary value into a fresh evaluation child after its cold checkpoint has loaded.
    /// No candidate fingerprint, adaptation counters, authority mode, or calibration runtime state is copied.
    internal PolicyBoundaryMountReceipt MountVerifiedPolicyBoundaryTrainingReceipt(
        in PolicyBoundaryTrainingReceipt training,
        in PolicyBoundaryMountReceipt mount,
        in CortexPolicyDecisionReadout destinationDecision)
        => MountVerifiedPolicyBoundaryTrainingReceiptCore(in training, in mount, in destinationDecision, null);

    internal PolicyBoundaryMountReceipt MountVerifiedPolicyBoundaryTrainingReceipt(
        in PolicyBoundaryTrainingReceipt training,
        in PolicyBoundaryMountReceipt mount,
        in CortexPolicyDecisionReadout destinationDecision,
        HomeostatDestinationHandshakeReceipt handshake)
    {
        ArgumentNullException.ThrowIfNull(handshake);
        handshake.Validate();
        if (!string.Equals(mount.DestinationHandshakeReceiptDigest, handshake.ReceiptDigest, StringComparison.Ordinal))
            throw new InvalidDataException("policy-boundary mount is not bound to its Homeostat destination handshake");
        if (handshake.PhysicalStep != mount.MountStep || !handshake.Readout.Equals(destinationDecision))
            throw new InvalidDataException("policy-boundary mount destination decision disagrees with the owner handshake");
        return MountVerifiedPolicyBoundaryTrainingReceiptCore(in training, in mount, in destinationDecision, handshake);
    }

    private PolicyBoundaryMountReceipt MountVerifiedPolicyBoundaryTrainingReceiptCore(
        in PolicyBoundaryTrainingReceipt training,
        in PolicyBoundaryMountReceipt mount,
        in CortexPolicyDecisionReadout destinationDecision,
        HomeostatDestinationHandshakeReceipt? handshake)
    {
        long t0 = Stopwatch.GetTimestamp();
        if (_runtimeRun is null)
            throw new InvalidOperationException("policy-boundary training mount requires a bound evaluation runtime");
        if (ForkRailRole != CortexForkRailRoles.Evaluation)
            throw new InvalidOperationException("policy-boundary training mount requires the evaluation rail");
        if (handshake is null && mount.Relation == PolicyBoundaryMountRelations.OfflineCalibrationToColdEvaluationAfterHandshake)
            throw new InvalidDataException("post-handshake policy-boundary mount requires the Homeostat owner receipt");
        string destinationChildID = Path.GetFileName(Path.GetFullPath(_runtimeRun.Dir));
        CortexPolicyID policy = new(training.Policy);
        IPolicyBoundaryDomain domain = RequirePolicyBoundaryDomain(policy);
        mount.Validate(in training, training.ParentRunID, destinationChildID, training.ColdSeedDigest, domain);
        if (!string.Equals(mount.DestinationChildID, destinationChildID, StringComparison.Ordinal))
            throw new InvalidDataException("policy-boundary mount destination does not match the bound evaluation child");

        if (handshake is not null)
        {
            handshake.Validate();
            if (!string.Equals(handshake.Policy.Value, policy.Value, StringComparison.Ordinal)
                || mount.DestinationHandshakeDecisionID != handshake.DecisionID
                || !string.Equals(mount.DestinationHandshakeReceiptDigest, handshake.ReceiptDigest, StringComparison.Ordinal)
                || !destinationDecision.Equals(handshake.Readout))
                throw new InvalidDataException("policy-boundary mount handshake policy/digest mismatch");
        }
        if (!_policyBoundaryObligations.TryGetValue(policy, out PolicyBoundaryObligation? obligation)
            || obligation.ID.Value != training.Obligation)
            throw new InvalidDataException("policy-boundary training obligation is not present in the cold evaluation checkpoint");
        PolicyState destinationPolicy = GetPolicy(policy);
        if (handshake is null && !destinationDecision.Equals(destinationPolicy.LastDecisionReadout))
            throw new InvalidDataException("policy-boundary mount destination decision is not the evaluation child's last execution");
        if (destinationDecision.GrammarRevision == global::Cogito.Grammar.GrammarRevisionID.Zero)
            throw new InvalidDataException("policy-boundary mount destination decision has no grammar revision");
        try { destinationDecision.Validate(destinationPolicy.Schema.ActionCount); }
        catch (InvalidDataException error)
        {
            throw new InvalidDataException("policy-boundary mount destination decision is not exact for its grammar revision", error);
        }
        ulong destinationFingerprint = GrammarPolicyReadout.ComputeFingerprint(destinationDecision.GrammarRevision, policy);
        if (destinationFingerprint == 0)
            throw new InvalidDataException("policy-boundary mount destination decision has no raw readout fingerprint");
        if (mount.DestinationDecisionReadoutFingerprint != destinationFingerprint
            || mount.DestinationDecisionReadoutRevision != destinationDecision.GrammarRevision.Value)
            throw new InvalidDataException("policy-boundary mount destination identity does not match the evaluation decision");
        PolicyBoundaryMountReceipt mounted = mount;

        PolicyBoundaryRational boundary = PolicyBoundaryRational.Parse(training.Boundary);
        if (!obligation.Identity.Policy.Equals(policy) || obligation.Identity.ObligationID.Value != training.Obligation)
            throw new InvalidDataException("policy-boundary training identity does not match the restored obligation");
        if (!string.Equals(training.Feature, obligation.Identity.Feature, StringComparison.Ordinal))
            throw new InvalidDataException("policy-boundary training feature/comparison does not match the restored obligation");
        PolicyBoundaryTrainingForkReceipt authorityImage = training.ForkAuthority
            ?? throw new InvalidDataException("policy-boundary training mount has no fork authority");
        PolicyBoundaryForkReceipt authority = authorityImage.ToDomain();
        authority.Validate(domain);
        if (!string.Equals(PolicyBoundaryObligation.ComputeReceiptDigest(in authority), training.ForkAuthorityDigest, StringComparison.Ordinal))
            throw new InvalidDataException("policy-boundary training authority digest mismatch at mount");
        obligation.MountVerifiedTrainingReceipt(in authority, training.Comparison, domain);
        if (obligation.Winner is not PolicyBoundaryCandidate winner || training.Comparison != winner.Comparison)
            throw new InvalidDataException("policy-boundary training mount did not install its verified comparison");
        _mountedPolicyBoundaryLineage[policy] = (training, mounted);

        byte[] encoded = PolicyBoundaryMountReceipt.Encode(in mounted, in training, domain);
        _runtimeRun.WriteAtomic("policy-boundary.mount.ron", stream => stream.Write(encoded));
        PolicyBoundaryMountReceipt restored = PolicyBoundaryMountReceipt.Decode(
            encoded, in training, training.ParentRunID, destinationChildID, training.ColdSeedDigest, domain);
        if (restored.ReceiptDigest != mounted.ReceiptDigest)
            throw new InvalidDataException("policy-boundary mount RON round-trip changed the receipt digest");
        _ = TapePacketCreator.AppendPolicyBoundaryTrainingMount(_runtimeTape!, _runtimeJournal!, Step, in mounted);
        Trace.Cortex.Boundary("policy.boundary.mount",
            $"source={training.SourceChildID} destination={mounted.DestinationChildID} policy={training.Policy} boundary={boundary} start={mounted.EvaluationStartStep} end={mounted.EvaluationEndStep} mount={mounted.MountStep} wall={Stopwatch.GetElapsedTime(t0).TotalMilliseconds:F3}ms result=PASS");
        return mounted;
    }

    internal (PolicyBoundaryMountReceipt Receipt, long MountWallMilliseconds, long MountRawTicks) MountVerifiedPolicyBoundaryTrainingReceiptWithAccounting(
        in PolicyBoundaryTrainingReceipt training,
        in PolicyBoundaryMountReceipt mount,
        in CortexPolicyDecisionReadout destinationDecision)
    {
        long t0 = Stopwatch.GetTimestamp();
        PolicyBoundaryMountReceipt receipt = MountVerifiedPolicyBoundaryTrainingReceipt(in training, in mount, in destinationDecision);
        return (receipt,
            Stopwatch.GetElapsedTime(t0).Ticks / TimeSpan.TicksPerMillisecond,
            Math.Max(1, Stopwatch.GetTimestamp() - t0));
    }

    internal (PolicyBoundaryMountReceipt Receipt, long MountWallMilliseconds, long MountRawTicks) MountVerifiedPolicyBoundaryTrainingReceiptWithAccounting(
        in PolicyBoundaryTrainingReceipt training,
        in PolicyBoundaryMountReceipt mount,
        in CortexPolicyDecisionReadout destinationDecision,
        HomeostatDestinationHandshakeReceipt handshake)
    {
        long t0 = Stopwatch.GetTimestamp();
        PolicyBoundaryMountReceipt receipt = MountVerifiedPolicyBoundaryTrainingReceipt(in training, in mount, in destinationDecision, handshake);
        return (receipt,
            Stopwatch.GetElapsedTime(t0).Ticks / TimeSpan.TicksPerMillisecond,
            Math.Max(1, Stopwatch.GetTimestamp() - t0));
    }

    internal static bool IsMountVerified(
        in PolicyBoundaryMountReceipt mount,
        in PolicyBoundaryTrainingReceipt training,
        IPolicyBoundaryDomain domain)
    {
        try
        {
            mount.Validate(in training, training.ParentRunID, mount.DestinationChildID, training.ColdSeedDigest, domain);
            return mount.VerifiedReceipt && mount.VerifiedContent;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    /// Consume the Homeostat-owned step-zero handshake, persist its sidecar, and mount only the
    /// handshake-bound policy receipt. The runtime owns this transaction; a composite runner only
    /// supplies the training receipt and evaluation window.
    internal (PolicyBoundaryMountReceipt Mount, long HandshakeWallMilliseconds, long MountWallMilliseconds, long HandshakeRawTicks, long MountRawTicks, HomeostatDestinationHandshakeReceipt Handshake, string HandshakePath, string HandshakeSHA256)
        MountDestinationHandshake(int completedStep, in PolicyBoundaryTrainingReceipt training, int evaluationSteps)
    {
        long handshakeStarted = Stopwatch.GetTimestamp();
        HomeostatDestinationHandshakeReceipt handshake = ConsumeHomeostatDestinationHandshake(completedStep);
        byte[] handshakeBytes = HomeostatDestinationHandshakeReceipt.Encode(in handshake);
        const string handshakePath = "homeostat.destination-handshake.ron";
        CurrentRun.WriteAtomic(handshakePath, stream => stream.Write(handshakeBytes));
        string handshakeSHA256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(CurrentRun.PathOf(handshakePath))));
        HomeostatDestinationHandshakeReceipt restoredHandshake = HomeostatDestinationHandshakeReceipt.Decode(
            File.ReadAllBytes(CurrentRun.PathOf(handshakePath)));
        if (!string.Equals(restoredHandshake.ReceiptDigest, handshake.ReceiptDigest, StringComparison.Ordinal))
            throw new InvalidDataException("Homeostat destination handshake RON round-trip changed its receipt digest");
        // LastDecisionReadout is diagnostic only: policy verification probes may overwrite it after the owner receipt.
        _ = TryReadExecutedPolicyDecisionIdentity(HomeostatPolicyBoundaryDomain.Instance.PolicyID, out _, out _);
        PolicyBoundaryMountReceipt requested = PolicyBoundaryMountReceipt.CreateVerifiedAfterHandshake(
            in training, Path.GetFileName(CurrentRun.Dir), 1, evaluationSteps, mountStep: 0,
            handshake.readoutFingerprint, handshake.grammarRevision, in handshake, HomeostatPolicyBoundaryDomain.Instance);
        long mountStarted = Stopwatch.GetTimestamp();
        long handshakeWall = Stopwatch.GetElapsedTime(handshakeStarted).Ticks / TimeSpan.TicksPerMillisecond;
        CortexPolicyDecisionReadout ownerReadout = handshake.Readout;
        (PolicyBoundaryMountReceipt mounted, long mountWall, long mountRawTicks) = MountVerifiedPolicyBoundaryTrainingReceiptWithAccounting(
            in training, in requested, in ownerReadout, handshake);
        MarkHomeostatDestinationHandshakeMounted(handshake.ReceiptDigest);
        Trace.Cortex.Boundary("policy.boundary.destination-handshake",
            $"step={completedStep} sidecar={handshakePath} sha={handshakeSHA256} receipt={handshake.ReceiptDigest} handshake={handshakeWall}ms mount={mountWall}ms");
        long handshakeRawTicks = Math.Max(1, mountStarted - handshakeStarted);
        return (mounted, handshakeWall, mountWall, handshakeRawTicks, mountRawTicks, restoredHandshake, handshakePath, handshakeSHA256);
    }

    internal void RestorePolicyBoundaryLineage(
        in PolicyBoundaryTrainingReceipt training,
        in PolicyBoundaryMountReceipt mount)
    {
        if (mount.DestinationDecisionReadoutFingerprint == 0 || mount.DestinationDecisionReadoutRevision == 0)
            throw new InvalidDataException("policy-boundary lineage restore lacks destination decision readout identity");
        CortexPolicyID policy = new(training.Policy);
        IPolicyBoundaryDomain domain = RequirePolicyBoundaryDomain(policy);
        training.Validate(domain);
        mount.Validate(in training, training.ParentRunID, mount.DestinationChildID, training.ColdSeedDigest, domain);
        if (!_policyBoundaryObligations.TryGetValue(policy, out PolicyBoundaryObligation? obligation)
            || obligation.ID.Value != training.Obligation)
            throw new InvalidDataException("policy-boundary lineage restore addresses an unknown obligation");
        PolicyBoundaryTrainingForkReceipt authorityImage = training.ForkAuthority
            ?? throw new InvalidDataException("policy-boundary lineage restore has no fork authority");
        PolicyBoundaryForkReceipt authority = authorityImage.ToDomain();
        authority.Validate(domain);
        if (!string.Equals(PolicyBoundaryObligation.ComputeReceiptDigest(in authority), training.ForkAuthorityDigest, StringComparison.Ordinal))
            throw new InvalidDataException("policy-boundary lineage restore authority digest mismatch");
        if (obligation.Receipt is PolicyBoundaryForkReceipt receipt)
        {
            receipt.Validate(domain);
            if (receipt.SourceDecisionReadoutFingerprint != training.SourceDecisionReadoutFingerprint
                || receipt.SourceDecisionReadoutRevision != training.SourceDecisionReadoutRevision
                || !string.Equals(training.ForkReceiptDigest, PolicyBoundaryObligation.ComputeReceiptDigest(in receipt), StringComparison.Ordinal))
                throw new InvalidDataException("policy-boundary lineage restore recombined source authority evidence");
        }
        else
        {
            obligation.MountVerifiedTrainingReceipt(in authority, training.Comparison, domain);
        }
        _mountedPolicyBoundaryLineage[policy] = (training, mount);
    }

    private readonly record struct PolicyBoundaryTrialOverride(
        CortexPolicyID Policy,
        PolicyBoundaryObligationID Obligation,
        PolicyBoundaryArms Arm,
        ushort FeatureID,
        PolicyBoundaryRational Boundary);

    internal readonly record struct PolicyBoundaryGateObservation(
        bool Evaluated,
        double Observed,
        PolicyBoundaryRational Boundary,
        PolicyBoundaryComparisons Comparison,
        bool Satisfied)
    {
        internal bool AllowsProduction => !Evaluated || Satisfied;
    }

    private const uint PolicyBoundaryCheckpointTag = 0x424F424C; // BOBL
    private const uint PolicyBoundaryCheckpointVersion = 15;
    private const string PolicyBoundaryReceiptFile = "policy_boundary_obligations.tsv";
    private const string PolicyBoundaryAdmissionCensusFile = "policy-boundary.admission-census.tsv";
    private const string PolicyBoundaryOpportunityCensusFile = "policy-boundary.opportunity-census.tsv";
    private const string PolicyBoundaryReceiptHeader =
        "step\tpolicy\tobligation\tboundary\tbaseline\thorizons\tcontinuity\tmatched_spend\tforced_null_behavior_executed\tverified\tarm_evidence\treceipt_digest\tsource_readout_fingerprint\tsource_candidate_fingerprint\tsource_readout_revision\tfunding_decision_id";
    private const int PolicyBoundaryReceiptColumnCount = 16;
    private const string PolicyBoundaryAdmissionCensusHeader =
        "funding_id\tdecision\tbefore_child_directories\tbefore_markers\tafter_child_directories\tafter_markers\tbefore_status\tafter_status";
    private const string PolicyBoundaryOpportunityCensusHeader =
        "step\tobligation_available\treadout_available\texact_readout\tready_readout\treadout_revision\treadout_fingerprint\tcanonical_required\tcanonical_covered\tcanonical_missing\tcanonical_required_digest\tcanonical_covered_digest\tcanonical_missing_digest\tcanonical_attribution";
    private readonly Dictionary<CortexPolicyID, PolicyBoundaryObligation> _policyBoundaryObligations = new();
    private PolicyBoundaryTrialOverride? _policyBoundaryTrialOverride;

    private enum PolicyBoundaryAdmissionCensusStatuses : byte
    {
        Counted,
        Unavailable,
    }

    // A materialization marker is placement intent, not a terminal result.  Keep
    // the recovery state typed so a retryable partial generation cannot be
    // mistaken for biological failure, while an authenticated rejection still
    // fails closed.
    private enum PolicyBoundaryGenerationStates : byte
    {
        Incomplete,
        Invalid,
        Complete,
    }

    private readonly record struct PolicyBoundaryGenerationCensus(
        PolicyBoundaryGenerationStates State,
        string Reason,
        int ChildDirectories,
        int MaterializationMarkers,
        int CompleteRails,
        int InvalidArtifacts,
        int NextGeneration);

    private const string PolicyBoundaryGenerationTransitionFile = "policy-boundary.generation-transitions.tsv";
    private const string PolicyBoundaryGenerationTransitionHeader =
        "funding_id\tstate\treason\tchild_directories\tmarkers\tcomplete_rails\tinvalid_artifacts\tnext_generation";

    private static PolicyBoundaryObligation CreateRegisteredHomeostatBoundaryObligation(World world, CortexRunConfig config)
    {
        ushort criticalityMetricID = checked((ushort)(400 + (int)HomeostatPolicyFeatureIDs.Criticality));
        string worldIdentity = string.IsNullOrWhiteSpace(config.ExpectedWorldSHA256)
            ? string.Concat(config.Curriculum, ":", world.CorpusBytes.ToString(CultureInfo.InvariantCulture))
            : config.ExpectedWorldSHA256;
        PolicyBoundaryIdentity identity = new(
            HomeostatPolicyBoundaryDomain.Instance.PolicyID,
            string.Concat("homeostat-schema:", HomeostatPolicyBoundaryDomain.Instance.Schema.FeatureCount.ToString(CultureInfo.InvariantCulture), ":",
                HomeostatPolicyBoundaryDomain.Instance.Schema.ActionCount.ToString(CultureInfo.InvariantCulture), ":",
                HomeostatPolicyBoundaryDomain.Instance.Schema.OutcomeCount.ToString(CultureInfo.InvariantCulture)),
            string.Concat("grammar:", config.PolicyAuthorityCeiling, ":", config.PolicyProposalInterval.ToString(CultureInfo.InvariantCulture)),
            string.Concat("world:", worldIdentity),
            criticalityMetricID.ToString(CultureInfo.InvariantCulture),
            "criticality");
        return new PolicyBoundaryObligation(identity);
    }

    /// Fork-only arm seam. The override is deliberately transient: it is installed after seed load, consumed by the
    /// ordinary production guard, and never serialized or promoted. Final authority still requires the verified receipt.
    internal void SetPolicyBoundaryTrialOverride(
        CortexPolicyID policy,
        PolicyBoundaryObligationID obligation,
        PolicyBoundaryArms arm,
        ushort featureID,
        PolicyBoundaryRational boundary)
    {
        if (AllowsAutonomicSpawning)
            throw new InvalidOperationException("boundary trial overrides are fork-only");
        if (arm == PolicyBoundaryArms.ForcedDivergentNull && boundary == PolicyBoundaryRational.Zero)
            throw new ArgumentException("forced-null boundary must remain a real divergent threshold", nameof(boundary));
        _policyBoundaryTrialOverride = new(policy, obligation, arm, featureID, boundary);
    }

    /// Install one policy-owned threshold obligation. The obligation is inert until a verified matched-fork receipt
    /// is selected; this keeps an observed constant from becoming an actuator merely because it was proposed.
    internal void RegisterPolicyBoundaryObligation(PolicyBoundaryObligation obligation)
    {
        ArgumentNullException.ThrowIfNull(obligation);
        if (!_policies.ContainsKey(obligation.Identity.Policy))
            throw new InvalidOperationException($"policy '{obligation.Identity.Policy}' must be registered before its boundary obligation");
        if (!_policyBoundaryDomains.ContainsKey(obligation.Identity.Policy))
            throw new InvalidOperationException($"policy-boundary domain '{obligation.Identity.Policy}' must be registered before its boundary obligation");
        if (_policyBoundaryObligations.TryGetValue(obligation.Identity.Policy, out PolicyBoundaryObligation? prior)
            && prior.ID != obligation.ID)
            throw new InvalidDataException($"policy '{obligation.Identity.Policy}' already owns a different boundary obligation");
        _policyBoundaryObligations[obligation.Identity.Policy] = obligation;
    }

    /// Register the semantic owner beside its generic policy schema. A domain
    /// cannot be replaced with another implementation once custody is mounted.
    internal void RegisterPolicyBoundaryDomain(IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        if (!_policies.TryGetValue(domain.PolicyID, out PolicyState? policyState))
            throw new InvalidOperationException($"policy '{domain.PolicyID}' must be registered before its boundary domain");
        if (_policyBoundaryDomains.ContainsKey(domain.PolicyID))
            throw new InvalidDataException($"policy-boundary domain '{domain.PolicyID}' is already registered");
        domain.PolicyBinding.Validate();
        if (!domain.PolicyBinding.PolicyID.Equals(domain.PolicyID))
            throw new InvalidDataException($"policy-boundary domain '{domain.PolicyID}' binding addresses '{domain.PolicyBinding.PolicyID}'");
        CortexPolicySchema registered = policyState.Schema;
        CortexPolicySchema supplied = domain.Schema;
        PolicyCanonicalStateID[] canonicalStates = domain.CanonicalStates;
        if (!domain.PolicyID.Equals(supplied.Policy)
            || !supplied.Policy.Equals(registered.Policy)
            || supplied.FeatureCount != registered.FeatureCount
            || supplied.ActionCount != registered.ActionCount
            || supplied.OutcomeCount != registered.OutcomeCount
            || supplied.ModeCeiling != registered.ModeCeiling
            || supplied.Admission != registered.Admission
            || !domain.SeedAuthority.IsValid
            || !Enum.IsDefined(domain.CanonicalStateKind)
            || !Enum.IsDefined(domain.CanonicalScopeMode)
            || canonicalStates is null
            || domain.CanonicalScopeMode == PolicyCanonicalScopeModes.Enumerated && canonicalStates.Length == 0
            || domain.CanonicalScopeMode != PolicyCanonicalScopeModes.Enumerated && canonicalStates.Length != 0)
            throw new InvalidDataException($"policy-boundary domain '{domain.PolicyID}' has malformed schema or authority inputs");
        foreach (PolicyCanonicalStateID canonicalState in canonicalStates)
            if (!domain.ValidateCanonicalState(in canonicalState))
                throw new InvalidDataException($"policy-boundary domain '{domain.PolicyID}' has an invalid canonical state");
        _policyBoundaryDomains.Add(domain.PolicyID, domain);
    }

    internal bool TryGetPolicyBoundaryDomain(CortexPolicyID policy, out IPolicyBoundaryDomain domain)
        => _policyBoundaryDomains.TryGetValue(policy, out domain!);

    internal IPolicyBoundaryDomain RequirePolicyBoundaryDomain(CortexPolicyID policy)
        => TryGetPolicyBoundaryDomain(policy, out IPolicyBoundaryDomain domain)
            ? domain
            : throw new InvalidDataException($"no policy-boundary domain is registered for {policy}");

    internal bool TryReadPolicyBoundaryReadout(
        CortexPolicyID policy,
        ReadOnlySpan<MetricSample> features,
        out PolicyBoundaryReadout readout)
    {
        if (_policyBoundaryObligations.TryGetValue(policy, out PolicyBoundaryObligation? obligation))
        {
            _ = RequirePolicyBoundaryDomain(policy);
            return obligation.TryReadGuard(features, out readout);
        }
        readout = default;
        return false;
    }

    internal bool TryGetPolicyBoundaryObligation(
        CortexPolicyID policy,
        out PolicyBoundaryObligation obligation)
        => _policyBoundaryObligations.TryGetValue(policy, out obligation!);

    internal bool TryReadHomeostatBoundaryReceipt(out PolicyBoundaryForkReceipt receipt)
    {
        if (_policyBoundaryObligations.TryGetValue(HomeostatPolicyBoundaryDomain.Instance.PolicyID, out PolicyBoundaryObligation? obligation)
            && obligation.Receipt is PolicyBoundaryForkReceipt selected)
        {
            receipt = selected;
            return true;
        }
        receipt = default;
        return false;
    }

    /// The fork seam invokes this after real step-0 sensing/model work and before runtime settlement. The
    /// Homeostat owns the decision; Cortex only transports the typed receipt to the post-completion mount hook.
    internal void CaptureHomeostatDestinationHandshake(int physicalStep, bool forceExplicit = false)
    {
        if (physicalStep != 0) return;
        if (_destinationHandshakeOwnerReceipt is not null)
        {
            if (_destinationHandshakeReceipt is not null
                && !string.Equals(_destinationHandshakeReceipt.ReceiptDigest, _destinationHandshakeOwnerReceipt.ReceiptDigest, StringComparison.Ordinal))
                throw new InvalidDataException("destination handshake duplicate capture conflicts with the owner receipt");
            _destinationHandshakeReceipt = _destinationHandshakeOwnerReceipt;
            return;
        }
        HomeostatDestinationHandshakeReceipt receipt = Homeostat.CreateDestinationHandshake(this, physicalStep, forceExplicit);
        receipt.ValidateForPhysicalStep(0);
        _destinationHandshakeOwnerReceipt = receipt;
        _destinationHandshakeReceipt = receipt;
        Trace.Cortex.Boundary("policy.boundary.destination-handshake",
            $"step={physicalStep} source={receipt.source} decision={receipt.decisionID} policy={receipt.policy} fp={receipt.readoutFingerprint:X16} revision={receipt.grammarRevision} receipt={receipt.receiptDigest}");
    }

    internal HomeostatDestinationHandshakeReceipt ConsumeHomeostatDestinationHandshake(int physicalStep)
    {
        HomeostatDestinationHandshakeReceipt receipt = _destinationHandshakeReceipt ?? _destinationHandshakeOwnerReceipt
            ?? throw new InvalidDataException("destination handshake was not captured by the owner before mount");
        _destinationHandshakeReceipt = null;
        receipt.ValidateForPhysicalStep(physicalStep);
        return receipt;
    }

    internal void MarkHomeostatDestinationHandshakeMounted(string receiptDigest)
    {
        if (string.IsNullOrWhiteSpace(receiptDigest)) throw new ArgumentException("handshake receipt digest is required", nameof(receiptDigest));
        if (!string.IsNullOrEmpty(_destinationHandshakeMountedDigest))
            throw new InvalidDataException("destination handshake mount was attempted more than once");
        if (_destinationHandshakeOwnerReceipt is null
            || !string.Equals(_destinationHandshakeOwnerReceipt.ReceiptDigest, receiptDigest, StringComparison.Ordinal))
            throw new InvalidDataException("destination handshake mount is not bound to the owner token");
        _destinationHandshakeMountedDigest = receiptDigest;
    }

    private bool TryGrantPolicyBoundary(
        CortexPolicyID policy,
        in PolicyBoundaryForkReceipt receipt)
    {
        if (!_policyBoundaryObligations.TryGetValue(policy, out PolicyBoundaryObligation? obligation)) return false;
        if (!receipt.Verified) return false;
        if (receipt.SourceDecisionReadoutFingerprint == 0 || receipt.SourceDecisionCandidateFingerprint == 0 || receipt.SourceDecisionReadoutRevision == 0) return false;
        byte[] packet = TapePacketCreator.EncodePolicyBoundaryReceipt(
            policy, RequirePolicyBoundaryDomain(policy), in receipt);
        if (!PolicyBoundaryTapeVerifier.TryRead(packet, RequirePolicyBoundaryDomain(policy),
                out PolicyBoundaryForkReceipt decoded, out CortexPolicyID decodedPolicy)
            || !decodedPolicy.Equals(policy)
            || !string.Equals(PolicyBoundaryObligation.ComputeReceiptDigest(in decoded),
                PolicyBoundaryObligation.ComputeReceiptDigest(in receipt), StringComparison.Ordinal)) return false;
        IPolicyBoundaryDomain domain = RequirePolicyBoundaryDomain(policy);
        obligation.Select(receipt, domain);
        _ = AppendPolicyBoundaryReceipt(policy, in receipt, out _);
        Trace.Cortex.Boundary(
            "policy.boundary.verify",
            $"policy={policy} obligation={receipt.Obligation} boundary={receipt.CandidateBoundary} horizons={string.Join(',', receipt.Horizons)} continuity={(receipt.ContinuityExact ? 1 : 0)} matched-spend={(receipt.MatchedSpend ? 1 : 0)} forced-null={(receipt.ForcedNullBehaviorExecuted ? 1 : 0)} result={(receipt.Verified ? "PASS" : "FAIL")}");
        return receipt.Verified;
    }

    /// Fund, execute, settle, and only then admit a boundary receipt. The fork runner owns every arm delta; this
    /// seam owns the policy journal so a caller cannot bypass reserve/refund accounting with a hand-built receipt.
    internal bool TryRunPaidPolicyBoundary(
        in CortexPolicyTrialQuotaDecision funding,
        CortexForkSeed seed,
        PolicyBoundaryIdentity identity,
        PolicyBoundaryRational baselineBoundary,
        PolicyBoundaryRational candidateBoundary,
        CortexForkArm<PolicyBoundaryTrialOutcome>[] baselineArms,
        CortexForkArm<PolicyBoundaryTrialOutcome>[] candidateArms,
        CortexForkArm<PolicyBoundaryTrialOutcome>[] forcedNullArms,
        CortexForkArm<PolicyBoundaryTrialOutcome>[] reflexArms,
        int[] horizons,
        ulong readoutFingerprint,
        ulong candidateFingerprint,
        ulong candidateReadoutRevision,
        PolicyBoundaryTeacherCorroboration? teacherCorroboration,
        out PolicyBoundaryForkReceipt receipt,
        out CortexPolicyTrialCompletion settlement)
    {
        receipt = default;
        settlement = default;
        IPolicyBoundaryDomain domain = RequirePolicyBoundaryDomain(funding.Policy);
        if (identity.Policy.Value.Length == 0 || horizons is null || horizons.Length == 0) return false;
        if (funding.Decision is not (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused)
            || !funding.Policy.Equals(identity.Policy)
            || funding.CandidateFingerprint != candidateFingerprint
            || funding.ReadoutFingerprint != readoutFingerprint
            || funding.RequestedHorizonSteps != horizons[^1]
            || funding.ArmCount != 4)
            return false;
        if (!_policyTrialQuotaByID.TryGetValue(funding.QuotaDecisionID, out CortexPolicyTrialQuotaDecision admitted)
            || !QuotaIdentityMatches(in admitted, in funding))
            return false;
        ValidatePolicyBoundaryMaterializationContracts(seed, in funding, baselineArms, candidateArms, forcedNullArms, reflexArms);
        string fundingAttemptID = funding.QuotaDecisionID.ToString();
        (int beforeChildDirectories, int beforeMaterializationMarkers) = (0, 0);
        bool beforeCensusCaptured = true;
        try
        {
            (beforeChildDirectories, beforeMaterializationMarkers) =
                CountPolicyBoundaryChildren(CurrentRun.Dir, fundingAttemptID);
        }
        catch (Exception censusError) when (censusError is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            beforeCensusCaptured = false;
            try
            {
                AppendPolicyBoundaryAdmissionCensus(in funding, -1, -1, (-1, -1),
                    PolicyBoundaryAdmissionCensusStatuses.Unavailable,
                    PolicyBoundaryAdmissionCensusStatuses.Unavailable);
                FlushPolicyJournalBuffer();
            }
            catch (Exception censusReceiptError) when (censusReceiptError is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Trace.Cortex.Boundary("policy.boundary.admission-census-failure",
                    $"funding={funding.QuotaDecisionID} phase=before-persist type={censusReceiptError.GetType().Name} message={censusReceiptError.Message}");
            }
            Trace.Cortex.Boundary("policy.boundary.admission-census-failure",
                $"funding={funding.QuotaDecisionID} phase=before type={censusError.GetType().Name} message={censusError.Message}");
        }
        try
        {
            receipt = PolicyBoundaryForkRunner.Run(this, domain, seed, identity, baselineBoundary, candidateBoundary,
                baselineArms, candidateArms, forcedNullArms, reflexArms, horizons,
                readoutFingerprint, candidateFingerprint, candidateReadoutRevision);
            receipt = receipt with
            {
                QuotaDecisionID = funding.QuotaDecisionID,
                SourceDecisionCandidateFingerprint = candidateFingerprint,
            };
            if (teacherCorroboration is not null)
            {
                teacherCorroboration.Validate();
                receipt = receipt with { TeacherCorroboration = teacherCorroboration };
            }
            receipt.Validate(domain);
            if (receipt.Verified)
            {
                settlement = SettleAuthenticatedPolicyBoundary(in funding, horizons, out PolicyBoundaryForkReceipt terminalReceipt);
                receipt = terminalReceipt with
                {
                    TeacherCorroboration = receipt.TeacherCorroboration,
                    ExecutionCorroboration = receipt.ExecutionCorroboration,
                };
            }
            else
            {
                long actual = receipt.ComputeTerminalMatchedSpend();
                settlement = CompletePolicyTrial(in funding, actual, null, CortexPolicyVerifierOutcomes.Failed, null);
                FlushPolicyJournalBuffer();
            }
            if (!receipt.Verified || settlement.ActualExecutedArmSteps + settlement.ReclaimedOrUnused != funding.PlannedArmSteps)
                return false;
            if (!_policyBoundaryObligations.ContainsKey(identity.Policy)) return false;
            return true;
        }
        catch (Exception exception)
        {
            if (funding.Decision is CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused)
            {
                // Child completion can fail after materialization but before the
                // parent settlement. Preserve the true before/after census when
                // available, then settle and flush again without masking the root error.
                try
                {
                    if (beforeCensusCaptured)
                    {
                        (int materializedChildren, int materializationMarkers) =
                            CountPolicyBoundaryChildren(CurrentRun.Dir, fundingAttemptID);
                        AppendPolicyBoundaryAdmissionCensus(in funding, beforeChildDirectories,
                            beforeMaterializationMarkers, (materializedChildren, materializationMarkers));
                    }
                }
                catch (Exception censusError) when (censusError is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    try
                    {
                        AppendPolicyBoundaryAdmissionCensus(in funding, beforeChildDirectories, beforeMaterializationMarkers,
                            (-1, -1), PolicyBoundaryAdmissionCensusStatuses.Counted,
                            PolicyBoundaryAdmissionCensusStatuses.Unavailable);
                        FlushPolicyJournalBuffer();
                    }
                    catch (Exception censusReceiptError) when (censusReceiptError is IOException or UnauthorizedAccessException or InvalidDataException)
                    {
                        Trace.Cortex.Boundary("policy.boundary.admission-census-failure",
                            $"funding={funding.QuotaDecisionID} phase=after-persist type={censusReceiptError.GetType().Name} message={censusReceiptError.Message}");
                    }
                    Trace.Cortex.Boundary("policy.boundary.admission-census-failure",
                        $"funding={funding.QuotaDecisionID} phase=after type={censusError.GetType().Name} message={censusError.Message}");
                }
                try { FlushPolicyJournalBuffer(); }
                catch (Exception flushError) when (flushError is IOException or UnauthorizedAccessException)
                {
                    Trace.Cortex.Boundary("policy.boundary.admission-census-failure",
                        $"funding={funding.QuotaDecisionID} phase=before-settlement type={flushError.GetType().Name} message={flushError.Message}");
                }
                PolicyBoundaryGenerationStates generationState = PolicyBoundaryGenerationStates.Invalid;
                string generationStateReason = "generation-reconcile-failed";
                PolicyBoundaryGenerationCensus generationCensus = default;
                try
                {
                    _ = TryReadTerminalHomeostatBoundaryGeneration(in funding, funding.ReadoutFingerprint, horizons,
                        out _, out _, out _, out generationState, out generationStateReason, out generationCensus);
                }
                catch (Exception recoveryError) when (recoveryError is InvalidDataException or IOException
                    or UnauthorizedAccessException or ArgumentException)
                {
                    generationState = PolicyBoundaryGenerationStates.Invalid;
                    generationStateReason = "generation-reconcile-exception";
                    generationCensus = new(generationState, generationStateReason, 0, 0, 0, 1, 0);
                    Trace.Cortex.Boundary("policy.boundary.generation-reject",
                        $"funding={funding.QuotaDecisionID} type={recoveryError.GetType().Name} message={recoveryError.Message}");
                }
                if (generationState == PolicyBoundaryGenerationStates.Incomplete)
                {
                    try
                    {
                        AppendPolicyBoundaryGenerationTransition(in funding, in generationCensus);
                        FlushPolicyJournalBuffer();
                    }
                    catch (Exception transitionError) when (transitionError is IOException or UnauthorizedAccessException or InvalidDataException)
                    {
                        Trace.Cortex.Boundary("policy.boundary.generation-transition-failure",
                            $"funding={funding.QuotaDecisionID} type={transitionError.GetType().Name} message={transitionError.Message}");
                    }
                    Trace.Cortex.Boundary("policy.boundary.generation-incomplete",
                        $"funding={funding.QuotaDecisionID} reason={generationStateReason} next-generation={generationCensus.NextGeneration} children={generationCensus.ChildDirectories} complete-rails={generationCensus.CompleteRails}");
                    throw;
                }
                try
                {
                    settlement = _policyTrialCompletionByID.TryGetValue(funding.QuotaDecisionID, out CortexPolicyTrialCompletion existing)
                        ? existing
                        : CompletePolicyTrial(in funding, 0, null, CortexPolicyVerifierOutcomes.Failed, null);
                }
                catch (Exception settlementError) when (settlementError is InvalidDataException or InvalidOperationException)
                {
                    Trace.Cortex.Boundary("policy.boundary.settlement-failure",
                        $"funding={funding.QuotaDecisionID} type={settlementError.GetType().Name} message={settlementError.Message}");
                }
                try { FlushPolicyJournalBuffer(); }
                catch (Exception flushError) when (flushError is IOException or UnauthorizedAccessException)
                {
                    Trace.Cortex.Boundary("policy.boundary.admission-census-failure",
                        $"funding={funding.QuotaDecisionID} phase=after-settlement type={flushError.GetType().Name} message={flushError.Message}");
                }
            }
            Trace.Cortex.Boundary(
                "policy.boundary.failure",
                $"policy={identity.Policy} funding={funding.QuotaDecisionID} step={funding.QuotaStep} planned={funding.PlannedArmSteps} actual={settlement.ActualExecutedArmSteps} refund={settlement.ReclaimedOrUnused} exception={exception.GetType().FullName} message={exception.Message}");
            throw;
        }
    }

    /// Repository-native divergence owns its boundary packet at the same transition
    /// that settles the four arms.  Returning the emitted bytes keeps continuation
    /// links bound to that exact event without a later tape search.
    internal bool TryRunPaidPolicyBoundaryWithReceipt(
        in CortexPolicyTrialQuotaDecision funding,
        CortexForkSeed seed,
        PolicyBoundaryIdentity identity,
        PolicyBoundaryRational baselineBoundary,
        PolicyBoundaryRational candidateBoundary,
        CortexForkArm<PolicyBoundaryTrialOutcome>[] baselineArms,
        CortexForkArm<PolicyBoundaryTrialOutcome>[] candidateArms,
        CortexForkArm<PolicyBoundaryTrialOutcome>[] forcedNullArms,
        CortexForkArm<PolicyBoundaryTrialOutcome>[] reflexArms,
        int[] horizons,
        ulong readoutFingerprint,
        ulong candidateFingerprint,
        ulong candidateReadoutRevision,
        PolicyBoundaryTeacherCorroboration? teacherCorroboration,
        out PolicyBoundaryForkReceipt receipt,
        out CortexPolicyTrialCompletion settlement,
        out TapeEventID boundaryEventID,
        out byte[] boundaryPayload)
    {
        boundaryEventID = default;
        boundaryPayload = [];
        if (!TryRunPaidPolicyBoundary(in funding, seed, identity, baselineBoundary, candidateBoundary,
                baselineArms, candidateArms, forcedNullArms, reflexArms, horizons,
                readoutFingerprint, candidateFingerprint, candidateReadoutRevision, teacherCorroboration,
                out receipt, out settlement)) return false;
        return TryEmitPolicyBoundaryReceipt(funding.Policy, in receipt, out boundaryEventID, out boundaryPayload);
    }

    internal void TryRunHomeostatBoundaryAtStep(CortexRunConfig config, bool terminalRecoveryOnly = false)
    {
        if (!AllowsPolicyBoundaryAssay
            || config.PolicyAuthorityCeiling != CortexPolicyAuthorities.Grammar
            || _config.Learning.Policies.TrialAllocation is not { Authority: CortexPolicyAuthorities.Grammar, ArmSteps: > 0 }) return;
        // A trial allocation says a run MAY fund boundary assays; it does not say the homeostat is
        // mounted. A runtime that funds trials for its own domain while running the homeostat off
        // (the native crawler does exactly that) has no homeostat to assay, and demanding its domain
        // here killed the run at the first completed step.
        if (!TryGetPolicyBoundaryDomain(HomeostatPolicyBoundaryDomain.Instance.PolicyID, out IPolicyBoundaryDomain domain)) return;
        int[] horizons = config.PolicyTrialHorizons is null ? [16, 64, 256] : [.. config.PolicyTrialHorizons];
        if (horizons.Length == 0 || horizons[^1] <= 0) return;

        // A paid lease and its authenticated child receipts are durable authority.  Resume
        // must reconcile them before consulting the learner's transient readout: the learner can
        // legitimately be non-exact (or not ready) after a checkpoint while the lease still owns
        // a completed generation that must be settled exactly once.
        bool durableRecovery = TryReconcilePaidHomeostatBoundaryTrials(default, horizons,
            out CortexPolicyTrialQuotaDecision pendingFunding, out bool terminalSettled,
            out CortexPolicyTrialQuotaDecision recoveredFunding, out PolicyBoundaryForkReceipt recoveredReceipt,
            out CortexPolicyTrialCompletion recoveredSettlement);
        bool recoveringLease = durableRecovery && pendingFunding.QuotaDecisionID.Value != 0;
        if (terminalSettled)
        {
            if (!_policyBoundaryObligations.TryGetValue(HomeostatPolicyBoundaryDomain.Instance.PolicyID, out PolicyBoundaryObligation? settledObligation))
                return;
            if (!settledObligation.Receipt.HasValue)
            {
                RecoveredHomeostatBoundaryAttachmentOutcomes attachment = AttachRecoveredHomeostatBoundaryTrial(
                    in recoveredFunding, in recoveredSettlement, in recoveredReceipt);
                if (attachment is not RecoveredHomeostatBoundaryAttachmentOutcomes.Attached)
                {
                    Trace.Cortex.Boundary("policy.trial-recovered-not-admitted",
                        $"id={recoveredFunding.QuotaDecisionID} reason={attachment}");
                    return;
                }
            }
            return;
        }
        if (terminalRecoveryOnly && !durableRecovery) return;
        bool obligationAvailable = _policyBoundaryObligations.TryGetValue(
            HomeostatPolicyBoundaryDomain.Instance.PolicyID,
            out PolicyBoundaryObligation? obligation)
            && !obligation.Receipt.HasValue;
        CortexPolicyReadoutReceipt readout = default;
        bool readoutAvailable = obligationAvailable && TryReadPolicyReadout(HomeostatPolicyBoundaryDomain.Instance.PolicyID, out readout);
        bool exactReadout = readoutAvailable && readout.IsExact;
        bool readyReadout = exactReadout && IsPolicyReadoutReady(HomeostatPolicyBoundaryDomain.Instance.PolicyID, readout.Fingerprint);
        AppendPolicyBoundaryOpportunityCensus(obligationAvailable, readoutAvailable, exactReadout, readyReadout,
            readoutAvailable ? readout : default);
        Trace.Cortex.Boundary(
            "policy.boundary.opportunity",
            $"step={Step} obligation={(obligationAvailable ? 1 : 0)} readout={(readoutAvailable ? 1 : 0)} exact={(exactReadout ? 1 : 0)} ready={(readyReadout ? 1 : 0)} recovery={(recoveringLease ? 1 : 0)}");
        if (!obligationAvailable || !recoveringLease && !(readoutAvailable && exactReadout && readyReadout)) return;
        PolicyBoundaryObligation activeObligation = obligation!;
        if (_runtimeHomeostat is null) return;

        CortexPolicyDecision liveSourceDecision = _runtimeHomeostat.LastBoundaryPolicyDecision;
        CortexPolicyDecision sourceDecision = default;
        PolicyBoundarySeedCustody recoveryCustody = null!;
        if (recoveringLease)
        {
            if (!TryReadPolicyBoundarySeedCustodyDocument(CurrentRun.Dir, Path.GetFileName(CurrentRun.Dir),
                    pendingFunding.QuotaDecisionID.ToString(), out recoveryCustody)
                || !TryDecodeCanonicalState(recoveryCustody.canonicalState, out PolicyCanonicalStateID recoveryState)
                || !TryReadBoundarySourceCorroboration(recoveryCustody.sourceDecisionID, recoveryCustody.sourceDecisionEventID,
                    pendingFunding.CandidateRevision, pendingFunding.ReadoutFingerprint,
                    recoveryCustody.sourceCandidateFingerprint, recoveryCustody.sourceSupportDigest,
                    out TapeEventID recoveryCorroborationEventID, out CortexPolicySelectionCauses recoveryCause, recoveryState)
                || recoveryCorroborationEventID.Value != recoveryCustody.sourceCorroborationEventID
                || recoveryCause != recoveryCustody.sourceSelectionCause
                || !TryReadPolicyDecisionIdentityEvent(new TapeEventID(recoveryCustody.sourceDecisionEventID),
                    new CortexPolicyDecisionID(recoveryCustody.sourceDecisionID), out CortexPolicyDecisionReadout restoredReadout))
                return;
            CortexPolicyDecisionID restoredDecisionID = new(recoveryCustody.sourceDecisionID);
            sourceDecision = new CortexPolicyDecision(restoredDecisionID, HomeostatPolicyBoundaryDomain.Instance.PolicyID, restoredReadout);
            readout = new CortexPolicyReadoutReceipt(pendingFunding.CandidateRevision,
                pendingFunding.ReadoutFingerprint, recoveryCustody.readoutCachedContexts,
                recoveryCustody.readoutComparisons, recoveryCustody.readoutAgreements,
                recoveryCustody.readoutMisses,
                recoveryCustody.sourceSupportDigest,
                recoveryCustody.sourceCandidateFingerprint, CanonicalState: recoveryState);
        }
        if (!recoveringLease)
        {
            // The active tuple is not copied into ordinary launchpad packets.  The
            // organism must first emit its boundary corroboration, then lookup consumes
            // that exact tape event rather than reconstructing candidate metadata.
            if (!TryReadLatestHomeostatPolicyDecision(in readout, out CortexPolicyDecision corroboratedSourceDecision))
            {
                if (!IsLiveHomeostatBoundaryDecision(in liveSourceDecision, in readout)
                    || !TryEmitPolicyBoundarySourceCorroboration(in readout, in liveSourceDecision,
                        out CortexPolicyBoundarySourceCorroboration _, out TapeEventID _)
                    || !TryReadLatestHomeostatPolicyDecision(in readout, out corroboratedSourceDecision)) return;
            }
            if (!IsLiveHomeostatBoundaryDecision(in corroboratedSourceDecision, in readout)) return;
            sourceDecision = corroboratedSourceDecision;
        }
        // A denied maturity/fuel attempt is not a boundary transition. Do not
        // propose, freeze, or persist anything until the Paid lease callback.
        // A resumed lease is the sole exception: its immutable v6 custody is the
        // complete source of the candidate and is never resampled from Homeostat.
        PolicyBoundaryCandidate candidate;
        int sourceStep;
        string sourceProvenance;
        TapeEventID sourceDecisionEventID = FindPolicyDecisionEvent(sourceDecision.DecisionID);
        if (recoveringLease)
        {
            if (recoveryCustody.schemaVersion < 6
                || recoveryCustody.obligation != activeObligation.ID.Value
                || !PolicyBoundaryRational.TryParse(recoveryCustody.boundary, out PolicyBoundaryRational recoveredBoundary)
                || !Enum.IsDefined(recoveryCustody.comparison)
                || recoveryCustody.comparison == PolicyBoundaryComparisons.Unknown
                || string.IsNullOrWhiteSpace(recoveryCustody.provenance)
                || recoveryCustody.sourceStep < 0)
                return;
            candidate = new PolicyBoundaryCandidate(recoveredBoundary, recoveryCustody.comparison, recoveryCustody.provenance);
            sourceStep = recoveryCustody.sourceStep;
            sourceProvenance = recoveryCustody.provenance;
        }
        else
        {
            CortexPolicyDecisionReadout sourceReadout = sourceDecision.Readout;
            if (sourceDecisionEventID.Value <= 0
                || !TryReadAuthenticatedHomeostatCriticality(sourceDecisionEventID, sourceDecision.DecisionID,
                    in sourceReadout,
                    out double criticality, out sourceProvenance))
                return;
            PolicyBoundaryRational exactBoundary = PolicyBoundaryRational.FromDouble(criticality);
            candidate = new PolicyBoundaryCandidate(exactBoundary, PolicyBoundaryComparisons.LessThanOrEqual,
                sourceProvenance);
            sourceStep = _runtimeHomeostat.TryReadLastBoundaryCanonicalState(out _, out int boundaryStep)
                ? boundaryStep : Step;
        }
        CortexForkSeed? preparedSeed = null;
        bool canonicalMatch = recoveringLease
            ? TryDecodeCanonicalState(recoveryCustody.canonicalState, out PolicyCanonicalStateID recoveredCanonicalState)
                && IsRegisteredPolicyBoundaryCanonicalStateValid(recoveredCanonicalState)
            : sourceDecision.ReadoutContext.IsCanonical
                && sourceDecision.ReadoutContext.CanonicalState == readout.CanonicalState;
        ulong readoutFingerprint = readout.Fingerprint;
        ulong candidateFingerprint = readout.ReadoutCandidateFingerprint;
        if (readoutFingerprint == 0 || candidateFingerprint == 0)
            return;
        CortexPolicyTrialAuthorityIdentity authorityIdentity = CortexPolicyTrialAuthorityIdentity.FromReadout(in readout);
        CortexPolicyTrialQuotaDecision funding = pendingFunding.QuotaDecisionID.Value != 0
            ? ReusePolicyTrialQuota(in pendingFunding)
            : DecidePolicyTrialQuota(HomeostatPolicyBoundaryDomain.Instance.PolicyID, in authorityIdentity, horizons[^1], armCount: 4,
                preparePaidLease: fundingID =>
                {
                    // This callback is reached only after all candidate/maturity/fuel
                    // gates pass. The proposal, completed-step seed, and authenticated
                    // custody are one paid-boundary transaction.
                    activeObligation.Propose(candidate);
                    preparedSeed = MaterializeCompletedStepForkSeed();
                    string custody = PersistPolicyBoundarySeedV6(fundingID, HomeostatPolicyBoundaryDomain.Instance.PolicyID,
                        candidateFingerprint, Step, readout.Revision, preparedSeed!, sourceDecision.DecisionID.Value,
                        sourceDecisionEventID.Value, readout.ReadoutCandidateOccurrenceDigest,
                        readout.ReadoutCandidateFingerprint, readoutFingerprint, CortexPolicyQuotaDecisions.Paid,
                        readout.CanonicalState, candidate.Boundary, candidate.Comparison, candidate.Provenance,
                        sourceStep, activeObligation.ID.Value, readout.CachedContexts, readout.Comparisons,
                        readout.Agreements, readout.Misses)
                        ?? throw new InvalidDataException($"paid Homeostat boundary {fundingID} has no exact readout custody");
                    Trace.Cortex.Boundary("policy.boundary.paid-audit",
                        $"source_step={sourceStep} funding_step={Step} funding={fundingID} boundary={candidate.Boundary} comparison={candidate.Comparison} provenance={candidate.Provenance} source_event={sourceDecisionEventID} readout={readoutFingerprint:X16} candidate={candidateFingerprint:X16} revision={readout.Revision.Value} canonical_match={(canonicalMatch ? 1 : 0)} custody={custody}");
                    return custody;
                });
        // The funding row is the durable authority for which materialized children belong to this
        // policy trial. Flush it before the pre-child census so the current attempt is visible without
        // treating every sibling autonomy rail as a policy child.
        FlushPolicyJournalBuffer();
        string fundingAttemptID = funding.QuotaDecisionID.ToString();
        (int beforeChildDirectories, int beforeMaterializationMarkers) =
            CountPolicyBoundaryChildren(CurrentRun.Dir, fundingAttemptID);
        if (funding.Decision is not (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused))
        {
            AppendPolicyBoundaryAdmissionCensus(in funding, beforeChildDirectories, beforeMaterializationMarkers,
                CountPolicyBoundaryChildren(CurrentRun.Dir, fundingAttemptID));
            return;
        }
        CortexForkSeed seed;
        if (pendingFunding.QuotaDecisionID.Value != 0)
        {
            // A resumed lease is bound to the immutable seed captured when it was
            // paid.  The live terminal checkpoint is never a substitute: it may
            // already include later learning and would fork a different experiment.
            if (!TryLoadPolicyBoundarySeed(in pendingFunding, out seed))
            {
                Trace.Cortex.Boundary("policy.boundary.seed-custody-missing",
                    $"id={pendingFunding.QuotaDecisionID} step={pendingFunding.QuotaStep} current={Step}");
                return;
            }
        }
        else
        {
            seed = preparedSeed ?? throw new InvalidDataException("paid policy boundary lost its prepared seed");
            if (!TryLoadPolicyBoundarySeed(in funding, out CortexForkSeed verifiedSeed)
                || verifiedSeed.ColdSeedDigest != seed.ColdSeedDigest)
                throw new InvalidDataException($"paid policy boundary {funding.QuotaDecisionID} lost its seed custody");
        }
        CortexForkArm<PolicyBoundaryTrialOutcome>[][] arms = new CortexForkArm<PolicyBoundaryTrialOutcome>[horizons.Length][];
        string parentRunID = Path.GetFileName(CurrentRun.Dir);
        for (int i = 0; i < horizons.Length; i++)
        {
            (Run baselineRun, CortexForkMaterializationContract baselineContract) =
                CurrentRun.CreateMaterializedChildRun(CortexForkRailRoles.Baseline, fundingAttemptID, seed.ColdSeedDigest);
            (Run candidateRun, CortexForkMaterializationContract candidateContract) =
                CurrentRun.CreateMaterializedChildRun(CortexForkRailRoles.Candidate, fundingAttemptID, seed.ColdSeedDigest);
            (Run forcedNullRun, CortexForkMaterializationContract forcedNullContract) =
                CurrentRun.CreateMaterializedChildRun(CortexForkRailRoles.ForcedNull, fundingAttemptID, seed.ColdSeedDigest);
            (Run reflexFrozenRun, CortexForkMaterializationContract reflexFrozenContract) =
                CurrentRun.CreateMaterializedChildRun(CortexForkRailRoles.ReflexFrozen, fundingAttemptID, seed.ColdSeedDigest);
            arms[i] = [CreateHomeostatBoundaryArm(baselineRun.Dir, Step, horizons[i], PolicyBoundaryArms.Baseline, config, authorityIdentity, CortexPolicyAuthorities.Launchpad, railRole: CortexForkRailRoles.Baseline, parentRunID: parentRunID, materializationContract: baselineContract, obligation: activeObligation.ID, candidateBoundary: candidate.Boundary),
                CreateHomeostatBoundaryArm(candidateRun.Dir, Step, horizons[i], PolicyBoundaryArms.Candidate, config, authorityIdentity, CortexPolicyAuthorities.Grammar, railRole: CortexForkRailRoles.Candidate, parentRunID: parentRunID, materializationContract: candidateContract, obligation: activeObligation.ID, candidateBoundary: candidate.Boundary),
                CreateHomeostatBoundaryArm(forcedNullRun.Dir, Step, horizons[i], PolicyBoundaryArms.ForcedDivergentNull, config, authorityIdentity, CortexPolicyAuthorities.Grammar, forced: true, requireOrdinaryOutcome: _loopLineageEnabled && horizons[i] == horizons[^1], railRole: CortexForkRailRoles.ForcedNull, parentRunID: parentRunID, materializationContract: forcedNullContract, obligation: activeObligation.ID, candidateBoundary: candidate.Boundary),
                CreateHomeostatBoundaryArm(reflexFrozenRun.Dir, Step, horizons[i], PolicyBoundaryArms.ReflexFrozenControl, config, authorityIdentity, CortexPolicyAuthorities.Shadow, frozen: true, railRole: CortexForkRailRoles.ReflexFrozen, parentRunID: parentRunID, materializationContract: reflexFrozenContract, obligation: activeObligation.ID, candidateBoundary: candidate.Boundary)];
        }
        AppendPolicyBoundaryAdmissionCensus(in funding, beforeChildDirectories, beforeMaterializationMarkers,
            CountPolicyBoundaryChildren(CurrentRun.Dir, fundingAttemptID));
        PolicyBoundaryTeacherCorroboration? teacherCorroboration = null;
        LoopClosureR4Provenance? divergenceProvenance = null;
        // A Shadow source is still a valid causal decision.  Its checkpointed
        // decision object may have been reconstructed without the transient
        // context key, so recover the canonical context from the paid readout
        // before selecting the already-custodied R4 corroboration.  Succession verifies
        // the later child publication; it must not erase the source's derivation.
        GrammarPolicyContextKey sourceContext = sourceDecision.ReadoutContext.IsCanonical
            ? sourceDecision.ReadoutContext
            : new(readout.CanonicalState, HomeostatPolicyBoundaryDomain.Instance.Schema.ActionCount,
                _config.Learning.Policies.ReadoutDeliberationQuota);
        PolicyCanonicalStateID sourceCanonicalState = sourceContext.IsCanonical
            ? sourceContext.CanonicalState : default;
        if (sourceContext.IsCanonical && sourceCanonicalState == readout.CanonicalState
            && sourceDecision.Policy.Equals(domain.PolicyID)
            && sourceCanonicalState.Policy.Equals(domain.PolicyID)
            && domain.TryVerifyR4(this, in sourceDecision, out TapeEventID selectedDecisionEventID,
                out LoopClosureR4Provenance selectedProvenance)
            && selectedDecisionEventID == sourceDecisionEventID)
        {
            divergenceProvenance = selectedProvenance;
            teacherCorroboration = new PolicyBoundaryTeacherCorroboration(in selectedProvenance);
        }
        bool admitted = TryRunPaidPolicyBoundary(in funding, seed, activeObligation.Identity, PolicyBoundaryRational.Zero,
            candidate.Boundary,
            arms.Select(static row => row[0]).ToArray(), arms.Select(static row => row[1]).ToArray(),
            arms.Select(static row => row[2]).ToArray(), arms.Select(static row => row[3]).ToArray(), horizons, readoutFingerprint, candidateFingerprint,
            readout.Revision.Value, teacherCorroboration, out PolicyBoundaryForkReceipt boundaryReceipt, out CortexPolicyTrialCompletion settlement);
        if (!admitted) return;

        // The fork executor has now sealed the four matched rails and the
        // funding lease has settled.  Build both divergence rails from that exact
        // receipt; no action or outcome is reconstructed from a readout.
        if (!TryReadHomeostatDivergenceArms(in sourceDecision, in boundaryReceipt,
                out PolicyBoundaryDivergenceCandidateTerminal candidateOutcome,
                out PolicyBoundaryDivergenceArmOutcome forcedNullOutcome))
            return;
        if (divergenceProvenance is LoopClosureR4Provenance provenance)
        {
            LoopClosureDigest forkArmDigest = DigestExecutionArms(in boundaryReceipt);
            LoopClosureDigest childExecutionDigest = DigestExecutedDivergenceChildExecution(
                forcedNullOutcome.DecisionID, forcedNullOutcome.OutcomeID);
            PaidDivergenceExecutionCorroboration executionCorroboration = PaidDivergenceExecutionCorroboration.Create(
                provenance.Training.ReadoutTrainingCorroborationSHA256,
                funding.QuotaDecisionID,
                funding.ReadoutFingerprint,
                funding.CandidateFingerprint,
                funding.CandidateRevision,
                forkArmDigest,
                childExecutionDigest,
                forcedNullOutcome.DecisionID,
                forcedNullOutcome.OutcomeID,
                forcedNullOutcome.ExecutedOutcomeEventID,
                forcedNullOutcome.ExecutedOutcomePayloadSHA256);
            activeObligation.AttachExecutionCorroboration(in boundaryReceipt, in executionCorroboration, domain);
            if (activeObligation.StagedReceipt is not PolicyBoundaryForkReceipt settledReceipt)
                return;
            boundaryReceipt = settledReceipt;
            PolicyBoundaryTeacherCorroboration teacher = new(in provenance);
            if (TryAdjudicatePaidDivergence(in sourceDecision, in readout, in funding, in settlement,
                in boundaryReceipt, in candidateOutcome, in forcedNullOutcome, teacher, out PolicyBoundaryDivergenceAdjudication adjudication, provenance))
            {
                if (TryGrantPolicyBoundary(HomeostatPolicyBoundaryDomain.Instance.PolicyID, in boundaryReceipt))
                {
                    WriteLoopClosureDivergenceProof(in adjudication);
                    CloseLoopClosureAdjudication(in adjudication);
                    RecordVerifiedPolicyReadout(HomeostatPolicyBoundaryDomain.Instance.PolicyID, readoutFingerprint, candidateFingerprint, readout.Revision);
                    _ = TryGrantVerifiedPolicySuccession(HomeostatPolicyBoundaryDomain.Instance.PolicyID, readoutFingerprint, candidateFingerprint, readout.Revision);
                }
                else
                {
                    activeObligation.DiscardStagedExecutionCorroboration();
                }
            }
            else
            {
                activeObligation.DiscardStagedExecutionCorroboration();
            }
        }
        else
        {
            if (TryAdjudicatePaidDivergence(in sourceDecision, in readout, in funding, in settlement,
                in boundaryReceipt, in candidateOutcome, in forcedNullOutcome, null, out _))
            {
                if (TryGrantPolicyBoundary(HomeostatPolicyBoundaryDomain.Instance.PolicyID, in boundaryReceipt))
                {
                    RecordVerifiedPolicyReadout(HomeostatPolicyBoundaryDomain.Instance.PolicyID, readoutFingerprint, candidateFingerprint, readout.Revision);
                    _ = TryGrantVerifiedPolicySuccession(HomeostatPolicyBoundaryDomain.Instance.PolicyID, readoutFingerprint, candidateFingerprint, readout.Revision);
                }
            }
        }
    }

    private enum RecoveredHomeostatBoundaryAttachmentOutcomes
    {
        Attached,
        R4Unavailable,
        DivergenceAdjudicationRejected,
        BoundaryGrantRejected,
        SuccessionRejected,
        InvalidObligation,
        InvalidRuntime,
        InvalidSeedCustody,
        InvalidCanonicalState,
        InvalidSourceCorroboration,
        InvalidSourceDecision,
        InvalidReceiptIdentity,
        InvalidSettlement,
        InvalidAccountingBasis,
        InvalidDivergenceArms,
        InvalidExecutionCorroboration,
    }

    private void TraceRecoveredHomeostatBoundaryAttachmentInvalid(
        RecoveredHomeostatBoundaryAttachmentOutcomes reason,
        in CortexPolicyTrialQuotaDecision funding,
        in CortexPolicyTrialCompletion settlement,
        in PolicyBoundaryForkReceipt receipt,
        PolicyBoundarySeedCustody? custody = null,
        PolicyCanonicalStateID custodyState = default,
        CortexPolicyDecisionReadout restoredReadout = default,
        CortexPolicyReadoutReceipt recoveredReadout = default,
        string r4Result = "not-attempted",
        string teacher = "not-attempted",
        string executionCorroboration = "not-attempted",
        string adjudication = "not-attempted")
    {
        StringBuilder snapshot = new();
        snapshot.Append("reason=").Append(reason)
            .Append(" step=").Append(Step)
            .Append(" funding=").Append(funding.QuotaDecisionID)
            .Append(" funding_policy=").Append(funding.Policy)
            .Append(" funding_candidate=").Append(funding.CandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture))
            .Append(" funding_readout=").Append(funding.ReadoutFingerprint.ToString("X16", CultureInfo.InvariantCulture))
            .Append(" funding_revision=").Append(funding.CandidateRevision.Value)
            .Append(" settlement=").Append(settlement.QuotaDecisionID)
            .Append(" settlement_verifier=").Append(settlement.VerifierOutcome)
            .Append(" settlement_actual=").Append(settlement.ActualExecutedArmSteps)
            .Append(" settlement_refund=").Append(settlement.ReclaimedOrUnused)
            .Append(" settlement_planned=").Append(funding.PlannedArmSteps)
            .Append(" receipt_funding=").Append(receipt.QuotaDecisionID)
            .Append(" receipt_obligation=").Append(receipt.Obligation)
            .Append(" receipt_ladder_spend=").Append(TryComputeReceiptSpend(receipt, terminal: false))
            .Append(" receipt_terminal_spend=").Append(TryComputeReceiptSpend(receipt, terminal: true))
            .Append(" receipt_horizons=").Append(receipt.Horizons is null ? "none" : string.Join(',', receipt.Horizons));
        if (custody is PolicyBoundarySeedCustody seed)
        {
            snapshot.Append(" custody_source_decision=").Append(seed.sourceDecisionID)
                .Append(" custody_source_event=").Append(seed.sourceDecisionEventID)
                .Append(" custody_source_corroboration=").Append(seed.sourceCorroborationEventID)
                .Append(" custody_source_cause=").Append(seed.sourceSelectionCause)
                .Append(" custody_revision=").Append(seed.candidateRevision)
                .Append(" custody_support=").Append(seed.sourceSupportDigest.ToString("X16", CultureInfo.InvariantCulture))
                .Append(" custody_candidate=").Append(seed.sourceCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture))
                .Append(" custody_readout=").Append(seed.readoutFingerprint.ToString("X16", CultureInfo.InvariantCulture))
                .Append(" custody_state=").Append(seed.canonicalState)
                .Append(" custody_decoded_state=").Append(EncodeCanonicalState(in custodyState));
        }
        snapshot.Append(" restored_readout=revision:").Append(restoredReadout.GrammarRevision.Value)
            .Append(",launchpad:").Append(restoredReadout.LaunchpadAction)
            .Append(",raw:").Append(restoredReadout.RawCandidateAction)
            .Append(",selected:").Append(restoredReadout.SelectedCandidateAction)
            .Append(",executed:").Append(restoredReadout.ExecutedAction)
            .Append(",authority:").Append(restoredReadout.Authority)
            .Append(",cause:").Append(restoredReadout.SelectionCause)
            .Append(",support:").Append(restoredReadout.ReadoutCandidateOccurrenceDigest.ToString("X16", CultureInfo.InvariantCulture))
            .Append(",candidate:").Append(restoredReadout.ReadoutCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture))
            .Append(" recovered_readout=revision:").Append(recoveredReadout.Revision.Value)
            .Append(",fingerprint:").Append(recoveredReadout.Fingerprint.ToString("X16", CultureInfo.InvariantCulture))
            .Append(",support:").Append(recoveredReadout.ReadoutCandidateOccurrenceDigest.ToString("X16", CultureInfo.InvariantCulture))
            .Append(",candidate:").Append(recoveredReadout.ReadoutCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture))
            .Append(",cached:").Append(recoveredReadout.CachedContexts)
            .Append(",comparisons:").Append(recoveredReadout.Comparisons)
            .Append(",agreements:").Append(recoveredReadout.Agreements)
            .Append(",misses:").Append(recoveredReadout.Misses)
            .Append(",state:").Append(EncodeCanonicalState(recoveredReadout.CanonicalState))
            .Append(" r4=").Append(r4Result)
            .Append(" teacher=").Append(teacher)
            .Append(" execution_witness=").Append(executionCorroboration)
            .Append(" adjudication=").Append(adjudication);
        if (receipt.Arms is not null && receipt.Horizons is { Length: > 0 })
        {
            int terminalHorizon = receipt.Horizons[^1];
            snapshot.Append(" terminal_arms=");
            for (int index = 0; index < receipt.Arms.Length; index++)
            {
                PolicyBoundaryArmReceipt arm = receipt.Arms[index];
                if (arm.Horizon != terminalHorizon) continue;
                if (index != 0) snapshot.Append(';');
                snapshot.Append(arm.Arm).Append("{decision=").Append(arm.ExecutedDecisionID)
                    .Append(",launchpad=").Append(arm.ExecutedLaunchpadAction)
                    .Append(",raw=").Append(arm.ExecutedRawCandidateAction)
                    .Append(",selected=").Append(arm.ExecutedSelectedCandidateAction)
                    .Append(",executed=").Append(arm.ExecutedAction)
                    .Append(",authority=").Append(arm.ExecutedAuthority)
                    .Append(",cause=").Append(arm.ExecutedSelectionCause)
                    .Append(",event=").Append(arm.ExecutedDecisionEventID)
                    .Append(",seed=").Append(arm.ForcedDivergenceSeed)
                    .Append(",behavior=").Append(arm.BehaviorallyExecuted ? 1 : 0)
                    .Append(",diverged=").Append(arm.Diverged ? 1 : 0).Append('}');
            }
        }
        Trace.Cortex.Boundary("policy.trial-recovered-attachment-invalid", snapshot.ToString());
    }

    private static long TryComputeReceiptSpend(in PolicyBoundaryForkReceipt receipt, bool terminal)
    {
        try { return terminal ? receipt.ComputeTerminalMatchedSpend() : receipt.ComputeLadderMatchedSpend(); }
        catch (Exception error) when (error is InvalidDataException or OverflowException) { return -1; }
    }

    private static string ComputeRecoveredAttachmentReceiptIdentity(in PolicyBoundaryForkReceipt receipt)
    {
        PolicyBoundaryForkReceipt identity = receipt with { TeacherCorroboration = null, ExecutionCorroboration = null };
        return PolicyBoundaryObligation.ComputeReceiptDigest(in identity);
    }

    private RecoveredHomeostatBoundaryAttachmentOutcomes RejectRecoveredHomeostatBoundaryAttachment(
        RecoveredHomeostatBoundaryAttachmentOutcomes reason,
        in CortexPolicyTrialQuotaDecision funding,
        in CortexPolicyTrialCompletion settlement,
        in PolicyBoundaryForkReceipt receipt,
        PolicyBoundarySeedCustody? custody = null,
        PolicyCanonicalStateID custodyState = default,
        CortexPolicyDecisionReadout restoredReadout = default,
        CortexPolicyReadoutReceipt recoveredReadout = default,
        string r4Result = "not-attempted",
        string teacher = "not-attempted",
        string executionCorroboration = "not-attempted",
        string adjudication = "not-attempted")
    {
        TraceRecoveredHomeostatBoundaryAttachmentInvalid(reason, in funding, in settlement, in receipt,
            custody, custodyState, restoredReadout, recoveredReadout, r4Result,
            teacher, executionCorroboration, adjudication);
        return reason;
    }

    private RecoveredHomeostatBoundaryAttachmentOutcomes AttachRecoveredHomeostatBoundaryTrial(
        in CortexPolicyTrialQuotaDecision funding,
        in CortexPolicyTrialCompletion settlement,
        in PolicyBoundaryForkReceipt receipt,
        LoopClosureR4Provenance? fixtureProvenance = null)
    {
        IPolicyBoundaryDomain domain = RequirePolicyBoundaryDomain(HomeostatPolicyBoundaryDomain.Instance.PolicyID);
        if (!_policyBoundaryObligations.TryGetValue(HomeostatPolicyBoundaryDomain.Instance.PolicyID, out PolicyBoundaryObligation? obligation))
            return RejectRecoveredHomeostatBoundaryAttachment(RecoveredHomeostatBoundaryAttachmentOutcomes.InvalidObligation,
                in funding, in settlement, in receipt);
        bool alreadyAttached = false;
        if (obligation.Receipt is PolicyBoundaryForkReceipt existingReceipt)
        {
            if (ComputeRecoveredAttachmentReceiptIdentity(in existingReceipt)
                == ComputeRecoveredAttachmentReceiptIdentity(in receipt)
                && existingReceipt.QuotaDecisionID.Equals(funding.QuotaDecisionID))
                alreadyAttached = true;
            else
                return RejectRecoveredHomeostatBoundaryAttachment(RecoveredHomeostatBoundaryAttachmentOutcomes.InvalidObligation,
                    in funding, in settlement, in receipt);
        }
        if (_runtimeHomeostat is null)
            return RejectRecoveredHomeostatBoundaryAttachment(RecoveredHomeostatBoundaryAttachmentOutcomes.InvalidRuntime,
                in funding, in settlement, in receipt);
        if (!TryReadPolicyBoundarySeedCustodyDocument(CurrentRun.Dir, Path.GetFileName(CurrentRun.Dir),
                funding.QuotaDecisionID.ToString(), out PolicyBoundarySeedCustody custody))
            return RejectRecoveredHomeostatBoundaryAttachment(RecoveredHomeostatBoundaryAttachmentOutcomes.InvalidSeedCustody,
                in funding, in settlement, in receipt);
        if (!TryDecodeCanonicalState(custody.canonicalState, out PolicyCanonicalStateID custodyState))
            return RejectRecoveredHomeostatBoundaryAttachment(RecoveredHomeostatBoundaryAttachmentOutcomes.InvalidCanonicalState,
                in funding, in settlement, in receipt, custody);
        bool sourceCorroborationValid = TryReadBoundarySourceCorroboration(custody.sourceDecisionID, custody.sourceDecisionEventID,
            funding.CandidateRevision, funding.ReadoutFingerprint, custody.sourceCandidateFingerprint,
            custody.sourceSupportDigest, out TapeEventID recoveredCorroborationEventID,
            out CortexPolicySelectionCauses recoveredSourceCause, custodyState)
            && recoveredCorroborationEventID.Value == custody.sourceCorroborationEventID
            && recoveredSourceCause == custody.sourceSelectionCause;
        if (!sourceCorroborationValid)
            return RejectRecoveredHomeostatBoundaryAttachment(RecoveredHomeostatBoundaryAttachmentOutcomes.InvalidSourceCorroboration,
                in funding, in settlement, in receipt, custody, custodyState);
        if (!TryReadPolicyDecisionIdentityEvent(new TapeEventID(custody.sourceDecisionEventID),
                new CortexPolicyDecisionID(custody.sourceDecisionID), out CortexPolicyDecisionReadout restoredReadout))
            return RejectRecoveredHomeostatBoundaryAttachment(RecoveredHomeostatBoundaryAttachmentOutcomes.InvalidSourceDecision,
                in funding, in settlement, in receipt, custody, custodyState);
        CortexPolicyDecision sourceDecision = new(new CortexPolicyDecisionID(custody.sourceDecisionID), HomeostatPolicyBoundaryDomain.Instance.PolicyID, restoredReadout);
        CortexPolicyReadoutReceipt readout = new(funding.CandidateRevision,
            funding.ReadoutFingerprint, custody.readoutCachedContexts,
            custody.readoutComparisons, custody.readoutAgreements,
            custody.readoutMisses,
            custody.sourceSupportDigest, custody.sourceCandidateFingerprint,
            CanonicalState: custodyState);
        if (custody.schemaVersion < 7
            || !TryReadBoundarySourceCorroborationReceipt(custody.sourceDecisionID, custody.sourceDecisionEventID,
                funding.CandidateRevision, funding.ReadoutFingerprint, custody.sourceCandidateFingerprint,
                custody.sourceSupportDigest, out CortexPolicyBoundarySourceCorroboration sourceCorroboration,
                out TapeEventID _, custodyState)
            || !sourceCorroboration.Matches(in readout))
            return RejectRecoveredHomeostatBoundaryAttachment(RecoveredHomeostatBoundaryAttachmentOutcomes.InvalidSourceCorroboration,
                in funding, in settlement, in receipt, custody, custodyState, restoredReadout, readout);
        if (alreadyAttached)
            return RecoveredHomeostatBoundaryAttachmentOutcomes.Attached;
        if (!receipt.QuotaDecisionID.Equals(funding.QuotaDecisionID)
            || receipt.Obligation != obligation.ID)
            return RejectRecoveredHomeostatBoundaryAttachment(RecoveredHomeostatBoundaryAttachmentOutcomes.InvalidReceiptIdentity,
                in funding, in settlement, in receipt, custody, custodyState, restoredReadout, readout);
        if (!settlement.QuotaDecisionID.Equals(funding.QuotaDecisionID)
            || settlement.VerifierOutcome != CortexPolicyVerifierOutcomes.Passed
            || settlement.ActualExecutedArmSteps + settlement.ReclaimedOrUnused != funding.PlannedArmSteps)
            return RejectRecoveredHomeostatBoundaryAttachment(RecoveredHomeostatBoundaryAttachmentOutcomes.InvalidSettlement,
                in funding, in settlement, in receipt, custody, custodyState, restoredReadout, readout);
        long terminalMatchedSpend;
        try { terminalMatchedSpend = receipt.ComputeTerminalMatchedSpend(); }
        catch (InvalidDataException)
        {
            return RejectRecoveredHomeostatBoundaryAttachment(RecoveredHomeostatBoundaryAttachmentOutcomes.InvalidAccountingBasis,
                in funding, in settlement, in receipt, custody, custodyState, restoredReadout, readout);
        }
        if (terminalMatchedSpend != settlement.ActualExecutedArmSteps)
            return RejectRecoveredHomeostatBoundaryAttachment(RecoveredHomeostatBoundaryAttachmentOutcomes.InvalidAccountingBasis,
                in funding, in settlement, in receipt, custody, custodyState, restoredReadout, readout);
        if (!TryReadHomeostatDivergenceArms(in sourceDecision, in receipt,
                out PolicyBoundaryDivergenceCandidateTerminal candidateOutcome,
                out PolicyBoundaryDivergenceArmOutcome forcedNullOutcome))
            return RejectRecoveredHomeostatBoundaryAttachment(RecoveredHomeostatBoundaryAttachmentOutcomes.InvalidDivergenceArms,
                in funding, in settlement, in receipt, custody, custodyState, restoredReadout, readout);
        GrammarPolicyContextKey recoveredContext = new(
            custodyState, HomeostatPolicyBoundaryDomain.Instance.Schema.ActionCount, _config.Learning.Policies.ReadoutDeliberationQuota);
        LoopClosureR4Provenance provenance;
        if (fixtureProvenance is LoopClosureR4Provenance suppliedProvenance)
            provenance = suppliedProvenance;
        else if (!domain.ValidateCanonicalState(in custodyState)
            || !sourceDecision.Policy.Equals(domain.PolicyID)
            || !domain.TryVerifyR4(this, in sourceDecision, out TapeEventID verifiedDecisionEventID, out provenance)
            || verifiedDecisionEventID.Value != custody.sourceDecisionEventID)
            return RejectRecoveredHomeostatBoundaryAttachment(RecoveredHomeostatBoundaryAttachmentOutcomes.R4Unavailable,
                in funding, in settlement, in receipt, custody, custodyState, restoredReadout, readout,
                "rejected", "unavailable");
        PolicyBoundaryTeacherCorroboration teacher = new(in provenance);
        LoopClosureDigest forkArmDigest = DigestExecutionArms(in receipt);
        LoopClosureDigest childExecutionDigest = DigestExecutedDivergenceChildExecution(
            forcedNullOutcome.DecisionID, forcedNullOutcome.OutcomeID);
        PaidDivergenceExecutionCorroboration executionCorroboration = PaidDivergenceExecutionCorroboration.Create(
            provenance.Training.ReadoutTrainingCorroborationSHA256,
            funding.QuotaDecisionID,
            funding.ReadoutFingerprint,
            funding.CandidateFingerprint,
            funding.CandidateRevision,
            forkArmDigest,
            childExecutionDigest,
            forcedNullOutcome.DecisionID,
            forcedNullOutcome.OutcomeID,
            forcedNullOutcome.ExecutedOutcomeEventID,
            forcedNullOutcome.ExecutedOutcomePayloadSHA256);
        obligation.AttachExecutionCorroboration(in receipt, in executionCorroboration, domain);
        if (obligation.StagedReceipt is not PolicyBoundaryForkReceipt settledReceipt)
            return RejectRecoveredHomeostatBoundaryAttachment(RecoveredHomeostatBoundaryAttachmentOutcomes.InvalidExecutionCorroboration,
                in funding, in settlement, in receipt, custody, custodyState, restoredReadout, readout,
                "passed", "constructed", "missing-staged-receipt");
        PolicyBoundaryForkReceipt workingReceipt = settledReceipt;
        if (!TryAdjudicatePaidDivergence(in sourceDecision, in readout, in funding, in settlement,
            in workingReceipt, in candidateOutcome, in forcedNullOutcome, teacher, out PolicyBoundaryDivergenceAdjudication adjudication, provenance))
        {
            obligation.DiscardStagedExecutionCorroboration();
            return RejectRecoveredHomeostatBoundaryAttachment(RecoveredHomeostatBoundaryAttachmentOutcomes.DivergenceAdjudicationRejected,
                in funding, in settlement, in receipt, custody, custodyState, restoredReadout, readout,
                "passed", "constructed", "staged", "rejected");
        }
        if (!TryGrantPolicyBoundary(HomeostatPolicyBoundaryDomain.Instance.PolicyID, in workingReceipt))
        {
            obligation.DiscardStagedExecutionCorroboration();
            return RejectRecoveredHomeostatBoundaryAttachment(RecoveredHomeostatBoundaryAttachmentOutcomes.BoundaryGrantRejected,
                in funding, in settlement, in receipt, custody, custodyState, restoredReadout, readout,
                "passed", "constructed", "staged", "passed");
        }
        WriteLoopClosureDivergenceProof(in adjudication);
        CloseLoopClosureAdjudication(in adjudication);
        RecordVerifiedPolicyReadout(HomeostatPolicyBoundaryDomain.Instance.PolicyID, readout.Fingerprint, readout.ReadoutCandidateFingerprint, readout.Revision);
        return TryGrantVerifiedPolicySuccession(HomeostatPolicyBoundaryDomain.Instance.PolicyID, readout.Fingerprint,
            readout.ReadoutCandidateFingerprint, readout.Revision)
            ? RecoveredHomeostatBoundaryAttachmentOutcomes.Attached
            : RejectRecoveredHomeostatBoundaryAttachment(RecoveredHomeostatBoundaryAttachmentOutcomes.SuccessionRejected,
                in funding, in settlement, in receipt, custody, custodyState, restoredReadout, readout,
                "passed", "constructed", "staged", "passed");
    }

    private bool TryEmitPolicyBoundarySourceCorroboration(
        in CortexPolicyReadoutReceipt readout,
        in CortexPolicyDecision sourceDecision,
        out CortexPolicyBoundarySourceCorroboration corroboration,
        out TapeEventID corroborationEventID)
        => TryEmitPolicyBoundarySourceCorroborationForDomain(
            HomeostatPolicyBoundaryDomain.Instance, in readout, in sourceDecision,
            out corroboration, out corroborationEventID);

    internal bool TryEmitPolicyBoundarySourceCorroborationForDomain(
        IPolicyBoundaryDomain domain,
        in CortexPolicyReadoutReceipt readout,
        in CortexPolicyDecision sourceDecision,
        out CortexPolicyBoundarySourceCorroboration corroboration,
        out TapeEventID corroborationEventID)
    {
        corroboration = default;
        corroborationEventID = default;
        PolicyCanonicalStateID readoutState = readout.CanonicalState;
        if (_runtimeTape is null || _runtimeJournal is null
            || sourceDecision.DecisionID.Value == 0 || !sourceDecision.Policy.Equals(domain.PolicyID)
            || !domain.ValidateCanonicalState(in readoutState)) return false;
        TapeEventID sourceEventID = FindPolicyDecisionEvent(sourceDecision.DecisionID);
        if (sourceEventID.Value <= 0
            || !_runtimeTape.TryGetEventView(sourceEventID, out TapeEventView sourceView)
            || !string.Equals(sourceView.Source, "policy:" + domain.PolicyID.Value, StringComparison.Ordinal)
            || !_runtimeTape.Resolve(sourceEventID, out byte[] sourcePayload)) return false;
        CortexPolicyDecisionPacket sourcePacket;
        try { sourcePacket = TapePacketCreator.DecodePolicyDecision(sourcePayload); }
        catch (InvalidDataException) { return false; }
        if (!sourcePacket.DecisionID.Equals(sourceDecision.DecisionID)
            || sourcePacket.Readout.Authority != sourceDecision.Authority
            || sourcePacket.Readout.SelectionCause != sourceDecision.SelectionCause
            || sourcePacket.Readout.GrammarRevision == GrammarRevisionID.Zero)
            return false;
        if (TryReadBoundarySourceCorroborationReceipt(
                sourceDecision.DecisionID.Value, sourceEventID.Value,
                readout.Revision, readout.Fingerprint,
                readout.ReadoutCandidateFingerprint, readout.ReadoutCandidateOccurrenceDigest,
                out corroboration, out corroborationEventID, readout.CanonicalState, domain))
            return true;
        corroboration = new CortexPolicyBoundarySourceCorroboration(
            domain.PolicyID, sourceDecision.DecisionID, sourceEventID,
            sourceDecision.Authority, sourceDecision.SelectionCause,
            readout.Revision, readout.Fingerprint, readout.ReadoutCandidateFingerprint,
            readout.ReadoutCandidateOccurrenceDigest, "", readout.CachedContexts,
            readout.Comparisons, readout.Agreements, readout.Misses)
        { CanonicalState = readout.CanonicalState };
        corroboration = corroboration with { CorroborationDigest = corroboration.ComputeDigest() };
        corroborationEventID = TapePacketCreator.AppendPolicyBoundarySourceCorroboration(_runtimeTape, _runtimeJournal, Step, in corroboration);
        return corroborationEventID.Value > 0;
    }

    private bool IsLiveHomeostatBoundaryDecision(
        in CortexPolicyDecision decision,
        in CortexPolicyReadoutReceipt readout)
    {
        PolicyCanonicalStateID canonicalState = readout.CanonicalState;
        return decision.DecisionID.Value != 0
            && decision.Policy.Equals(HomeostatPolicyBoundaryDomain.Instance.PolicyID)
            && decision.Readout.GrammarRevision == readout.Revision
            && decision.Readout.ReadoutCandidateFingerprint == readout.ReadoutCandidateFingerprint
            && decision.Readout.ReadoutCandidateOccurrenceDigest == readout.ReadoutCandidateOccurrenceDigest
            && IsVerifiedPolicyScope(
                HomeostatPolicyBoundaryDomain.Instance.PolicyID, in canonicalState, readout.Fingerprint,
                readout.ReadoutCandidateFingerprint, readout.ReadoutCandidateOccurrenceDigest,
                readout.Revision);
    }

    private bool TryReadBoundarySourceCorroboration(
        ulong sourceDecisionID,
        long sourceDecisionEventID,
        GrammarRevisionID expectedRevision,
        ulong expectedReadoutFingerprint,
        ulong expectedCandidateFingerprint,
        ulong expectedSupportDigest,
        out TapeEventID corroborationEventID,
        out CortexPolicySelectionCauses sourceSelectionCause,
        PolicyCanonicalStateID expectedCanonicalState = default,
        IPolicyBoundaryDomain? expectedDomain = null)
    {
        bool found = TryReadBoundarySourceCorroborationReceipt(sourceDecisionID, sourceDecisionEventID,
            expectedRevision, expectedReadoutFingerprint, expectedCandidateFingerprint,
            expectedSupportDigest, out CortexPolicyBoundarySourceCorroboration corroboration,
            out corroborationEventID, expectedCanonicalState, expectedDomain);
        sourceSelectionCause = found ? corroboration.SourceSelectionCause : default;
        return found;
    }

    private bool TryReadBoundarySourceCorroborationReceipt(
        ulong sourceDecisionID,
        long sourceDecisionEventID,
        GrammarRevisionID expectedRevision,
        ulong expectedReadoutFingerprint,
        ulong expectedCandidateFingerprint,
        ulong expectedSupportDigest,
        out CortexPolicyBoundarySourceCorroboration sourceCorroboration,
        out TapeEventID corroborationEventID,
        PolicyCanonicalStateID expectedCanonicalState = default,
        IPolicyBoundaryDomain? expectedDomain = null)
    {
        sourceCorroboration = default;
        corroborationEventID = default;
        if (_runtimeTape is null || sourceDecisionID == 0 || sourceDecisionEventID <= 0
            || expectedRevision == GrammarRevisionID.Zero || expectedReadoutFingerprint == 0
            || expectedCandidateFingerprint == 0 || expectedSupportDigest == 0) return false;
        EnsureBoundarySourceCorroborationIndex();
        BoundarySourceCorroborationKey key = new(sourceDecisionID, sourceDecisionEventID,
            expectedRevision, expectedReadoutFingerprint, expectedCandidateFingerprint, expectedSupportDigest);
        if (!_boundarySourceCorroborationIndex.TryGetValue(key, out List<(TapeEventID EventID, CortexPolicyBoundarySourceCorroboration Corroboration)>? corroborationes))
            return false;
        foreach ((TapeEventID eventID, CortexPolicyBoundarySourceCorroboration corroboration) in corroborationes)
        {
            if (expectedCanonicalState.Version != 0 && corroboration.CanonicalState != expectedCanonicalState) continue;
            IPolicyBoundaryDomain corroborationDomain = expectedDomain
                ?? (_policyBoundaryDomains.TryGetValue(corroboration.Policy, out IPolicyBoundaryDomain registeredDomain)
                    ? registeredDomain : HomeostatPolicyBoundaryDomain.Instance);
            PolicyCanonicalStateID corroborationState = corroboration.CanonicalState;
            if (!corroboration.Policy.Equals(corroborationDomain.PolicyID)
                || !corroborationDomain.ValidateCanonicalState(in corroborationState)
                || !_runtimeTape.TryGetEventView(corroboration.SourceDecisionEventID, out TapeEventView sourceView)
                || !string.Equals(sourceView.Source, "policy:" + corroborationDomain.PolicyID.Value, StringComparison.Ordinal)
                || sourceView.Provenance != Provenances.Execution
                || !_runtimeTape.Resolve(corroboration.SourceDecisionEventID, out byte[] sourcePayload)) return false;
            CortexPolicyDecisionPacket sourcePacket;
            try { sourcePacket = TapePacketCreator.DecodePolicyDecision(sourcePayload); }
            catch (InvalidDataException) { return false; }
            if (!sourcePacket.DecisionID.Equals(corroboration.SourceDecisionID)
                || sourcePacket.Readout.GrammarRevision != expectedRevision
                || sourcePacket.Readout.ReadoutCandidateFingerprint != expectedCandidateFingerprint
                || sourcePacket.Readout.ReadoutCandidateOccurrenceDigest != expectedSupportDigest
                || sourcePacket.Readout.Authority != corroboration.SourceAuthority
                || sourcePacket.Readout.SelectionCause != corroboration.SourceSelectionCause)
                return false;
            sourceCorroboration = corroboration;
            corroborationEventID = eventID;
            return true;
        }
        return false;
    }

    private void EnsureBoundarySourceCorroborationIndex()
    {
        if (!ReferenceEquals(_boundarySourceCorroborationIndexTape, _runtimeTape))
        {
            _boundarySourceCorroborationIndexTape = _runtimeTape;
            _boundarySourceCorroborationIndex.Clear();
            _boundarySourceCorroborationIndexRevision = new TapeRevision(-1);
            _boundarySourceCorroborationIndexNextID = 0;
        }
        if (_runtimeTape is null || _runtimeTape.Revision == _boundarySourceCorroborationIndexRevision) return;
        for (long id = _boundarySourceCorroborationIndexNextID; id < _runtimeTape.NextId; id++)
        {
            TapeEventID eventID = new(id);
            if (!_runtimeTape.TryGetEventView(eventID, out TapeEventView view)) continue;
            if (!string.Equals(view.Source, "policy-boundary:source", StringComparison.Ordinal)
                || view.Provenance != Provenances.Execution) continue;
            if (!_runtimeTape.Resolve(eventID, out byte[] payload)
                || !TapePacketCreator.TryReadPolicyBoundarySourceCorroboration(payload, out CortexPolicyBoundarySourceCorroboration corroboration)
                || !_policyBoundaryDomains.ContainsKey(corroboration.Policy))
                throw new InvalidDataException($"policy boundary source corroboration {eventID} is malformed or names the wrong policy");
            BoundarySourceCorroborationKey key = new(corroboration.SourceDecisionID.Value,
                corroboration.SourceDecisionEventID.Value, corroboration.ReadoutRevision,
                corroboration.ReadoutFingerprint, corroboration.CandidateFingerprint, corroboration.OccurrenceDigest);
            if (!_boundarySourceCorroborationIndex.TryGetValue(key, out List<(TapeEventID EventID, CortexPolicyBoundarySourceCorroboration Corroboration)>? entries))
                _boundarySourceCorroborationIndex[key] = entries = new();
            entries.Add((eventID, corroboration));
        }
        _boundarySourceCorroborationIndexNextID = _runtimeTape.NextId;
        _boundarySourceCorroborationIndexRevision = _runtimeTape.Revision;
    }

    /// Evaluate one pending forced-intent rearm opportunity.  This is deliberately
    /// a typed verdict: every gate names the exact missing join, so a preserved
    /// forced child can be diagnosed without reconstructing compound booleans.
    private CortexPolicyPendingForcedTrialRearmEvaluation EvaluatePendingForcedTrialRearm(
        CortexPolicyID policy,
        in CortexPolicyPendingForcedTrialIntent pending,
        bool hasCanonicalState,
        in PolicyCanonicalStateID canonicalState,
        ulong readoutFingerprint,
        ulong candidateFingerprint,
        ulong supportDigest,
        GrammarRevisionID candidateRevision)
    {
        PolicyState state = GetPolicy(policy);
        if (!_policyBoundaryDomains.TryGetValue(policy, out IPolicyBoundaryDomain domain))
            return CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
                CortexPolicyPendingForcedTrialRearmDenialSpecies.BoundaryObligationMismatch, in pending,
                candidateFingerprint, candidateRevision);
        if (!pending.IsBound) return CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
            CortexPolicyPendingForcedTrialRearmDenialSpecies.IntentNotBound, in pending,
            candidateFingerprint, candidateRevision);
        if (!hasCanonicalState || !domain.ValidateCanonicalState(in canonicalState)) return CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
            CortexPolicyPendingForcedTrialRearmDenialSpecies.CanonicalStateMissing, in pending,
            candidateFingerprint, candidateRevision);
        if (state.ReadoutCandidateFingerprint != candidateFingerprint)
            return CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
                CortexPolicyPendingForcedTrialRearmDenialSpecies.CurrentCandidateMismatch, in pending,
                candidateFingerprint, candidateRevision);
        if (state.ReadoutCandidateRevision != candidateRevision)
            return CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
                CortexPolicyPendingForcedTrialRearmDenialSpecies.CurrentRevisionMismatch, in pending,
                candidateFingerprint, candidateRevision);
        if (!IsPolicyReadoutReady(policy, readoutFingerprint))
            return CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
                CortexPolicyPendingForcedTrialRearmDenialSpecies.ReadoutNotReady, in pending,
                candidateFingerprint, candidateRevision);
        if (!IsVerifiedPolicyScope(policy, in canonicalState, readoutFingerprint,
                candidateFingerprint, supportDigest, candidateRevision))
            return CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
                CortexPolicyPendingForcedTrialRearmDenialSpecies.CanonicalScopeMissing, in pending,
                candidateFingerprint, candidateRevision);
        if (pending.CanonicalState != canonicalState)
            return CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
                CortexPolicyPendingForcedTrialRearmDenialSpecies.SuccessorCanonicalStateMismatch, in pending,
                candidateFingerprint, candidateRevision);
        if (pending.ReadoutFingerprint != readoutFingerprint)
            return CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
                CortexPolicyPendingForcedTrialRearmDenialSpecies.SuccessorReadoutMismatch, in pending,
                candidateFingerprint, candidateRevision);
        if (pending.CandidateFingerprint != candidateFingerprint)
            return CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
                CortexPolicyPendingForcedTrialRearmDenialSpecies.CurrentCandidateMismatch, in pending,
                candidateFingerprint, candidateRevision);
        if (pending.CandidateRevision != candidateRevision)
            return CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
                CortexPolicyPendingForcedTrialRearmDenialSpecies.CurrentRevisionMismatch, in pending,
                candidateFingerprint, candidateRevision);
        if (pending.SuccessorOccurrenceDigest != supportDigest)
            return CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
                CortexPolicyPendingForcedTrialRearmDenialSpecies.SuccessorOccurrenceMismatch, in pending,
                candidateFingerprint, candidateRevision);
        if (readoutFingerprint == 0 || candidateFingerprint == 0 || supportDigest == 0
            || candidateRevision == GrammarRevisionID.Zero)
            return CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
                CortexPolicyPendingForcedTrialRearmDenialSpecies.SourceAuditOnlyMismatch, in pending,
                candidateFingerprint, candidateRevision);
        if (pending.Arm != (byte)PolicyBoundaryArms.ForcedDivergentNull)
            return CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
                CortexPolicyPendingForcedTrialRearmDenialSpecies.BoundaryArmMismatch, in pending,
                candidateFingerprint, candidateRevision);
        if (pending.FeatureID != domain.BoundaryFeatureID
            ) return CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
                CortexPolicyPendingForcedTrialRearmDenialSpecies.BoundaryFeatureMismatch, in pending,
                candidateFingerprint, candidateRevision);
        if (!_policyBoundaryObligations.TryGetValue(policy, out PolicyBoundaryObligation? obligation)
            || !string.Equals(obligation.ID.Value, pending.ObligationID, StringComparison.Ordinal))
            return CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
                CortexPolicyPendingForcedTrialRearmDenialSpecies.BoundaryObligationMismatch, in pending,
                candidateFingerprint, candidateRevision);
        if (!IsAuthenticatedAuditOnlyDigest(pending.AuditOnlyDigest))
            return CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
                CortexPolicyPendingForcedTrialRearmDenialSpecies.AuditOnlyDigestInvalid, in pending,
                candidateFingerprint, candidateRevision);
        string parentDirectory = Path.GetFullPath(Path.Combine(CurrentRun.Dir, "..", ".."));
        string custodyPath = Path.Combine(parentDirectory, PolicyBoundarySeedCustodyDirectory,
            new CortexPolicyQuotaDecisionID(pending.QuotaID).ToString(), PolicyBoundarySeedCustodyFile);
        if (!File.Exists(custodyPath))
            return CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
                CortexPolicyPendingForcedTrialRearmDenialSpecies.AuditOnlySidecarMissing, in pending,
                candidateFingerprint, candidateRevision);
        if (!TryReadPolicyBoundarySeedCustodyDocument(parentDirectory, pending.SourceRunID,
            new CortexPolicyQuotaDecisionID(pending.QuotaID).ToString(), out PolicyBoundarySeedCustody custody))
            return CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
                CortexPolicyPendingForcedTrialRearmDenialSpecies.AuditOnlySidecarMismatch, in pending,
                candidateFingerprint, candidateRevision);
        if (custody.schemaVersion < 5
            || custody.sourceFundingDecision is not (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused)
            || !string.Equals(custody.policy, pending.Policy.Value, StringComparison.Ordinal)
            || !string.Equals(custody.policy, domain.PolicyID.Value, StringComparison.Ordinal)
            || custody.fundingID != pending.QuotaID
            || custody.sourceDecisionID != pending.SourceDecisionID
            || custody.sourceDecisionEventID != pending.SourceDecisionEventID
            || custody.sourceSupportDigest != pending.SourceOccurrenceDigest
            || custody.sourceCandidateFingerprint != pending.SourceCandidateFingerprint
            || custody.readoutFingerprint != pending.SourceReadoutFingerprint
            || custody.candidateFingerprint != pending.SourceQuotaCandidateFingerprint
            || custody.candidateRevision != pending.SourceCandidateRevision.Value
            || custody.canonicalState != EncodeCanonicalState(pending.SourceCanonicalState)
            || custody.sourceFundingDecision != pending.SourceQuotaDecision
            || !string.Equals(custody.custodyDigest, pending.AuditOnlyDigest, StringComparison.Ordinal))
            return CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
                CortexPolicyPendingForcedTrialRearmDenialSpecies.AuditOnlySidecarMismatch, in pending,
                candidateFingerprint, candidateRevision);
        if (custody.sourceCorroborationEventID != pending.SourceCorroborationEventID)
            return CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
                CortexPolicyPendingForcedTrialRearmDenialSpecies.SourceCorroborationMismatch, in pending,
                candidateFingerprint, candidateRevision);
        // The custody sidecar is evidence, not the funding authority.  Join it to
        // the immutable Paid row in the source run's root journal so deleting or
        // rewriting that row cannot leave an executable orphan behind.
        if (!TryReadRootPolicyFundingDecision(parentDirectory, pending.SourceRunID, in pending, in custody,
                out CortexPolicyTrialQuotaDecision rootFunding, out bool rootFundingMissing)
            || rootFunding.Decision != pending.SourceQuotaDecision
            || rootFunding.CandidateFingerprint != pending.SourceQuotaCandidateFingerprint
            || rootFunding.ReadoutFingerprint != pending.SourceReadoutFingerprint
            || rootFunding.CandidateRevision != pending.SourceCandidateRevision
            || rootFunding.QuotaStep != custody.fundingStep
            || !string.Equals(rootFunding.SeedAuditOnlyDigest, pending.AuditOnlyDigest, StringComparison.Ordinal))
            return CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
                rootFundingMissing ? CortexPolicyPendingForcedTrialRearmDenialSpecies.RootQuotaMissing
                    : CortexPolicyPendingForcedTrialRearmDenialSpecies.RootQuotaMismatch, in pending,
                candidateFingerprint, candidateRevision);
        if (_runtimeTape is null || pending.SourceCorroborationEventID <= 0
            || !_runtimeTape.TryGetEventView(new TapeEventID(pending.SourceCorroborationEventID), out _))
            return CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
                CortexPolicyPendingForcedTrialRearmDenialSpecies.SourceCorroborationMissing, in pending,
                candidateFingerprint, candidateRevision);
        if (TryReadBoundarySourceCorroboration(custody.sourceDecisionID, custody.sourceDecisionEventID,
            pending.SourceCandidateRevision, pending.SourceReadoutFingerprint, pending.SourceCandidateFingerprint,
            pending.SourceOccurrenceDigest, out TapeEventID corroborationEventID,
            out CortexPolicySelectionCauses sourceSelectionCause, pending.SourceCanonicalState)
            && corroborationEventID.Value == pending.SourceCorroborationEventID
            && sourceSelectionCause == custody.sourceSelectionCause)
            return CortexPolicyPendingForcedTrialRearmEvaluation.Allow(in pending, candidateFingerprint, candidateRevision);
        return CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
            CortexPolicyPendingForcedTrialRearmDenialSpecies.SourceCorroborationMismatch, in pending,
            candidateFingerprint, candidateRevision);
    }

    private bool TryReadRootPolicyFundingDecision(
        string rootDirectory,
        string sourceRunID,
        in CortexPolicyPendingForcedTrialIntent pending,
        in PolicyBoundarySeedCustody custody,
        out CortexPolicyTrialQuotaDecision decision,
        out bool missing)
    {
        decision = default;
        missing = false;
        if (!TryGetPolicyBoundaryDomain(pending.Policy, out IPolicyBoundaryDomain domain))
            return false;
        RootFundingMemoKey memoKey = new(domain.PolicyID, rootDirectory, sourceRunID, pending.QuotaID);
        if (_rootFundingMemo.TryGetValue(memoKey, out CortexPolicyTrialQuotaDecision cachedDecision))
        {
            decision = cachedDecision;
            return true;
        }
        string normalizedRoot = Path.GetFullPath(rootDirectory);
        string rootName = Path.GetFileName(Path.TrimEndingDirectorySeparator(normalizedRoot));
        string sourceDirectory = string.Equals(rootName, sourceRunID, StringComparison.Ordinal)
            ? normalizedRoot
            : Path.Combine(normalizedRoot, sourceRunID);
        string path = Path.Combine(sourceDirectory, PolicyTrialQuotaJournalFile);
        ulong pendingQuotaID = pending.QuotaID;
        bool Reject(string reason)
        {
            Trace.Cortex.Warn("policy.root-funding-reject", $"id={pendingQuotaID:X} path={path} reason={reason}");
            return false;
        }
        if (!File.Exists(path)) { missing = true; return Reject("missing"); }
        string text;
        try { text = File.ReadAllText(path, Encoding.UTF8); }
        catch (IOException) { return Reject("read"); }
        string[] lines = text.Split('\n');
        if (lines.Length == 0 || !lines[0].TrimStart('\uFEFF').Equals(PolicyTrialQuotaJournalHeader, StringComparison.Ordinal))
            return Reject($"header:{(lines.Length == 0 ? "empty" : lines[0].TrimStart('\uFEFF'))}");
        bool found = false;
        CortexPolicyTrialQuotaDecision paid = default;
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Length == 0) continue;
            string[] columns = lines[i].Split('\t');
            if (columns.Length != 22)
            {
                if (columns.Length > 0 && columns[0].Equals(pendingQuotaID.ToString("X"), StringComparison.OrdinalIgnoreCase))
                    return Reject($"columns:{columns.Length}");
                continue;
            }
            CortexPolicyQuotaDecisionID id;
            try { id = new(ulong.Parse(columns[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture)); }
            catch (FormatException) { continue; }
            if (id.Value != pending.QuotaID) continue;
            try
            {
                CortexPolicyQuotaDecisions verdict = Enum.Parse<CortexPolicyQuotaDecisions>(columns[8]);
                decision = new(id, new CortexPolicyID(columns[1]),
                    ulong.Parse(columns[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    int.Parse(columns[3], CultureInfo.InvariantCulture), int.Parse(columns[4], CultureInfo.InvariantCulture),
                    int.Parse(columns[5], CultureInfo.InvariantCulture), long.Parse(columns[6], CultureInfo.InvariantCulture),
                    long.Parse(columns[7], CultureInfo.InvariantCulture), verdict,
                    long.Parse(columns[9], CultureInfo.InvariantCulture), long.Parse(columns[10], CultureInfo.InvariantCulture))
                {
                    CandidateState = Enum.Parse<CortexPolicyTrialCandidateStates>(columns[11]),
                    DenialReason = Enum.Parse<CortexPolicyTrialDenialReasons>(columns[12]),
                    CandidateOriginStep = int.Parse(columns[13], CultureInfo.InvariantCulture),
                    CandidateCurrentStep = int.Parse(columns[14], CultureInfo.InvariantCulture),
                    CandidateRequiredStep = int.Parse(columns[15], CultureInfo.InvariantCulture),
                    CandidateRevision = new GrammarRevisionID(ulong.Parse(columns[16], CultureInfo.InvariantCulture)),
                    AllocationIdentity = columns[17], AllocationDigest = columns[18],
                    AllocationArmSteps = long.Parse(columns[19], CultureInfo.InvariantCulture),
                    SeedAuditOnlyDigest = columns[20],
                    ReadoutFingerprint = ulong.Parse(columns[21], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                };
                if (!decision.Policy.Equals(domain.PolicyID)
                    || decision.Decision is not (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused))
                    return Reject("policy-or-verdict");
                if (!QuotaAllocationShapeIsValid(in decision))
                    return Reject($"accounting:{decision.Decision} planned={decision.PlannedArmSteps} reserved={decision.HeldArmSteps} horizon={decision.RequestedHorizonSteps} arms={decision.ArmCount} allocation={decision.AllocationArmSteps}/{decision.AllocationIdentity}/{decision.AllocationDigest} candidate={decision.CandidateState}/{decision.DenialReason}/{decision.CandidateOriginStep}/{decision.CandidateCurrentStep}/{decision.CandidateRequiredStep} custody={decision.SeedAuditOnlyDigest.Length}");
                if (decision.QuotaStep != custody.fundingStep
                    || decision.CandidateFingerprint != pending.SourceQuotaCandidateFingerprint
                    || decision.ReadoutFingerprint != pending.SourceReadoutFingerprint
                    || decision.CandidateRevision != pending.SourceCandidateRevision
                    || !string.Equals(decision.SeedAuditOnlyDigest, pending.AuditOnlyDigest, StringComparison.Ordinal))
                {
                    Trace.Cortex.Warn("policy.root-funding-mismatch", $"id={id.Value:X} step={decision.QuotaStep}/{custody.fundingStep} candidate={decision.CandidateFingerprint:X16}/{pending.SourceQuotaCandidateFingerprint:X16} readout={decision.ReadoutFingerprint:X16}/{pending.SourceReadoutFingerprint:X16} revision={decision.CandidateRevision.Value}/{pending.SourceCandidateRevision.Value} custody={decision.SeedAuditOnlyDigest == pending.AuditOnlyDigest}");
                    return Reject("identity");
                }
                if (!found)
                {
                    if (decision.Decision != CortexPolicyQuotaDecisions.Paid) return Reject("first-not-paid");
                    paid = decision;
                    found = true;
                }
                else if (decision.Decision != CortexPolicyQuotaDecisions.Reused
                    || !QuotaImmutableTupleMatches(in paid, in decision)
                    || decision.UsedSteps != 0
                    || decision.RemainingQuota > paid.RemainingQuota)
                {
                    Trace.Cortex.Warn("policy.root-funding-reuse-conflict", $"id={id.Value:X} verdict={decision.Decision} immutable={QuotaImmutableTupleMatches(in paid, in decision)} charged={decision.UsedSteps} remaining={decision.RemainingQuota}/{paid.RemainingQuota} paid={FormatPolicyTrialQuotaRow(in paid)} reused={FormatPolicyTrialQuotaRow(in decision)}");
                    return Reject("reuse-conflict");
                }
            }
            catch (Exception error) when (error is FormatException or ArgumentException or OverflowException)
            {
                return Reject("parse");
            }
        }
        decision = paid;
        if (found)
            _rootFundingMemo[memoKey] = decision;
        return found;
    }

    /// Funding IDs restored into policy state are only usable after they join to
    /// the durable funding authority. A local Paid row authenticates through
    /// its seed custody; a source-successor child deliberately has no local row,
    /// so its bound intent joins the custody sidecar back to the source run's
    /// root funding journal.
    private bool TryAuthenticatePolicyTrialQuotaIdentity(
        PolicyState state,
        CortexPolicyQuotaDecisionID fundingID,
        string? expectedAuditOnlyDigest = null)
    {
        if (fundingID.Value == 0)
            return false;
        if (_policyTrialQuotaByID.TryGetValue(fundingID, out CortexPolicyTrialQuotaDecision localFunding))
        {
            if (localFunding.Policy.Equals(state.Schema.Policy)
                && localFunding.Decision is CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused
                && TryAuthenticatePaidAuditOnly(ref localFunding))
            {
                if (expectedAuditOnlyDigest is null || localFunding.SeedAuditOnlyDigest == expectedAuditOnlyDigest)
                {
                    _policyTrialQuotaByID[fundingID] = localFunding;
                    return true;
                }
            }
            // A materialized child may carry the parent's row while its seed
            // custody lives at the source root. Fall through to the sidecar
            // join rather than treating that local copy as the authority.
        }
        CortexPolicyPendingForcedTrialIntent pending = state.PendingForcedTrialIntent;
        return pending.IsBound
            && pending.QuotaID == fundingID.Value
            && TryAuthenticatePendingForcedTrialIntentAuthority(in pending, expectedAuditOnlyDigest)
            || TryAuthenticatePolicyTrialQuotaSidecar(state, fundingID, expectedAuditOnlyDigest);
    }

    private bool TryAuthenticatePolicyTrialQuotaSidecar(
        PolicyState state,
        CortexPolicyQuotaDecisionID fundingID,
        string? expectedAuditOnlyDigest)
    {
        if (!_policyBoundaryDomains.TryGetValue(state.Schema.Policy, out IPolicyBoundaryDomain domain))
            return false;
        bool Reject(string species, string path = "")
        {
            Trace.Cortex.Boundary("policy.trial-funding-sidecar-reject",
                $"funding={fundingID} policy={state.Schema.Policy} species={species} run={_runtimeRun?.Dir ?? "none"} path={path}");
            return false;
        }
        if (_runtimeRun is null) return Reject("runtime-unbound");
        string currentDirectory = Path.GetFullPath(_runtimeRun.Dir);
        string rootDirectory = Path.GetFullPath(Path.Combine(currentDirectory, "..", ".."));
        string path = Path.Combine(rootDirectory, PolicyBoundarySeedCustodyDirectory,
            fundingID.ToString(), PolicyBoundarySeedCustodyFile);
        if (!File.Exists(path)) return Reject("sidecar-missing", path);
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            PolicyBoundarySeedCustody custody = RonSerializer.Deserialize<PolicyBoundarySeedCustody>(bytes);
            if (!bytes.AsSpan().SequenceEqual(custody.Encode())
                || custody.fundingID != fundingID.Value
                || !string.Equals(custody.policy, state.Schema.Policy.Value, StringComparison.Ordinal)
                || !IsAuthenticatedAuditOnlyDigest(custody.custodyDigest)
                || custody.custodyDigest != custody.ComputeDigest()
                || expectedAuditOnlyDigest is not null && !string.Equals(expectedAuditOnlyDigest, custody.custodyDigest, StringComparison.Ordinal)
                || !TryDecodeCanonicalState(custody.canonicalState, out PolicyCanonicalStateID sourceState)
                || domain.CanonicalScopeMode != PolicyCanonicalScopeModes.None && !domain.ValidateCanonicalState(in sourceState))
                return Reject("sidecar-identity", path);
            GrammarRevisionID sourceRevision = new(custody.candidateRevision);
            if (!TryReadBoundarySourceCorroboration(custody.sourceDecisionID, custody.sourceDecisionEventID,
                    sourceRevision, custody.readoutFingerprint, custody.sourceCandidateFingerprint,
                    custody.sourceSupportDigest, out TapeEventID corroborationEventID,
                    out CortexPolicySelectionCauses sourceCause, sourceState)
                || corroborationEventID.Value != custody.sourceCorroborationEventID
                || sourceCause != custody.sourceSelectionCause)
                return Reject("sidecar-source-corroboration",
                    $"{path} decision={custody.sourceDecisionID} event={custody.sourceDecisionEventID} corroboration={custody.sourceCorroborationEventID} revision={sourceRevision.Value} readout={custody.readoutFingerprint:X16} candidate={custody.sourceCandidateFingerprint:X16} support={custody.sourceSupportDigest:X16} tape={_runtimeTape?.Count ?? -1}");
            CortexPolicyPendingForcedTrialIntent sourceTuple = new(
                state.Schema.Policy, custody.fundingID, custody.sourceFundingDecision, 1,
                custody.sourceDecisionID, custody.sourceDecisionEventID, custody.sourceCorroborationEventID, custody.sourceSupportDigest,
                custody.sourceCandidateFingerprint, custody.candidateFingerprint, custody.readoutFingerprint,
                sourceRevision, sourceState, custody.readoutFingerprint,
                custody.candidateFingerprint, sourceRevision, custody.sourceSupportDigest,
                sourceState, "", (byte)PolicyBoundaryArms.ForcedDivergentNull,
                domain.BoundaryFeatureID, custody.sourceRunID, custody.custodyDigest);
            if (!TryReadPolicyBoundarySeedCustodyDocument(rootDirectory, custody.sourceRunID,
                    fundingID.ToString(), out PolicyBoundarySeedCustody verifiedCustody))
                return Reject("sidecar-document", path);
            bool authenticated = TryReadRootPolicyFundingDecision(rootDirectory, custody.sourceRunID,
                in sourceTuple, in verifiedCustody, out CortexPolicyTrialQuotaDecision rootFunding, out _)
                && rootFunding.Decision is CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused
                && rootFunding.QuotaDecisionID.Equals(fundingID)
                && string.Equals(rootFunding.SeedAuditOnlyDigest, custody.custodyDigest, StringComparison.Ordinal);
            return authenticated || Reject("root-funding", path);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or FormatException)
        {
            return Reject($"sidecar-read-{error.GetType().Name}", path);
        }
    }

    private bool TryAuthenticatePendingForcedTrialIntentAuthority(
        in CortexPolicyPendingForcedTrialIntent pending,
        string? expectedAuditOnlyDigest = null)
    {
        if (!pending.IsBound || pending.QuotaID == 0 || _runtimeRun is null
            || pending.SourceQuotaDecision is not (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused))
            return false;
        if (expectedAuditOnlyDigest is not null && !string.Equals(expectedAuditOnlyDigest, pending.AuditOnlyDigest, StringComparison.Ordinal))
            return false;
        string currentDirectory = Path.GetFullPath(_runtimeRun.Dir);
        string currentRunID = Path.GetFileName(Path.TrimEndingDirectorySeparator(currentDirectory));
        string rootDirectory = string.Equals(currentRunID, pending.SourceRunID, StringComparison.Ordinal)
            ? currentDirectory
            : Path.GetFullPath(Path.Combine(currentDirectory, "..", ".."));
        if (!TryReadPolicyBoundarySeedCustodyDocument(rootDirectory, pending.SourceRunID,
                new CortexPolicyQuotaDecisionID(pending.QuotaID).ToString(), out PolicyBoundarySeedCustody custody))
            return false;
        if (!string.Equals(custody.custodyDigest, pending.AuditOnlyDigest, StringComparison.Ordinal))
            return false;
        return TryReadRootPolicyFundingDecision(rootDirectory, pending.SourceRunID,
            in pending, in custody, out CortexPolicyTrialQuotaDecision rootFunding, out _)
            && rootFunding.Decision == pending.SourceQuotaDecision
            && rootFunding.QuotaDecisionID.Value == pending.QuotaID;
    }

    internal void ValidateDeferredPolicyTrialAuthority()
    {
        if (_runtimeRun is null)
        {
            _policyTrialAuthorityValidationPending = true;
            return;
        }
        if (!_policyTrialAuthorityValidationPending)
            return;
        foreach (PolicyState state in _policies.Values)
        {
            if (state.ActiveTrialQuotaID.Value != 0)
            {
                if (!state.SuppressTrialPackets
                    || !TryAuthenticatePolicyTrialQuotaIdentity(state, state.ActiveTrialQuotaID))
                    throw new InvalidDataException("policy trial runtime binding carries unauthenticated active funding authority");
            }
            if (state.PendingForcedTrialIntent.IsBound)
            {
                if (!TryAuthenticatePendingForcedTrialIntentAuthority(state.PendingForcedTrialIntent))
                    throw new InvalidDataException("policy trial runtime binding carries unauthenticated pending custody");
            }
        }
        _policyTrialAuthorityValidationPending = false;
    }

    private TapeEventID FindPolicyDecisionEvent(CortexPolicyDecisionID decisionID)
    {
        if (_runtimeTape is null || decisionID.Value == 0) return new TapeEventID(-1);
        TapeEventID appendedEvent = _latestHomeostatDecisionEventID;
        if (_latestHomeostatDecisionEventDecisionID.Equals(decisionID)
            && appendedEvent.Value > 0
            && _runtimeTape.TryGetEventView(appendedEvent, out TapeEventView appendedView)
            && string.Equals(appendedView.Source, "policy:" + HomeostatPolicyBoundaryDomain.Instance.PolicyID.Value, StringComparison.Ordinal)
            && appendedView.Provenance == Provenances.Execution
            && _runtimeTape.Resolve(appendedEvent, out byte[] appendedPayload))
        {
            try
            {
                CortexPolicyDecisionPacket packet = TapePacketCreator.DecodePolicyDecision(appendedPayload);
                if (packet.DecisionID.Equals(decisionID)) return appendedEvent;
            }
            catch (InvalidDataException) { }
        }
        TapeEventID found = new(-1);
        foreach (TapeEventView view in _runtimeTape.GetEventViews())
        {
            if (!string.Equals(view.Source, "policy:" + HomeostatPolicyBoundaryDomain.Instance.PolicyID.Value, StringComparison.Ordinal)
                || view.Provenance != Provenances.Execution)
                continue;
            if (!_runtimeTape.Resolve(view.Id, out byte[] payload)) continue;
            try
            {
                CortexPolicyDecisionPacket packet = TapePacketCreator.DecodePolicyDecision(payload);
                if (packet.DecisionID.Equals(decisionID) && view.Id.Value > found.Value) found = view.Id;
            }
            catch (InvalidDataException) { }
        }
        return found;
    }

    internal bool TryReadPolicyOutcomeEvidence(
        CortexPolicyDecisionID decisionID,
        out CortexPolicyOutcomeEvidence evidence)
    {
        evidence = default;
        if (_runtimeTape is null || _runtimeJournal is null || _runtimeRun is null || decisionID.Value == 0)
            return false;
        CortexPolicyOutcomeEvidence? found = null;
        foreach (TapeEventView view in _runtimeTape.GetEventViews())
        {
            if (!string.Equals(view.Source, "policy:" + HomeostatPolicyBoundaryDomain.Instance.PolicyID.Value, StringComparison.Ordinal)
                || view.Provenance != Provenances.Execution
                || !_runtimeTape.Resolve(view.Id, out byte[] payload)
                || !TapePacketCreator.TryDecodePolicyOutcome(payload, out CortexPolicyOutcomePacket packet)
                || !packet.DecisionID.Equals(decisionID)) continue;
            string digest = TapePacketCreator.DigestPolicyOutcomePayload(payload);
            int matchingJournalRows = 0;
            int step = -1;
            foreach (string line in _runtimeJournal.EnumerateAllLines(_runtimeRun.PathOf("journal.log")))
            {
                string[] columns = line.Split('\t');
                if (columns.Length < 4 || columns[1] != "policy-outcome" || columns[2] != view.Id.ToString()
                    || columns[3] != view.Source) continue;
                if (!TapePacketCreator.TryReadPolicyOutcomeJournalRow(line, view.Id, view.Source, in packet,
                        digest, payload.Length, out int rowStep))
                    throw new InvalidDataException("policy outcome journal row disagrees with its tape payload");
                matchingJournalRows++;
                step = rowStep;
            }
            if (matchingJournalRows != 1)
                throw new InvalidDataException(matchingJournalRows == 0
                    ? "policy outcome tape event has no journal custody row"
                    : "policy outcome tape event has duplicate journal custody rows");
            CortexPolicyOutcomeEvidence candidate = new(view.Id, step, digest, packet);
            candidate.Validate(HomeostatPolicyBoundaryDomain.Instance.Schema);
            if (packet.Outcomes.Length != 2 || packet.Outcomes[0].MetricID.Value != 500 || packet.Outcomes[1].MetricID.Value != 501)
                throw new InvalidDataException("Homeostat policy outcome does not carry its ordinary sensor metrics");
            if (found is not null)
                throw new InvalidDataException($"policy decision {decisionID} has multiple ordinary POLICY-OUTCOME packets");
            found = candidate;
        }
        if (found is CortexPolicyOutcomeEvidence resolved)
        {
            evidence = resolved;
            return true;
        }
        return false;
    }

    private bool TryReadPolicyDecisionIdentityEvent(
        TapeEventID eventID,
        CortexPolicyDecisionID expectedDecisionID,
        out CortexPolicyDecisionReadout readout)
    {
        readout = default;
        if (_runtimeTape is null || eventID.Value <= 0 || expectedDecisionID.Value == 0
            || !_runtimeTape.TryGetEventView(eventID, out TapeEventView view)
            || !string.Equals(view.Source, "policy:" + HomeostatPolicyBoundaryDomain.Instance.PolicyID.Value, StringComparison.Ordinal)
            || view.Provenance != Provenances.Execution || !_runtimeTape.Resolve(eventID, out byte[] payload)) return false;
        try
        {
            CortexPolicyDecisionPacket packet = TapePacketCreator.DecodePolicyDecision(payload);
            if (!packet.DecisionID.Equals(expectedDecisionID)) return false;
            readout = packet.Readout;
            return true;
        }
        catch (InvalidDataException) { return false; }
    }

    private bool TryReadAuthenticatedHomeostatCriticality(
        TapeEventID eventID,
        CortexPolicyDecisionID expectedDecisionID,
        in CortexPolicyDecisionReadout expectedReadout,
        out double criticality,
        out string provenance)
    {
        criticality = double.NaN;
        provenance = "";
        ushort featureID = checked((ushort)(400 + (int)HomeostatPolicyFeatureIDs.Criticality));
        if (_runtimeTape is null || eventID.Value <= 0 || expectedDecisionID.Value == 0
            || !_runtimeTape.TryGetEventView(eventID, out TapeEventView view)
            || !string.Equals(view.Source, "policy:" + HomeostatPolicyBoundaryDomain.Instance.PolicyID.Value, StringComparison.Ordinal)
            || view.Provenance != Provenances.Execution || !_runtimeTape.Resolve(eventID, out byte[] payload))
            return false;
        try
        {
            CortexPolicyDecisionPacket packet = TapePacketCreator.DecodePolicyDecision(payload);
            if (!packet.DecisionID.Equals(expectedDecisionID)
                || !packet.Readout.Equals(expectedReadout)
                || packet.Features is null || packet.Features.Length == 0)
                return false;
            for (int index = 0; index < packet.Features.Length; index++)
            {
                MetricSample sample = packet.Features[index];
                if (sample.MetricID.Value != featureID) continue;
                criticality = sample.Value.Kind switch
                {
                    NumericKinds.F64 => sample.Value.GetF64(),
                    NumericKinds.I64 => sample.Value.GetI64(),
                    NumericKinds.U64 => sample.Value.GetU64(),
                    _ => double.NaN,
                };
                if (!double.IsFinite(criticality)) return false;
                provenance = $"tape-policy-decision:{eventID.Value}:feature:{featureID}";
                return true;
            }
        }
        catch (InvalidDataException) { }
        return false;
    }

    private bool TryReadLatestHomeostatPolicyDecision(
        in CortexPolicyReadoutReceipt readout,
        out CortexPolicyDecision decision)
    {
        decision = default;
        if (_runtimeTape is null) return false;
        PolicyCanonicalStateID canonicalState = readout.CanonicalState;
        CortexPolicyDecision latest = default;
        long latestEventID = long.MinValue;
        bool found = false;
        foreach (TapeEventView view in _runtimeTape.GetEventViews())
        {
            if (!string.Equals(view.Source, "policy-boundary:source", StringComparison.Ordinal)
                || view.Provenance != Provenances.Execution || !_runtimeTape.Resolve(view.Id, out byte[] corroborationPayload)
                || !TapePacketCreator.TryReadPolicyBoundarySourceCorroboration(corroborationPayload, out CortexPolicyBoundarySourceCorroboration corroboration)
                || !corroboration.Policy.Equals(HomeostatPolicyBoundaryDomain.Instance.PolicyID)
                || corroboration.ReadoutRevision != readout.Revision
                || corroboration.ReadoutFingerprint != readout.Fingerprint
                || corroboration.CandidateFingerprint != readout.ReadoutCandidateFingerprint
                || corroboration.OccurrenceDigest != readout.ReadoutCandidateOccurrenceDigest
                || corroboration.CanonicalState != readout.CanonicalState
                || !IsVerifiedPolicyScope(
                    HomeostatPolicyBoundaryDomain.Instance.PolicyID, in canonicalState, readout.Fingerprint,
                    readout.ReadoutCandidateFingerprint, readout.ReadoutCandidateOccurrenceDigest,
                    readout.Revision))
                continue;
            if (!_runtimeTape.TryGetEventView(corroboration.SourceDecisionEventID, out TapeEventView sourceView)
                || !string.Equals(sourceView.Source, "policy:" + HomeostatPolicyBoundaryDomain.Instance.PolicyID.Value, StringComparison.Ordinal)
                || sourceView.Provenance != Provenances.Execution
                || !_runtimeTape.Resolve(corroboration.SourceDecisionEventID, out byte[] payload)) return false;
            try
            {
                CortexPolicyDecisionPacket packet = TapePacketCreator.DecodePolicyDecision(payload);
                if (!packet.DecisionID.Equals(corroboration.SourceDecisionID)
                    || packet.Readout.Authority != corroboration.SourceAuthority
                    || packet.Readout.SelectionCause != corroboration.SourceSelectionCause)
                    return false;
                GrammarPolicyContextKey sourceContext = new(
                    canonicalState, HomeostatPolicyBoundaryDomain.Instance.Schema.ActionCount,
                    _config.Learning.Policies.ReadoutDeliberationQuota);
                decision = new CortexPolicyDecision(
                    packet.DecisionID, HomeostatPolicyBoundaryDomain.Instance.PolicyID, packet.Readout, in sourceContext);
                if (!found || corroboration.SourceDecisionEventID.Value > latestEventID)
                {
                    latest = decision;
                    latestEventID = corroboration.SourceDecisionEventID.Value;
                    found = true;
                }
            }
            catch (InvalidDataException) { return false; }
        }
        if (!found) return false;
        decision = latest;
        return true;
    }

    private static LoopClosureDigest DigestExecutionArms(in PolicyBoundaryForkReceipt receipt)
    {
        StringBuilder text = new("policy-boundary-arms-v3|");
        foreach (PolicyBoundaryArmReceipt arm in receipt.Arms)
            text.Append((byte)arm.Arm).Append('|').Append(arm.Horizon).Append('|')
                .Append(arm.PaidCloseDelta).Append('|').Append(arm.MatchedSpend).Append('|')
                .Append(arm.ContinuityExact ? 1 : 0).Append('|').Append(arm.ChildProcessCompleted ? 1 : 0).Append('|')
                .Append(arm.GrammarExecutionsDelta).Append('|').Append(arm.TrialAdaptationTransitions).Append('|')
                .Append(arm.AdaptationEnabled ? 1 : 0).Append('|')
                .Append((byte)arm.ExecutionOutcome).Append('|').Append(arm.RequestCount).Append('|').Append(arm.GuardAdmittedCount).Append('|')
                .Append(arm.LastRequestDecisionID.Value).Append('|').Append(arm.LastRequestStep).Append('|')
                .Append(arm.LastRequestReadout.LaunchpadAction).Append('|').Append(arm.LastRequestReadout.RawCandidateAction).Append('|')
                .Append(arm.LastRequestReadout.SelectedCandidateAction).Append('|').Append(arm.LastRequestReadout.ExecutedAction).Append('|')
                .Append((byte)arm.LastRequestReadout.Authority).Append('|').Append(arm.LastRequestReadout.GrammarRevision.Value).Append('|')
                .Append((byte)arm.LastRequestReadout.SelectionCause).Append('|')
                .Append(arm.LastRequestReadout.ReadoutCandidateOccurrenceDigest.ToString("X16")).Append('|')
                .Append(arm.LastRequestReadout.ReadoutCandidateFingerprint.ToString("X16")).Append('|')
                .Append(arm.ExecutedDecisionID.Value).Append('|').Append(arm.ExecutedStep).Append('|').Append(arm.ExecutedLaunchpadAction).Append('|')
                .Append(arm.ExecutedRawCandidateAction).Append('|').Append(arm.ExecutedSelectedCandidateAction).Append('|')
                .Append(arm.ExecutedAction).Append('|').Append((byte)arm.ExecutedAuthority).Append('|')
                .Append((byte)arm.ExecutedSelectionCause).Append('|')
                .Append(arm.ExecutedReadoutFingerprint.ToString("X16"))
                .Append('|').Append(arm.ExecutedReadoutRevision).Append('|')
                .Append(arm.ExecutedReadoutOccurrenceDigest.ToString("X16")).Append('|')
                .Append(arm.ExecutedCandidateFingerprint.ToString("X16")).Append('|')
                .Append(arm.ExecutedCanonicalState.Version == 0 ? "" : arm.ExecutedCanonicalState.Policy.Value).Append('|')
                .Append((byte)arm.ExecutedCanonicalState.Kind).Append('|')
                .Append(arm.ExecutedCanonicalState.Version).Append('|')
                .Append(arm.ExecutedCanonicalState.Value.ToString("X16")).Append('|')
                .Append(arm.ExecutedDecisionEventID.Value).Append('|')
                .Append(arm.ForcedDivergenceSeed.ToString("X16")).Append('|');
        return new LoopClosureDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))));
    }

    internal static LoopClosureDigest DigestExecutedDivergenceChildExecution(
        CortexPolicyDecisionID executedDivergenceDecision,
        LoopClosureDigest executedDivergenceOutcome)
    {
        // Frozen digest token policy-boundary-executed-dissent-child-v3; identifier-side name is Divergence.
        string text = string.Join('|', "policy-boundary-executed-dissent-child-v3", executedDivergenceDecision.Value,
            executedDivergenceOutcome.Value);
        return new LoopClosureDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
    }

    internal static bool TryReadPolicyBoundaryDivergenceArms(
        in CortexPolicyDecision decision,
        in PolicyBoundaryForkReceipt receipt,
        IPolicyBoundaryDomain domain,
        out PolicyBoundaryDivergenceCandidateTerminal candidate,
        out PolicyBoundaryDivergenceArmOutcome forcedNull)
    {
        candidate = default;
        forcedNull = default;
        ArgumentNullException.ThrowIfNull(domain);
        if (decision.DecisionID.Value == 0 || !decision.Policy.Equals(domain.PolicyID))
            return false;
        // The terminal horizon is the only arm result that can represent the
        // paid closure; earlier rows are intermediate ladder observations.
        if (receipt.Horizons is null || receipt.Horizons.Length == 0 || receipt.Arms is null)
            return false;
        int horizon = receipt.Horizons[^1];
        PolicyBoundaryArmReceipt candidateRow = default;
        PolicyBoundaryArmReceipt nullRow = default;
        PolicyBoundaryArmReceipt baselineRow = default;
        PolicyBoundaryArmReceipt reflexRow = default;
        bool foundCandidate = false;
        bool foundNull = false;
        bool foundBaseline = false;
        bool foundReflex = false;
        for (int index = 0; index < receipt.Arms.Length; index++)
        {
            PolicyBoundaryArmReceipt row = receipt.Arms[index];
            if (row.Horizon != horizon) continue;
            if (row.Arm == PolicyBoundaryArms.Candidate) { candidateRow = row; foundCandidate = true; }
            if (row.Arm == PolicyBoundaryArms.ForcedDivergentNull) { nullRow = row; foundNull = true; }
            if (row.Arm == PolicyBoundaryArms.Baseline) { baselineRow = row; foundBaseline = true; }
            if (row.Arm == PolicyBoundaryArms.ReflexFrozenControl) { reflexRow = row; foundReflex = true; }
        }
        if (!foundCandidate || !foundNull || !foundBaseline || !foundReflex) return false;
        try
        {
            baselineRow.ValidateExecutedDecisionIdentity(domain);
            candidateRow.ValidateRequestAccounting(domain);
            if (candidateRow.ExecutionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted)
                candidateRow.ValidateExecutedDecisionIdentity(domain, requireGrammar: true);
            nullRow.ValidateExecutedDecisionIdentity(domain);
            reflexRow.ValidateExecutedDecisionIdentity(domain);
        }
        catch (InvalidDataException) { return false; }
        if (candidateRow.ExecutionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted)
        {
            try
            {
                candidateRow.ValidateExecutedReadoutAncestry(domain.PolicyID, receipt.SourceDecisionReadoutRevision,
                    domain, nullRow.ExecutedReadoutFingerprint, nullRow.ExecutedReadoutRevision);
                nullRow.ValidateExecutedReadoutAncestry(domain.PolicyID, receipt.SourceDecisionReadoutRevision,
                    domain, candidateRow.ExecutedReadoutFingerprint, candidateRow.ExecutedReadoutRevision);
            }
            catch (InvalidDataException) { return false; }
        }
        else
        {
            try { nullRow.ValidateExecutedReadoutAncestry(domain.PolicyID, receipt.SourceDecisionReadoutRevision,
                domain); }
            catch (InvalidDataException) { return false; }
        }
        if (candidateRow.ExecutionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted
            && (candidateRow.ExecutedReadoutFingerprint != nullRow.ExecutedReadoutFingerprint
                || candidateRow.ExecutedReadoutRevision != nullRow.ExecutedReadoutRevision))
            return false;
        if (candidateRow.ExecutionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted
            && (candidateRow.ExecutedReadoutOccurrenceDigest == 0
                || candidateRow.ExecutedCandidateFingerprint == 0
                || candidateRow.ExecutedSelectionCause != CortexPolicySelectionCauses.GrammarCandidate
                || candidateRow.ExecutedSelectedCandidateAction != candidateRow.ExecutedAction))
            return false;
        if (nullRow.ExecutedSelectionCause != CortexPolicySelectionCauses.TrialOverride
            || nullRow.ExecutedAction == nullRow.ExecutedLaunchpadAction
            || nullRow.ExecutedAction == nullRow.ExecutedRawCandidateAction)
            return false;
        PolicyBoundaryDivergenceArmOutcome? candidateExecution = null;
        if (candidateRow.ExecutionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted)
        {
            candidateExecution = new(
                PolicyBoundaryDivergenceArmKinds.LiveCandidate,
                candidateRow.ExecutedAction,
                candidateRow.ExecutedAuthority,
                candidateRow.ExecutedSelectionCause,
                ChildProcessCompleted: candidateRow.ChildProcessCompleted,
                BehaviorallyExecuted: candidateRow.BehaviorallyExecuted,
                Diverged: false,
                horizon,
                candidateRow.MatchedSpend,
                PolicyBoundaryDivergenceProof.ComputeOutcomeID(in receipt, in candidateRow, candidateRow.ExecutedAction,
                    domain))
            {
                DecisionID = candidateRow.ExecutedDecisionID,
                LaunchpadAction = candidateRow.ExecutedLaunchpadAction,
                RawCandidateAction = candidateRow.ExecutedRawCandidateAction,
                SelectedCandidateAction = candidateRow.ExecutedSelectedCandidateAction,
                ReadoutFingerprint = candidateRow.ExecutedReadoutFingerprint,
                ReadoutRevision = candidateRow.ExecutedReadoutRevision,
                ReadoutOccurrenceDigest = candidateRow.ExecutedReadoutOccurrenceDigest,
                CandidateFingerprint = candidateRow.ExecutedCandidateFingerprint,
                ExecutedOutcomeEventID = candidateRow.ExecutedOutcomeEventID,
                ExecutedOutcomePayloadSHA256 = candidateRow.ExecutedOutcomePayloadSHA256,
            };
        }
        candidate = new(
            candidateRow.ExecutionOutcome,
            candidateRow.RequestCount,
            candidateRow.GuardAdmittedCount,
            horizon,
            candidateRow.MatchedSpend,
            candidateExecution);
        // The forced arm's own executed decision is carried by its typed rail
        // receipt.  Never relabel it with the parent's launchpad action.
        forcedNull = new(
            PolicyBoundaryDivergenceArmKinds.ForcedNull,
            nullRow.ExecutedAction,
            nullRow.ExecutedAuthority,
            nullRow.ExecutedSelectionCause,
            ChildProcessCompleted: nullRow.ChildProcessCompleted,
            BehaviorallyExecuted: nullRow.BehaviorallyExecuted,
            Diverged: nullRow.Diverged,
            horizon,
            nullRow.MatchedSpend,
            PolicyBoundaryDivergenceProof.ComputeOutcomeID(in receipt, in nullRow, nullRow.ExecutedAction,
                domain))
        {
            DecisionID = nullRow.ExecutedDecisionID,
            LaunchpadAction = nullRow.ExecutedLaunchpadAction,
            RawCandidateAction = nullRow.ExecutedRawCandidateAction,
            SelectedCandidateAction = nullRow.ExecutedSelectedCandidateAction,
            ReadoutFingerprint = nullRow.ExecutedReadoutFingerprint,
            ReadoutRevision = nullRow.ExecutedReadoutRevision,
            ReadoutOccurrenceDigest = nullRow.ExecutedReadoutOccurrenceDigest,
            CandidateFingerprint = nullRow.ExecutedCandidateFingerprint,
            ExecutedOutcomeEventID = nullRow.ExecutedOutcomeEventID,
            ExecutedOutcomePayloadSHA256 = nullRow.ExecutedOutcomePayloadSHA256,
        };
        return true;
    }

    private static bool TryReadHomeostatDivergenceArms(
        in CortexPolicyDecision decision,
        in PolicyBoundaryForkReceipt receipt,
        out PolicyBoundaryDivergenceCandidateTerminal candidate,
        out PolicyBoundaryDivergenceArmOutcome forcedNull)
        => TryReadPolicyBoundaryDivergenceArms(in decision, in receipt, HomeostatPolicyBoundaryDomain.Instance,
            out candidate, out forcedNull);

    internal static bool VerifyPolicyBoundaryDivergenceTemporalSplitFixture(TextWriter output)
    {
        CortexPolicyID policy = HomeostatPolicyBoundaryDomain.Instance.PolicyID;
        GrammarRevisionID revision = new(1);
        ulong fingerprint = GrammarPolicyReadout.ComputeFingerprint(revision, policy);
        PolicyBoundaryArmReceipt CreateArm(
            PolicyBoundaryArms arm,
            int launchpad,
            int raw,
            int selected,
            int executed,
            CortexPolicyAuthorities authority,
            CortexPolicySelectionCauses cause,
            long decision)
            => new(arm, 1, 1, 1, true, true)
            {
                ExecutionOutcome = CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted,
                RequestCount = 1,
                GuardAdmittedCount = 1,
                LastRequestDecisionID = new CortexPolicyDecisionID((ulong)decision),
                LastRequestStep = 1,
                LastRequestReadout = new CortexPolicyDecisionReadout(
                    launchpad, raw, selected, executed, authority, revision, cause,
                    cause == CortexPolicySelectionCauses.Launchpad ? 0UL : 2UL,
                    cause == CortexPolicySelectionCauses.Launchpad ? 0UL : 3UL),
                ExecutedDecisionID = new CortexPolicyDecisionID((ulong)decision),
                ExecutedStep = 1,
                ExecutedLaunchpadAction = launchpad,
                ExecutedRawCandidateAction = raw,
                ExecutedSelectedCandidateAction = selected,
                ExecutedAction = executed,
                ExecutedAuthority = authority,
                ExecutedSelectionCause = cause,
                ExecutedReadoutFingerprint = fingerprint,
                ExecutedReadoutRevision = revision.Value,
                ExecutedReadoutOccurrenceDigest = cause == CortexPolicySelectionCauses.Launchpad ? 0UL : 2UL,
                ExecutedCandidateFingerprint = cause == CortexPolicySelectionCauses.Launchpad ? 0UL : 3UL,
                ExecutedCanonicalState = new PolicyCanonicalStateID(policy, PolicyCanonicalStateKinds.Homeostat, 1, 0x205UL),
                ExecutedDecisionEventID = cause == CortexPolicySelectionCauses.TrialOverride
                    ? new TapeEventID(decision + 100) : default,
                ExecutedOutcomeEventID = cause == CortexPolicySelectionCauses.TrialOverride
                    ? new TapeEventID(decision + 101) : default,
                ExecutedOutcomePayloadSHA256 = cause == CortexPolicySelectionCauses.TrialOverride
                    ? new string('d', 64) : "",
                ForcedDivergenceSeed = cause == CortexPolicySelectionCauses.TrialOverride
                    ? 0xD1E3UL : 0UL,
                Diverged = arm == PolicyBoundaryArms.ForcedDivergentNull
                    && executed != launchpad
                    && executed != raw,
                AdaptationEnabled = arm != PolicyBoundaryArms.ReflexFrozenControl,
            };
        PolicyBoundaryArmReceipt[] arms =
        [
            CreateArm(PolicyBoundaryArms.Baseline, 0, -1, -1, 0, CortexPolicyAuthorities.Launchpad, CortexPolicySelectionCauses.Launchpad, 1),
            // Organic agreement is valid: the learned candidate executes under
            // Grammar custody while the independent forced arm supplies the
            // required executed divergence.
            CreateArm(PolicyBoundaryArms.Candidate, 0, 0, 0, 0, CortexPolicyAuthorities.Grammar, CortexPolicySelectionCauses.GrammarCandidate, 2),
            CreateArm(PolicyBoundaryArms.ForcedDivergentNull, 0, 2, 3, 3, CortexPolicyAuthorities.Grammar, CortexPolicySelectionCauses.TrialOverride, 3),
            CreateArm(PolicyBoundaryArms.ReflexFrozenControl, 0, 1, 1, 0, CortexPolicyAuthorities.Shadow, CortexPolicySelectionCauses.ShadowCandidate, 4),
        ];
        GrammarRevisionID childRevision = new(2);
        ulong childFingerprint = GrammarPolicyReadout.ComputeFingerprint(childRevision, policy);
        arms[1] = arms[1] with { ExecutedReadoutFingerprint = childFingerprint, ExecutedReadoutRevision = childRevision.Value, ExecutedReadoutOccurrenceDigest = 4 };
        arms[2] = arms[2] with { ExecutedReadoutFingerprint = childFingerprint, ExecutedReadoutRevision = childRevision.Value, ExecutedReadoutOccurrenceDigest = 5 };
        PolicyBoundaryForkReceipt receipt = new(
            new PolicyBoundaryObligationID("temporal-split"), PolicyBoundaryRational.Zero, PolicyBoundaryRational.Parse("1/2"),
            [1], arms, true, true, true, true, fingerprint, revision.Value)
        {
            SourceDecisionCandidateFingerprint = 3,
        };
        CortexPolicyDecision parent = new(
            new CortexPolicyDecisionID(5), policy,
            new CortexPolicyDecisionReadout(0, 2, 2, 2, CortexPolicyAuthorities.Grammar, revision,
                CortexPolicySelectionCauses.GrammarCandidate, 2, 3));
        bool splitAccepted = TryReadHomeostatDivergenceArms(in parent, in receipt, out PolicyBoundaryDivergenceCandidateTerminal candidate, out PolicyBoundaryDivergenceArmOutcome forcedNull)
            && candidate.ExecutedOutcome is PolicyBoundaryDivergenceArmOutcome candidateExecution
            && candidateExecution.Action == candidateExecution.LaunchpadAction
            && !candidateExecution.Diverged
            && forcedNull.Action != forcedNull.LaunchpadAction
            && forcedNull.Action != forcedNull.RawCandidateAction;
        PolicyBoundaryDivergenceArmOutcome candidateExecutionReceipt = candidate.ExecutedOutcome ?? default;
        bool candidateAgreementAccepted = arms[1].ExecutedAction == arms[1].ExecutedLaunchpadAction;
        CortexPolicyDecision earlyExecution = new(arms[1].ExecutedDecisionID, policy,
            new CortexPolicyDecisionReadout(arms[1].ExecutedLaunchpadAction, arms[1].ExecutedRawCandidateAction,
                arms[1].ExecutedSelectedCandidateAction, arms[1].ExecutedAction, arms[1].ExecutedAuthority,
                childRevision, arms[1].ExecutedSelectionCause, arms[1].ExecutedReadoutOccurrenceDigest,
                arms[1].ExecutedCandidateFingerprint));
        CortexPolicyDecision terminalShadow = new(new CortexPolicyDecisionID(99), policy,
            new CortexPolicyDecisionReadout(0, 1, 1, 0, CortexPolicyAuthorities.Shadow,
                new GrammarRevisionID(3), CortexPolicySelectionCauses.ShadowCandidate, 6, 7));
        CortexPolicyDecision? trialExecution = earlyExecution.SelectionCause == CortexPolicySelectionCauses.GrammarCandidate
            ? earlyExecution : null;
        trialExecution = terminalShadow.SelectionCause == CortexPolicySelectionCauses.GrammarCandidate
            ? terminalShadow : trialExecution;
        bool earlyExecutionPreserved = trialExecution is CortexPolicyDecision observed
            && observed.DecisionID.Equals(earlyExecution.DecisionID)
            && observed.SelectionCause == CortexPolicySelectionCauses.GrammarCandidate;
        PolicyBoundaryForkReceipt aliasReceipt = receipt with
        {
            Arms = [.. arms[..1], arms[1] with
            {
                ExecutedAuthority = CortexPolicyAuthorities.Shadow,
                ExecutedSelectionCause = CortexPolicySelectionCauses.ShadowCandidate,
            }, .. arms[2..]],
        };
        PolicyBoundaryForkReceipt forcedAliasReceipt = receipt with { Arms = [.. arms[..2], arms[2] with { ExecutedRawCandidateAction = arms[2].ExecutedAction }, .. arms[3..]] };
        PolicyBoundaryForkReceipt crossIdentityReceipt = receipt with { Arms = [.. arms[..1], arms[1] with { ExecutedReadoutFingerprint = fingerprint ^ 1UL }, .. arms[2..]] };
        PolicyBoundaryForkReceipt missingExecutionStepReceipt = receipt with { Arms = [.. arms[..1], arms[1] with { ExecutedStep = -1 }, .. arms[2..]] };
        PolicyBoundaryForkReceipt missingForcedCustodyReceipt = receipt with
        {
            Arms = [.. arms[..2], arms[2] with { ExecutedDecisionEventID = default, ForcedDivergenceSeed = 0 }, .. arms[3..]],
        };
        bool aliasRejected = !TryReadHomeostatDivergenceArms(in parent, in aliasReceipt, out _, out _);
        bool forcedAliasRejected = !TryReadHomeostatDivergenceArms(in parent, in forcedAliasReceipt, out _, out _);
        bool crossIdentityRejected = !TryReadHomeostatDivergenceArms(in parent, in crossIdentityReceipt, out _, out _);
        bool missingExecutionStepRejected = !TryReadHomeostatDivergenceArms(in parent, in missingExecutionStepReceipt, out _, out _);
        bool missingForcedCustodyRejected = !TryReadHomeostatDivergenceArms(in parent, in missingForcedCustodyReceipt, out _, out _);
        PolicyBoundaryArmReceipt nonDivergentForced = arms[2] with
        {
            ExecutedSelectedCandidateAction = 0,
            ExecutedAction = 0,
            Diverged = false,
        };
        PolicyBoundaryForkReceipt nonDivergentForcedReceipt = receipt with
        {
            Arms = [.. arms[..2], nonDivergentForced, .. arms[3..]],
        };
        bool nonDivergentForcedRejected = !TryReadHomeostatDivergenceArms(in parent, in nonDivergentForcedReceipt, out _, out _);
        bool executionCorroborationRoundTrip = false;
        bool executionChildIdentityRejected = false;
        bool packetRoundTrip = false;
        bool missingPolicyRejected = false;
        bool mismatchedPolicyRejected = false;
        bool scopeOmissionRejected = false;
        bool scopeMismatchRejected = false;
        bool legacyExecutionSchemaRejected = false;
        try
        {
            int[] corroborationHorizons = [1, 2, 3];
            PolicyBoundaryArmReceipt[] corroborationArms = [.. corroborationHorizons.SelectMany(horizon => arms.Select(arm => arm with { Horizon = horizon }))];
            PolicyBoundaryForkReceipt fundedReceipt = receipt with
            {
                Horizons = corroborationHorizons,
                Arms = corroborationArms,
                QuotaDecisionID = new CortexPolicyQuotaDecisionID(1),
            };
            try
            {
                byte[] packet = TapePacketCreator.EncodePolicyBoundaryReceipt(
                    policy, HomeostatPolicyBoundaryDomain.Instance, in fundedReceipt);
                packetRoundTrip = PolicyBoundaryTapeVerifier.TryRead(packet, HomeostatPolicyBoundaryDomain.Instance,
                    out PolicyBoundaryForkReceipt decoded, out CortexPolicyID decodedPolicy)
                    && decodedPolicy.Equals(policy)
                    && PolicyBoundaryObligation.ComputeReceiptDigest(in decoded) == PolicyBoundaryObligation.ComputeReceiptDigest(in fundedReceipt);
                string packetText = Encoding.ASCII.GetString(packet);
                legacyExecutionSchemaRejected = !PolicyBoundaryTapeVerifier.TryRead(
                    Encoding.ASCII.GetBytes(packetText.Replace("execution-schema=7", "execution-schema=6", StringComparison.Ordinal)),
                    HomeostatPolicyBoundaryDomain.Instance, out _, out _);
                string missingPolicyPacket = packetText.Replace($"\tpolicy={policy.Value}", "", StringComparison.Ordinal);
                missingPolicyRejected = !PolicyBoundaryTapeVerifier.TryRead(Encoding.ASCII.GetBytes(missingPolicyPacket),
                    HomeostatPolicyBoundaryDomain.Instance, out _, out _);
                string mismatchedPolicyPacket = packetText.Replace($"\tpolicy={policy.Value}", "\tpolicy=foreign-policy", StringComparison.Ordinal);
                mismatchedPolicyRejected = !PolicyBoundaryTapeVerifier.TryRead(Encoding.ASCII.GetBytes(mismatchedPolicyPacket),
                    HomeostatPolicyBoundaryDomain.Instance, out _, out _);
                PolicyBoundaryArmReceipt omitted = corroborationArms[1] with { ExecutedCanonicalState = default };
                PolicyBoundaryForkReceipt missingScope = fundedReceipt with { Arms = [.. corroborationArms[..1], omitted, .. corroborationArms[2..]] };
                try { _ = TapePacketCreator.EncodePolicyBoundaryReceipt(policy, HomeostatPolicyBoundaryDomain.Instance, in missingScope); }
                catch (InvalidDataException) { scopeOmissionRejected = true; }
                PolicyBoundaryArmReceipt alien = corroborationArms[1] with
                {
                    ExecutedCanonicalState = new PolicyCanonicalStateID(new CortexPolicyID("foreign-policy"), PolicyCanonicalStateKinds.Homeostat, 1, 0x205UL),
                };
                PolicyBoundaryForkReceipt mismatchedScope = fundedReceipt with { Arms = [.. corroborationArms[..1], alien, .. corroborationArms[2..]] };
                try { _ = TapePacketCreator.EncodePolicyBoundaryReceipt(policy, HomeostatPolicyBoundaryDomain.Instance, in mismatchedScope); }
                catch (InvalidDataException) { scopeMismatchRejected = true; }
            }
            catch (InvalidDataException) { }
            PolicyBoundaryArmReceipt terminalForcedNull = corroborationArms.Single(arm =>
                arm.Arm == PolicyBoundaryArms.ForcedDivergentNull && arm.Horizon == corroborationHorizons[^1]);
            LoopClosureDigest terminalForcedNullOutcome = PolicyBoundaryDivergenceProof.ComputeOutcomeID(
                in fundedReceipt, in terminalForcedNull, terminalForcedNull.ExecutedAction,
                HomeostatPolicyBoundaryDomain.Instance);
            PaidDivergenceExecutionCorroboration execution = PaidDivergenceExecutionCorroboration.Create(
                new LoopClosureDigest(new string('a', 64)), new CortexPolicyQuotaDecisionID(1), fingerprint, 3, revision,
                DigestExecutionArms(in fundedReceipt), DigestExecutedDivergenceChildExecution(
                    terminalForcedNull.ExecutedDecisionID, terminalForcedNullOutcome),
                terminalForcedNull.ExecutedDecisionID, terminalForcedNullOutcome,
                terminalForcedNull.ExecutedOutcomeEventID, terminalForcedNull.ExecutedOutcomePayloadSHA256);
            fundedReceipt = fundedReceipt with { ExecutionCorroboration = execution };
                fundedReceipt.Validate(HomeostatPolicyBoundaryDomain.Instance);
            executionCorroborationRoundTrip = fundedReceipt.ExecutionCorroboration is PaidDivergenceExecutionCorroboration restored
                && restored.PaidDivergenceExecutionCorroborationSHA256 == execution.PaidDivergenceExecutionCorroborationSHA256;
            try
            {
                PaidDivergenceExecutionCorroboration forgedExecution = new(
                    execution.ReadoutTrainingCorroborationSHA256, execution.QuotaDecisionID,
                    execution.QuotaReadoutFingerprint,
                    execution.QuotaCandidateFingerprint, execution.FundingCandidateRevision,
                    execution.ForkArmSHA256, execution.ChildExecutionReceiptSHA256,
                    candidateExecutionReceipt.DecisionID, execution.ExecutedDivergenceOutcomeID,
                    execution.PaidDivergenceExecutionCorroborationSHA256);
                _ = fundedReceipt with { ExecutionCorroboration = forgedExecution };
                executionChildIdentityRejected = false;
            }
            catch (InvalidDataException) { executionChildIdentityRejected = true; }
        }
        catch (InvalidDataException) { }
        bool passed = splitAccepted && candidateAgreementAccepted && earlyExecutionPreserved && aliasRejected && forcedAliasRejected && crossIdentityRejected && missingExecutionStepRejected && missingForcedCustodyRejected && nonDivergentForcedRejected
            && legacyExecutionSchemaRejected
            && executionCorroborationRoundTrip && executionChildIdentityRejected && packetRoundTrip && missingPolicyRejected && mismatchedPolicyRejected && scopeOmissionRejected && scopeMismatchRejected;
        output.WriteLine($"  policy-boundary divergence temporal split · parent-raw={parent.RawCandidateAction} child-raw={candidateExecutionReceipt.RawCandidateAction} child-executed={candidateExecutionReceipt.Action} forced-null={forcedNull.Action} · candidate-agreement={(candidateAgreementAccepted ? "accepted" : "REJECTED")} · early-execution={(earlyExecutionPreserved ? "preserved" : "LOST")} · alias={(aliasRejected ? "rejected" : "ACCEPTED")} · forced-alias={(forcedAliasRejected ? "rejected" : "ACCEPTED")} · cross-identity={(crossIdentityRejected ? "rejected" : "ACCEPTED")} · execution-step={(missingExecutionStepRejected ? "required" : "OPTIONAL")} · forced-custody={(missingForcedCustodyRejected ? "required" : "OPTIONAL")} · forced-divergence={(nonDivergentForcedRejected ? "required" : "OPTIONAL")} · packet={(packetRoundTrip ? "round-trip" : "FAIL")} legacy-schema={(legacyExecutionSchemaRejected ? "rejected" : "ACCEPTED")} scope-omission={(scopeOmissionRejected ? "rejected" : "ACCEPTED")} scope-mismatch={(scopeMismatchRejected ? "rejected" : "ACCEPTED")} · execution={(executionCorroborationRoundTrip ? "round-trip" : "FAIL")} child-identity={(executionChildIdentityRejected ? "rejected" : "ACCEPTED")} · {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    private static (bool Passed, string Receipt) VerifySourceSuccessorReplayFixture(
        Cortex source,
        CortexForkSeed seed,
        in CortexPolicyDecision sourceDecision,
        TapeEventID sourceDecisionEventID,
        in CortexPolicyBoundarySourceCorroboration sourceCorroboration,
        PolicyBoundaryObligation obligation)
    {
        const ulong fundingID = 0x205F1D1UL;
        const ulong sourceFundingCandidate = 0x5802UL;
        ulong sourceRawCandidate = sourceCorroboration.CandidateFingerprint;
        ulong sourceReadoutFingerprint = sourceCorroboration.ReadoutFingerprint;
        ulong sourceOccurrence = sourceCorroboration.OccurrenceDigest;
        bool historicalContinuationAccepted = false;
        bool historicalContinuationCauseTamperRejected = false;
        bool historicalContinuationSourceStateTamperRejected = false;
        bool historicalContinuationCustodyMutationRejected = false;
        bool successorDivergenceAccepted = false;
        bool successorDivergenceRelationNullsRejected = false;
        const ulong successorSupport = 0x206UL;
        const ulong sourceSeed = 0x205206UL;
        PolicyCanonicalStateID sourceState = new(HomeostatPolicyBoundaryDomain.Instance.PolicyID, PolicyCanonicalStateKinds.Homeostat, 1, 0x205UL);
        PolicyCanonicalStateID successorState = new(HomeostatPolicyBoundaryDomain.Instance.PolicyID, PolicyCanonicalStateKinds.Homeostat, 1, 0x206UL);
        GrammarRevisionID revision = sourceCorroboration.ReadoutRevision;
        GrammarPolicyContextKey successorContext = new(successorState, HomeostatPolicyBoundaryDomain.Instance.Schema.ActionCount,
            source.Config.Learning.Policies.ReadoutDeliberationQuota);
        string sourceRootDirectory = source.CurrentRun.Dir;
        string sourceRunID = Path.GetFileName(sourceRootDirectory);
        PolicyState sourceStateData = source.GetPolicy(HomeostatPolicyBoundaryDomain.Instance.PolicyID);
        CortexPolicyModes savedMode = sourceStateData.Mode;
        CortexPolicyAuthorities savedAuthority = sourceStateData.Authority;
        GrammarRevisionID savedRevision = sourceStateData.ReadoutCandidateRevision;
        ulong savedCandidate = sourceStateData.ReadoutCandidateFingerprint;
        PolicyCanonicalStateID savedCanonicalState = sourceStateData.ReadoutCandidateState;
        ulong savedSupport = sourceStateData.ReadoutCandidateOccurrenceDigest;
        int savedAction = sourceStateData.ReadoutCandidateAction;
        bool savedPending = sourceStateData.ReadoutCandidatePending;
        int savedComparisons = sourceStateData.ShadowComparisons;
        int savedAgreements = sourceStateData.ShadowAgreements;
        int savedMisses = sourceStateData.EmulationMisses;
        CortexPolicyPendingForcedTrialIntent savedForcedIntent = sourceStateData.PendingForcedTrialIntent;
        CortexPolicyQuotaDecisionID savedActiveQuotaID = sourceStateData.ActiveTrialQuotaID;
        bool savedSuppressTrialPackets = sourceStateData.SuppressTrialPackets;
        int savedTrialGrammarExecutionsRemaining = sourceStateData.TrialGrammarExecutionsRemaining;
        int savedTrialActionOffset = sourceStateData.TrialActionOffset;
        ulong? savedTrialForcedDivergenceSeed = sourceStateData.TrialForcedDivergenceSeed;
        ulong savedTrialForcedDivergenceExecutions = sourceStateData.TrialForcedDivergenceExecutions;
        CortexPolicySelectionCauses savedTrialExecutionCause = sourceStateData.TrialExecutionCause;
        CortexPolicyTrialExecutionOutcomes savedTrialExecutionOutcome = sourceStateData.TrialExecutionOutcome;
        CortexPolicyDecision? savedTrialExecutionCorroboration = sourceStateData.TrialExecutionCorroboration;
        ulong savedTrialExecutionReadoutFingerprint = sourceStateData.TrialExecutionReadoutFingerprint;
        int savedTrialExecutionStep = sourceStateData.TrialExecutionStep;
        long savedTrialRequestCount = sourceStateData.TrialRequestCount;
        long savedTrialGuardAdmittedCount = sourceStateData.TrialGuardAdmittedCount;
        CortexPolicyDecision? savedTrialLastRequest = sourceStateData.TrialLastRequest;
        int savedTrialLastRequestStep = sourceStateData.TrialLastRequestStep;
        PolicyTrialExecutionHistory savedHistoricalTrialExecution = sourceStateData.HistoricalTrialExecution;
        bool savedTrialFrozen = sourceStateData.TrialFrozen;
        try
        {
            if (source._runtimeTape is null || source._runtimeJournal is null || revision == GrammarRevisionID.Zero)
                return (false, "missing-source-runtime");
            CortexPolicyBoundarySourceCorroboration source205Corroboration = sourceCorroboration with { CanonicalState = sourceState };
            source205Corroboration = source205Corroboration with { CorroborationDigest = source205Corroboration.ComputeDigest() };
            TapeEventID source205CorroborationEventID = TapePacketCreator.AppendPolicyBoundarySourceCorroboration(
                source._runtimeTape, source._runtimeJournal, source.Step, in source205Corroboration);
            CortexPolicyQuotaDecisionID custodyQuotaID = new(fundingID);
            string? custodyDigest = source.PersistPolicyBoundarySeed(custodyQuotaID, HomeostatPolicyBoundaryDomain.Instance.PolicyID,
                sourceFundingCandidate, checked(seed.NextStep - 1), revision, seed,
                sourceDecision.DecisionID.Value, sourceDecisionEventID.Value, sourceOccurrence,
                sourceRawCandidate, sourceReadoutFingerprint, CortexPolicyQuotaDecisions.Paid, sourceState);
            if (custodyDigest is null)
                return (false, "source-custody-not-persisted");
            // Keep the funding authority only in the source/root run.  The forced
            // child deliberately receives no local copy of this row; custody must
            // join back to this exact Paid verdict during rearm.
            CortexPolicyTrialQuotaDecision sourceFunding = new(
                custodyQuotaID, HomeostatPolicyBoundaryDomain.Instance.PolicyID, sourceFundingCandidate,
                checked(seed.NextStep - 1), 1, 1, 1, 1,
                CortexPolicyQuotaDecisions.Paid, 1, 0)
            {
                CandidateState = CortexPolicyTrialCandidateStates.Active,
                CandidateOriginStep = checked(seed.NextStep - 1),
                CandidateCurrentStep = checked(seed.NextStep - 1),
                CandidateRequiredStep = -1,
                CandidateRevision = revision,
                CanonicalState = sourceState,
                ReadoutFingerprint = sourceReadoutFingerprint,
                AllocationIdentity = "fixture-source",
                AllocationDigest = CortexPolicyTrialAllocation.ComputeDigest(
                    HomeostatPolicyBoundaryDomain.Instance.PolicyID, CortexPolicyAuthorities.Grammar, 1, "fixture-source"),
                AllocationArmSteps = 1,
                SeedAuditOnlyDigest = custodyDigest,
            };
            source.AppendPolicyTrialQuota(in sourceFunding);
            CortexPolicyTrialQuotaDecision resumedFunding = sourceFunding with
            {
                Decision = CortexPolicyQuotaDecisions.Reused,
                UsedSteps = 0,
                RemainingQuota = sourceFunding.RemainingQuota,
            };
            source.AppendPolicyTrialQuota(in resumedFunding);
            source.FlushPolicyJournalBuffer();
            sourceStateData.Mode = CortexPolicyModes.Autonomic;
            sourceStateData.Authority = CortexPolicyAuthorities.Grammar;
            sourceStateData.ReadoutCandidateRevision = revision;
            sourceStateData.ReadoutCandidateFingerprint = sourceFundingCandidate;
            sourceStateData.ReadoutCandidateState = sourceState;
            sourceStateData.ReadoutCandidateOccurrenceDigest = sourceOccurrence;
            sourceStateData.ReadoutCandidatePending = false;
            sourceStateData.ShadowComparisons = 1;
            sourceStateData.ShadowAgreements = 1;
            sourceStateData.EmulationMisses = 0;
            sourceStateData.ActiveTrialQuotaID = custodyQuotaID;
            sourceStateData.SuppressTrialPackets = true;
            sourceStateData.TrialGrammarExecutionsRemaining = -1;
            sourceStateData.TrialActionOffset = 0;
            sourceStateData.TrialForcedDivergenceSeed = sourceSeed;
            sourceStateData.TrialForcedDivergenceExecutions = 0;
            sourceStateData.TrialExecutionCause = CortexPolicySelectionCauses.TrialOverride;
            sourceStateData.TrialExecutionOutcome = CortexPolicyTrialExecutionOutcomes.NotAttempted;
            sourceStateData.TrialExecutionCorroboration = null;
            sourceStateData.TrialExecutionReadoutFingerprint = 0;
            sourceStateData.TrialExecutionStep = -1;
            sourceStateData.TrialRequestCount = 0;
            sourceStateData.TrialGuardAdmittedCount = 0;
            sourceStateData.TrialLastRequest = null;
            sourceStateData.TrialLastRequestStep = -1;
            sourceStateData.HistoricalTrialExecution = default;
            sourceStateData.TrialFrozen = false;
            sourceStateData.PendingForcedTrialIntent = new(
                HomeostatPolicyBoundaryDomain.Instance.PolicyID, fundingID, CortexPolicyQuotaDecisions.Paid, sourceSeed,
                sourceDecision.DecisionID.Value, sourceDecisionEventID.Value, source205CorroborationEventID.Value,
                sourceOccurrence, sourceRawCandidate, sourceFundingCandidate, sourceReadoutFingerprint, revision, sourceState,
                sourceReadoutFingerprint, sourceFundingCandidate, revision, sourceOccurrence, sourceState,
                obligation.ID.Value, (byte)PolicyBoundaryArms.ForcedDivergentNull,
                checked((ushort)(400 + (int)HomeostatPolicyFeatureIDs.Criticality)), sourceRunID, custodyDigest);
            CortexPolicyCheckpointDelta delta = source.CapturePolicyCheckpointDelta();
            Cortex child = new(source.Config);
            foreach (var pair in source._policies) child.RegisterPolicy(pair.Value.Schema);
            if (source.TryGetPolicyBoundaryDomain(obligation.Identity.Policy, out IPolicyBoundaryDomain sourceDomain))
                child.RegisterPolicyBoundaryDomain(sourceDomain);
            child.RegisterPolicyBoundaryObligation(obligation);
            string focusedChildPath = Path.Combine(sourceRootDirectory, "children", "focused-source-successor");
            Directory.CreateDirectory(Path.GetDirectoryName(focusedChildPath)!);
            if (Directory.Exists(focusedChildPath)) Directory.Delete(focusedChildPath, recursive: true);
            Run childRun = global::Cogito.Run.Create(focusedChildPath);
            string childFundingJournal = childRun.PathOf(PolicyTrialQuotaJournalFile);
            if (File.Exists(childFundingJournal)) File.Delete(childFundingJournal);
            child._runtimeRun = childRun;
            // Policy deltas carry state/economy, while the source corroboration and
            // decision events live in the tape keyframe.  Clone both runtime
            // logs so replay preserves the original event IDs without letting
            // the child append its forced decision into the parent tape.
            if (source._runtimeTape is null || source._runtimeJournal is null)
                throw new InvalidDataException("missing-source-runtime");
            Tape childTape = new();
            childTape.MountLog(new MemoryStream());
            using (MemoryStream tapeBytes = new())
            {
                using (CkptWriter writer = new(tapeBytes)) source._runtimeTape.Save(writer);
                tapeBytes.Position = 0;
                using CkptReader reader = new(tapeBytes);
                childTape.Load(reader);
            }
            Journal childJournal = new();
            using (MemoryStream journalBytes = new())
            {
                using (CkptWriter writer = new(journalBytes)) source._runtimeJournal.Save(writer);
                journalBytes.Position = 0;
                using CkptReader reader = new(journalBytes);
                childJournal.Load(reader);
            }
            child._runtimeTape = childTape;
            child._runtimeJournal = childJournal;
            child._runtimeInstallRevision = source._runtimeInstallRevision;
            child.SealPolicyReadoutRoster();
            CortexPolicyCheckpointDelta replayDelta;
            using (MemoryStream encodedDelta = new())
            {
                CortexPolicyCheckpointDelta serializableDelta = delta with
                {
                    TrialQuotaCursor = 0, TrialCompletionCursor = 0,
                    ReadoutQuotaCursor = 0, ReadoutCompletionCursor = 0,
                    AllocationCursor = 0,
                    // The live parent cache contains raw feature contexts from the
                    // ordinary loop.  A child replay must not sweep those entries
                    // under its canonical-only successor proof; carry the policy
                    // state while rebuilding the one canonical successor entry below.
                    States = delta.States.Select(static state => state with
                    {
                        Cache = state.Cache with
                        {
                            Entries = Array.Empty<PolicyReadoutCacheEntryReplacement>(),
                            QuotaJournal = Array.Empty<GrammarPolicyReadoutQuotaRecord>(),
                        },
                    }).ToArray(),
                };
                using (CkptWriter writer = new(encodedDelta)) WriteCheckpointDelta(writer, in serializableDelta);
                byte[] bytes = encodedDelta.ToArray();
                using MemoryStream roundTrip = new(bytes);
                using CkptReader reader = new(roundTrip);
                replayDelta = ReadCheckpointDelta(reader);
                if (reader.RemainingBytes != 0) throw new InvalidDataException("forced successor delta replay left trailing bytes");
                using MemoryStream replayBytes = new();
                using (CkptWriter writer = new(replayBytes)) WriteCheckpointDelta(writer, in replayDelta);
                if (!bytes.AsSpan().SequenceEqual(replayBytes.ToArray()))
                    throw new InvalidDataException("forced successor checkpoint delta SaveLoadSave drifted");
            }
            child.ApplyPolicyCheckpointDelta(in replayDelta);
            if (child._policyTrialQuotaByID.ContainsKey(custodyQuotaID)
                || File.Exists(childFundingJournal))
                throw new InvalidDataException("forced successor child retained a local parent funding row");
            PolicyState successor = child.GetPolicy(HomeostatPolicyBoundaryDomain.Instance.PolicyID);
            child.BindPendingForcedTrialIntent(HomeostatPolicyBoundaryDomain.Instance.PolicyID,
                fundingID, CortexPolicyQuotaDecisions.Paid,
                sourceDecision.DecisionID.Value, sourceDecisionEventID.Value, source205CorroborationEventID.Value,
                sourceOccurrence, sourceRawCandidate, sourceReadoutFingerprint, sourceFundingCandidate, revision,
                in sourceState, obligation.ID.Value, (byte)PolicyBoundaryArms.ForcedDivergentNull,
                checked((ushort)(400 + (int)HomeostatPolicyFeatureIDs.Criticality)), sourceRunID, custodyDigest);
            CortexForkMaterializationContract completionContract = new(
                sourceRunID, new CortexPolicyQuotaDecisionID(fundingID).ToString(),
                Path.GetFileName(child.CurrentRun.Dir), seed.ColdSeedDigest);
            CortexPolicyTrialAuthorityIdentity completionIdentity = new(
                new(sourceReadoutFingerprint), new(sourceFundingCandidate), revision)
            {
                CanonicalState = sourceState,
            };
            CortexForkArm<PolicyBoundaryTrialOutcome> completionArm = CreateHomeostatBoundaryArm(
                child.CurrentRun.Dir, seed.NextStep, 1, PolicyBoundaryArms.ForcedDivergentNull,
                child.Config.ToRunConfig(null), completionIdentity, CortexPolicyAuthorities.Grammar,
                forced: true, railRole: CortexForkRailRoles.ForcedNull,
                parentRunID: sourceRunID, materializationContract: completionContract,
                obligation: obligation.ID, candidateBoundary: PolicyBoundaryRational.Zero);
            child.DisableAutonomicSpawning();
            if (child._runtimeInstallRevision is not { } childInstallRevision
                ) throw new InvalidDataException("missing-child-publication");
            // The child publication is intentionally cloned from the source for
            // tape custody.  Mint the next publication through the real grammar
            // publication path so the successor revision is an actual runtime
            // transition, not a number written only into the policy receipt.
            GrammarRevisionID successorRevision = revision.Next();
            RePairResult successorGrammar = childInstallRevision.Snapshot.ToRePairResult(cloneArrays: false);
            global::Cogito.Grammar.InstallRevision successorInstallRevision = global::Cogito.Grammar.InstallRevision.FromRePair(
                successorRevision, childInstallRevision.Revision, in successorGrammar,
                childInstallRevision.Snapshot, childInstallRevision.Overlay);
            child.SwapGrammar(in successorInstallRevision, advancePolicies: false);
            // Sweep the empty replay cache once before installing the deliberately
            // forged-but-bound successor readout used by this custody falsifier.
            _ = GrammarPolicyReadout.ReadCanonicalCache(in successorInstallRevision, HomeostatPolicyBoundaryDomain.Instance.PolicyID,
                in successorState, HomeostatPolicyBoundaryDomain.Instance.Schema.ActionCount,
                successorContext.DeliberationDepth, successor.ReadoutCache);
            GrammarPolicyDecision grammarDecision = new(1, 1, 1, successorRevision,
                new GrammarContinuationQuotaCompletion(1, 1, 0, 1, 1),
                GrammarPolicyReadout.ComputeStateFingerprint(HomeostatPolicyBoundaryDomain.Instance.PolicyID, in successorState))
            { OccurrenceDigest = successorSupport };
            CortexPolicyQuotaDecisionID successorReadoutQuotaID = GrammarPolicyReadout.ComputeQuotaID(
                HomeostatPolicyBoundaryDomain.Instance.PolicyID, successorRevision, 0, in successorContext, in grammarDecision);
            successor.ReadoutCache.StoreBound(HomeostatPolicyBoundaryDomain.Instance.PolicyID, successorRevision, in successorContext, in grammarDecision,
                successorReadoutQuotaID);
            ulong successorCandidate = GrammarPolicyReadout.ComputeCandidateFingerprint(HomeostatPolicyBoundaryDomain.Instance.PolicyID, in successorState, in grammarDecision);
            successor.CanonicalCandidates[successorState] = new PolicyState.CanonicalCandidateEvidence(
                successorState, grammarDecision.Action, successorCandidate, successorSupport, successorRevision, child.Step)
            { Comparisons = 1, Agreements = 1 };
            successor.ReadoutCandidateRevision = successorRevision;
            successor.ReadoutCandidateFingerprint = successorCandidate;
            successor.ReadoutCandidateState = successorState;
            successor.ReadoutCandidateOccurrenceDigest = successorSupport;
            successor.ReadoutCandidateAction = grammarDecision.Action;
            successor.ShadowComparisons = child.Config.Learning.Policies.ShadowDecisions;
            successor.ShadowAgreements = child.Config.Learning.Policies.ShadowDecisions;
            successor.EmulationMisses = 0;
            successor.CanonicalProgramDigestDirty = true;
            RefreshCanonicalProgramDigest(successor, HomeostatPolicyBoundaryDomain.Instance.PolicyID);
            // The successor changes the execution scope, not the paid readout
            // program. Keep the source program identity pinned across the epoch split.
            successor.ReadoutCandidateSetDigest = sourceReadoutFingerprint;
            successor.CanonicalProgramDigestDirty = false;
            ulong activeFingerprint = ReadActivePolicyFingerprint(successor);
            successor.AssayedReadoutFingerprint = activeFingerprint;
            successor.AssayedFingerprint = successorCandidate;
            bool successorScopeVerified = child.TryGrantVerifiedPolicyScope(HomeostatPolicyBoundaryDomain.Instance.PolicyID,
                in successorState, activeFingerprint, successorCandidate, successorSupport, successorRevision);
            successor.Authority = CortexPolicyAuthorities.Grammar;
            successor.TrialGrammarExecutionsRemaining = 1;
            bool readyBefore = child.IsPolicyReadoutReady(HomeostatPolicyBoundaryDomain.Instance.PolicyID, activeFingerprint);
            bool scopeBefore = child.IsVerifiedPolicyScope(HomeostatPolicyBoundaryDomain.Instance.PolicyID, in successorState,
                activeFingerprint, successorCandidate, successorSupport, successorRevision);
            CortexPolicyPendingForcedTrialIntent successorPending = successor.PendingForcedTrialIntent with
            {
                ReadoutFingerprint = activeFingerprint,
                CandidateFingerprint = successorCandidate,
                CandidateRevision = successorRevision,
                SuccessorOccurrenceDigest = successorSupport,
                CanonicalState = successorState,
            };
            CortexPolicyPendingForcedTrialRearmEvaluation custodyEvaluation = child.EvaluatePendingForcedTrialRearm(
                HomeostatPolicyBoundaryDomain.Instance.PolicyID, in successorPending, true, in successorState, activeFingerprint,
                successorCandidate, successorSupport, successorRevision);
            bool custodyBefore = readyBefore && scopeBefore && custodyEvaluation.Allowed;
            bool epochSupportTamperRejected = false;
            bool epochCandidateTamperRejected = false;
            bool rearmAtomicRejected = false;
            if (custodyBefore)
            {
                CortexPolicyPendingForcedTrialIntent pendingBeforeRearm = successor.PendingForcedTrialIntent;
                CortexPolicyQuotaDecisionID activeBeforeRearm = successor.ActiveTrialQuotaID;
                bool suppressBeforeRearm = successor.SuppressTrialPackets;
                rearmAtomicRejected = !child.TryBindVerifiedSuccessorTrialEpoch(HomeostatPolicyBoundaryDomain.Instance.PolicyID,
                        in successorPending, in successorState, activeFingerprint, successorCandidate,
                        successorSupport ^ 1UL, successorRevision)
                    && successor.PendingForcedTrialIntent.Equals(pendingBeforeRearm)
                    && successor.ActiveTrialQuotaID.Equals(activeBeforeRearm)
                    && successor.SuppressTrialPackets == suppressBeforeRearm;
                try
                {
                    child.RestorePaidPolicyTrialEpoch(HomeostatPolicyBoundaryDomain.Instance.PolicyID,
                        new CortexPolicyQuotaDecisionID(successorPending.QuotaID), successorPending.AuditOnlyDigest,
                        CortexPolicySelectionCauses.TrialOverride, sourceSeed, in successorState,
                        activeFingerprint, successorCandidate, successorSupport ^ 1UL, successorRevision);
                }
                catch (InvalidDataException) { epochSupportTamperRejected = true; }
                try
                {
                    child.RestorePaidPolicyTrialEpoch(HomeostatPolicyBoundaryDomain.Instance.PolicyID,
                        new CortexPolicyQuotaDecisionID(successorPending.QuotaID), successorPending.AuditOnlyDigest,
                        CortexPolicySelectionCauses.TrialOverride, sourceSeed, in successorState,
                        activeFingerprint, successorCandidate ^ 1UL, successorSupport, successorRevision);
                }
                catch (InvalidDataException) { epochCandidateTamperRejected = true; }
            }
            string custodyParent = Path.GetFullPath(Path.Combine(child.CurrentRun.Dir, "..", ".."));
            bool custodyFile = Cortex.TryReadPolicyBoundarySeedCustodyDocument(custodyParent, successorPending.SourceRunID,
                new CortexPolicyQuotaDecisionID(successorPending.QuotaID).ToString(), out PolicyBoundarySeedCustody custodyDoc);
            CortexPolicyTrialQuotaDecision rootFundingDecision = default;
            bool rootFunding = custodyFile && child.TryReadRootPolicyFundingDecision(custodyParent, successorPending.SourceRunID,
                in successorPending, in custodyDoc, out rootFundingDecision, out _);
            if (custodyBefore)
            {
                // Bind the verified successor atomically. A failed custody or
                // scope check must leave the replay state byte-identical.
                if (!child.TryBindVerifiedSuccessorTrialEpoch(HomeostatPolicyBoundaryDomain.Instance.PolicyID, in successorPending,
                        in successorState, activeFingerprint, successorCandidate, successorSupport,
                        successorRevision))
                    throw new InvalidDataException("source-successor atomic rearm was rejected");
                child.RestorePaidPolicyTrialEpoch(HomeostatPolicyBoundaryDomain.Instance.PolicyID,
                    new CortexPolicyQuotaDecisionID(successorPending.QuotaID), successorPending.AuditOnlyDigest,
                    CortexPolicySelectionCauses.TrialOverride, sourceSeed, in successorState,
                    activeFingerprint, successorCandidate, successorSupport, successorRevision);
            }
            child._policyBoundaryTrialOverride = new PolicyBoundaryTrialOverride(
                HomeostatPolicyBoundaryDomain.Instance.PolicyID, obligation.ID, PolicyBoundaryArms.ForcedDivergentNull,
                checked((ushort)(400 + (int)HomeostatPolicyFeatureIDs.Criticality)),
                PolicyBoundaryRational.Parse("1"));
            MetricSample[] features = new MetricSample[successor.Schema.FeatureCount];
            for (int index = 0; index < features.Length; index++)
                features[index] = new MetricSample(new MetricID(checked((ushort)(400 + index))), NumericValue.FromI64(0));
            CortexPolicyDecision first = child.ChoosePolicyAction(HomeostatPolicyBoundaryDomain.Instance.PolicyID, 0, in successorState, features);
            PolicyTrialExecutionHistory firstHistory = successor.HistoricalTrialExecution;
            if (custodyFile && firstHistory.IsPresent)
            {
                try
                {
                    historicalContinuationAccepted = child.AuthenticatePolicyBoundaryContinuation(
                        in custodyDoc, CortexPolicySelectionCauses.TrialOverride, in sourceState)
                        == PolicyBoundaryContinuationModes.PreserveHistorical;
                }
                catch (InvalidDataException)
                {
                    historicalContinuationAccepted = false;
                }
                try
                {
                    _ = child.AuthenticatePolicyBoundaryContinuation(
                        in custodyDoc, CortexPolicySelectionCauses.GrammarCandidate, in sourceState);
                }
                catch (InvalidDataException)
                {
                    historicalContinuationCauseTamperRejected = true;
                }
                try
                {
                    _ = child.AuthenticatePolicyBoundaryContinuation(
                        in custodyDoc, CortexPolicySelectionCauses.TrialOverride, in successorState);
                }
                catch (InvalidDataException)
                {
                    historicalContinuationSourceStateTamperRejected = true;
                }
                try
                {
                    PolicyBoundarySeedCustody mutatedCustody = RonSerializer.Deserialize<PolicyBoundarySeedCustody>(custodyDoc.Encode());
                    mutatedCustody.readoutFingerprint ^= 1UL;
                    _ = child.AuthenticatePolicyBoundaryContinuation(
                        in mutatedCustody, CortexPolicySelectionCauses.TrialOverride, in sourceState);
                }
                catch (InvalidDataException)
                {
                    historicalContinuationCustodyMutationRejected = true;
                }
            }
            bool firstOverride = first.Readout.SelectionCause == CortexPolicySelectionCauses.TrialOverride
                && firstHistory.IsPresent
                && firstHistory.Outcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted;
            bool executionFingerprintPinned = child.TryReadPolicyTrialExecutionReceipt(HomeostatPolicyBoundaryDomain.Instance.PolicyID,
                out _, out _, out _, out _, out _, out _, out _, out _, out ulong executionReadoutFingerprint,
                out _)
                && executionReadoutFingerprint == activeFingerprint;
            bool executionFingerprintRevisionMismatch = executionFingerprintPinned
                && executionReadoutFingerprint != GrammarPolicyReadout.ComputeFingerprint(successorRevision, HomeostatPolicyBoundaryDomain.Instance.PolicyID);
            child.CommitPolicyCheckpointDelta();
            CortexPolicyCheckpointDelta executionDelta = child.CapturePolicyCheckpointDelta();
            // Replay starts from an empty policy economy, so promote the captured
            // suffix into a complete baseline image before exercising the codec.
            executionDelta = executionDelta with
            {
                TrialQuotaCursor = 0,
                TrialQuota = child._policyTrialQuotaDecisions.ToArray(),
                TrialCompletionCursor = 0,
                TrialCompletions = child._policyTrialCompletions.ToArray(),
                ReadoutQuotaCursor = 0,
                ReadoutQuota = child._policyReadoutQuotaDecisions.ToArray(),
                ReadoutCompletionCursor = 0,
                ReadoutCompletions = child._policyReadoutCompletions.ToArray(),
                AllocationCursor = 0,
                Allocations = child._policyReadoutAllocations.ToArray(),
            };
            using MemoryStream executionDeltaBytes = new();
            using (CkptWriter executionDeltaWriter = new(executionDeltaBytes))
                WriteCheckpointDelta(executionDeltaWriter, in executionDelta);
            executionDeltaBytes.Position = 0;
            CortexPolicyCheckpointDelta replayedExecutionDelta;
            using (CkptReader executionDeltaReader = new(executionDeltaBytes))
                replayedExecutionDelta = ReadCheckpointDelta(executionDeltaReader);
            Cortex executionReplay = new(child.Config);
            foreach (var pair in child._policies) executionReplay.RegisterPolicy(pair.Value.Schema);
            if (child.TryGetPolicyBoundaryDomain(obligation.Identity.Policy, out IPolicyBoundaryDomain childDomain))
                executionReplay.RegisterPolicyBoundaryDomain(childDomain);
            executionReplay.RegisterPolicyBoundaryObligation(obligation);
            executionReplay.SealPolicyReadoutRoster();
            executionReplay.ApplyPolicyCheckpointDelta(in replayedExecutionDelta);
            bool executionFingerprintSaveLoad = executionReplay.TryReadPolicyTrialExecutionReceipt(HomeostatPolicyBoundaryDomain.Instance.PolicyID,
                out _, out _, out _, out _, out _, out _, out _, out _, out ulong replayedExecutionReadoutFingerprint,
                out _)
                && replayedExecutionReadoutFingerprint == executionReadoutFingerprint;
            PolicyBoundaryTrialOutcome completionOutcome = completionArm.ReadCompletion(child);
            bool productionCompletionVerified = completionOutcome.ExecutionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted
                && completionOutcome.ExecutedSelectionCause == CortexPolicySelectionCauses.TrialOverride
                && completionOutcome.ExecutedCandidateFingerprint == successorCandidate
                && completionOutcome.ExecutedReadoutOccurrenceDigest == successorSupport;
            PolicyBoundaryArmReceipt successorDivergenceArm = new(
                PolicyBoundaryArms.ForcedDivergentNull, 1, completionOutcome.PaidCloseDelta,
                completionOutcome.MatchedSpend, completionOutcome.ContinuityExact,
                completionOutcome.ChildProcessCompleted, completionOutcome.GrammarExecutionsDelta,
                completionOutcome.TrialAdaptationTransitions, completionOutcome.AdaptationEnabled)
            {
                ExecutionOutcome = completionOutcome.ExecutionOutcome,
                RequestCount = completionOutcome.RequestCount,
                GuardAdmittedCount = completionOutcome.GuardAdmittedCount,
                LastRequestDecisionID = completionOutcome.LastRequestDecisionID,
                LastRequestStep = completionOutcome.LastRequestStep,
                LastRequestReadout = completionOutcome.LastRequestReadout,
                ExecutedDecisionID = completionOutcome.ExecutedDecisionID,
                ExecutedStep = completionOutcome.ExecutedStep,
                ExecutedLaunchpadAction = completionOutcome.ExecutedLaunchpadAction,
                ExecutedRawCandidateAction = completionOutcome.ExecutedRawCandidateAction,
                ExecutedSelectedCandidateAction = completionOutcome.ExecutedSelectedCandidateAction,
                ExecutedAction = completionOutcome.ExecutedAction,
                ExecutedAuthority = completionOutcome.ExecutedAuthority,
                ExecutedSelectionCause = completionOutcome.ExecutedSelectionCause,
                ExecutedReadoutFingerprint = completionOutcome.ExecutedReadoutFingerprint,
                ExecutedReadoutRevision = completionOutcome.ExecutedReadoutRevision,
                ExecutedReadoutOccurrenceDigest = completionOutcome.ExecutedReadoutOccurrenceDigest,
                ExecutedCandidateFingerprint = completionOutcome.ExecutedCandidateFingerprint,
                ExecutedCanonicalState = completionOutcome.ExecutedCanonicalState,
                ExecutedDecisionEventID = completionOutcome.ExecutedDecisionEventID,
                ExecutedOutcomeEventID = completionOutcome.ExecutedOutcomeEventID,
                ExecutedOutcomePayloadSHA256 = completionOutcome.ExecutedOutcomePayloadSHA256,
                ForcedDivergenceSeed = completionOutcome.ForcedDivergenceSeed,
                Diverged = completionOutcome.ExecutedAction != completionOutcome.ExecutedLaunchpadAction
                    && completionOutcome.ExecutedAction != completionOutcome.ExecutedRawCandidateAction,
            };
            successorDivergenceAccepted = PolicyBoundaryDivergenceProof.AcceptExecutedReadoutRelation(
                in successorDivergenceArm, sourceReadoutFingerprint, sourceFundingCandidate,
                sourceOccurrence, revision.Value, HomeostatPolicyBoundaryDomain.Instance);
            PolicyBoundaryArmReceipt sameRevisionDivergenceArm = successorDivergenceArm with
            {
                ExecutedReadoutRevision = revision.Value,
                ExecutedReadoutOccurrenceDigest = sourceOccurrence,
                ExecutedCandidateFingerprint = sourceFundingCandidate ^ 1UL,
            };
            bool sameRevisionDivergenceRejected = !PolicyBoundaryDivergenceProof.AcceptExecutedReadoutRelation(
                in sameRevisionDivergenceArm,
                sourceReadoutFingerprint, sourceFundingCandidate, sourceOccurrence, revision.Value,
                HomeostatPolicyBoundaryDomain.Instance);
            PolicyBoundaryArmReceipt zeroSupportDivergenceArm = successorDivergenceArm with
            {
                ExecutedReadoutOccurrenceDigest = 0,
            };
            bool zeroSupportDivergenceRejected = !PolicyBoundaryDivergenceProof.AcceptExecutedReadoutRelation(
                in zeroSupportDivergenceArm,
                sourceReadoutFingerprint, sourceFundingCandidate, sourceOccurrence, revision.Value,
                HomeostatPolicyBoundaryDomain.Instance);
            PolicyBoundaryArmReceipt zeroCandidateDivergenceArm = successorDivergenceArm with
            {
                ExecutedCandidateFingerprint = 0,
            };
            bool zeroCandidateDivergenceRejected = !PolicyBoundaryDivergenceProof.AcceptExecutedReadoutRelation(
                in zeroCandidateDivergenceArm,
                sourceReadoutFingerprint, sourceFundingCandidate, sourceOccurrence, revision.Value,
                HomeostatPolicyBoundaryDomain.Instance);
            PolicyBoundaryArmReceipt wrongAuthorityDivergenceArm = successorDivergenceArm with
            {
                ExecutedAuthority = CortexPolicyAuthorities.Shadow,
            };
            bool wrongAuthorityDivergenceRejected = !PolicyBoundaryDivergenceProof.AcceptExecutedReadoutRelation(
                in wrongAuthorityDivergenceArm,
                sourceReadoutFingerprint, sourceFundingCandidate, sourceOccurrence, revision.Value,
                HomeostatPolicyBoundaryDomain.Instance);
            PolicyBoundaryArmReceipt invalidStateDivergenceArm = successorDivergenceArm with
            {
                ExecutedCanonicalState = default,
            };
            bool invalidStateDivergenceRejected = !PolicyBoundaryDivergenceProof.AcceptExecutedReadoutRelation(
                in invalidStateDivergenceArm,
                sourceReadoutFingerprint, sourceFundingCandidate, sourceOccurrence, revision.Value,
                HomeostatPolicyBoundaryDomain.Instance);
            successorDivergenceRelationNullsRejected = sameRevisionDivergenceRejected
                && zeroSupportDivergenceRejected
                && zeroCandidateDivergenceRejected
                && wrongAuthorityDivergenceRejected
                && invalidStateDivergenceRejected;
            bool secondStayedOutsideCompletedEpoch;
            try
            {
                CortexPolicyDecision second = child.ChoosePolicyAction(HomeostatPolicyBoundaryDomain.Instance.PolicyID, 0, in successorState, features);
                secondStayedOutsideCompletedEpoch = second.Readout.SelectionCause != CortexPolicySelectionCauses.TrialOverride
                    && successor.HistoricalTrialExecution == firstHistory;
            }
            catch (InvalidDataException)
            {
                secondStayedOutsideCompletedEpoch = successor.HistoricalTrialExecution == firstHistory;
            }
            bool exactlyOnce = firstOverride && secondStayedOutsideCompletedEpoch;
            // The second action intentionally consumes the live pending intent.  Re-pin
            // the preserved successor tuple for the read-only denial probes below.
            successor.ReadoutCandidateFingerprint = successorCandidate;
            successor.ReadoutCandidateRevision = successorRevision;
            successor.ReadoutCandidateState = successorState;
            successor.ReadoutCandidateOccurrenceDigest = successorSupport;
            successor.ShadowComparisons = child.Config.Learning.Policies.ShadowDecisions;
            successor.ShadowAgreements = child.Config.Learning.Policies.ShadowDecisions;
            successor.EmulationMisses = 0;
            successor.ReadoutLearnerEvidenceTrusted = true;
            successor.VerifiedScopes[successorState] = new PolicyVerifiedScopeEntry(
                successorState, activeFingerprint, successorCandidate, successorSupport, successorRevision);
            CortexPolicyPendingForcedTrialIntent pending = sourceStateData.PendingForcedTrialIntent;
            bool sourceTupleImmutable = sourceStateData.PendingForcedTrialIntent == pending
                && sourceStateData.ReadoutCandidateState == sourceState;
            CortexPolicyPendingForcedTrialRearmEvaluation sourceStateTamper = child.EvaluatePendingForcedTrialRearm(
                HomeostatPolicyBoundaryDomain.Instance.PolicyID, successorPending with { SourceCanonicalState = successorState }, true, in successorState,
                activeFingerprint, successorCandidate, successorSupport, successorRevision);
            CortexPolicyPendingForcedTrialRearmEvaluation successorTamper = child.EvaluatePendingForcedTrialRearm(
                HomeostatPolicyBoundaryDomain.Instance.PolicyID, successorPending with { SuccessorOccurrenceDigest = successorSupport + 1 }, true, in successorState,
                activeFingerprint, successorCandidate, successorSupport, successorRevision);
            CortexPolicyPendingForcedTrialRearmEvaluation verdictTamper = child.EvaluatePendingForcedTrialRearm(
                HomeostatPolicyBoundaryDomain.Instance.PolicyID, successorPending with { SourceQuotaDecision = CortexPolicyQuotaDecisions.Denied }, true, in successorState,
                activeFingerprint, successorCandidate, successorSupport, successorRevision);
            CortexPolicyPendingForcedTrialRearmEvaluation fundedCandidateSwap = child.EvaluatePendingForcedTrialRearm(
                HomeostatPolicyBoundaryDomain.Instance.PolicyID, successorPending with { SourceQuotaCandidateFingerprint = sourceRawCandidate }, true, in successorState,
                activeFingerprint, successorCandidate, successorSupport, successorRevision);
            CortexPolicyPendingForcedTrialRearmEvaluation rawSwap = child.EvaluatePendingForcedTrialRearm(
                HomeostatPolicyBoundaryDomain.Instance.PolicyID, successorPending with { SourceCandidateFingerprint = successorPending.SourceCandidateFingerprint ^ 1UL }, true, in successorState,
                activeFingerprint, successorCandidate, successorSupport, successorRevision);
            bool sourceStateTamperRejected = !sourceStateTamper.Allowed;
            bool successorTamperRejected = !successorTamper.Allowed;
            bool verdictTamperRejected = !verdictTamper.Allowed;
            bool fundedCandidateSwapRejected = !fundedCandidateSwap.Allowed;
            bool rawSwapRejected = !rawSwap.Allowed;
            bool sourceStateSpecies = sourceStateTamper.DenialSpecies
                == CortexPolicyPendingForcedTrialRearmDenialSpecies.AuditOnlySidecarMismatch;
            bool successorSpecies = successorTamper.DenialSpecies
                == CortexPolicyPendingForcedTrialRearmDenialSpecies.SuccessorOccurrenceMismatch;
            bool verdictSpecies = verdictTamper.DenialSpecies
                == CortexPolicyPendingForcedTrialRearmDenialSpecies.IntentNotBound;
            bool fundedSwapSpecies = fundedCandidateSwap.DenialSpecies
                == CortexPolicyPendingForcedTrialRearmDenialSpecies.AuditOnlySidecarMismatch;
            bool rawSwapSpecies = rawSwap.DenialSpecies
                == CortexPolicyPendingForcedTrialRearmDenialSpecies.AuditOnlySidecarMismatch;
            bool readinessSpecies;
            bool candidateSpecies;
            bool revisionSpecies;
            bool scopeSpecies;
            CortexPolicyPendingForcedTrialRearmEvaluation readinessProbe;
            CortexPolicyPendingForcedTrialRearmEvaluation candidateProbe;
            CortexPolicyPendingForcedTrialRearmEvaluation revisionProbe;
            CortexPolicyPendingForcedTrialRearmEvaluation scopeProbe;
            successor.ReadoutLearnerEvidenceTrusted = false;
            readinessProbe = child.EvaluatePendingForcedTrialRearm(HomeostatPolicyBoundaryDomain.Instance.PolicyID, in successorPending,
                true, in successorState, activeFingerprint, successorCandidate, successorSupport, successorRevision);
            successor.ReadoutLearnerEvidenceTrusted = true;
            candidateProbe = child.EvaluatePendingForcedTrialRearm(HomeostatPolicyBoundaryDomain.Instance.PolicyID, in successorPending,
                true, in successorState, activeFingerprint, successorCandidate ^ 1UL, successorSupport, successorRevision);
            revisionProbe = child.EvaluatePendingForcedTrialRearm(HomeostatPolicyBoundaryDomain.Instance.PolicyID, in successorPending,
                true, in successorState, activeFingerprint, successorCandidate, successorSupport,
                new GrammarRevisionID(successorRevision.Value ^ 1UL));
            PolicyVerifiedScopeEntry savedScope = successor.VerifiedScopes[successorState];
            PolicyTrialExecutionHistory savedHistory = successor.HistoricalTrialExecution;
            // Historical execution evidence is not a live scope authority.
            // Suppress both authorities for this negative probe so it exercises
            // the actual missing-scope denial species.
            successor.HistoricalTrialExecution = default;
            successor.VerifiedScopes.Remove(successorState);
            scopeProbe = child.EvaluatePendingForcedTrialRearm(HomeostatPolicyBoundaryDomain.Instance.PolicyID, in successorPending,
                true, in successorState, activeFingerprint, successorCandidate, successorSupport, successorRevision);
            successor.VerifiedScopes[successorState] = savedScope;
            successor.HistoricalTrialExecution = savedHistory;
            readinessSpecies = readinessProbe.DenialSpecies
                == CortexPolicyPendingForcedTrialRearmDenialSpecies.ReadoutNotReady;
            candidateSpecies = candidateProbe.DenialSpecies
                == CortexPolicyPendingForcedTrialRearmDenialSpecies.CurrentCandidateMismatch;
            revisionSpecies = revisionProbe.DenialSpecies
                == CortexPolicyPendingForcedTrialRearmDenialSpecies.CurrentRevisionMismatch;
            scopeSpecies = scopeProbe.DenialSpecies
                == CortexPolicyPendingForcedTrialRearmDenialSpecies.CanonicalScopeMissing;
            bool duplicateRejected = false;
            CortexPolicyPendingForcedTrialRearmDenialSpecies duplicateSpecies = CortexPolicyPendingForcedTrialRearmDenialSpecies.None;
            CortexPolicyTrialQuotaDecision conflictingFunding = resumedFunding with
            {
                CandidateFingerprint = resumedFunding.CandidateFingerprint ^ 1UL,
            };
            string fundingJournalPath = Path.Combine(sourceRootDirectory, PolicyTrialQuotaJournalFile);
            byte[] fundingJournalBefore = File.ReadAllBytes(fundingJournalPath);
            try
            {
                File.AppendAllText(fundingJournalPath, FormatPolicyTrialQuotaRow(in conflictingFunding) + "\n", Encoding.UTF8);
                child._rootFundingMemo.Clear();
                CortexPolicyPendingForcedTrialRearmEvaluation duplicate = child.EvaluatePendingForcedTrialRearm(
                    HomeostatPolicyBoundaryDomain.Instance.PolicyID, in successorPending, true, in successorState, activeFingerprint,
                    successorCandidate, successorSupport, successorRevision);
                duplicateRejected = !duplicate.Allowed;
                duplicateSpecies = duplicate.DenialSpecies;
                duplicateRejected = duplicateRejected
                    && duplicateSpecies == CortexPolicyPendingForcedTrialRearmDenialSpecies.RootQuotaMismatch;
            }
            finally { File.WriteAllBytes(fundingJournalPath, fundingJournalBefore); }
            bool custodySidecarMissing = false;
            bool rootFundingMissing = false;
            bool corroborationMismatch = false;
            string custodyPath = Path.Combine(custodyParent, PolicyBoundarySeedCustodyDirectory,
                new CortexPolicyQuotaDecisionID(successorPending.QuotaID).ToString(), PolicyBoundarySeedCustodyFile);
            byte[] custodyBytes = File.ReadAllBytes(custodyPath);
            try
            {
                File.Delete(custodyPath);
                CortexPolicyPendingForcedTrialRearmEvaluation missingCustody = child.EvaluatePendingForcedTrialRearm(
                    HomeostatPolicyBoundaryDomain.Instance.PolicyID, in successorPending, true, in successorState, activeFingerprint,
                    successorCandidate, successorSupport, successorRevision);
                custodySidecarMissing = missingCustody.DenialSpecies
                    == CortexPolicyPendingForcedTrialRearmDenialSpecies.AuditOnlySidecarMissing;
            }
            finally { File.WriteAllBytes(custodyPath, custodyBytes); }
            byte[] rootFundingBytes = File.ReadAllBytes(fundingJournalPath);
            try
            {
                File.Delete(fundingJournalPath);
                child._rootFundingMemo.Clear();
                CortexPolicyPendingForcedTrialRearmEvaluation missingRoot = child.EvaluatePendingForcedTrialRearm(
                    HomeostatPolicyBoundaryDomain.Instance.PolicyID, in successorPending, true, in successorState, activeFingerprint,
                    successorCandidate, successorSupport, successorRevision);
                rootFundingMissing = missingRoot.DenialSpecies
                    == CortexPolicyPendingForcedTrialRearmDenialSpecies.RootQuotaMissing;
            }
            finally { File.WriteAllBytes(fundingJournalPath, rootFundingBytes); }
            CortexPolicyPendingForcedTrialRearmEvaluation corroborationProbe = child.EvaluatePendingForcedTrialRearm(
                HomeostatPolicyBoundaryDomain.Instance.PolicyID, successorPending with { SourceCorroborationEventID = sourceDecisionEventID.Value },
                true, in successorState, activeFingerprint, successorCandidate, successorSupport, successorRevision);
            corroborationMismatch = corroborationProbe.DenialSpecies is
                CortexPolicyPendingForcedTrialRearmDenialSpecies.SourceCorroborationMismatch or
                CortexPolicyPendingForcedTrialRearmDenialSpecies.SourceCorroborationMissing;
            CortexPolicyPendingForcedTrialIntent stateOnlyIntent = default(CortexPolicyPendingForcedTrialIntent) with
            { Policy = HomeostatPolicyBoundaryDomain.Instance.PolicyID, SourceQuotaDecision = CortexPolicyQuotaDecisions.Denied };
            CortexPolicyPendingForcedTrialRearmEvaluation unboundReceipt = CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
                CortexPolicyPendingForcedTrialRearmDenialSpecies.IntentNotBound, in stateOnlyIntent, 0xB1UL, new GrammarRevisionID(1)) with
            {
                Policy = HomeostatPolicyBoundaryDomain.Instance.PolicyID, QuotaID = 0xD1UL, SourceDecisionID = 0xD2UL,
                SourceDecisionEventID = 3, SourceCorroborationEventID = 4, SourceOccurrenceDigest = 5,
                SourceCandidateFingerprint = 6, SourceQuotaCandidateFingerprint = 7,
                SourceReadoutFingerprint = 8, SourceCandidateRevision = new GrammarRevisionID(9),
                ReadoutFingerprint = 10, OccurrenceDigest = 11, Arm = 1, FeatureID = 2,
                ObligationID = "unbound", SourceRunID = "unbound", AuditOnlyDigest = "unbound",
            };
            CortexPolicyPendingForcedTrialRearmEvaluation armedReceipt = CortexPolicyPendingForcedTrialRearmEvaluation.Denied(
                CortexPolicyPendingForcedTrialRearmDenialSpecies.AlreadyArmed, in stateOnlyIntent, 0xA1UL, new GrammarRevisionID(1)) with { Policy = HomeostatPolicyBoundaryDomain.Instance.PolicyID };
            CortexPolicyPendingForcedTrialRearmEvaluation latestArmedReceipt = armedReceipt with { CandidateFingerprint = 0xA2UL };
            ulong beforeStateOnlyIntent = child.ReadPendingForcedTrialRearmDenialCount(CortexPolicyPendingForcedTrialRearmDenialSpecies.IntentNotBound);
            ulong beforeAlreadyArmed = child.ReadPendingForcedTrialRearmDenialCount(CortexPolicyPendingForcedTrialRearmDenialSpecies.AlreadyArmed);
            long unboundEventID = child._runtimeTape?.NextId ?? -1;
            child.RecordPendingForcedTrialRearm(HomeostatPolicyBoundaryDomain.Instance.PolicyID, in unboundReceipt);
            bool unboundCanonical = child._runtimeTape is not null
                && child._runtimeTape.Resolve(new TapeEventID(unboundEventID), out byte[] unboundPacket)
                && TapePacketCreator.TryDecodePolicyPendingForcedTrialRearm(unboundPacket,
                    out CortexPolicyID unboundPolicy, out CortexPolicyPendingForcedTrialRearmEvaluation decodedUnbound)
                && unboundPolicy.Equals(HomeostatPolicyBoundaryDomain.Instance.PolicyID)
                && decodedUnbound.DenialSpecies == CortexPolicyPendingForcedTrialRearmDenialSpecies.IntentNotBound
                && decodedUnbound.CandidateFingerprint == unboundReceipt.CandidateFingerprint
                && decodedUnbound.CandidateRevision == unboundReceipt.CandidateRevision
                && decodedUnbound.QuotaID == 0 && decodedUnbound.SourceDecisionID == 0
                && decodedUnbound.SourceQuotaDecision == CortexPolicyQuotaDecisions.Denied
                && decodedUnbound.ObligationID is null && decodedUnbound.SourceRunID is null
                && decodedUnbound.AuditOnlyDigest is null;
            child.RecordPendingForcedTrialRearm(HomeostatPolicyBoundaryDomain.Instance.PolicyID, in armedReceipt);
            child.RecordPendingForcedTrialRearm(HomeostatPolicyBoundaryDomain.Instance.PolicyID, in latestArmedReceipt);
            byte[]? latestStateOnlyPacket = child._runtimeTape is not null
                && child._runtimeTape.Resolve(new TapeEventID(child._runtimeTape.NextId - 1), out byte[] stateOnlyPacket)
                ? stateOnlyPacket : null;
            CortexPolicyID stateOnlyPolicy = default;
            CortexPolicyPendingForcedTrialRearmEvaluation decodedStateOnly = default;
            bool stateOnlyPacketDecoded = latestStateOnlyPacket is not null
                && TapePacketCreator.TryDecodePolicyPendingForcedTrialRearm(latestStateOnlyPacket, out stateOnlyPolicy, out decodedStateOnly);
            bool stateOnlyDecoded = stateOnlyPacketDecoded
                && stateOnlyPolicy.Equals(HomeostatPolicyBoundaryDomain.Instance.PolicyID) && decodedStateOnly == latestArmedReceipt;
            bool RejectStateOnlyMutation(string field, string replacement)
            {
                if (latestStateOnlyPacket is null) return false;
                string encodedStateOnly = Encoding.ASCII.GetString(latestStateOnlyPacket);
                string marker = "\t" + field + "=";
                int markerOffset = encodedStateOnly.IndexOf(marker, StringComparison.Ordinal);
                if (markerOffset < 0) return false;
                int valueOffset = markerOffset + marker.Length;
                int valueEnd = encodedStateOnly.IndexOf('\t', valueOffset);
                if (valueEnd < 0) valueEnd = encodedStateOnly.Length;
                string mutated = encodedStateOnly[..valueOffset] + replacement + encodedStateOnly[valueEnd..];
                return !TapePacketCreator.TryDecodePolicyPendingForcedTrialRearm(
                    Encoding.ASCII.GetBytes(mutated), out _, out _);
            }
            (string Field, string Value)[] stateOnlyForbiddenOperands =
            [
                ("funding", "1"), ("outcome", "Allowed"), ("source_funding", "Paid"),
                ("source_decision", "1"), ("source_event", "1"), ("source_witness", "1"),
                ("source_support", "1"), ("source_candidate", "1"),
                ("source_funded_candidate", "1"), ("source_readout", "1"),
                ("source_revision", "1"), ("source_state", "bad"), ("readout", "1"),
                ("support", "1"), ("state", "bad"), ("arm", "1"), ("feature", "1"),
                ("obligation", "x"), ("bound", "1"), ("source_run", "x"), ("custody", "x"),
            ];
            bool stateOnlyOperandMutationRejected = stateOnlyForbiddenOperands.All(
                mutation => RejectStateOnlyMutation(mutation.Field, mutation.Value));
            ulong liveStateOnlyIntent = child.ReadPendingForcedTrialRearmDenialCount(CortexPolicyPendingForcedTrialRearmDenialSpecies.IntentNotBound);
            ulong liveAlreadyArmed = child.ReadPendingForcedTrialRearmDenialCount(CortexPolicyPendingForcedTrialRearmDenialSpecies.AlreadyArmed);
            long stateOnlyNextID = child._runtimeTape?.NextId ?? 0;
            if (child._runtimeTape is not null && child._runtimeTape.Count > 1)
                child._runtimeTape.Reorder(Enumerable.Range(0, child._runtimeTape.Count).Reverse().ToArray());
            child._rearmReceiptIndexTape = null;
            ulong resumedStateOnlyIntent = child.ReadPendingForcedTrialRearmDenialCount(CortexPolicyPendingForcedTrialRearmDenialSpecies.IntentNotBound);
            ulong resumedAlreadyArmed = child.ReadPendingForcedTrialRearmDenialCount(CortexPolicyPendingForcedTrialRearmDenialSpecies.AlreadyArmed);
            bool latestAfterReorder = child._latestRearmReceipt.TryGetValue(HomeostatPolicyBoundaryDomain.Instance.PolicyID, out CortexPolicyPendingForcedTrialRearmEvaluation latestAfterReorderReceipt)
                && latestAfterReorderReceipt == latestArmedReceipt;
            child.RecordPendingForcedTrialRearm(HomeostatPolicyBoundaryDomain.Instance.PolicyID, in latestArmedReceipt);
            bool duplicateStateOnlySuppressed = child._runtimeTape?.NextId == stateOnlyNextID;
            bool stateOnlyReceiptPassed = stateOnlyDecoded && liveStateOnlyIntent == beforeStateOnlyIntent + 1
                && liveAlreadyArmed == beforeAlreadyArmed + 2 && resumedStateOnlyIntent == liveStateOnlyIntent
                && resumedAlreadyArmed == liveAlreadyArmed && latestAfterReorder && duplicateStateOnlySuppressed
                && stateOnlyOperandMutationRejected && unboundCanonical;
            // InstallRevision advances may carry the paid epoch. The completed execution
            // must remain readable from its funding-bound history across each publication.
            GrammarRevisionID postInstallRevisionRevision = new(checked(successorRevision.Value + 1));
            GrammarSnapshot postInstallRevisionSnapshot = new(postInstallRevisionRevision,
                childInstallRevision.Snapshot.Rules, childInstallRevision.Snapshot.Compressed,
                childInstallRevision.Snapshot.TotalSavings, childInstallRevision.Snapshot.AlphabetSize);
            child.AdvancePolicyInstallRevision(new InstallRevision(
                postInstallRevisionSnapshot, GrammarDelta.CreateEmpty(postInstallRevisionRevision)));
            bool historicalReceiptAfterInstallRevision = child.TryReadPolicyTrialExecutionReceiptForQuota(
                HomeostatPolicyBoundaryDomain.Instance.PolicyID, new CortexPolicyQuotaDecisionID(fundingID),
                out CortexPolicyTrialExecutionOutcomes _, out _, out _, out _, out _, out _, out _, out _, out _, out _);
            bool historicalScopeAfterInstallRevision = child.TryReadPolicyTrialExecutionScope(
                HomeostatPolicyBoundaryDomain.Instance.PolicyID, out PolicyVerifiedScopeEntry historicalScope)
                && historicalScope.State == successorState
                && !child.GetPolicy(HomeostatPolicyBoundaryDomain.Instance.PolicyID).VerifiedScopes.ContainsKey(successorState);
            GrammarRevisionID secondInstallRevisionRevision = new(checked(postInstallRevisionRevision.Value + 1));
            GrammarSnapshot secondInstallRevisionSnapshot = new(secondInstallRevisionRevision,
                childInstallRevision.Snapshot.Rules, childInstallRevision.Snapshot.Compressed,
                childInstallRevision.Snapshot.TotalSavings, childInstallRevision.Snapshot.AlphabetSize);
            child.AdvancePolicyInstallRevision(new InstallRevision(
                secondInstallRevisionSnapshot, GrammarDelta.CreateEmpty(secondInstallRevisionRevision)));
            bool historicalReceiptAfterSecondInstallRevision = child.TryReadPolicyTrialExecutionReceiptForQuota(
                HomeostatPolicyBoundaryDomain.Instance.PolicyID, new CortexPolicyQuotaDecisionID(fundingID),
                out _, out _, out _, out _, out _, out _, out _, out _, out _, out _);
            bool nextFundingCannotReadPriorHistory = !child.TryReadPolicyTrialExecutionReceiptForQuota(
                HomeostatPolicyBoundaryDomain.Instance.PolicyID, new CortexPolicyQuotaDecisionID(fundingID + 1),
                out _, out _, out _, out _, out _, out _, out _, out _, out _, out _);
            PolicyState historicalState = child.GetPolicy(HomeostatPolicyBoundaryDomain.Instance.PolicyID);
            GrammarRevisionID savedHistoricalRevision = historicalState.ReadoutCandidateRevision;
            ulong savedHistoricalFingerprint = historicalState.ReadoutCandidateFingerprint;
            PolicyCanonicalStateID savedHistoricalCanonicalState = historicalState.ReadoutCandidateState;
            ulong savedHistoricalSupport = historicalState.ReadoutCandidateOccurrenceDigest;
            ulong savedHistoricalProgram = historicalState.ReadoutCandidateSetDigest;
            historicalState.ReadoutCandidateRevision = successorRevision;
            historicalState.ReadoutCandidateFingerprint = successorCandidate;
            historicalState.ReadoutCandidateState = successorState;
            historicalState.ReadoutCandidateOccurrenceDigest = successorSupport + 1;
            historicalState.ReadoutCandidateSetDigest = activeFingerprint;
            bool supportDriftReceiptLeaked = child.TryReadPolicyTrialExecutionReceipt(
                HomeostatPolicyBoundaryDomain.Instance.PolicyID, out CortexPolicyTrialExecutionOutcomes supportDriftOutcome,
                out _, out _, out _, out _, out _, out _, out _, out ulong supportDriftFingerprint, out _)
                && supportDriftOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted
                && supportDriftFingerprint == executionReadoutFingerprint;
            bool supportDriftRejected = !supportDriftReceiptLeaked
                && !child.TryReadPolicyTrialExecutionScope(HomeostatPolicyBoundaryDomain.Instance.PolicyID, out _);
            historicalState.ReadoutCandidateRevision = savedHistoricalRevision;
            historicalState.ReadoutCandidateFingerprint = savedHistoricalFingerprint;
            historicalState.ReadoutCandidateState = savedHistoricalCanonicalState;
            historicalState.ReadoutCandidateOccurrenceDigest = savedHistoricalSupport;
            historicalState.ReadoutCandidateSetDigest = savedHistoricalProgram;
            CortexPolicyCheckpointDelta historyDelta = child.CapturePolicyCheckpointDelta();
            // Replay starts from an empty policy image. Preserve the absolute
            // cursor contract by promoting this suffix to a complete baseline,
            // just as the execution round-trip above does.
            historyDelta = historyDelta with
            {
                TrialQuotaCursor = 0,
                TrialQuota = child._policyTrialQuotaDecisions.ToArray(),
                TrialCompletionCursor = 0,
                TrialCompletions = child._policyTrialCompletions.ToArray(),
                ReadoutQuotaCursor = 0,
                ReadoutQuota = child._policyReadoutQuotaDecisions.ToArray(),
                ReadoutCompletionCursor = 0,
                ReadoutCompletions = child._policyReadoutCompletions.ToArray(),
                AllocationCursor = 0,
                Allocations = child._policyReadoutAllocations.ToArray(),
            };
            byte[] historyDeltaBytes;
            using (MemoryStream encodedHistoryDelta = new())
            {
                using (CkptWriter writer = new(encodedHistoryDelta)) WriteCheckpointDelta(writer, in historyDelta);
                historyDeltaBytes = encodedHistoryDelta.ToArray();
            }
            CortexPolicyCheckpointDelta decodedHistoryDelta;
            using (MemoryStream encodedHistoryDelta = new(historyDeltaBytes))
            using (CkptReader reader = new(encodedHistoryDelta)) decodedHistoryDelta = ReadCheckpointDelta(reader);
            Cortex deltaHistoryReplay = new(child.Config);
            foreach (var pair in child._policies) deltaHistoryReplay.RegisterPolicy(pair.Value.Schema);
            deltaHistoryReplay.SealPolicyReadoutRoster();
            deltaHistoryReplay.ApplyPolicyCheckpointDelta(in decodedHistoryDelta);
            bool historicalDeltaRoundTrip = deltaHistoryReplay.TryReadPolicyTrialExecutionScope(
                HomeostatPolicyBoundaryDomain.Instance.PolicyID, out PolicyVerifiedScopeEntry deltaHistoryScope)
                && deltaHistoryScope == historicalScope;
            CortexPolicyReadoutStateReplacement[] malformedHistoryStates = decodedHistoryDelta.States
                .Select(static state => state.HistoricalTrialExecution.IsPresent
                    ? state with { HistoricalTrialExecution = state.HistoricalTrialExecution with { Scope = default } }
                    : state).ToArray();
            CortexPolicyCheckpointDelta malformedHistoryDelta = decodedHistoryDelta with { States = malformedHistoryStates };
            bool malformedHistoryRejected = false;
            try
            {
                Cortex malformedHistoryReplay = new(child.Config);
                foreach (var pair in child._policies) malformedHistoryReplay.RegisterPolicy(pair.Value.Schema);
                malformedHistoryReplay.SealPolicyReadoutRoster();
                malformedHistoryReplay.ApplyPolicyCheckpointDelta(in malformedHistoryDelta);
            }
            catch (InvalidDataException) { malformedHistoryRejected = true; }
            bool historicalCheckpointRoundTrip = false;
            bool historicalLegacySaveRejected = false;
            bool deferredKeyframeAuthRejected = false;
            bool deferredDeltaAuthRejected = false;
            using (MemoryStream policyImage = new())
            {
                using (CkptWriter writer = new(policyImage)) child.SavePolicyState(writer);
                byte[] policyImageBytes = policyImage.ToArray();
                Cortex checkpointHistoryReplay = new(child.Config);
                foreach (var pair in child._policies) checkpointHistoryReplay.RegisterPolicy(pair.Value.Schema);
                checkpointHistoryReplay.SealPolicyReadoutRoster();
                using (MemoryStream encodedPolicyImage = new(policyImageBytes, writable: false))
                using (CkptReader reader = new(encodedPolicyImage)) checkpointHistoryReplay.LoadPolicyState(reader);
                byte[] resavedPolicyImage;
                using (MemoryStream resaved = new())
                {
                    using (CkptWriter writer = new(resaved)) checkpointHistoryReplay.SavePolicyState(writer);
                    resavedPolicyImage = resaved.ToArray();
                }
                historicalCheckpointRoundTrip = checkpointHistoryReplay.TryReadPolicyTrialExecutionScope(
                    HomeostatPolicyBoundaryDomain.Instance.PolicyID, out PolicyVerifiedScopeEntry checkpointHistoryScope)
                    && checkpointHistoryScope == historicalScope
                    && !checkpointHistoryReplay.GetPolicy(HomeostatPolicyBoundaryDomain.Instance.PolicyID).VerifiedScopes.ContainsKey(successorState)
                    && policyImageBytes.AsSpan().SequenceEqual(resavedPolicyImage);
            }
            using (MemoryStream legacyPolicyImage = new())
            using (CkptWriter writer = new(legacyPolicyImage))
            {
                try { child.SavePolicyState(writer, policySchema: 11); }
                catch (InvalidDataException) { historicalLegacySaveRejected = true; }
            }
            const ulong forgedDeferredQuotaID = 0xD3F3AAEDUL;
            historicalState.ActiveTrialQuotaID = new CortexPolicyQuotaDecisionID(forgedDeferredQuotaID);
            historicalState.SuppressTrialPackets = true;
            try
            {
                using MemoryStream forgedImage = new();
                using (CkptWriter writer = new(forgedImage)) child.SavePolicyState(writer);
                Cortex keyframeReplay = new(child.Config);
                foreach (var pair in child._policies) keyframeReplay.RegisterPolicy(pair.Value.Schema);
                keyframeReplay.SealPolicyReadoutRoster();
                forgedImage.Position = 0;
                using (CkptReader reader = new(forgedImage)) keyframeReplay.LoadPolicyState(reader);
                try
                {
                    keyframeReplay.BindCheckpointRuntime(child.CurrentRun, new Tape(), new Journal(), null!, 0);
                }
                catch (InvalidDataException)
                {
                    try { _ = keyframeReplay.CurrentRun; }
                    catch (InvalidOperationException) { deferredKeyframeAuthRejected = true; }
                }
            }
            finally
            {
                historicalState.ActiveTrialQuotaID = default;
                historicalState.SuppressTrialPackets = false;
            }
            CortexPolicyReadoutStateReplacement[] forgedDeltaStates = executionDelta.States
                .Select(static state => state.Policy.Equals(HomeostatPolicyBoundaryDomain.Instance.PolicyID)
                    ? state with
                    {
                        SuppressTrialPackets = true,
                        ActiveTrialQuotaID = new CortexPolicyQuotaDecisionID(forgedDeferredQuotaID),
                    }
                    : state).ToArray();
            CortexPolicyCheckpointDelta forgedDelta = executionDelta with { States = forgedDeltaStates };
            Cortex deltaReplayBeforeBind = new(child.Config);
            foreach (var pair in child._policies) deltaReplayBeforeBind.RegisterPolicy(pair.Value.Schema);
            deltaReplayBeforeBind.SealPolicyReadoutRoster();
            deltaReplayBeforeBind.ApplyPolicyCheckpointDelta(in forgedDelta);
            try
            {
                deltaReplayBeforeBind.BindCheckpointRuntime(child.CurrentRun, new Tape(), new Journal(), null!, 0);
            }
            catch (InvalidDataException)
            {
                try { _ = deltaReplayBeforeBind.CurrentRun; }
                catch (InvalidOperationException) { deferredDeltaAuthRejected = true; }
            }
            GrammarRevisionID distinctInstallRevisionRevision = new(checked(postInstallRevisionRevision.Value + 1));
            GrammarSnapshot distinctInstallRevisionSnapshot = new(distinctInstallRevisionRevision,
                childInstallRevision.Snapshot.Rules, childInstallRevision.Snapshot.Compressed,
                childInstallRevision.Snapshot.TotalSavings, childInstallRevision.Snapshot.AlphabetSize);
            child.AdvancePolicyInstallRevision(new InstallRevision(
                distinctInstallRevisionSnapshot, GrammarDelta.CreateEmpty(distinctInstallRevisionRevision)));
            PolicyState distinctState = child.GetPolicy(HomeostatPolicyBoundaryDomain.Instance.PolicyID);
            ulong distinctCandidate = successorCandidate ^ 0x0101010101010101UL;
            ulong distinctSupport = successorSupport ^ 0x0202020202020202UL;
            if (distinctSupport == 0) distinctSupport = 1;
            distinctState.ReadoutCandidateRevision = distinctInstallRevisionRevision;
            distinctState.ReadoutCandidateFingerprint = distinctCandidate;
            distinctState.ReadoutCandidateOccurrenceDigest = distinctSupport;
            distinctState.ReadoutCandidateState = successorState;
            distinctState.ReadoutCandidateAction = checked((int)(successorState.Value % (ulong)distinctState.Schema.ActionCount));
            distinctState.CanonicalCandidates[successorState] = new PolicyState.CanonicalCandidateEvidence(
                successorState, distinctState.ReadoutCandidateAction, distinctCandidate, distinctSupport,
                distinctInstallRevisionRevision, child.Step);
            distinctState.VerifiedScopes[successorState] = new PolicyVerifiedScopeEntry(
                successorState, activeFingerprint, distinctCandidate, distinctSupport, distinctInstallRevisionRevision);
            CortexPolicyTrialAuthorityIdentity distinctTrialIdentity = new(
                new(activeFingerprint), new(distinctCandidate), distinctInstallRevisionRevision)
            {
                CanonicalState = successorState,
            };
            child.DisableAutonomicSpawning();
            child.SetPolicyTrialAuthority(HomeostatPolicyBoundaryDomain.Instance.PolicyID, in distinctTrialIdentity,
                CortexPolicyAuthorities.Grammar, grammarExecutionQuota: -1);
            bool distinctInstallRevisionRejected = !child.TryReadPolicyTrialExecutionReceiptForQuota(
                HomeostatPolicyBoundaryDomain.Instance.PolicyID, new CortexPolicyQuotaDecisionID(fundingID),
                out _, out _, out _, out _, out _, out _, out _, out _, out _, out _);
            string receipt = $"source=205 successor=206 local_funding=0 override={(exactlyOnce ? "1x" : $"FAIL({first.Readout.SelectionCause}/{successor.TrialExecutionOutcome}/{successor.TrialForcedDivergenceSeed.HasValue})")} completion={(productionCompletionVerified ? "verified" : "FAIL")} scope={(successorScopeVerified ? "verified" : "FAIL")} divergence_successor={(successorDivergenceAccepted ? "accepted" : "FAIL")} divergence_successor_nulls={(successorDivergenceRelationNullsRejected ? "same-revision/zero-support/zero-candidate/wrong-authority/invalid-state-rejected" : "FAIL")} execution_fp={(executionFingerprintPinned && executionFingerprintRevisionMismatch ? "pinned" : "FAIL")} execution_replay={(executionFingerprintSaveLoad ? "exact" : "FAIL")} custody={(custodyBefore ? "valid" : "FAIL")} root={(rootFunding && rootFundingDecision.Decision == CortexPolicyQuotaDecisions.Paid ? "Paid" : "FAIL")} replay={(successorScopeVerified ? "exact" : "FAIL")} immutable={(sourceTupleImmutable ? "yes" : "NO")} continuation={(historicalContinuationAccepted && historicalContinuationCauseTamperRejected && historicalContinuationSourceStateTamperRejected && historicalContinuationCustodyMutationRejected ? "source-successor/preserve-historical/cause-tamper-rejected/source-state-tamper-rejected/custody-mutation-rejected" : $"FAIL(accepted={historicalContinuationAccepted}/cause-tamper={historicalContinuationCauseTamperRejected}/source-state-tamper={historicalContinuationSourceStateTamperRejected}/custody-mutation={historicalContinuationCustodyMutationRejected})")} resume_pair=accepted duplicate={(duplicateRejected ? "rejected" : $"ACCEPTED({duplicateSpecies})")} source_state={(sourceStateTamperRejected && sourceStateSpecies ? "custody-sidecar" : $"ACCEPTED({sourceStateTamper.DenialSpecies})")} successor_tamper={(successorTamperRejected && successorSpecies ? "successor-support" : $"ACCEPTED({successorTamper.DenialSpecies})")} verdict={(verdictTamperRejected && verdictSpecies ? "intent-binding" : $"ACCEPTED({verdictTamper.DenialSpecies})")} paid_swap={(fundedCandidateSwapRejected && fundedSwapSpecies ? "custody-sidecar" : $"ACCEPTED({fundedCandidateSwap.DenialSpecies})")} raw_swap={(rawSwapRejected && rawSwapSpecies ? "custody-sidecar" : $"ACCEPTED({rawSwap.DenialSpecies})")} readiness={(readinessSpecies ? "named" : $"FAIL({readinessProbe.DenialSpecies})")} candidate={(candidateSpecies ? "named" : $"FAIL({candidateProbe.DenialSpecies})")} revision={(revisionSpecies ? "named" : $"FAIL({revisionProbe.DenialSpecies})")} scope_reason={(scopeSpecies ? "named" : $"FAIL({scopeProbe.DenialSpecies})")} custody_missing={(custodySidecarMissing ? "named" : "FAIL")} root_missing={(rootFundingMissing ? "named" : "FAIL")} corroboration={(corroborationMismatch ? "named" : $"FAIL({corroborationProbe.DenialSpecies})")}";
            receipt += $" rearm_state={(stateOnlyReceiptPassed ? "roundtrip-reindexed" : "FAIL")}";
            receipt += $" history={(historicalReceiptAfterInstallRevision && historicalReceiptAfterSecondInstallRevision && historicalScopeAfterInstallRevision ? "publication-carried/second-carried" : "FAIL")} checkpoint={(historicalCheckpointRoundTrip ? "exact" : "FAIL")} legacy_save={(historicalLegacySaveRejected ? "rejected" : "ACCEPTED")} delta={(historicalDeltaRoundTrip ? "exact" : "FAIL")} funding_bind={(nextFundingCannotReadPriorHistory ? "bound" : "LEAK")}";
            receipt += $" epoch={(supportDriftRejected && distinctInstallRevisionRejected && epochSupportTamperRejected && epochCandidateTamperRejected && rearmAtomicRejected ? "support-drift-rejected/candidate-drift-rejected/atomic-rearm-rejected/distinct-publication-rejected" : $"FAIL(support={supportDriftRejected}/candidate={epochCandidateTamperRejected}/atomic={rearmAtomicRejected}/distinct={distinctInstallRevisionRejected})")} deferred_auth={(deferredKeyframeAuthRejected && deferredDeltaAuthRejected ? "keyframe-rejected/delta-rejected" : $"FAIL(keyframe={deferredKeyframeAuthRejected}/delta={deferredDeltaAuthRejected})")}";
            return (exactlyOnce && productionCompletionVerified && successorScopeVerified && successorDivergenceAccepted && successorDivergenceRelationNullsRejected && executionFingerprintPinned && executionFingerprintRevisionMismatch && executionFingerprintSaveLoad && custodyBefore && epochSupportTamperRejected && epochCandidateTamperRejected && rearmAtomicRejected && sourceTupleImmutable && historicalContinuationAccepted && historicalContinuationCauseTamperRejected && historicalContinuationSourceStateTamperRejected && historicalContinuationCustodyMutationRejected && duplicateRejected && sourceStateTamperRejected && successorTamperRejected && verdictTamperRejected && fundedCandidateSwapRejected && rawSwapRejected && sourceStateSpecies && successorSpecies && verdictSpecies && fundedSwapSpecies && rawSwapSpecies && readinessSpecies && candidateSpecies && revisionSpecies && scopeSpecies && custodySidecarMissing && rootFundingMissing && corroborationMismatch && stateOnlyReceiptPassed && historicalReceiptAfterInstallRevision && historicalReceiptAfterSecondInstallRevision && historicalScopeAfterInstallRevision && historicalCheckpointRoundTrip && historicalLegacySaveRejected && historicalDeltaRoundTrip && malformedHistoryRejected && nextFundingCannotReadPriorHistory && supportDriftRejected && distinctInstallRevisionRejected && deferredKeyframeAuthRejected && deferredDeltaAuthRejected, receipt);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or InvalidOperationException or ArgumentException)
        {
            return (false, $"error={error.GetType().Name}:{error.Message}");
        }
        finally
        {
            sourceStateData.Mode = savedMode;
            sourceStateData.Authority = savedAuthority;
            sourceStateData.ReadoutCandidateRevision = savedRevision;
            sourceStateData.ReadoutCandidateFingerprint = savedCandidate;
            sourceStateData.ReadoutCandidateState = savedCanonicalState;
            sourceStateData.ReadoutCandidateOccurrenceDigest = savedSupport;
            sourceStateData.ReadoutCandidateAction = savedAction;
            sourceStateData.ReadoutCandidatePending = savedPending;
            sourceStateData.ShadowComparisons = savedComparisons;
            sourceStateData.ShadowAgreements = savedAgreements;
            sourceStateData.EmulationMisses = savedMisses;
            sourceStateData.PendingForcedTrialIntent = savedForcedIntent;
            sourceStateData.ActiveTrialQuotaID = savedActiveQuotaID;
            sourceStateData.SuppressTrialPackets = savedSuppressTrialPackets;
            sourceStateData.TrialGrammarExecutionsRemaining = savedTrialGrammarExecutionsRemaining;
            sourceStateData.TrialActionOffset = savedTrialActionOffset;
            sourceStateData.TrialForcedDivergenceSeed = savedTrialForcedDivergenceSeed;
            sourceStateData.TrialForcedDivergenceExecutions = savedTrialForcedDivergenceExecutions;
            sourceStateData.TrialExecutionCause = savedTrialExecutionCause;
            sourceStateData.TrialExecutionOutcome = savedTrialExecutionOutcome;
            sourceStateData.TrialExecutionCorroboration = savedTrialExecutionCorroboration;
            sourceStateData.TrialExecutionReadoutFingerprint = savedTrialExecutionReadoutFingerprint;
            sourceStateData.TrialExecutionStep = savedTrialExecutionStep;
            sourceStateData.TrialRequestCount = savedTrialRequestCount;
            sourceStateData.TrialGuardAdmittedCount = savedTrialGuardAdmittedCount;
            sourceStateData.TrialLastRequest = savedTrialLastRequest;
            sourceStateData.TrialLastRequestStep = savedTrialLastRequestStep;
            sourceStateData.HistoricalTrialExecution = savedHistoricalTrialExecution;
            sourceStateData.TrialFrozen = savedTrialFrozen;
        }
    }

    internal static bool VerifyPolicyBoundaryMaterializationContractFixture(TextWriter output)
    {
        string token = Guid.NewGuid().ToString("N");
        string corpusPath = Path.GetFullPath(Path.Combine(".tmp", $"policy-boundary-contract-{token}.txt"));
        string? parentDirectory = null;
        CortexPolicyDecision focusedSourceDecision = default;
        TapeEventID focusedSourceEventID = default;
        CortexPolicyBoundarySourceCorroboration focusedSourceCorroboration = default;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(corpusPath)!);
            File.WriteAllText(corpusPath, "alpha beta gamma\nalpha beta delta\n");
            CortexConfig config = new()
            {
                RunName = $"policy-boundary-contract-{token}",
                Steps = 4,
                Seed = 0xC0117011UL,
                Curriculum = new CortexFlatPoolCurriculum
                {
                    Corpus = new CogitoCorpus { Path = corpusPath, Glob = "*.txt" },
                    IntakeBatch = 1,
                    SeedSpans = 1,
                    MixEvery = 1,
                },
                Learning = new CortexLearningConfig
                {
                    Homeostat = new CortexHomeostatConfig { Autonomy = HomeostatAutonomyModes.Full },
                    Policies = new CortexPolicyLearningConfig
                    {
                        DefaultMode = CortexPolicyModes.Autonomic,
                        TrialHorizons = [1, 2, 3],
                        TrialAllocation = new CortexPolicyTrialAllocationConfig
                        {
                            Authority = CortexPolicyAuthorities.Grammar,
                            ArmSteps = 4096,
                            Identity = "policy-boundary-recovery-fixture",
                        },
                    },
                },
            };
            CortexForkSeed? seed = null;
            bool exact = false;
            bool wrongFundingRejected = false;
            bool wrongParentRejected = false;
            bool wrongColdRejected = false;
            bool wrongChildRejected = false;
            bool repeatRejected = false;
            bool wrongNFundingRejected = false;
            bool wrongNParentRejected = false;
            bool wrongNColdRejected = false;
            bool policyJournalCustody = false;
            bool missingPolicyJournalRejected = false;
            bool nonExactContinuityRejected = false;
            bool allTerminalReceipts = false;
            bool recoveryOrphanReused = false;
            bool recoveryTerminalSettled = false;
            bool recoverySecondResumeNoOp = false;
            bool recoveryIdentityRejected = false;
            bool recoveryMixedGenerationRejected = false;
            bool recoveryForeignGenerationRejected = false;
            bool recoveryCustodyMutationRejected = false;
            bool recoverySettlementCustodyMissingRepaired = false;
            bool recoverySettlementCustodyMissingRejected = false;
            bool recoverySettlementCustodyMutationRejected = false;
            bool directSettlementDurable = false;
            bool recoveredAttachFixturePassed = false;
            bool recoveryMissingSourceEventRejected = false;
            bool recoveryForgedSourceEventRejected = false;
            bool executedIdentityCustody = false;
            bool missingExecutedIdentityRejected = false;
            bool forgedExecutedIdentityRejected = false;
            bool rawAliasCandidateRejected = false;
            bool boundaryCorroborationRoundTrip = false;
            bool boundaryCorroborationTamperRejected = false;
            bool boundaryCorroborationStaleRejected = false;
            bool boundaryCorroborationMalformedRejected = false;
            bool ordinaryLaunchpadCandidateFree = false;
            bool authorityIdentityFixturePassed = false;
            bool identityProbeIsolated = false;
            string authorityIdentityFixtureReceipt = "";
            bool verificationStaleIgnored = false;
            bool verificationCurrentRestored = false;
            bool verificationLatestMatchingPassed = false;
            bool verificationMissingRejected = false;
            bool verificationConflictRejected = false;
            bool successorSupportTamperRejected = false;
            int terminalHistoricalScopeAcceptedCount = 0;
            int terminalHistoricalScopeMismatchRejectedCount = 0;
            bool sourceSuccessorFixturePassed = false;
            string sourceSuccessorFixtureReceipt = "";
            CortexForkSeed? identitySeedForFixture = null;
            CortexForkSeed? verifiedSeedForFixture = null;
            CortexPolicyTrialAuthorityIdentity? identityForFixture = null;
            CortexPolicyDecision identitySourceDecisionForFixture = default;
            TapeEventID identitySourceDecisionEventIDForFixture = default;
            CortexPolicyBoundarySourceCorroboration identityCorroborationForFixture = default;
            ulong identitySupportDigestForFixture = 0;
            string identityCoverageState = "";
            bool ladderInterventionsOnce = false;
            int ladderInterventionCount = 0;
            int continuationNotAttemptedCount = 0;
            int continuationGuardDeniedCount = 0;
            int continuationHistoricalCount = 0;
            int continuationRungOneCount = 0;
            int continuationRungOneNotAttemptedCount = 0;
            int continuationRungOneGuardDeniedCount = 0;
            int continuationRungOneHistoricalCount = 0;
            int continuationRungOneSeedAncestryCount = 0;
            int guardDeniedPortraitAcceptedCount = 0;
            int guardDeniedPortraitMalformedRejectedCount = 0;
            int publicationBeforeFirstActionCount = 0;
            int baselineCauseCount = 0;
            int candidateCauseCount = 0;
            int forcedCauseCount = 0;
            int reflexCauseCount = 0;
            int forcedExecutionPaidCapturedCount = 0;
            int ordinaryOutcomeCustodyCount = 0;
            bool failureCensusFlushedWithoutSettlement = false;
            bool failureGenerationTransitionDurable = false;
            bool failureRetryUnsettled = false;
            bool failureTamperedCompleteRailRejected = false;
            bool terminalOutcomeReconstructed = false;
            bool terminalOutcomeOmissionRejected = false;
            bool terminalOutcomeDigestRejected = false;
            bool terminalOutcomeRungSubstitutionRejected = false;
            // The transition portrait below is intentionally stale and cross-wired.  It is a
            // diagnostic specimen only; production materialization binds the live tuple captured
            // after the probe has run.
            const ulong transitionFundingFingerprint = 0x5802UL;
            const ulong transitionSourceCandidateFingerprint = 0xCB89UL;
            PolicyCanonicalStateID transitionCanonicalState = new(
                HomeostatPolicyBoundaryDomain.Instance.PolicyID, PolicyCanonicalStateKinds.Homeostat, 1, 1);
            ulong fixtureFundingFingerprint = 0;
            ulong fixtureSourceCandidateFingerprint = 0;
            ulong fixtureSupportDigest = 0;
            GrammarRevisionID fixtureRevision = GrammarRevisionID.Zero;
            PolicyCanonicalStateID fixtureCanonicalState = default;
            PolicyCanonicalStateID identityCanonicalStateForFixture = default;
            CortexPolicyTrialAuthorityIdentity fixtureAuthorityIdentity = default;
            Cortex parent = new(config);
            int exit = Drive(parent, config.ToRunConfig(parent.MountedCurriculum), checkpointRunEnd: true, afterCompletedStep: (runtime, completedStep) =>
            {
                if (seed is not null) return;
                // Capture one ordinary Homeostat boundary, including its exact step.
                // A later child state is a successor opportunity and must earn a
                // typed rearm; it must never be disguised as source-state recurrence.
                if (runtime._runtimeHomeostat is not Homeostat sourceHomeostat
                    || sourceHomeostat.LastBoundaryPolicyDecision.DecisionID.Value == 0
                    || !sourceHomeostat.TryReadLastBoundaryCanonicalState(
                        out PolicyCanonicalStateID sourceCanonicalState, out int sourceBoundaryStep)
                    || sourceBoundaryStep != completedStep)
                    return;
                CortexPolicyDecision fixtureSourceDecision = new(
                    new CortexPolicyDecisionID(0xB747F1FAD2C27001UL), HomeostatPolicyBoundaryDomain.Instance.PolicyID,
                    new CortexPolicyDecisionReadout(0, 1, 1, 1, CortexPolicyAuthorities.Grammar,
                        new GrammarRevisionID(1), CortexPolicySelectionCauses.GrammarCandidate,
                        ReadoutCandidateOccurrenceDigest: 1, ReadoutCandidateFingerprint: transitionSourceCandidateFingerprint));
                TapeEventID fixtureSourceDecisionEventID;
                runtime.SetRuntimeForkBoundary(true);
                try
                {
                    if (runtime._runtimeTape is null || runtime._runtimeJournal is null)
                        throw new InvalidDataException("fixture source decision requires the live tape and journal");
                    fixtureSourceDecisionEventID = TapePacketCreator.AppendPolicyDecision(
                        runtime._runtimeTape, runtime._runtimeJournal, completedStep, in fixtureSourceDecision,
                        [new MetricSample(new MetricID(0), NumericValue.FromI64(0))], actionCount: 2, out _);
                    CortexPolicyBoundarySourceCorroboration fixtureSourceCorroboration = new(
                        HomeostatPolicyBoundaryDomain.Instance.PolicyID, fixtureSourceDecision.DecisionID, fixtureSourceDecisionEventID,
                        fixtureSourceDecision.Authority, fixtureSourceDecision.SelectionCause,
                        fixtureSourceDecision.GrammarRevision, transitionFundingFingerprint,
                        transitionSourceCandidateFingerprint, fixtureSourceDecision.Readout.ReadoutCandidateOccurrenceDigest, "",
                        CachedContexts: 1, Comparisons: 1, Agreements: 1, Misses: 0);
                    fixtureSourceCorroboration = fixtureSourceCorroboration with
                    {
                        CanonicalState = transitionCanonicalState,
                    };
                    fixtureSourceCorroboration = fixtureSourceCorroboration with { CorroborationDigest = fixtureSourceCorroboration.ComputeDigest() };
                    _ = TapePacketCreator.AppendPolicyBoundarySourceCorroboration(runtime._runtimeTape, runtime._runtimeJournal,
                        completedStep, in fixtureSourceCorroboration);
                    focusedSourceDecision = fixtureSourceDecision;
                    focusedSourceEventID = fixtureSourceDecisionEventID;
                    focusedSourceCorroboration = fixtureSourceCorroboration;
                    byte[] corroborationPacket = TapePacketCreator.EncodePolicyBoundarySourceCorroboration(in fixtureSourceCorroboration);
                    boundaryCorroborationRoundTrip = TapePacketCreator.TryReadPolicyBoundarySourceCorroboration(corroborationPacket,
                        out CortexPolicyBoundarySourceCorroboration decodedCorroboration)
                        && decodedCorroboration == fixtureSourceCorroboration;
                    byte[] tamperedCorroborationPacket = [.. corroborationPacket];
                    tamperedCorroborationPacket[^1] = tamperedCorroborationPacket[^1] == (byte)'0' ? (byte)'1' : (byte)'0';
                    boundaryCorroborationTamperRejected = !TapePacketCreator.TryReadPolicyBoundarySourceCorroboration(tamperedCorroborationPacket, out _);
                    CortexPolicyBoundarySourceCorroboration staleCorroboration = fixtureSourceCorroboration with { ReadoutRevision = new GrammarRevisionID(2), CorroborationDigest = "" };
                    staleCorroboration = staleCorroboration with { CorroborationDigest = staleCorroboration.ComputeDigest() };
                    CortexPolicyReadoutReceipt currentCorroborationReadout = new(fixtureSourceCorroboration.ReadoutRevision,
                        fixtureSourceCorroboration.ReadoutFingerprint, 1, 1, 1, 0,
                        fixtureSourceCorroboration.OccurrenceDigest, fixtureSourceCorroboration.CandidateFingerprint);
                    boundaryCorroborationStaleRejected = TapePacketCreator.TryReadPolicyBoundarySourceCorroboration(
                        TapePacketCreator.EncodePolicyBoundarySourceCorroboration(in staleCorroboration), out CortexPolicyBoundarySourceCorroboration parsedStale)
                        && !parsedStale.Matches(in currentCorroborationReadout)
                        && !TapePacketCreator.TryReadPolicyBoundarySourceCorroboration(ReadOnlySpan<byte>.Empty, out _);
                    CortexPolicyDecision ordinaryLaunchpadDecision = new(
                        new CortexPolicyDecisionID(0xB747F1FAD2C27002UL), HomeostatPolicyBoundaryDomain.Instance.PolicyID,
                        new CortexPolicyDecisionReadout(0, -1, -1, 0, CortexPolicyAuthorities.Launchpad,
                            new GrammarRevisionID(1), CortexPolicySelectionCauses.Launchpad));
                    TapeEventID ordinaryLaunchpadEvent = TapePacketCreator.AppendPolicyDecision(
                        runtime._runtimeTape, runtime._runtimeJournal, completedStep, in ordinaryLaunchpadDecision,
                        [new MetricSample(new MetricID(0), NumericValue.FromI64(0))], actionCount: 2, out _);
                    ordinaryLaunchpadCandidateFree = runtime._runtimeTape.Resolve(ordinaryLaunchpadEvent, out byte[] ordinaryPacket)
                        && TapePacketCreator.DecodePolicyDecision(ordinaryPacket).Readout is var ordinaryReadout
                        && ordinaryReadout.RawCandidateAction == -1 && ordinaryReadout.SelectedCandidateAction == -1
                        && ordinaryReadout.ReadoutCandidateFingerprint == 0 && ordinaryReadout.ReadoutCandidateOccurrenceDigest == 0;
                    // Cut the ordinary seed before the focused identity probe mutates policy
                    // state; recovery assertions continue to consume the original image.
                    runtime.FlushPolicyJournalBuffer();
                    byte[] ordinaryCheckpoint = runtime.CaptureForkSnapshot();
                    using MemoryStream ordinaryTapeLog = new();
                    runtime.CopyTapeLogTo(ordinaryTapeLog);
                    byte[] ordinaryCurve = File.ReadAllBytes(runtime.CurrentRun.PathOf("curve.tsv"));
                    (byte[] ordinaryExcursions, long ordinaryExcursionCursor) = runtime.CopyExcursionLog();
                    seed = CortexForkSeed.Materialize(checked(completedStep + 1), ordinaryCheckpoint, ordinaryTapeLog.ToArray(), ordinaryCurve,
                        PersistedConfigDigest(runtime.Config.ToRunConfig(null)), runtime.CopyPolicyJournals(), ordinaryExcursions, ordinaryExcursionCursor);
                    // Install a real canonical candidate before the fork image is cut.  The
                    // candidate fingerprint and its program digest are deliberately distinct so
                    // the production arm exercises all three identity coordinates instead of
                    // collapsing the program slot onto the candidate slot.
                    PolicyState identityState = runtime.GetPolicy(HomeostatPolicyBoundaryDomain.Instance.PolicyID);
                    GrammarRevisionID priorIdentityRevision = identityState.ReadoutCandidateRevision;
                    ulong priorIdentityFingerprint = identityState.ReadoutCandidateFingerprint;
                    ulong priorIdentityProgramDigest = identityState.ReadoutCandidateSetDigest;
                    PolicyCanonicalStateID priorIdentityState = identityState.ReadoutCandidateState;
                    ulong priorIdentitySupportDigest = identityState.ReadoutCandidateOccurrenceDigest;
                    int priorIdentityAction = identityState.ReadoutCandidateAction;
                    bool priorIdentityPending = identityState.ReadoutCandidatePending;
                    bool hadIdentityEvidence = identityState.CanonicalCandidates.ContainsKey(new PolicyCanonicalStateID(
                        HomeostatPolicyBoundaryDomain.Instance.PolicyID, PolicyCanonicalStateKinds.Homeostat, 1, 0xA17E5EEDUL));
                    GrammarRevisionID identityRevision = runtime.InstallRevision?.Revision ?? new GrammarRevisionID(1);
                    if (identityRevision == GrammarRevisionID.Zero) identityRevision = new GrammarRevisionID(1);
                    PolicyCanonicalStateID identityCanonicalState = new(HomeostatPolicyBoundaryDomain.Instance.PolicyID,
                        PolicyCanonicalStateKinds.Homeostat, 1, 0xA17E5EEDUL);
                    identityCanonicalStateForFixture = identityCanonicalState;
                    identityCoverageState = string.Concat('\t', identityCanonicalState.Policy.Value, '\t',
                        ((byte)identityCanonicalState.Kind).ToString(CultureInfo.InvariantCulture), '\t',
                        identityCanonicalState.Version.ToString(CultureInfo.InvariantCulture), '\t',
                        identityCanonicalState.Value.ToString("X16", CultureInfo.InvariantCulture));
                    const ulong identityCandidateFingerprint = 0xC011EC7ED00DCAFEUL;
                    const ulong identitySupportDigest = 0x51A7E5EED00DCAFEUL;
                    identitySupportDigestForFixture = identitySupportDigest;
                    PolicyState.CanonicalCandidateEvidence identityEvidence = new(
                        identityCanonicalState, 1, identityCandidateFingerprint, identitySupportDigest,
                        identityRevision, completedStep);
                    identityState.CanonicalCandidates[identityCanonicalState] = identityEvidence;
                    identityState.ReadoutCandidateRevision = identityRevision;
                    identityState.ReadoutCandidateFingerprint = identityCandidateFingerprint;
                    identityState.ReadoutCandidateState = identityCanonicalState;
                    identityState.ReadoutCandidateOccurrenceDigest = identitySupportDigest;
                    identityState.ReadoutCandidateAction = 1;
                    identityState.ReadoutCandidatePending = false;
                    identityState.CanonicalProgramDigestDirty = true;
                    RefreshCanonicalProgramDigest(identityState, HomeostatPolicyBoundaryDomain.Instance.PolicyID);
                    identityState.VerifiedScopes[identityCanonicalState] = new PolicyVerifiedScopeEntry(
                        identityCanonicalState, ReadActivePolicyFingerprint(identityState),
                        identityCandidateFingerprint, identitySupportDigest, identityRevision);
                    if (ReadActivePolicyFingerprint(identityState) == ReadActivePolicyCandidateFingerprint(identityState))
                        throw new InvalidDataException("production identity fixture did not separate program and candidate fingerprints");
                    identityForFixture = new CortexPolicyTrialAuthorityIdentity(
                        new(ReadActivePolicyFingerprint(identityState)),
                        new(identityState.ReadoutCandidateFingerprint), identityState.ReadoutCandidateRevision)
                    {
                        CanonicalState = identityCanonicalState,
                    };
                    CortexPolicyTrialAuthorityIdentity identitySourceAuthority = identityForFixture.Value;
                    identitySourceDecisionForFixture = new CortexPolicyDecision(
                        new CortexPolicyDecisionID(0xB747F1FAD2C27011UL), HomeostatPolicyBoundaryDomain.Instance.PolicyID,
                        new CortexPolicyDecisionReadout(0, 1, 1, 1, CortexPolicyAuthorities.Grammar,
                            identitySourceAuthority.CandidateRevision, CortexPolicySelectionCauses.GrammarCandidate,
                            ReadoutCandidateOccurrenceDigest: identitySupportDigest,
                            ReadoutCandidateFingerprint: identitySourceAuthority.CandidateFingerprint.Value));
                    identitySourceDecisionEventIDForFixture = TapePacketCreator.AppendPolicyDecision(
                        runtime._runtimeTape, runtime._runtimeJournal, completedStep, in identitySourceDecisionForFixture,
                        [new MetricSample(new MetricID(0), NumericValue.FromI64(0))], actionCount: 2, out _);
                    identityCorroborationForFixture = new CortexPolicyBoundarySourceCorroboration(
                        HomeostatPolicyBoundaryDomain.Instance.PolicyID, identitySourceDecisionForFixture.DecisionID, identitySourceDecisionEventIDForFixture,
                        identitySourceDecisionForFixture.Authority, identitySourceDecisionForFixture.SelectionCause,
                        identitySourceAuthority.CandidateRevision, identitySourceAuthority.ActiveProgramFingerprint.Value,
                        identitySourceAuthority.CandidateFingerprint.Value, identitySupportDigest, "")
                    {
                        CanonicalState = identityCanonicalState,
                    };
                    identityCorroborationForFixture = identityCorroborationForFixture with
                    {
                        CorroborationDigest = identityCorroborationForFixture.ComputeDigest(),
                    };
                    _ = TapePacketCreator.AppendPolicyBoundarySourceCorroboration(runtime._runtimeTape, runtime._runtimeJournal,
                        completedStep, in identityCorroborationForFixture);
                    runtime.FlushPolicyJournalBuffer();
                    byte[] checkpoint = runtime.CaptureForkSnapshot();
                    using MemoryStream tapeLog = new();
                    runtime.CopyTapeLogTo(tapeLog);
                    byte[] curve = File.ReadAllBytes(runtime.CurrentRun.PathOf("curve.tsv"));
                    (byte[] excursions, long excursionCursor) = runtime.CopyExcursionLog();
                    CortexForkSeed identitySeed = CortexForkSeed.Materialize(checked(completedStep + 1), checkpoint, tapeLog.ToArray(), curve,
                        PersistedConfigDigest(runtime.Config.ToRunConfig(null)), runtime.CopyPolicyJournals(), excursions, excursionCursor);
                    // Carry one stale strict row and two exact-active rows into the materialized
                    // child.  The second exact row is authoritative, proving latest-row
                    // succession without promoting the stale revision.
                    ulong identityReadoutFingerprint = ReadActivePolicyFingerprint(identityState);
                    string verificationRows = HomeostatPolicyBoundaryDomain.Instance.PolicyID.Value + "\t" + identityReadoutFingerprint.ToString("X16", CultureInfo.InvariantCulture) + "\t"
                        + identityCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture) + "\t40\t1\t1\t0\t0\t1\t1\t0\t1\t1\t0\tCompleteCoverage" + identityCoverageState + "\n"
                        + HomeostatPolicyBoundaryDomain.Instance.PolicyID.Value + "\t" + identityReadoutFingerprint.ToString("X16", CultureInfo.InvariantCulture) + "\t"
                        + identityCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture) + "\t" + identityRevision.Value.ToString(CultureInfo.InvariantCulture) + "\t1\t1\t0\t0\t1\t1\t0\t1\t1\t0\tCompleteCoverage" + identityCoverageState + "\n"
                        + HomeostatPolicyBoundaryDomain.Instance.PolicyID.Value + "\t" + identityReadoutFingerprint.ToString("X16", CultureInfo.InvariantCulture) + "\t"
                        + identityCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture) + "\t" + identityRevision.Value.ToString(CultureInfo.InvariantCulture) + "\t1\t1\t0\t1\t1\t1\t0\t1\t1\t0\tCompleteCoverage" + identityCoverageState + "\n";
                    CortexSeedSidecarSet identityJournals = identitySeed.CopyPolicyJournals()
                        .With(PolicyOccurrenceCheckReceiptFile, Encoding.UTF8.GetBytes(PolicyOccurrenceCheckReceiptHeader + "\n" + verificationRows));
                    identitySeed = CortexForkSeed.Materialize(identitySeed.NextStep, identitySeed.CopyCheckpoint(), identitySeed.CopyTapeSpanlog(),
                        identitySeed.CopyCurve(), identitySeed.PersistedConfigDigest, identityJournals,
                        identitySeed.CopyExcursions(), identitySeed.ExcursionCursor);
                    ulong priorAssayedReadout = identityState.AssayedReadoutFingerprint;
                    ulong priorAssayedCandidate = identityState.AssayedFingerprint;
                    ulong priorVerifiedReadout = identityState.VerifiedReadoutFingerprint;
                    ulong priorVerifiedCandidate = identityState.VerifiedFingerprint;
                    GrammarRevisionID priorVerifiedRevision = identityState.VerifiedRevision;
                    identityState.AssayedReadoutFingerprint = identityReadoutFingerprint;
                    identityState.AssayedFingerprint = identityCandidateFingerprint;
                    identityState.VerifiedReadoutFingerprint = identityReadoutFingerprint;
                    identityState.VerifiedFingerprint = identityCandidateFingerprint;
                    identityState.VerifiedRevision = identityRevision;
                    runtime.FlushPolicyJournalBuffer();
                    byte[] verifiedCheckpoint = runtime.CaptureForkSnapshot();
                    using MemoryStream verifiedTapeLog = new();
                    runtime.CopyTapeLogTo(verifiedTapeLog);
                    byte[] verifiedCurve = File.ReadAllBytes(runtime.CurrentRun.PathOf("curve.tsv"));
                    (byte[] verifiedExcursions, long verifiedExcursionCursor) = runtime.CopyExcursionLog();
                    verifiedSeedForFixture = CortexForkSeed.Materialize(identitySeed.NextStep, verifiedCheckpoint, verifiedTapeLog.ToArray(), verifiedCurve,
                        PersistedConfigDigest(runtime.Config.ToRunConfig(null)), identityJournals, verifiedExcursions, verifiedExcursionCursor);
                    // The identity probe owns its image; restore the parent after cutting it so
                    // the pre-existing custody fixture remains an independent measurement.
                    identityState.CanonicalCandidates.Remove(new PolicyCanonicalStateID(
                        HomeostatPolicyBoundaryDomain.Instance.PolicyID, PolicyCanonicalStateKinds.Homeostat, 1, 0xA17E5EEDUL));
                    identityState.VerifiedScopes.Remove(new PolicyCanonicalStateID(
                        HomeostatPolicyBoundaryDomain.Instance.PolicyID, PolicyCanonicalStateKinds.Homeostat, 1, 0xA17E5EEDUL));
                    identityState.ReadoutCandidateRevision = priorIdentityRevision;
                    identityState.ReadoutCandidateFingerprint = priorIdentityFingerprint;
                    identityState.ReadoutCandidateSetDigest = priorIdentityProgramDigest;
                    identityState.ReadoutCandidateState = priorIdentityState;
                    identityState.ReadoutCandidateOccurrenceDigest = priorIdentitySupportDigest;
                    identityState.ReadoutCandidateAction = priorIdentityAction;
                    identityState.ReadoutCandidatePending = priorIdentityPending;
                    identityState.AssayedReadoutFingerprint = priorAssayedReadout;
                    identityState.AssayedFingerprint = priorAssayedCandidate;
                    identityState.VerifiedReadoutFingerprint = priorVerifiedReadout;
                    identityState.VerifiedFingerprint = priorVerifiedCandidate;
                    identityState.VerifiedRevision = priorVerifiedRevision;
                    identityState.CanonicalProgramDigestDirty = true;
                    if (hadIdentityEvidence)
                        throw new InvalidDataException("production identity fixture cannot restore a pre-existing canonical probe state");
                    identitySeedForFixture = identitySeed;
                }
                finally { runtime.SetRuntimeForkBoundary(false); }
                parentDirectory = runtime.CurrentRun.Dir;
                // Identity state and its seed are captured before the production seed; all
                // authority-identity side effects are applied only after that seed is cut.
                CortexForkSeed BuildOccurrenceCheckSeed(CortexForkSeed source, string rows)
                {
                    CortexSeedSidecarSet journals = source.CopyPolicyJournals()
                        .With(PolicyOccurrenceCheckReceiptFile, Encoding.UTF8.GetBytes(PolicyOccurrenceCheckReceiptHeader + "\n" + rows));
                    return CortexForkSeed.Materialize(source.NextStep, source.CopyCheckpoint(), source.CopyTapeSpanlog(), source.CopyCurve(),
                        source.PersistedConfigDigest, journals, source.CopyExcursions(), source.ExcursionCursor);
                }
                (bool Success, bool Restored) RunOccurrenceCheckChild(CortexForkSeed probeSeed, bool expectRestored, bool expectPassed)
                {
                    bool restored = false;
                    (Run child, CortexForkMaterializationContract contract) = runtime.CurrentRun.CreateMaterializedChildRun(
                        CortexForkRailRoles.Calibration, "verification-history", probeSeed.ColdSeedDigest);
                    CortexForkArm<int> probe = new(child.Dir, () => Cortex.CreateCheckpointRuntime(runtime.Config.ToRunConfig(null)),
                        static cortex => cortex.Step,
                        afterRuntimeBind: (cortex, _) =>
                        {
                            bool passed;
                            restored = cortex.HasPolicyOccurrenceCheck(HomeostatPolicyBoundaryDomain.Instance.PolicyID,
                                identityForFixture!.Value.ActiveProgramFingerprint.Value,
                                identityForFixture.Value.CandidateFingerprint.Value, identityForFixture.Value.CandidateRevision, out passed);
                            if (restored != expectRestored || passed != expectPassed)
                                throw new InvalidDataException("verification-history child restored an unexpected current receipt");
                        }, completionMode: CortexForkCompletionModes.ExactAbsoluteStep,
                        railRole: CortexForkRailRoles.Calibration, parentRunID: Path.GetFileName(parentDirectory!),
                        materializationContract: contract);
                    CortexForkRunReceipt<int> receipt = CortexForkRunner.RunFork(runtime, probeSeed, probe, probeSeed.NextStep + 1);
                    return (receipt.ExitCode == 0, restored);
                }
                try
                {
                    CortexForkSeed identitySeed = identitySeedForFixture
                        ?? throw new InvalidDataException("verification-history fixture lost identity seed");
                    string staleOnlyRows = HomeostatPolicyBoundaryDomain.Instance.PolicyID.Value + "\t" + identityForFixture!.Value.ActiveProgramFingerprint.Value.ToString("X16", CultureInfo.InvariantCulture) + "\t"
                        + identityForFixture.Value.CandidateFingerprint.Value.ToString("X16", CultureInfo.InvariantCulture) + "\t40\t1\t1\t0\t0\t1\t1\t0\t1\t1\t0\tCompleteCoverage" + identityCoverageState + "\n";
                    (bool staleRun, bool staleRestored) = RunOccurrenceCheckChild(BuildOccurrenceCheckSeed(identitySeed, staleOnlyRows), expectRestored: false, expectPassed: false);
                    verificationStaleIgnored = staleRun && !staleRestored;
                    (bool currentRun, bool currentRestored) = RunOccurrenceCheckChild(identitySeed, expectRestored: true, expectPassed: true);
                    verificationCurrentRestored = currentRun && currentRestored;
                    verificationLatestMatchingPassed = verificationCurrentRestored;
                    CortexForkSeed verifiedSeed = verifiedSeedForFixture
                        ?? throw new InvalidDataException("verification-history fixture lost verified seed");
                    string conflictingRows = staleOnlyRows + HomeostatPolicyBoundaryDomain.Instance.PolicyID.Value + "\t" + identityForFixture.Value.ActiveProgramFingerprint.Value.ToString("X16", CultureInfo.InvariantCulture) + "\t"
                        + identityForFixture.Value.CandidateFingerprint.Value.ToString("X16", CultureInfo.InvariantCulture) + "\t" + identityForFixture.Value.CandidateRevision.Value.ToString(CultureInfo.InvariantCulture) + "\t1\t1\t0\t0\t1\t1\t0\t1\t1\t0\tCompleteCoverage" + identityCoverageState + "\n";
                    try { _ = RunOccurrenceCheckChild(BuildOccurrenceCheckSeed(verifiedSeed, conflictingRows), expectRestored: true, expectPassed: true); }
                    catch (InvalidDataException) { verificationConflictRejected = true; }
                    try { _ = RunOccurrenceCheckChild(BuildOccurrenceCheckSeed(verifiedSeed, staleOnlyRows), expectRestored: true, expectPassed: true); }
                    catch (InvalidDataException) { verificationMissingRejected = true; }
                }
                catch (InvalidDataException exception)
                {
                    authorityIdentityFixtureReceipt += $" verification={exception.Message}";
                }
                // Mount the pre-repair portrait in the live policy path: a paid Grammar rail
                // still carries its forced-null override when the publication advances. The next
                // ordinary decision must demote that rail through the revision-drift branch so
                // the transition receipt records the exact before-state that used to disappear.
                PolicyState transitionState = runtime.GetPolicy(HomeostatPolicyBoundaryDomain.Instance.PolicyID);
                GrammarRevisionID liveRevision = runtime.InstallRevision?.Revision ?? new GrammarRevisionID(2);
                GrammarRevisionID staleRevision = new(liveRevision.Value > 1 ? liveRevision.Value - 1 : 2);
                transitionState.Authority = CortexPolicyAuthorities.Grammar;
                transitionState.ReadoutCandidateRevision = staleRevision;
                transitionState.ReadoutCandidateFingerprint = transitionSourceCandidateFingerprint;
                transitionState.ReadoutCandidateState = transitionCanonicalState;
                transitionState.ReadoutCandidateOccurrenceDigest = 1;
                transitionState.ReadoutCandidateAction = 1;
                transitionState.ReadoutCandidatePending = false;
                transitionState.CanonicalCandidates[transitionCanonicalState] = new PolicyState.CanonicalCandidateEvidence(
                    transitionCanonicalState, 1, transitionSourceCandidateFingerprint, 1,
                    staleRevision, runtime.Step);
                RefreshCanonicalProgramDigest(transitionState, HomeostatPolicyBoundaryDomain.Instance.PolicyID);
                transitionState.LastDecisionReadout = fixtureSourceDecision.Readout;
                transitionState.TrialForcedDivergenceSeed = 0xF0CEDUL;
                transitionState.TrialActionOffset = 0;
                transitionState.TrialGrammarExecutionsRemaining = 1;
                transitionState.SuppressTrialPackets = true;
                transitionState.TrialExecutionCause = CortexPolicySelectionCauses.TrialOverride;
                transitionState.TrialExecutionOutcome = CortexPolicyTrialExecutionOutcomes.NotAttempted;
                transitionState.TrialExecutionCorroboration = null;
                transitionState.TrialExecutionReadoutFingerprint = 0;
                transitionState.TrialExecutionStep = -1;
                transitionState.TrialRequestCount = 0;
                transitionState.TrialGuardAdmittedCount = 0;
                transitionState.TrialLastRequest = null;
                transitionState.TrialLastRequestStep = -1;
                transitionState.TrialFrozen = false;
                MetricSample[] transitionFeatures = new MetricSample[transitionState.Schema.FeatureCount];
                for (int i = 0; i < transitionFeatures.Length; i++)
                    transitionFeatures[i] = new MetricSample(new MetricID(checked((ushort)i)), NumericValue.FromI64(0));
                transitionState.TrialExecutionOutcome = CortexPolicyTrialExecutionOutcomes.NotAttempted;
                transitionState.TrialExecutionCorroboration = null;
                transitionState.TrialExecutionReadoutFingerprint = 0;
                transitionState.TrialExecutionStep = -1;
                transitionState.TrialRequestCount = 0;
                transitionState.TrialGuardAdmittedCount = 0;
                transitionState.TrialLastRequest = null;
                transitionState.TrialLastRequestStep = -1;
                ulong transitionProgramFingerprint = ReadActivePolicyFingerprint(transitionState);
                transitionState.VerifiedScopes[transitionCanonicalState] = new PolicyVerifiedScopeEntry(
                    transitionCanonicalState, transitionProgramFingerprint, transitionSourceCandidateFingerprint, 1,
                    staleRevision);
                RefreshCanonicalProgramDigest(transitionState, HomeostatPolicyBoundaryDomain.Instance.PolicyID);
                // The identity-codec probe above deliberately uses a synthetic state.  A
                // materialized arm needs causal opportunity instead: bind the exact canonical
                // state selected by the ordinary Homeostat decision at this fork boundary.
                if (identityForFixture is not CortexPolicyTrialAuthorityIdentity identityAuthority
                    || identitySupportDigestForFixture == 0
                    || identityAuthority.CandidateRevision == GrammarRevisionID.Zero)
                    throw new InvalidDataException("production materialization fixture lost its live authority tuple");
                fixtureCanonicalState = sourceCanonicalState;
                fixtureRevision = identityAuthority.CandidateRevision;
                fixtureSupportDigest = identitySupportDigestForFixture;
                GrammarPolicyDecision materializationDecision = new(
                    1, 0, 0, fixtureRevision, default,
                    GrammarPolicyReadout.ComputeStateFingerprint(HomeostatPolicyBoundaryDomain.Instance.PolicyID, in fixtureCanonicalState))
                {
                    OccurrenceDigest = fixtureSupportDigest,
                };
                fixtureSourceCandidateFingerprint = GrammarPolicyReadout.ComputeCandidateFingerprint(
                    HomeostatPolicyBoundaryDomain.Instance.PolicyID, in fixtureCanonicalState, in materializationDecision);
                PolicyState materializationState = runtime.GetPolicy(HomeostatPolicyBoundaryDomain.Instance.PolicyID);
                // Retain the stale portrait as the transition probe's diagnostic input, but keep
                // its synthetic candidate out of the executable program digest.
                materializationState.CanonicalCandidates.Remove(transitionCanonicalState);
                materializationState.VerifiedScopes.Remove(transitionCanonicalState);
                materializationState.Authority = CortexPolicyAuthorities.Grammar;
                materializationState.ReadoutCandidateRevision = fixtureRevision;
                materializationState.ReadoutCandidateFingerprint = fixtureSourceCandidateFingerprint;
                materializationState.ReadoutCandidateState = fixtureCanonicalState;
                materializationState.ReadoutCandidateOccurrenceDigest = fixtureSupportDigest;
                materializationState.ReadoutCandidateAction = 1;
                materializationState.ReadoutCandidatePending = false;
                materializationState.AssayedReadoutFingerprint = 0;
                materializationState.AssayedFingerprint = 0;
                materializationState.VerifiedReadoutFingerprint = 0;
                materializationState.VerifiedFingerprint = 0;
                materializationState.VerifiedRevision = GrammarRevisionID.Zero;
                materializationState.CanonicalCandidates[fixtureCanonicalState] = new PolicyState.CanonicalCandidateEvidence(
                    fixtureCanonicalState, 1, fixtureSourceCandidateFingerprint, fixtureSupportDigest,
                    fixtureRevision, runtime.Step);
                materializationState.CanonicalProgramDigestDirty = true;
                RefreshCanonicalProgramDigest(materializationState, HomeostatPolicyBoundaryDomain.Instance.PolicyID);
                fixtureFundingFingerprint = ReadActivePolicyFingerprint(materializationState);
                if (fixtureFundingFingerprint == 0
                    || fixtureFundingFingerprint == fixtureSourceCandidateFingerprint)
                    throw new InvalidDataException("production materialization fixture did not derive a distinct live program fingerprint");
                fixtureAuthorityIdentity = new(
                    new(fixtureFundingFingerprint), new(fixtureSourceCandidateFingerprint), fixtureRevision)
                {
                    CanonicalState = fixtureCanonicalState,
                };
                RefreshCanonicalProgramDigest(materializationState, HomeostatPolicyBoundaryDomain.Instance.PolicyID);
                InvalidateCanonicalCoverage(materializationState);
                if (runtime._runtimeTape is null || runtime._runtimeJournal is null)
                    throw new InvalidDataException("production materialization fixture lacks source tape");
                CortexPolicyDecision materializationSourceDecision = new(
                    new CortexPolicyDecisionID(0xB747F1FAD2C27021UL), HomeostatPolicyBoundaryDomain.Instance.PolicyID,
                    new CortexPolicyDecisionReadout(0, 1, 1, 1, CortexPolicyAuthorities.Grammar,
                        fixtureRevision, CortexPolicySelectionCauses.GrammarCandidate,
                        fixtureSupportDigest, fixtureSourceCandidateFingerprint));
                TapeEventID materializationSourceEventID = TapePacketCreator.AppendPolicyDecision(
                    runtime._runtimeTape, runtime._runtimeJournal, completedStep, in materializationSourceDecision,
                    [new MetricSample(new MetricID(0), NumericValue.FromI64(0))], actionCount: 2, out _);
                CortexPolicyBoundarySourceCorroboration materializationSourceCorroboration = new(
                    HomeostatPolicyBoundaryDomain.Instance.PolicyID, materializationSourceDecision.DecisionID, materializationSourceEventID,
                    materializationSourceDecision.Authority, materializationSourceDecision.SelectionCause,
                    fixtureRevision, fixtureFundingFingerprint, fixtureSourceCandidateFingerprint,
                    fixtureSupportDigest, "", CachedContexts: 1, Comparisons: 1, Agreements: 1, Misses: 0)
                {
                    CanonicalState = fixtureCanonicalState,
                };
                materializationSourceCorroboration = materializationSourceCorroboration with
                {
                    CorroborationDigest = materializationSourceCorroboration.ComputeDigest(),
                };
                _ = TapePacketCreator.AppendPolicyBoundarySourceCorroboration(
                    runtime._runtimeTape, runtime._runtimeJournal, completedStep, in materializationSourceCorroboration);
                fixtureSourceDecision = materializationSourceDecision;
                fixtureSourceDecisionEventID = materializationSourceEventID;
                focusedSourceDecision = materializationSourceDecision;
                focusedSourceEventID = materializationSourceEventID;
                focusedSourceCorroboration = materializationSourceCorroboration;
                // Custody names the exact bytes every child loads. Cut that image after the
                // canonical candidate and source corroboration exist, then bind the parent funding
                // without manufacturing a second, unauthenticated seed image.
                runtime.SetRuntimeForkBoundary(true);
                runtime.FlushPolicyJournalBuffer();
                byte[] materializationCheckpoint = runtime.CaptureForkSnapshot();
                using MemoryStream materializationTapeLog = new();
                runtime.CopyTapeLogTo(materializationTapeLog);
                byte[] materializationCurve = File.ReadAllBytes(runtime.CurrentRun.PathOf("curve.tsv"));
                (byte[] materializationExcursions, long materializationExcursionCursor) = runtime.CopyExcursionLog();
                seed = CortexForkSeed.Materialize(checked(completedStep + 1), materializationCheckpoint, materializationTapeLog.ToArray(),
                    materializationCurve, PersistedConfigDigest(runtime.Config.ToRunConfig(null)),
                    runtime.CopyPolicyJournals(), materializationExcursions, materializationExcursionCursor);
                // The production seed is now immutable.  Exercise the authority-identity
                // constructor only against the live parent after this cut, then erase every
                // parent persistence surface before the callback returns.
                CortexPolicyQuotaDecisionID identityQuotaID = new(0xB747F1FAD2C27031UL);
                string identityFundingJournalPath = runtime.CurrentRun.PathOf(PolicyTrialQuotaJournalFile);
                string identitySettlementJournalPath = runtime.CurrentRun.PathOf(PolicyTrialCompletionJournalFile);
                string identityCustodyDirectory = Path.Combine(runtime.CurrentRun.Dir,
                    PolicyBoundarySeedCustodyDirectory, identityQuotaID.ToString());
                bool identityFundingJournalCaptured = false;
                bool identitySettlementJournalCaptured = false;
                bool identityFundingJournalExisted = false;
                bool identitySettlementJournalExisted = false;
                byte[] identityFundingJournalBefore = [];
                byte[] identitySettlementJournalBefore = [];
                long reservedBeforeIdentityProbe = runtime._policyTrialHeldSteps;
                try
                {
                    CortexPolicyTrialAuthorityIdentity liveIdentity = identityForFixture
                        ?? throw new InvalidDataException("production identity fixture lost its captured tuple");
                    if (!liveIdentity.IsValid)
                        throw new InvalidDataException("production identity fixture found an incomplete checkpoint tuple");
                    if (runtime._runtimeTape is null || runtime._runtimeJournal is null)
                        throw new InvalidDataException("production identity fixture lacks source tape");
                    CortexPolicyDecision identitySourceDecision = identitySourceDecisionForFixture;
                    TapeEventID identitySourceDecisionEventID = identitySourceDecisionEventIDForFixture;
                    CortexPolicyBoundarySourceCorroboration identityCorroboration = identityCorroborationForFixture;
                    if (identitySourceDecision.DecisionID.Value == 0 || identitySourceDecisionEventID.Value <= 0
                        || identityCorroboration.CorroborationDigest.Length != 64)
                        throw new InvalidDataException("production identity fixture lost its source corroboration tuple");

                    string identityAttemptID = identityQuotaID.ToString();
                    identityFundingJournalExisted = File.Exists(identityFundingJournalPath);
                    identityFundingJournalBefore = identityFundingJournalExisted
                        ? File.ReadAllBytes(identityFundingJournalPath) : [];
                    identityFundingJournalCaptured = true;
                    identitySettlementJournalExisted = File.Exists(identitySettlementJournalPath);
                    identitySettlementJournalBefore = identitySettlementJournalExisted
                        ? File.ReadAllBytes(identitySettlementJournalPath) : [];
                    identitySettlementJournalCaptured = true;
                    if (Directory.Exists(identityCustodyDirectory))
                        throw new InvalidDataException("production identity fixture found pre-existing tuple custody");
                    string identityAuditOnlyDigest = runtime.PersistPolicyBoundarySeed(identityQuotaID, HomeostatPolicyBoundaryDomain.Instance.PolicyID,
                            liveIdentity.CandidateFingerprint.Value, checked(identitySeedForFixture!.NextStep - 1), liveIdentity.CandidateRevision, identitySeedForFixture,
                            identitySourceDecision.DecisionID.Value, identitySourceDecisionEventID.Value,
                            identitySupportDigestForFixture, liveIdentity.CandidateFingerprint.Value,
                            liveIdentity.ActiveProgramFingerprint.Value, CortexPolicyQuotaDecisions.Paid,
                            identityCorroboration.CanonicalState)
                        ?? throw new InvalidDataException("production identity fixture could not persist tuple custody");
                    CortexPolicyTrialAllocation identityAllocation = CortexPolicyTrialAllocation.Bind(
                        HomeostatPolicyBoundaryDomain.Instance.PolicyID, config.Learning.Policies.TrialAllocation
                            ?? throw new InvalidDataException("production identity fixture has no trial allocation"));
                    int identityQuotaStep = checked(identitySeedForFixture.NextStep - 1);
                    CortexPolicyTrialQuotaDecision identityFunding = new(
                        identityQuotaID, HomeostatPolicyBoundaryDomain.Instance.PolicyID, liveIdentity.CandidateFingerprint.Value,
                        identityQuotaStep, 1, 1, 1, 1,
                        CortexPolicyQuotaDecisions.Paid, 1, 0)
                    {
                        CandidateState = CortexPolicyTrialCandidateStates.Active,
                        CandidateOriginStep = identityQuotaStep,
                        CandidateCurrentStep = identityQuotaStep,
                        CandidateRequiredStep = -1,
                        CandidateRevision = liveIdentity.CandidateRevision,
                        CanonicalState = identityCorroboration.CanonicalState,
                        ReadoutFingerprint = liveIdentity.ActiveProgramFingerprint.Value,
                        AllocationIdentity = identityAllocation.Identity,
                        AllocationDigest = identityAllocation.Digest,
                        AllocationArmSteps = identityAllocation.ArmSteps,
                        SeedAuditOnlyDigest = identityAuditOnlyDigest,
                    };
                    runtime._policyTrialQuotaDecisions.Add(identityFunding);
                    runtime._policyTrialQuotaByID.Add(identityFunding.QuotaDecisionID, identityFunding);
                    runtime._policyTrialHeldSteps = checked(runtime._policyTrialHeldSteps + identityFunding.HeldArmSteps);
                    runtime.AppendPolicyTrialQuota(in identityFunding);
                    runtime.FlushPolicyJournalBuffer();
                    CortexRunConfig identityConfig = runtime.Config.ToRunConfig(null);
                    PolicyBoundaryObligation identityObligation = runtime._policyBoundaryObligations[HomeostatPolicyBoundaryDomain.Instance.PolicyID];
                    bool ApplyIdentityArm(CortexPolicyTrialAuthorityIdentity identity, CortexForkRailRoles role)
                    {
                        (Run child, CortexForkMaterializationContract contract) = runtime.CurrentRun.CreateMaterializedChildRun(role, identityAttemptID, identitySeedForFixture.ColdSeedDigest);
                        CortexForkArm<PolicyBoundaryTrialOutcome> arm = CreateHomeostatBoundaryArm(
                            child.Dir, runtime.Step, 1, PolicyBoundaryArms.Candidate, identityConfig, identity,
                            CortexPolicyAuthorities.Grammar, railRole: role, parentRunID: Path.GetFileName(parentDirectory),
                            materializationContract: contract, obligation: identityObligation.ID, candidateBoundary: PolicyBoundaryRational.Zero);
                        CortexForkArm<int> probeArm = new(child.Dir, arm.CreateCortex, static cortex => cortex.Step,
                            interveneAfterLoad: arm.InterveneAfterLoad,
                            completionMode: CortexForkCompletionModes.ExactAbsoluteStep,
                            railRole: role, afterRuntimeBind: arm.AfterRuntimeBind,
                            parentRunID: arm.ParentRunID, materializationContract: arm.MaterializationContract);
                        CortexForkRunReceipt<int> receipt = CortexForkRunner.RunFork(runtime, identitySeedForFixture, probeArm, checked(identitySeedForFixture.NextStep + 1));
                        return receipt.ExitCode == 0;
                    }
                    bool acceptedTuple = ApplyIdentityArm(liveIdentity, CortexForkRailRoles.Calibration);
                    CortexPolicyTrialAuthorityIdentity swappedIdentity = new(
                        new(liveIdentity.CandidateFingerprint.Value), liveIdentity.CandidateFingerprint, liveIdentity.CandidateRevision);
                    CortexPolicyTrialAuthorityIdentity staleIdentity = liveIdentity with
                    {
                        CandidateRevision = new GrammarRevisionID(checked(liveIdentity.CandidateRevision.Value + 1)),
                    };
                    bool swappedRejected = false;
                    bool staleRejected = false;
                    try { _ = ApplyIdentityArm(swappedIdentity, CortexForkRailRoles.Evaluation); }
                    catch (InvalidOperationException error) when (error.Message.Contains("field=active_program_fingerprint", StringComparison.Ordinal)) { swappedRejected = true; }
                    try { _ = ApplyIdentityArm(staleIdentity, CortexForkRailRoles.Calibration); }
                    catch (InvalidOperationException error) when (error.Message.Contains("field=candidate_revision", StringComparison.Ordinal)) { staleRejected = true; }
                    authorityIdentityFixturePassed = acceptedTuple && swappedRejected && staleRejected;
                    authorityIdentityFixtureReceipt = $"program={liveIdentity.ActiveProgramFingerprint} candidate={liveIdentity.CandidateFingerprint} revision={liveIdentity.CandidateRevision.Value} accepted={(acceptedTuple ? "yes" : "no")} swapped={(swappedRejected ? "rejected" : "ACCEPTED")} stale={(staleRejected ? "rejected" : "ACCEPTED")}";
                }
                catch (Exception error)
                {
                    authorityIdentityFixtureReceipt = $"error={error.GetType().Name}:{error.Message}";
                    identityProbeIsolated = false;
                }
                finally
                {
                    Exception? cleanupFailure = null;
                    bool noIdentityFunding = false;
                    bool noIdentitySettlement = false;
                    bool fundingJournalRestored = false;
                    bool settlementJournalRestored = false;
                    bool custodyDeleted = false;
                    bool reservedRestored = false;
                    try
                    {
                        runtime._policyTrialCompletions.RemoveAll(row => row.QuotaDecisionID.Equals(identityQuotaID));
                        runtime._policyTrialCompletionByID.Remove(identityQuotaID);
                        if (runtime._policyTrialQuotaByID.Remove(identityQuotaID, out CortexPolicyTrialQuotaDecision removedIdentityFunding))
                            runtime._policyTrialHeldSteps = checked(runtime._policyTrialHeldSteps - removedIdentityFunding.HeldArmSteps);
                        runtime._policyTrialQuotaDecisions.RemoveAll(row => row.QuotaDecisionID.Equals(identityQuotaID));
                        runtime.InvalidatePolicyTrialReconcileMemo();
                        runtime.FlushPolicyJournalBuffer();
                        if (identityFundingJournalCaptured)
                        {
                            if (identityFundingJournalExisted)
                                runtime.CurrentRun.WriteAtomic(PolicyTrialQuotaJournalFile, stream => stream.Write(identityFundingJournalBefore));
                            else if (File.Exists(identityFundingJournalPath))
                                File.Delete(identityFundingJournalPath);
                        }
                        if (identitySettlementJournalCaptured)
                        {
                            if (identitySettlementJournalExisted)
                                runtime.CurrentRun.WriteAtomic(PolicyTrialCompletionJournalFile, stream => stream.Write(identitySettlementJournalBefore));
                            else if (File.Exists(identitySettlementJournalPath))
                                File.Delete(identitySettlementJournalPath);
                        }
                        if (Directory.Exists(identityCustodyDirectory))
                            Directory.Delete(identityCustodyDirectory, recursive: true);
                        noIdentityFunding = !runtime._policyTrialQuotaByID.ContainsKey(identityQuotaID)
                            && runtime._policyTrialQuotaDecisions.All(row => !row.QuotaDecisionID.Equals(identityQuotaID));
                        noIdentitySettlement = !runtime._policyTrialCompletionByID.ContainsKey(identityQuotaID)
                            && runtime._policyTrialCompletions.All(row => !row.QuotaDecisionID.Equals(identityQuotaID));
                        fundingJournalRestored = !identityFundingJournalCaptured || (File.Exists(identityFundingJournalPath)
                            ? File.ReadAllBytes(identityFundingJournalPath).SequenceEqual(identityFundingJournalBefore)
                            : !identityFundingJournalExisted);
                        settlementJournalRestored = !identitySettlementJournalCaptured || (File.Exists(identitySettlementJournalPath)
                            ? File.ReadAllBytes(identitySettlementJournalPath).SequenceEqual(identitySettlementJournalBefore)
                            : !identitySettlementJournalExisted);
                        custodyDeleted = !Directory.Exists(identityCustodyDirectory);
                        reservedRestored = runtime._policyTrialHeldSteps == reservedBeforeIdentityProbe;
                        identityProbeIsolated = noIdentityFunding && noIdentitySettlement
                            && fundingJournalRestored && settlementJournalRestored && custodyDeleted && reservedRestored;
                        if (!identityProbeIsolated)
                            throw new InvalidDataException("production identity fixture teardown leaked funding, settlement, journal, custody, or reservation state");
                        authorityIdentityFixtureReceipt += " funding=isolated";
                    }
                    catch (Exception error)
                    {
                        cleanupFailure = error;
                        identityProbeIsolated = false;
                        authorityIdentityFixtureReceipt += $" funding=LEAKED:{error.GetType().Name}:{error.Message}";
                    }
                    if (cleanupFailure is not null)
                        throw new InvalidDataException("production identity fixture cleanup failed", cleanupFailure);
                }
                string parentID = Path.GetFileName(parentDirectory);
                string attemptID = "B747F1FAD2C27CE2";
                ulong attemptQuotaID = ulong.Parse(attemptID, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                string attemptAuditOnlyDigest = runtime.PersistPolicyBoundarySeed(
                        new CortexPolicyQuotaDecisionID(attemptQuotaID), HomeostatPolicyBoundaryDomain.Instance.PolicyID, fixtureSourceCandidateFingerprint,
                        checked(seed.NextStep - 1), fixtureRevision, seed,
                        fixtureSourceDecision.DecisionID.Value, fixtureSourceDecisionEventID.Value,
                        fixtureSupportDigest,
                        fixtureSourceCandidateFingerprint, fixtureFundingFingerprint, CortexPolicyQuotaDecisions.Paid,
                        fixtureCanonicalState)
                    ?? throw new InvalidDataException("fixture failed to persist attempt-keyed seed custody");
                CortexPolicyTrialAllocation attemptAllocation = CortexPolicyTrialAllocation.Bind(
                    HomeostatPolicyBoundaryDomain.Instance.PolicyID, config.Learning.Policies.TrialAllocation!);
                CortexPolicyTrialQuotaDecision attemptFunding = new(
                    new CortexPolicyQuotaDecisionID(attemptQuotaID), HomeostatPolicyBoundaryDomain.Instance.PolicyID,
                    fixtureSourceCandidateFingerprint, checked(seed.NextStep - 1), 3, 4, 12, 12,
                    CortexPolicyQuotaDecisions.Paid, 12, 4084)
                {
                    ReadoutFingerprint = fixtureFundingFingerprint,
                    CandidateRevision = fixtureRevision,
                    AllocationIdentity = attemptAllocation.Identity,
                    AllocationDigest = attemptAllocation.Digest,
                    AllocationArmSteps = attemptAllocation.ArmSteps,
                    CanonicalState = fixtureCanonicalState,
                    SeedAuditOnlyDigest = attemptAuditOnlyDigest,
                };
                runtime._policyTrialQuotaDecisions.Add(attemptFunding);
                runtime._policyTrialQuotaByID.Add(attemptFunding.QuotaDecisionID, attemptFunding);
                runtime._policyTrialHeldSteps = checked(runtime._policyTrialHeldSteps + attemptFunding.HeldArmSteps);
                runtime.AppendPolicyTrialQuota(in attemptFunding);
                runtime.BindActiveTrialQuotaIdentity(HomeostatPolicyBoundaryDomain.Instance.PolicyID,
                    attemptFunding.QuotaDecisionID, attemptAuditOnlyDigest);
                CortexRunConfig runConfig = runtime.Config.ToRunConfig(null);
                PolicyBoundaryObligation fixtureObligation = runtime._policyBoundaryObligations[HomeostatPolicyBoundaryDomain.Instance.PolicyID];
                _ = runtime.InstallRevision
                    ?? throw new InvalidDataException("policy boundary fixture has no source publication");
                int[] horizons = [1, 2, 3];
                string fixtureCriticalityMetricID = Homeostat.GetPolicyFeatureMetricID(
                    (int)HomeostatPolicyFeatureIDs.Criticality).Value.ToString(CultureInfo.InvariantCulture);
                void PrepareFixtureArmSeed(Cortex cortex, PolicyBoundaryArms armKind, bool frozen, string childRunID,
                    CortexForkMaterializationContract contract)
                {
                    if (!TryReadPolicyBoundarySeedCustodyDocument(parentDirectory!, parentID, contract.AttemptID,
                            out PolicyBoundarySeedCustody boundCustody))
                        throw new InvalidDataException($"fixture arm {childRunID} has no authenticated seed custody");
                    CortexPolicyAuthorities authority = armKind switch
                    {
                        PolicyBoundaryArms.Baseline => CortexPolicyAuthorities.Launchpad,
                        PolicyBoundaryArms.ReflexFrozenControl => CortexPolicyAuthorities.Shadow,
                        _ => CortexPolicyAuthorities.Grammar,
                    };
                    bool forced = armKind == PolicyBoundaryArms.ForcedDivergentNull;
                    ulong? forcedSeed = forced
                        ? fixtureSourceCandidateFingerprint ^ 0x9E3779B97F4A7C15UL
                        : null;
                    PolicyCanonicalCoverageReceipt coverage = cortex.ReadCanonicalCoverage(HomeostatPolicyBoundaryDomain.Instance.PolicyID);
                    cortex.RecordPolicyOccurrenceCheck(HomeostatPolicyBoundaryDomain.Instance.PolicyID, fixtureFundingFingerprint,
                        comparisons: 1, agreements: 1, failures: 0, passed: true, coverage: coverage);
                    PolicyCanonicalStateID verifiedState = fixtureAuthorityIdentity.CanonicalState;
                    if (!Homeostat.TryGrantSharedPolicyScope(cortex, in verifiedState,
                            fixtureFundingFingerprint, fixtureSourceCandidateFingerprint,
                            fixtureSupportDigest, fixtureRevision))
                        throw new InvalidDataException($"fixture arm {childRunID} could not grant its canonical scope");
                    cortex.SetPolicyTrialAuthority(HomeostatPolicyBoundaryDomain.Instance.PolicyID, in fixtureAuthorityIdentity, authority,
                        grammarExecutionQuota: -1, forcedDivergenceSeed: forcedSeed, freezeAdaptation: frozen);
                    if (forced)
                    {
                        if (!TryDecodeCanonicalState(boundCustody.canonicalState, HomeostatPolicyBoundaryDomain.Instance, out PolicyCanonicalStateID custodyState))
                            throw new InvalidDataException($"fixture arm {childRunID} has no canonical custody scope");
                        cortex.BindPendingForcedTrialIntent(HomeostatPolicyBoundaryDomain.Instance.PolicyID,
                            boundCustody.fundingID, boundCustody.sourceFundingDecision, boundCustody.sourceDecisionID,
                            boundCustody.sourceDecisionEventID, boundCustody.sourceCorroborationEventID,
                            boundCustody.sourceSupportDigest, boundCustody.sourceCandidateFingerprint,
                            boundCustody.readoutFingerprint, boundCustody.candidateFingerprint,
                            new GrammarRevisionID(boundCustody.candidateRevision), in custodyState,
                            fixtureObligation.ID.Value, (byte)armKind,
                            checked((ushort)(400 + (int)HomeostatPolicyFeatureIDs.Criticality)),
                            boundCustody.sourceRunID, boundCustody.custodyDigest);
                    }
                    cortex.BindActiveTrialQuotaIdentity(HomeostatPolicyBoundaryDomain.Instance.PolicyID,
                        new CortexPolicyQuotaDecisionID(boundCustody.fundingID), boundCustody.custodyDigest);
                    // The ladder's rung-zero preparation hook is the one intervention point for
                    // each arm.  Do not count the later runtime bind or continuation rungs.
                    Interlocked.Increment(ref ladderInterventionCount);
                }
                CortexForkArm<PolicyBoundaryTrialOutcome> CreateFixtureArm(
                    Run child, int horizon, PolicyBoundaryArms armKind, CortexForkRailRoles railRole,
                    CortexForkMaterializationContract contract, bool frozen = false,
                    bool publishBeforeFirstAction = false)
                {
                    int generation = ParsePolicyBoundaryChildIndex(Path.GetFileName(child.Dir));
                    long seedPaid = 0;
                    ulong seedGrammar = 0;
                    ulong seedTransitions = 0;
                    return new(child.Dir, () => Cortex.CreateCheckpointRuntime(runConfig), cortex =>
                    {
                        if (!TryReadPolicyBoundarySeedCustodyDocument(parentDirectory!, parentID, contract.AttemptID,
                                out PolicyBoundarySeedCustody fixtureCustody))
                            throw new InvalidDataException("fixture rail has no authenticated seed custody");
                        long matchedSpend = checked((long)cortex.Step + 1 - fixtureCustody.nextStep);
                        bool forced = armKind == PolicyBoundaryArms.ForcedDivergentNull;
                        bool requireOrdinaryOutcome = forced && horizon == horizons[^1];
                        if (!cortex.TryReadPolicyTrialExecutionReceiptForQuota(HomeostatPolicyBoundaryDomain.Instance.PolicyID,
                                new CortexPolicyQuotaDecisionID(fixtureCustody.fundingID),
                                out CortexPolicyTrialExecutionOutcomes executionOutcome,
                                out long requestCount, out long guardAdmittedCount,
                                out CortexPolicyDecisionReadout lastRequestReadout,
                                out CortexPolicyDecisionID lastRequestDecisionID, out int lastRequestStep,
                                out CortexPolicyDecisionReadout executedReadout,
                                out CortexPolicyDecisionID executedDecisionID, out ulong executedFingerprint,
                                out int executedStep))
                            throw new InvalidDataException($"fixture child has no authenticated execution receipt arm={armKind} step={cortex.Step} suppress={cortex.GetPolicy(HomeostatPolicyBoundaryDomain.Instance.PolicyID).SuppressTrialPackets} active={cortex.GetPolicy(HomeostatPolicyBoundaryDomain.Instance.PolicyID).ActiveTrialQuotaID.Value:X} outcome={cortex.GetPolicy(HomeostatPolicyBoundaryDomain.Instance.PolicyID).TrialExecutionOutcome} cause={cortex.GetPolicy(HomeostatPolicyBoundaryDomain.Instance.PolicyID).TrialExecutionCause} history={cortex.GetPolicy(HomeostatPolicyBoundaryDomain.Instance.PolicyID).HistoricalTrialExecution.IsPresent}");
                        bool executionObserved = executionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted;
                        TapeEventID executedDecisionEventID = forced && executionObserved
                            ? cortex.FindPolicyDecisionEvent(executedDecisionID) : default;
                        CortexPolicyOutcomeEvidence outcomeEvidence = default;
                        bool hasOutcomeEvidence = executionObserved
                            && cortex.TryReadPolicyOutcomeEvidence(executedDecisionID, out outcomeEvidence);
                        if (requireOrdinaryOutcome && !hasOutcomeEvidence)
                            throw new InvalidDataException("fixture terminal forced-null rail lacks its ordinary policy outcome");
                        if (requireOrdinaryOutcome)
                            Interlocked.Increment(ref ordinaryOutcomeCustodyCount);
                        ulong forcedDivergenceSeed = forced
                            ? fixtureSourceCandidateFingerprint ^ 0x9E3779B97F4A7C15UL : 0;
                        PolicyCanonicalStateID fixtureSuccessorState = default;
                        if (executionObserved)
                        {
                            if (!cortex.TryReadPolicyTrialExecutionScopeForQuota(HomeostatPolicyBoundaryDomain.Instance.PolicyID,
                                    new CortexPolicyQuotaDecisionID(fixtureCustody.fundingID), out PolicyVerifiedScopeEntry scope))
                                throw new InvalidDataException("fixture child execution has no authenticated scope");
                            fixtureSuccessorState = scope.State;
                            bool executionScopeCandidateExact = executedReadout.SelectionCause == CortexPolicySelectionCauses.Launchpad
                                ? executedReadout.ReadoutCandidateFingerprint == 0
                                    && executedReadout.ReadoutCandidateOccurrenceDigest == 0
                                : scope.CandidateFingerprint == executedReadout.ReadoutCandidateFingerprint
                                    && scope.OccurrenceDigest == executedReadout.ReadoutCandidateOccurrenceDigest;
                            if (!scope.IsValid || scope.ReadoutFingerprint != executedFingerprint
                                || !executionScopeCandidateExact
                                || scope.Revision != executedReadout.GrammarRevision)
                                throw new InvalidDataException("fixture child execution scope disagrees with its receipt");
                        }
                        if (forced && horizon == horizons[^1]
                            && (!executionObserved || executedReadout.SelectionCause != CortexPolicySelectionCauses.TrialOverride
                            || executedDecisionEventID.Value <= 0 || forcedDivergenceSeed == 0
                            || !HomeostatPolicyBoundaryDomain.Instance.ValidateCanonicalState(in fixtureSuccessorState)))
                            throw new InvalidDataException("fixture forced-null rail lacks captured execution custody");
                        CortexPolicyRuntimeReceipt policyReceipt = cortex.ReadPolicyRuntimeReceipt(HomeostatPolicyBoundaryDomain.Instance.PolicyID);
                        PolicyBoundaryTrialOutcome outcome = new(
                            checked((long)policyReceipt.PaidGrammarOutcomes - seedPaid), matchedSpend, true,
                            checked((long)policyReceipt.GrammarExecutions - (long)seedGrammar),
                            checked((long)policyReceipt.TrialAdaptationTransitions - (long)seedTransitions),
                            policyReceipt.AdaptationEnabled)
                        {
                            ExecutionOutcome = executionOutcome,
                            RequestCount = requestCount,
                            GuardAdmittedCount = guardAdmittedCount,
                            LastRequestDecisionID = lastRequestDecisionID,
                            LastRequestStep = lastRequestStep,
                            LastRequestReadout = lastRequestReadout,
                            ExecutedDecisionID = executionObserved ? executedDecisionID : default,
                            ExecutedStep = executionObserved ? executedStep : -1,
                            ExecutedLaunchpadAction = executionObserved ? executedReadout.LaunchpadAction : -1,
                            ExecutedRawCandidateAction = executionObserved ? executedReadout.RawCandidateAction : -1,
                            ExecutedSelectedCandidateAction = executionObserved ? executedReadout.SelectedCandidateAction : -1,
                            ExecutedAction = executionObserved ? executedReadout.ExecutedAction : -1,
                            ExecutedAuthority = executionObserved ? executedReadout.Authority : CortexPolicyAuthorities.Launchpad,
                            ExecutedSelectionCause = executionObserved ? executedReadout.SelectionCause : CortexPolicySelectionCauses.Launchpad,
                            ExecutedReadoutFingerprint = executionObserved ? executedFingerprint : 0,
                            ExecutedReadoutRevision = executionObserved ? executedReadout.GrammarRevision.Value : 0,
                            ExecutedReadoutOccurrenceDigest = executionObserved ? executedReadout.ReadoutCandidateOccurrenceDigest : 0,
                            ExecutedCandidateFingerprint = executionObserved ? executedReadout.ReadoutCandidateFingerprint : 0,
                            ExecutedCanonicalState = executionObserved ? fixtureSuccessorState : default,
                            ExecutedDecisionEventID = executedDecisionEventID,
                            ExecutedOutcomeEventID = requireOrdinaryOutcome ? outcomeEvidence.EventID : default,
                            ExecutedOutcomePayloadSHA256 = requireOrdinaryOutcome ? outcomeEvidence.PayloadSHA256 : "",
                            ForcedDivergenceSeed = forced && executionObserved ? forcedDivergenceSeed : 0,
                        };
                        if (publishBeforeFirstAction && executionObserved)
                        {
                            switch (executedReadout.SelectionCause)
                            {
                                case CortexPolicySelectionCauses.Launchpad:
                                    Interlocked.Increment(ref baselineCauseCount); break;
                                case CortexPolicySelectionCauses.GrammarCandidate:
                                    Interlocked.Increment(ref candidateCauseCount); break;
                                case CortexPolicySelectionCauses.TrialOverride:
                                    Interlocked.Increment(ref forcedCauseCount); break;
                                case CortexPolicySelectionCauses.ShadowCandidate:
                                    Interlocked.Increment(ref reflexCauseCount); break;
                            }
                            if (forced && fixtureCustody.fundingID != 0 && executedDecisionEventID.Value > 0
                                && forcedDivergenceSeed != 0)
                                Interlocked.Increment(ref forcedExecutionPaidCapturedCount);
                        }
                        string fixtureOccurrenceCheckDigest = "";
                        string fixtureOccurrenceCheckCoverageDigest = "";
                        if (forced)
                        {
                            cortex.FlushPolicyJournalBuffer();
                            string verificationPath = cortex.CurrentRun.PathOf(PolicyOccurrenceCheckReceiptFile);
                            if (!File.Exists(verificationPath))
                                throw new InvalidDataException("fixture forced-null rail has no verification receipt");
                            fixtureOccurrenceCheckDigest = ComputePolicyBoundaryFileSHA256(verificationPath);
                            string coveragePath = cortex.CurrentRun.PathOf(PolicyOccurrenceCheckCoverageReceiptFile);
                            if (!File.Exists(coveragePath))
                                throw new InvalidDataException("fixture forced-null rail has no verification coverage receipt");
                            fixtureOccurrenceCheckCoverageDigest = ComputePolicyBoundaryFileSHA256(coveragePath);
                        }
                        PolicyBoundaryRailMetadata metadata = new(runtime.Step, horizon, armKind, railRole, outcome.ExecutedReadoutFingerprint, outcome, cortex.Step, contract,
                            fixtureCustody.sourceRunID, fixtureCustody.nextStep, fixtureCustody.custodyDigest,
                            new CortexForkDigests(fixtureCustody.checkpointSHA256, fixtureCustody.tapeSpanlogSHA256,
                                fixtureCustody.curveSHA256, fixtureCustody.excursionsSHA256), ParsePolicyBoundaryChildIndex(Path.GetFileName(child.Dir)),
                            fixtureObligation.ID, PolicyBoundaryRational.Zero, fixtureSuccessorState,
                            forced ? outcome.ExecutedReadoutOccurrenceDigest : 0,
                            fixtureOccurrenceCheckDigest, fixtureOccurrenceCheckCoverageDigest,
                            requireOrdinaryOutcome);
                        byte[] bytes = metadata.Encode(cortex.RequirePolicyBoundaryDomain(HomeostatPolicyBoundaryDomain.Instance.PolicyID));
                        cortex.CurrentRun.WriteAtomic("policy-boundary.rail.ron", stream => stream.Write(bytes));
                        return outcome;
                    }, interveneAfterLoad: cortex => PrepareFixtureArmSeed(
                        cortex, armKind, frozen, Path.GetFileName(child.Dir), contract),
                    railRole: railRole,
                    afterRuntimeBind: (cortex, window) =>
                    {
                        if (!TryReadPolicyBoundarySeedCustodyDocument(parentDirectory!, parentID, contract.AttemptID,
                                out PolicyBoundarySeedCustody boundCustody))
                            throw new InvalidDataException("fixture rail has no authenticated seed custody for active funding binding");
                        if (!publishBeforeFirstAction) return;
                        if (!TryDecodeCanonicalState(boundCustody.canonicalState, HomeostatPolicyBoundaryDomain.Instance, out PolicyCanonicalStateID restoreCanonicalState))
                            throw new InvalidDataException("fixture rail has no canonical custody scope");
                        CortexPolicyQuotaDecisionID restoreQuotaID = new(boundCustody.fundingID);
                        CortexPolicySelectionCauses restoreCause = armKind switch
                        {
                            PolicyBoundaryArms.Baseline => CortexPolicySelectionCauses.Launchpad,
                            PolicyBoundaryArms.ForcedDivergentNull => CortexPolicySelectionCauses.TrialOverride,
                            PolicyBoundaryArms.ReflexFrozenControl => CortexPolicySelectionCauses.ShadowCandidate,
                            _ => CortexPolicySelectionCauses.GrammarCandidate,
                        };
                        PolicyBoundaryContinuationModes continuation = cortex.AuthenticatePolicyBoundaryContinuation(
                            in boundCustody, restoreCause, in restoreCanonicalState);
                        switch (continuation)
                        {
                            case PolicyBoundaryContinuationModes.RestoreNotAttempted:
                                Interlocked.Increment(ref continuationNotAttemptedCount); break;
                            case PolicyBoundaryContinuationModes.PreserveGuardDenied:
                                Interlocked.Increment(ref continuationGuardDeniedCount); break;
                            case PolicyBoundaryContinuationModes.PreserveHistorical:
                                Interlocked.Increment(ref continuationHistoricalCount); break;
                        }
                        if (generation > 0)
                        {
                            Interlocked.Increment(ref continuationRungOneCount);
                            CortexForkSeedLoadRailDocument seedLoadIntent = CortexForkTerminalRunReceipt.ReadSeedRailDocument(
                                cortex.CurrentRun.PathOf("seed-load-intent.ron"));
                            CortexForkSeedLoadReceipt seedLoad = seedLoadIntent.Receipt;
                            if (!seedLoad.Bound || !seedLoad.Exact
                                || seedLoad.ExecutionWindow != window
                                || seedLoad.SourceNextStep != window.StartStep
                                || string.IsNullOrWhiteSpace(seedLoad.SourceRunID)
                                || seedLoad.AdoptionAncestry is not { Length: > 0 })
                                throw new InvalidDataException("fixture rung continuation has incomplete seed-load ancestry");
                            Interlocked.Increment(ref continuationRungOneSeedAncestryCount);
                            switch (continuation)
                            {
                                case PolicyBoundaryContinuationModes.RestoreNotAttempted:
                                    Interlocked.Increment(ref continuationRungOneNotAttemptedCount); break;
                                case PolicyBoundaryContinuationModes.PreserveGuardDenied:
                                    Interlocked.Increment(ref continuationRungOneGuardDeniedCount); break;
                                case PolicyBoundaryContinuationModes.PreserveHistorical:
                                    Interlocked.Increment(ref continuationRungOneHistoricalCount); break;
                            }
                        }
                        if (continuation == PolicyBoundaryContinuationModes.RestoreNotAttempted)
                        {
                            PolicyState boundState = cortex.GetPolicy(HomeostatPolicyBoundaryDomain.Instance.PolicyID);
                            ulong? restoreForcedSeed = restoreCause == CortexPolicySelectionCauses.TrialOverride
                                ? boundState.TrialForcedDivergenceSeed
                                : null;
                            cortex.RestorePaidPolicyTrialEpoch(HomeostatPolicyBoundaryDomain.Instance.PolicyID,
                                restoreQuotaID, boundCustody.custodyDigest,
                                restoreCause, restoreForcedSeed, in restoreCanonicalState,
                                boundCustody.readoutFingerprint, boundCustody.candidateFingerprint,
                                boundCustody.sourceSupportDigest, new GrammarRevisionID(boundCustody.candidateRevision));
                        }
                        CortexPolicyRuntimeReceipt boundReceipt = cortex.ReadPolicyRuntimeReceipt(HomeostatPolicyBoundaryDomain.Instance.PolicyID);
                        seedPaid = checked((long)boundReceipt.PaidGrammarOutcomes);
                        seedGrammar = boundReceipt.GrammarExecutions;
                        seedTransitions = boundReceipt.TrialAdaptationTransitions;
                        if (publishBeforeFirstAction)
                        {
                            if (cortex.InstallRevision is null)
                                throw new InvalidDataException("fixture publication-before-first-action is absent");
                            Interlocked.Increment(ref publicationBeforeFirstActionCount);
                        }
                    },
                    afterCompletedStepEveryStep: (cortex, completedStep) =>
                    {
                        if (!publishBeforeFirstAction
                            || checked(completedStep + 1) != checked(seed.NextStep + horizon))
                            return;
                        if (!TryReadPolicyBoundarySeedCustodyDocument(parentDirectory!, parentID, contract.AttemptID,
                                out PolicyBoundarySeedCustody boundCustody))
                            throw new InvalidDataException("fixture terminal action has no authenticated seed custody");
                        PolicyState state = cortex.GetPolicy(HomeostatPolicyBoundaryDomain.Instance.PolicyID);
                        if (state.HistoricalTrialExecution.IsPresent) return;
                        if (!TryDecodeCanonicalState(boundCustody.canonicalState, HomeostatPolicyBoundaryDomain.Instance, out PolicyCanonicalStateID canonicalState))
                            throw new InvalidDataException("fixture terminal action has no canonical custody scope");
                        CortexPolicySelectionCauses expectedCause = armKind switch
                        {
                            PolicyBoundaryArms.Baseline => CortexPolicySelectionCauses.Launchpad,
                            PolicyBoundaryArms.ForcedDivergentNull => CortexPolicySelectionCauses.TrialOverride,
                            PolicyBoundaryArms.ReflexFrozenControl => CortexPolicySelectionCauses.ShadowCandidate,
                            _ => CortexPolicySelectionCauses.GrammarCandidate,
                        };
                        MetricSample[] features = new MetricSample[HomeostatPolicyBoundaryDomain.Instance.Schema.FeatureCount];
                        for (int index = 0; index < features.Length; index++)
                            features[index] = new MetricSample(Homeostat.GetPolicyFeatureMetricID(index), NumericValue.FromI64(0));
                        bool verifiedScope = state.VerifiedScopes.TryGetValue(canonicalState, out PolicyVerifiedScopeEntry scope)
                            && scope.IsValid
                            && scope.ReadoutFingerprint == ReadActivePolicyFingerprint(state)
                            && scope.CandidateFingerprint == state.ReadoutCandidateFingerprint
                            && scope.OccurrenceDigest == state.ReadoutCandidateOccurrenceDigest
                            && scope.Revision == state.ReadoutCandidateRevision;
                        Trace.Cortex.Boundary("policy.fixture-terminal-opportunity",
                            $"arm={armKind} step={cortex.Step} funding={state.ActiveTrialQuotaID} authority={state.Authority} configured_cause={state.TrialExecutionCause} expected_cause={expectedCause} state={canonicalState} active_state={state.ReadoutCandidateState} readout={ReadActivePolicyFingerprint(state):X16} candidate={state.ReadoutCandidateFingerprint:X16} support={state.ReadoutCandidateOccurrenceDigest:X16} revision={state.ReadoutCandidateRevision.Value} scope={(verifiedScope ? 1 : 0)} forced_seed={FormatOptionalSeed(state.TrialForcedDivergenceSeed)} remaining={state.TrialGrammarExecutionsRemaining}");
                        CortexPolicyActionPreparation prepared = cortex.PreparePolicyAction(
                            HomeostatPolicyBoundaryDomain.Instance.PolicyID, 0, in canonicalState, true,
                            features, ReadOnlySpan<MetricID>.Empty);
                        Trace.Cortex.Boundary("policy.fixture-terminal-admission",
                            $"arm={armKind} evaluated={(prepared.BoundaryGate.Evaluated ? 1 : 0)} observed={prepared.BoundaryGate.Observed.ToString("R", CultureInfo.InvariantCulture)} boundary={prepared.BoundaryGate.Boundary} comparison={prepared.BoundaryGate.Comparison} satisfied={(prepared.BoundaryGate.Satisfied ? 1 : 0)} boundary_allows={(prepared.BoundaryAllowsProduction ? 1 : 0)} scope_allows={(prepared.CanonicalScopeAllowsProduction ? 1 : 0)} raw={prepared.RawCandidateAction}");
                        CortexPolicyDecision decision = cortex.ChoosePreparedPolicyAction(
                            in prepared, features, ReadOnlySpan<MetricID>.Empty);
                        if (decision.SelectionCause != expectedCause
                            || !cortex.TryReadPolicyTrialExecutionReceiptForQuota(HomeostatPolicyBoundaryDomain.Instance.PolicyID,
                                new CortexPolicyQuotaDecisionID(boundCustody.fundingID),
                                out CortexPolicyTrialExecutionOutcomes outcome, out _, out _, out _, out _, out _,
                                out _, out _, out _, out _)
                            || outcome != CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted)
                            throw new InvalidDataException($"fixture terminal action did not execute configured cause expected={expectedCause} actual={decision.SelectionCause}");
                        if (armKind == PolicyBoundaryArms.ForcedDivergentNull)
                        {
                            Span<MetricSample> outcomeSamples = stackalloc MetricSample[2]
                            {
                                new(new MetricID(500), NumericValue.FromI64(1)),
                                new(new MetricID(501), NumericValue.FromI64(1)),
                            };
                            cortex.ResolvePolicyOutcome(in decision, outcomeSamples, invariantClean: true, conservedCost: 1);
                        }
                    },
                    parentRunID: parentID,
                    materializationContract: contract,
                    persistCompletionBeforeLanding: (cortex, _, _, outcome) =>
                    {
                        if (armKind != PolicyBoundaryArms.ForcedDivergentNull
                            || outcome.ExecutionOutcome != CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted)
                            return;
                        if (!TryReadPolicyBoundarySeedCustodyDocument(parentDirectory!, parentID, contract.AttemptID,
                                out PolicyBoundarySeedCustody terminalCustody))
                            throw new InvalidDataException("fixture terminal scope check has no authenticated seed custody");
                        PolicyState terminalState = cortex.GetPolicy(HomeostatPolicyBoundaryDomain.Instance.PolicyID);
                        bool activeScopeCleared = !terminalState.SuppressTrialPackets
                            && terminalState.ActiveTrialQuotaID.Value == 0
                            && !terminalState.PendingForcedTrialIntent.HasSeed
                            && terminalState.HistoricalTrialExecution.IsPresent;
                        if (!activeScopeCleared
                            || !VerifyPolicyBoundaryTerminalScope(cortex, HomeostatPolicyBoundaryDomain.Instance,
                                new CortexPolicyQuotaDecisionID(terminalCustody.fundingID), in outcome,
                                out _))
                            throw new InvalidDataException("fixture terminal scope was not recovered from durable history");
                        Interlocked.Increment(ref terminalHistoricalScopeAcceptedCount);
                        PolicyBoundaryTrialOutcome mismatch = outcome with
                        {
                            ExecutedReadoutOccurrenceDigest = outcome.ExecutedReadoutOccurrenceDigest ^ 1UL,
                        };
                        if (VerifyPolicyBoundaryTerminalScope(cortex, HomeostatPolicyBoundaryDomain.Instance,
                                new CortexPolicyQuotaDecisionID(terminalCustody.fundingID), in mismatch,
                                out _))
                            throw new InvalidDataException("fixture terminal scope accepted a mismatched successor tuple");
                        Interlocked.Increment(ref terminalHistoricalScopeMismatchRejectedCount);
                    });
                }
                CortexForkArm<PolicyBoundaryTrialOutcome>[][] arms = new CortexForkArm<PolicyBoundaryTrialOutcome>[horizons.Length][];
                for (int i = 0; i < horizons.Length; i++)
                {
                    (Run baseline, CortexForkMaterializationContract baselineContract) =
                        runtime.CurrentRun.CreateMaterializedChildRun(CortexForkRailRoles.Baseline, attemptID, seed.ColdSeedDigest);
                    (Run candidate, CortexForkMaterializationContract candidateContract) =
                        runtime.CurrentRun.CreateMaterializedChildRun(CortexForkRailRoles.Candidate, attemptID, seed.ColdSeedDigest);
                    (Run forcedNull, CortexForkMaterializationContract forcedNullContract) =
                        runtime.CurrentRun.CreateMaterializedChildRun(CortexForkRailRoles.ForcedNull, attemptID, seed.ColdSeedDigest);
                    (Run reflex, CortexForkMaterializationContract reflexContract) =
                        runtime.CurrentRun.CreateMaterializedChildRun(CortexForkRailRoles.ReflexFrozen, attemptID, seed.ColdSeedDigest);
                    arms[i] =
                    [
                        CreateFixtureArm(baseline, horizons[i], PolicyBoundaryArms.Baseline, CortexForkRailRoles.Baseline,
                            baselineContract, publishBeforeFirstAction: true),
                        CreateFixtureArm(candidate, horizons[i], PolicyBoundaryArms.Candidate, CortexForkRailRoles.Candidate,
                            candidateContract, publishBeforeFirstAction: true),
                        CreateFixtureArm(forcedNull, horizons[i], PolicyBoundaryArms.ForcedDivergentNull, CortexForkRailRoles.ForcedNull,
                            forcedNullContract, publishBeforeFirstAction: true),
                        CreateFixtureArm(reflex, horizons[i], PolicyBoundaryArms.ReflexFrozenControl, CortexForkRailRoles.ReflexFrozen,
                            reflexContract, frozen: true, publishBeforeFirstAction: true),
                    ];
                }
                bool continuitySourceDigestMatchesSeed = arms.All(static row => row.All(static arm =>
                    arm.MaterializationContract is CortexForkMaterializationContract contract
                    && !string.IsNullOrWhiteSpace(contract.ColdSeedDigest)));
                if (!continuitySourceDigestMatchesSeed
                    || arms.SelectMany(static row => row).Any(arm =>
                        !string.Equals(arm.MaterializationContract!.Value.ColdSeedDigest,
                            seed.ColdSeedDigest, StringComparison.Ordinal)))
                        throw new InvalidDataException("continuity replay source digest disagrees with final paid seed");
                CortexPolicyTrialAllocation fixtureAllocation = CortexPolicyTrialAllocation.Bind(
                    HomeostatPolicyBoundaryDomain.Instance.PolicyID, config.Learning.Policies.TrialAllocation!);
                CortexPolicyReadoutReceipt recoveryReadout = new(fixtureRevision, fixtureFundingFingerprint, 1, 1, 1, 0, fixtureSupportDigest, fixtureSourceCandidateFingerprint);
                CortexPolicyTrialQuotaDecision AddRecoveryFunding(ulong id)
                {
                    CortexPolicyTrialQuotaDecision funding = new(new CortexPolicyQuotaDecisionID(id), HomeostatPolicyBoundaryDomain.Instance.PolicyID,
                        recoveryReadout.CandidateFingerprint, runtime.Step, horizons[^1], 4, 12, 12,
                        CortexPolicyQuotaDecisions.Paid, 12, 4084)
                    {
                        ReadoutFingerprint = recoveryReadout.Fingerprint,
                        CandidateRevision = fixtureRevision,
                        AllocationIdentity = fixtureAllocation.Identity,
                        AllocationDigest = fixtureAllocation.Digest,
                        AllocationArmSteps = fixtureAllocation.ArmSteps,
                        CanonicalState = fixtureCanonicalState,
                    };
                    funding = funding with { SeedAuditOnlyDigest = runtime.PersistPolicyBoundarySeed(
                        funding.QuotaDecisionID, funding.Policy, funding.CandidateFingerprint, funding.QuotaStep,
                        funding.CandidateRevision, seed, fixtureSourceDecision.DecisionID.Value,
                        fixtureSourceDecisionEventID.Value, fixtureSupportDigest,
                        fixtureSourceDecision.Readout.ReadoutCandidateFingerprint, fixtureFundingFingerprint, funding.Decision,
                        fixtureCanonicalState)
                        ?? throw new InvalidDataException("fixture failed to persist seed custody") };
                    runtime._policyTrialQuotaDecisions.Add(funding);
                    runtime._policyTrialQuotaByID.Add(funding.QuotaDecisionID, funding);
                    runtime._policyTrialHeldSteps = checked(runtime._policyTrialHeldSteps + funding.HeldArmSteps);
                    // Recovery reconciliation now joins terminal custody to the
                    // durable root Paid row, so the fixture must materialize
                    // the same authority surface as production funding.
                    runtime.AppendPolicyTrialQuota(in funding);
                    runtime.FlushPolicyJournalBuffer();
                    runtime.InvalidatePolicyTrialReconcileMemo();
                    return funding;
                }
                // Exercise the production failure seam with a valid paid quartet whose first
                // completion reader fails.  The catch must flush the attempted-child census before
                // settling the lease, leaving an auditable row rather than a refund-only ghost.
                CortexPolicyTrialQuotaDecision failureFunding = AddRecoveryFunding(0xB747F1FAD2C27CD1UL);
                int failureLadderInterventionsBefore = Volatile.Read(ref ladderInterventionCount);
                bool failureFundingSettled = false;
                CortexForkArm<PolicyBoundaryTrialOutcome> WrapFailureReader(CortexForkArm<PolicyBoundaryTrialOutcome> arm)
                    => new(arm.RunDirectory, arm.CreateCortex,
                        static _ => throw new InvalidOperationException("fixture child completion failed"),
                        arm.InterveneAfterLoad, arm.CompletionMode, arm.IsCompletionSatisfied, arm.AnytimeIdentity,
                        arm.RailRole, arm.AfterRuntimeBind, arm.ParentRunID, arm.AfterCompletedStep,
                        arm.AfterCompletedStepEveryStep, arm.AfterRunLanded, arm.BeforeCompletedStep,
                        arm.MaterializationContract, arm.PersistCompletionBeforeLanding);
                CortexForkArm<PolicyBoundaryTrialOutcome>[][] failureArms = new CortexForkArm<PolicyBoundaryTrialOutcome>[horizons.Length][];
                for (int i = 0; i < horizons.Length; i++)
                {
                    (Run failureBaseline, CortexForkMaterializationContract failureBaselineContract) =
                        runtime.CurrentRun.CreateMaterializedChildRun(CortexForkRailRoles.Baseline,
                            failureFunding.QuotaDecisionID.ToString(), seed.ColdSeedDigest);
                    (Run failureCandidate, CortexForkMaterializationContract failureCandidateContract) =
                        runtime.CurrentRun.CreateMaterializedChildRun(CortexForkRailRoles.Candidate,
                            failureFunding.QuotaDecisionID.ToString(), seed.ColdSeedDigest);
                    (Run failureForced, CortexForkMaterializationContract failureForcedContract) =
                        runtime.CurrentRun.CreateMaterializedChildRun(CortexForkRailRoles.ForcedNull,
                            failureFunding.QuotaDecisionID.ToString(), seed.ColdSeedDigest);
                    (Run failureReflex, CortexForkMaterializationContract failureReflexContract) =
                        runtime.CurrentRun.CreateMaterializedChildRun(CortexForkRailRoles.ReflexFrozen,
                            failureFunding.QuotaDecisionID.ToString(), seed.ColdSeedDigest);
                    CortexForkArm<PolicyBoundaryTrialOutcome> failureBaselineArm = CreateFixtureArm(
                        failureBaseline, horizons[i], PolicyBoundaryArms.Baseline, CortexForkRailRoles.Baseline,
                        failureBaselineContract);
                    CortexForkArm<PolicyBoundaryTrialOutcome> failureCandidateArm = CreateFixtureArm(
                        failureCandidate, horizons[i], PolicyBoundaryArms.Candidate, CortexForkRailRoles.Candidate,
                        failureCandidateContract);
                    CortexForkArm<PolicyBoundaryTrialOutcome> failureForcedArm = CreateFixtureArm(
                        failureForced, horizons[i], PolicyBoundaryArms.ForcedDivergentNull, CortexForkRailRoles.ForcedNull,
                        failureForcedContract);
                    CortexForkArm<PolicyBoundaryTrialOutcome> failureReflexArm = CreateFixtureArm(
                        failureReflex, horizons[i], PolicyBoundaryArms.ReflexFrozenControl, CortexForkRailRoles.ReflexFrozen,
                        failureReflexContract, frozen: true);
                    failureArms[i] =
                    [
                        i == 0 ? WrapFailureReader(failureBaselineArm) : failureBaselineArm,
                        WrapFailureReader(failureCandidateArm),
                        WrapFailureReader(failureForcedArm),
                        WrapFailureReader(failureReflexArm),
                    ];
                }
                try
                {
                    _ = runtime.TryRunPaidPolicyBoundary(in failureFunding, seed,
                        new PolicyBoundaryIdentity(HomeostatPolicyBoundaryDomain.Instance.PolicyID, "fixture-candidate", "fixture-grammar",
                            "fixture-production", fixtureCriticalityMetricID, "criticality"),
                        PolicyBoundaryRational.Zero, new PolicyBoundaryRational(1, 1),
                        failureArms.Select(static row => row[0]).ToArray(),
                        failureArms.Select(static row => row[1]).ToArray(),
                        failureArms.Select(static row => row[2]).ToArray(),
                        failureArms.Select(static row => row[3]).ToArray(), horizons,
                        fixtureFundingFingerprint, fixtureSourceCandidateFingerprint, fixtureRevision.Value, null,
                        out _, out CortexPolicyTrialCompletion failureSettlement);
                }
                catch (AggregateException exception) when (exception.InnerExceptions.Count != 0
                    && exception.InnerExceptions.All(static childFailure => childFailure is InvalidOperationException failure
                        && failure.Message == "fixture child completion failed"))
                {
                    string censusPath = runtime.CurrentRun.PathOf(PolicyBoundaryAdmissionCensusFile);
                    string[] censusRows = File.Exists(censusPath) ? File.ReadAllLines(censusPath) : [];
                    failureCensusFlushedWithoutSettlement = censusRows.Length > 1
                        && censusRows[^1].StartsWith(failureFunding.QuotaDecisionID.ToString() + "\t", StringComparison.Ordinal)
                        && !runtime._policyTrialCompletionByID.ContainsKey(failureFunding.QuotaDecisionID);
                    string transitionPath = runtime.CurrentRun.PathOf(PolicyBoundaryGenerationTransitionFile);
                    string[] transitionRows = File.Exists(transitionPath) ? File.ReadAllLines(transitionPath) : [];
                    failureGenerationTransitionDurable = transitionRows.Length > 1
                        && transitionRows[^1].StartsWith(failureFunding.QuotaDecisionID.ToString() + "\tIncomplete\t", StringComparison.Ordinal);
                    failureFundingSettled = runtime._policyTrialCompletionByID.ContainsKey(failureFunding.QuotaDecisionID);

                    CortexForkArm<PolicyBoundaryTrialOutcome>[][] retryArms = new CortexForkArm<PolicyBoundaryTrialOutcome>[horizons.Length][];
                    for (int i = 0; i < horizons.Length; i++)
                    {
                        (Run retryBaseline, CortexForkMaterializationContract retryBaselineContract) =
                            runtime.CurrentRun.CreateMaterializedChildRun(CortexForkRailRoles.Baseline,
                                failureFunding.QuotaDecisionID.ToString(), seed.ColdSeedDigest);
                        (Run retryCandidate, CortexForkMaterializationContract retryCandidateContract) =
                            runtime.CurrentRun.CreateMaterializedChildRun(CortexForkRailRoles.Candidate,
                                failureFunding.QuotaDecisionID.ToString(), seed.ColdSeedDigest);
                        (Run retryForced, CortexForkMaterializationContract retryForcedContract) =
                            runtime.CurrentRun.CreateMaterializedChildRun(CortexForkRailRoles.ForcedNull,
                                failureFunding.QuotaDecisionID.ToString(), seed.ColdSeedDigest);
                        (Run retryReflex, CortexForkMaterializationContract retryReflexContract) =
                            runtime.CurrentRun.CreateMaterializedChildRun(CortexForkRailRoles.ReflexFrozen,
                                failureFunding.QuotaDecisionID.ToString(), seed.ColdSeedDigest);
                        retryArms[i] =
                        [
                            WrapFailureReader(CreateFixtureArm(retryBaseline, horizons[i], PolicyBoundaryArms.Baseline,
                                CortexForkRailRoles.Baseline, retryBaselineContract)),
                            WrapFailureReader(CreateFixtureArm(retryCandidate, horizons[i], PolicyBoundaryArms.Candidate,
                                CortexForkRailRoles.Candidate, retryCandidateContract)),
                            WrapFailureReader(CreateFixtureArm(retryForced, horizons[i], PolicyBoundaryArms.ForcedDivergentNull,
                                CortexForkRailRoles.ForcedNull, retryForcedContract)),
                            WrapFailureReader(CreateFixtureArm(retryReflex, horizons[i], PolicyBoundaryArms.ReflexFrozenControl,
                                CortexForkRailRoles.ReflexFrozen, retryReflexContract, frozen: true)),
                        ];
                    }
                    try
                    {
                        _ = runtime.TryRunPaidPolicyBoundary(in failureFunding, seed,
                            new PolicyBoundaryIdentity(HomeostatPolicyBoundaryDomain.Instance.PolicyID, "fixture-candidate", "fixture-grammar",
                                "fixture-production", fixtureCriticalityMetricID, "criticality"),
                            PolicyBoundaryRational.Zero, new PolicyBoundaryRational(1, 1),
                            retryArms.Select(static row => row[0]).ToArray(),
                            retryArms.Select(static row => row[1]).ToArray(),
                            retryArms.Select(static row => row[2]).ToArray(),
                            retryArms.Select(static row => row[3]).ToArray(), horizons,
                            fixtureFundingFingerprint, fixtureSourceCandidateFingerprint, fixtureRevision.Value, null,
                            out _, out _);
                    }
                    catch (AggregateException retryException) when (retryException.InnerExceptions.Count != 0
                        && retryException.InnerExceptions.All(static childFailure => childFailure is InvalidOperationException failure
                            && failure.Message == "fixture child completion failed")) { }
                    failureRetryUnsettled = !runtime._policyTrialCompletionByID.ContainsKey(failureFunding.QuotaDecisionID);
                }
                finally
                {
                    runtime._policyTrialCompletions.RemoveAll(row => row.QuotaDecisionID.Equals(failureFunding.QuotaDecisionID));
                    runtime._policyTrialCompletionByID.Remove(failureFunding.QuotaDecisionID);
                    runtime._policyTrialQuotaDecisions.RemoveAll(row => row.QuotaDecisionID.Equals(failureFunding.QuotaDecisionID));
                    runtime._policyTrialQuotaByID.Remove(failureFunding.QuotaDecisionID);
                    if (!failureFundingSettled)
                        runtime._policyTrialHeldSteps = checked(runtime._policyTrialHeldSteps - failureFunding.HeldArmSteps);
                    runtime.InvalidatePolicyTrialReconcileMemo();
                    Volatile.Write(ref ladderInterventionCount, failureLadderInterventionsBefore);
                }
                int attemptFundingIndex = runtime._policyTrialQuotaDecisions.FindIndex(
                    row => row.QuotaDecisionID.Equals(attemptFunding.QuotaDecisionID));
                if (attemptFundingIndex < 0
                    || !runtime._policyTrialQuotaByID.Remove(attemptFunding.QuotaDecisionID))
                    throw new InvalidDataException("fixture lost its registered trial before recovery isolation");
                runtime._policyTrialQuotaDecisions.RemoveAt(attemptFundingIndex);
                runtime._policyTrialHeldSteps = checked(
                    runtime._policyTrialHeldSteps - attemptFunding.HeldArmSteps);
                runtime.InvalidatePolicyTrialReconcileMemo();
                CortexPolicyTrialQuotaDecision orphanFunding = AddRecoveryFunding(0xB747F1FAD2C27CE3UL);
                (Run staleChild, CortexForkMaterializationContract staleContract) =
                    runtime.CurrentRun.CreateMaterializedChildRun(CortexForkRailRoles.Calibration,
                        orphanFunding.QuotaDecisionID.ToString(), seed.ColdSeedDigest);
                bool pendingOrphan = runtime.TryReconcilePaidHomeostatBoundaryTrials(in recoveryReadout, horizons,
                    out CortexPolicyTrialQuotaDecision pendingOrphanFunding, out bool orphanTerminal);
                if (pendingOrphan && !orphanTerminal && pendingOrphanFunding.QuotaDecisionID.Equals(orphanFunding.QuotaDecisionID))
                {
                    CortexPolicyTrialQuotaDecision reused = runtime.ReusePolicyTrialQuota(in pendingOrphanFunding);
                    recoveryOrphanReused = reused.Decision == CortexPolicyQuotaDecisions.Reused
                        && reused.UsedSteps == 0
                        && runtime.CurrentRun.NextChildRunID(CortexForkRailRoles.Calibration) != Path.GetFileName(staleChild.Dir);
                }
                CortexPolicyTrialQuotaDecision mutatedOrphan = orphanFunding with { AllocationDigest = "mutated-allocation-digest" };
                runtime._policyTrialQuotaByID[orphanFunding.QuotaDecisionID] = mutatedOrphan;
                for (int i = 0; i < runtime._policyTrialQuotaDecisions.Count; i++)
                    if (runtime._policyTrialQuotaDecisions[i].QuotaDecisionID.Equals(orphanFunding.QuotaDecisionID))
                        runtime._policyTrialQuotaDecisions[i] = mutatedOrphan;
                runtime.InvalidatePolicyTrialReconcileMemo();
                try
                {
                    _ = runtime.TryReconcilePaidHomeostatBoundaryTrials(in recoveryReadout, horizons, out _, out _);
                }
                catch (InvalidDataException) { recoveryIdentityRejected = true; }
                runtime._policyTrialQuotaByID[orphanFunding.QuotaDecisionID] = orphanFunding;
                for (int i = 0; i < runtime._policyTrialQuotaDecisions.Count; i++)
                    if (runtime._policyTrialQuotaDecisions[i].QuotaDecisionID.Equals(orphanFunding.QuotaDecisionID))
                        runtime._policyTrialQuotaDecisions[i] = orphanFunding;
                runtime.InvalidatePolicyTrialReconcileMemo();
                string orphanCustodyPath = Path.Combine(runtime.CurrentRun.Dir, PolicyBoundarySeedCustodyDirectory,
                    orphanFunding.QuotaDecisionID.ToString(), PolicyBoundarySeedCustodyFile);
                byte[] orphanCustodyBytes = File.ReadAllBytes(orphanCustodyPath);
                File.Delete(orphanCustodyPath);
                try { _ = runtime.TryReconcilePaidHomeostatBoundaryTrials(in recoveryReadout, horizons, out _, out _); }
                catch (InvalidDataException) { recoveryMissingSourceEventRejected = true; }
                File.WriteAllBytes(orphanCustodyPath, orphanCustodyBytes);
                PolicyBoundarySeedCustody forgedSourceCustody = RonSerializer.Deserialize<PolicyBoundarySeedCustody>(orphanCustodyBytes);
                forgedSourceCustody.sourceDecisionEventID = checked(forgedSourceCustody.sourceDecisionEventID + 1);
                forgedSourceCustody.custodyDigest = forgedSourceCustody.ComputeDigest();
                File.WriteAllBytes(orphanCustodyPath, forgedSourceCustody.Encode());
                try { _ = runtime.TryReconcilePaidHomeostatBoundaryTrials(in recoveryReadout, horizons, out _, out _); }
                catch (InvalidDataException) { recoveryForgedSourceEventRejected = true; }
                File.WriteAllBytes(orphanCustodyPath, orphanCustodyBytes);
                runtime._policyTrialQuotaDecisions.RemoveAll(row => row.QuotaDecisionID.Equals(orphanFunding.QuotaDecisionID));
                runtime._policyTrialQuotaByID.Remove(orphanFunding.QuotaDecisionID);
                runtime._policyTrialHeldSteps = checked(runtime._policyTrialHeldSteps - orphanFunding.HeldArmSteps);
                runtime.InvalidatePolicyTrialReconcileMemo();

                // Build a complete, internally coherent four-arm generation from a foreign seed while
                // funding remains bound to the original immutable custody. Reconciliation must reject
                // the quartet as a whole; accepting only mixed-arm rejection would leave this seam open.
                CortexPolicyTrialQuotaDecision foreignFunding = AddRecoveryFunding(0xB747F1FAD2C27CF3UL);
                string foreignCustodyPath = Path.Combine(runtime.CurrentRun.Dir, PolicyBoundarySeedCustodyDirectory,
                    foreignFunding.QuotaDecisionID.ToString(), PolicyBoundarySeedCustodyFile);
                byte[] originalForeignCustodyBytes = File.ReadAllBytes(foreignCustodyPath);
                PolicyBoundarySeedCustody originalForeignCustody = RonSerializer.Deserialize<PolicyBoundarySeedCustody>(originalForeignCustodyBytes);
                byte[] foreignExcursions = seed.CopyExcursions();
                int excursionHeaderBreak = Array.IndexOf(foreignExcursions, (byte)'\n');
                if (excursionHeaderBreak >= 0 && (excursionHeaderBreak == 0 || foreignExcursions[excursionHeaderBreak - 1] != (byte)'\r'))
                {
                    byte[] normalizedExcursions = new byte[checked(foreignExcursions.Length + 1)];
                    Buffer.BlockCopy(foreignExcursions, 0, normalizedExcursions, 0, excursionHeaderBreak);
                    normalizedExcursions[excursionHeaderBreak] = (byte)'\r';
                    Buffer.BlockCopy(foreignExcursions, excursionHeaderBreak, normalizedExcursions,
                        excursionHeaderBreak + 1, foreignExcursions.Length - excursionHeaderBreak);
                    foreignExcursions = normalizedExcursions;
                }
                CortexForkSeed foreignSeed = CortexForkSeed.Materialize(seed.NextStep, seed.CopyCheckpoint(),
                    seed.CopyTapeSpanlog(), seed.CopyCurve(), seed.PersistedConfigDigest, runtime.CopyPolicyJournals(),
                    foreignExcursions, seed.ExcursionCursor);
                PolicyBoundarySeedCustody foreignCustody = originalForeignCustody;
                foreignCustody.nextStep = foreignSeed.NextStep;
                foreignCustody.coldSeedDigest = foreignSeed.ColdSeedDigest;
                foreignCustody.checkpointSHA256 = foreignSeed.Digests.CheckpointSHA256;
                foreignCustody.tapeSpanlogSHA256 = foreignSeed.Digests.TapeSpanlogSHA256;
                foreignCustody.curveSHA256 = foreignSeed.Digests.CurveSHA256;
                foreignCustody.excursionsSHA256 = foreignSeed.Digests.ExcursionsSHA256;
                foreignCustody.custodyDigest = foreignCustody.ComputeDigest();
                File.WriteAllBytes(foreignCustodyPath, foreignCustody.Encode());
                int ladderInterventionsBeforeForeign = Volatile.Read(ref ladderInterventionCount);
                try
                {
                    CortexForkRailRoles[] foreignRoles =
                        [CortexForkRailRoles.Baseline, CortexForkRailRoles.Candidate,
                         CortexForkRailRoles.ForcedNull, CortexForkRailRoles.ReflexFrozen];
                    PolicyBoundaryArms[] foreignArms =
                        [PolicyBoundaryArms.Baseline, PolicyBoundaryArms.Candidate,
                         PolicyBoundaryArms.ForcedDivergentNull, PolicyBoundaryArms.ReflexFrozenControl];
                    CortexForkArm<PolicyBoundaryTrialOutcome>[] foreignForkArms = new CortexForkArm<PolicyBoundaryTrialOutcome>[4];
                    for (int i = 0; i < foreignForkArms.Length; i++)
                    {
                        (Run foreignChild, CortexForkMaterializationContract foreignContract) =
                            runtime.CurrentRun.CreateMaterializedChildRun(foreignRoles[i],
                                foreignFunding.QuotaDecisionID.ToString(), foreignSeed.ColdSeedDigest);
                        foreignForkArms[i] = CreateFixtureArm(foreignChild, horizons[^1], foreignArms[i], foreignRoles[i],
                            foreignContract, frozen: foreignArms[i] == PolicyBoundaryArms.ReflexFrozenControl);
                        try { _ = CortexForkRunner.RunFork(runtime, foreignSeed, foreignForkArms[i], foreignSeed.NextStep + 1); }
                        catch (Exception error) when (error is InvalidDataException or AggregateException)
                        {
                            recoveryForeignGenerationRejected = true;
                            break;
                        }
                    }
                    try
                    {
                        bool foreignRecovery = runtime.TryReconcilePaidHomeostatBoundaryTrials(in recoveryReadout, horizons,
                            out CortexPolicyTrialQuotaDecision foreignPending, out bool foreignTerminal);
                        recoveryForeignGenerationRejected = foreignRecovery && !foreignTerminal
                            && foreignPending.QuotaDecisionID.Equals(foreignFunding.QuotaDecisionID);
                    }
                    catch (InvalidDataException)
                    {
                        // The original funding custody is restored before reconciliation; a coherent
                        // foreign quartet must fail closed at the immutable source-custody gate.
                        recoveryForeignGenerationRejected = true;
                    }
                }
                finally
                {
                    File.WriteAllBytes(foreignCustodyPath, originalForeignCustodyBytes);
                    Volatile.Write(ref ladderInterventionCount, ladderInterventionsBeforeForeign);
                }
                runtime._policyTrialQuotaDecisions.RemoveAll(row => row.QuotaDecisionID.Equals(foreignFunding.QuotaDecisionID));
                runtime._policyTrialQuotaByID.Remove(foreignFunding.QuotaDecisionID);
                runtime._policyTrialHeldSteps = checked(runtime._policyTrialHeldSteps - foreignFunding.HeldArmSteps);
                runtime.InvalidatePolicyTrialReconcileMemo();
                runtime._policyTrialQuotaDecisions.Insert(attemptFundingIndex, attemptFunding);
                runtime._policyTrialQuotaByID.Add(attemptFunding.QuotaDecisionID, attemptFunding);
                runtime._policyTrialHeldSteps = checked(
                    runtime._policyTrialHeldSteps + attemptFunding.HeldArmSteps);
                runtime.InvalidatePolicyTrialReconcileMemo();
                PolicyBoundaryIdentity identity = new(HomeostatPolicyBoundaryDomain.Instance.PolicyID, "fixture-candidate", "fixture-grammar",
                    "fixture-production", fixtureCriticalityMetricID, "criticality");
                try
                {
                    bool directSuccess = runtime.TryRunPaidPolicyBoundary(in attemptFunding, seed, identity,
                        new PolicyBoundaryRational(-1, 1), new PolicyBoundaryRational(1, 1),
                        arms.Select(static row => row[0]).ToArray(), arms.Select(static row => row[1]).ToArray(),
                        arms.Select(static row => row[2]).ToArray(), arms.Select(static row => row[3]).ToArray(), horizons,
                        fixtureFundingFingerprint, fixtureSourceCandidateFingerprint, fixtureRevision.Value, null,
                        out PolicyBoundaryForkReceipt continuityReceipt, out CortexPolicyTrialCompletion directSettlement);
                    if (!directSuccess
                        || directSettlement.VerifierOutcome != CortexPolicyVerifierOutcomes.Passed
                        || directSettlement.ActualExecutedArmSteps + directSettlement.ReclaimedOrUnused != attemptFunding.PlannedArmSteps)
                        throw new InvalidDataException("fixture direct paid success did not settle its authenticated lease");
                    string directSettlementCustodyPath = Path.Combine(runtime.CurrentRun.Dir, PolicyBoundarySeedCustodyDirectory,
                        attemptFunding.QuotaDecisionID.ToString(), "settlement-custody.ron");
                    directSettlementDurable = File.Exists(directSettlementCustodyPath)
                        && File.Exists(runtime.CurrentRun.PathOf(PolicyTrialCompletionJournalFile))
                        && File.ReadAllLines(runtime.CurrentRun.PathOf(PolicyTrialCompletionJournalFile)).Length > 1;
                    if (!directSettlementDurable)
                        throw new InvalidDataException("fixture direct paid success did not persist settlement custody and journal");

                    string terminalForcedRailPath = Path.Combine(
                        arms[^1][(int)PolicyBoundaryArms.ForcedDivergentNull].RunDirectory,
                        "policy-boundary.rail.ron");
                    byte[] terminalForcedRailBytes = File.ReadAllBytes(terminalForcedRailPath);
                    PolicyBoundaryRailMetadataDocument terminalForcedRail =
                        RonSerializer.Deserialize<PolicyBoundaryRailMetadataDocument>(terminalForcedRailBytes);
                    PolicyBoundaryArmReceipt terminalForcedReceipt = continuityReceipt.Arms.Single(arm =>
                        arm.Arm == PolicyBoundaryArms.ForcedDivergentNull && arm.Horizon == horizons[^1]);
                    terminalOutcomeReconstructed = terminalForcedRail.ordinaryOutcomeRequired
                        && terminalForcedRail.executedOutcomeEventID > 0
                        && terminalForcedRail.executedOutcomePayloadSHA256.Length == 64
                        && terminalForcedReceipt.ExecutedOutcomeEventID.Value == terminalForcedRail.executedOutcomeEventID
                        && terminalForcedReceipt.ExecutedOutcomePayloadSHA256 == terminalForcedRail.executedOutcomePayloadSHA256;
                    if (!terminalOutcomeReconstructed)
                        throw new InvalidDataException("fixture terminal forced-null outcome custody was lost during rail reconstruction");

                    bool RejectTerminalForcedRailMutation(Action<PolicyBoundaryRailMetadataDocument> mutate)
                    {
                        try
                        {
                            PolicyBoundaryRailMetadataDocument changed =
                                RonSerializer.Deserialize<PolicyBoundaryRailMetadataDocument>(terminalForcedRailBytes);
                            mutate(changed);
                            File.WriteAllBytes(terminalForcedRailPath, RonSerializer.SerializeToUtf8(in changed));
                            bool complete = runtime.TryReadTerminalHomeostatBoundaryGeneration(
                                in attemptFunding, attemptFunding.ReadoutFingerprint, horizons,
                                out _, out _, out _, out PolicyBoundaryGenerationStates mutationState,
                                out _, out _, requireReceipt: true);
                            return !complete && mutationState == PolicyBoundaryGenerationStates.Invalid;
                        }
                        finally
                        {
                            File.WriteAllBytes(terminalForcedRailPath, terminalForcedRailBytes);
                        }
                    }

                    terminalOutcomeOmissionRejected = RejectTerminalForcedRailMutation(static rail =>
                    {
                        rail.executedOutcomeEventID = 0;
                        rail.executedOutcomePayloadSHA256 = "";
                    });
                    terminalOutcomeDigestRejected = RejectTerminalForcedRailMutation(static rail =>
                        rail.executedOutcomePayloadSHA256 = new string('0', 64));
                    byte[] nonterminalForcedRailBytes = File.ReadAllBytes(Path.Combine(
                        arms[^2][(int)PolicyBoundaryArms.ForcedDivergentNull].RunDirectory,
                        "policy-boundary.rail.ron"));
                    try
                    {
                        File.WriteAllBytes(terminalForcedRailPath, nonterminalForcedRailBytes);
                        bool complete = runtime.TryReadTerminalHomeostatBoundaryGeneration(
                            in attemptFunding, attemptFunding.ReadoutFingerprint, horizons,
                            out _, out _, out _, out PolicyBoundaryGenerationStates substitutionState,
                            out _, out _, requireReceipt: true);
                        terminalOutcomeRungSubstitutionRejected = !complete
                            && substitutionState == PolicyBoundaryGenerationStates.Invalid;
                    }
                    finally
                    {
                        File.WriteAllBytes(terminalForcedRailPath, terminalForcedRailBytes);
                    }

                    string directTamperedRailPath = Path.Combine(arms[^1][1].RunDirectory, "policy-boundary.rail.ron");
                    byte[] directRailBytes = File.ReadAllBytes(directTamperedRailPath);
                    try
                    {
                        byte[] forgedRailBytes = directRailBytes.ToArray();
                        forgedRailBytes[^1] ^= 0x01;
                        File.WriteAllBytes(directTamperedRailPath, forgedRailBytes);
                        bool completeAfterTamper = runtime.TryReadTerminalHomeostatBoundaryGeneration(
                            in attemptFunding, attemptFunding.ReadoutFingerprint, horizons,
                            out _, out _, out _, out PolicyBoundaryGenerationStates tamperedState,
                            out _, out _, requireReceipt: true);
                        failureTamperedCompleteRailRejected = !completeAfterTamper
                            && tamperedState == PolicyBoundaryGenerationStates.Invalid;
                    }
                    finally
                    {
                        File.WriteAllBytes(directTamperedRailPath, directRailBytes);
                    }

                    // Recovery attach fixture: preserve the exact readout quality tuple in a
                    // schema-7 custody sidecar, then attach the already-authenticated candidate
                    // agreement plus forced TrialOverride divergence.  The second attach must be
                    // a digest-bound no-op; a quality mutation must fail closed.
                    string attachCustodyPath = Path.Combine(runtime.CurrentRun.Dir, PolicyBoundarySeedCustodyDirectory,
                        attemptFunding.QuotaDecisionID.ToString(), PolicyBoundarySeedCustodyFile);
                    byte[] attachCustodyBytes = File.ReadAllBytes(attachCustodyPath);
                    PolicyBoundarySeedCustody attachCustody = RonSerializer.Deserialize<PolicyBoundarySeedCustody>(attachCustodyBytes);
                    PolicyBoundaryObligation attachObligation = runtime._policyBoundaryObligations[HomeostatPolicyBoundaryDomain.Instance.PolicyID];
                    PolicyBoundaryRational attachBoundary = attachObligation.Candidates.Count > 0
                        ? attachObligation.Candidates[0].Boundary : continuityReceipt.CandidateBoundary;
                    attachObligation.Propose(new PolicyBoundaryCandidate(attachBoundary,
                        PolicyBoundaryComparisons.LessThanOrEqual, "fixture-recovered-attach"));
                    attachCustody.schemaVersion = 7;
                    attachCustody.boundary = attachBoundary.ToString();
                    attachCustody.comparison = PolicyBoundaryComparisons.LessThanOrEqual;
                    attachCustody.provenance = "fixture-recovered-attach";
                    attachCustody.sourceStep = attemptFunding.QuotaStep;
                    attachCustody.obligation = runtime._policyBoundaryObligations[HomeostatPolicyBoundaryDomain.Instance.PolicyID].ID.Value;
                    attachCustody.readoutCachedContexts = 1;
                    attachCustody.readoutComparisons = 1;
                    attachCustody.readoutAgreements = 1;
                    attachCustody.readoutMisses = 0;
                    attachCustody.custodyDigest = attachCustody.ComputeDigest();
                    File.WriteAllBytes(attachCustodyPath, attachCustody.Encode());
                    PolicyCanonicalStateID attachState = fixtureCanonicalState;
                    GrammarPolicyContextKey attachContext = new(attachState,
                        HomeostatPolicyBoundaryDomain.Instance.Schema.ActionCount, config.Learning.Policies.ReadoutDeliberationQuota);
                    LoopClosureCompositionEpisode attachEpisode = LoopClosureCompositionEpisode.Create(
                        new LoopClosureCompositionEpisodeID("fixture-recovered-attach"),
                        focusedSourceEventID, [new TapeEventID(focusedSourceEventID.Value + 1)], new GrammarRevisionID(1));
                    GrammarFoldProvenanceReceipt attachFold = GrammarFoldProvenanceReceipt.Create(
                        new GrammarRevisionID(1), new GrammarRevisionID(2),
                        [focusedSourceEventID, new TapeEventID(focusedSourceEventID.Value + 1),
                            new TapeEventID(focusedSourceEventID.Value + 2)], [attachEpisode]);
                    LoopClosureTeacherPacketProvenance attachTeacher = LoopClosureTeacherPacketProvenance.Create(
                        attachEpisode.EpisodeID, attachFold.PreviousRevision,
                        [focusedSourceEventID, new TapeEventID(focusedSourceEventID.Value + 1)], attachEpisode.EvidenceDigest);
                    ReadoutTrainingCorroboration attachTraining = ReadoutTrainingCorroboration.Create(
                        HomeostatPolicyBoundaryDomain.Instance.PolicyID, new TapeEventID(focusedSourceEventID.Value + 2), focusedSourceEventID,
                        [new TapeEventID(focusedSourceEventID.Value + 1)], attachEpisode.EvidenceDigest, attachEpisode.EpisodeID,
                        attachEpisode.EpisodeDigest, attachFold.PreviousRevision, attachFold.Revision,
                        attachFold.ConsumedEventIDs, attachFold.ConsumedEventDigest, attachFold.ReceiptDigest,
                        in attachState, in attachContext, fixtureSourceCandidateFingerprint,
                        fixtureSupportDigest, fixtureRevision, focusedSourceDecision.DecisionID,
                        focusedSourceEventID);
                    LoopClosureR4Provenance attachProvenance = LoopClosureR4Provenance.Create(
                        in attachEpisode, in attachFold, in attachTeacher, in attachTraining);
                    // The materialization fixture's direct receipt reuses the parent decision
                    // identity for each child arm. Recovery custody requires distinct child
                    // identities, so keep the authenticated arm content and mint fixture-only
                    // child IDs before exercising the attach transaction.
                    PolicyBoundaryArmReceipt[] recoveredAttachArms = continuityReceipt.Arms
                        .Select(static (row, index) =>
                        {
                            ulong childID = checked(0x100000UL + (ulong)index);
                            return row with
                            {
                                LastRequestDecisionID = row.LastRequestDecisionID.Value == 0
                                    ? row.LastRequestDecisionID : new CortexPolicyDecisionID(childID),
                                ExecutedDecisionID = row.ExecutedDecisionID.Value == 0
                                    ? row.ExecutedDecisionID : new CortexPolicyDecisionID(childID + 0x100000UL),
                            };
                        }).ToArray();
                    PolicyBoundaryForkReceipt recoveredAttachReceipt = continuityReceipt with
                    {
                        CandidateBoundary = attachBoundary,
                        Arms = recoveredAttachArms,
                    };
                    CortexPolicyReadoutReceipt attachReadout = new(
                        attemptFunding.CandidateRevision, attemptFunding.ReadoutFingerprint,
                        attachCustody.readoutCachedContexts, attachCustody.readoutComparisons,
                        attachCustody.readoutAgreements, attachCustody.readoutMisses,
                        attachCustody.sourceSupportDigest, attachCustody.sourceCandidateFingerprint,
                        CanonicalState: attachState);
                    if (!runtime.TryGrantVerifiedPolicyScope(HomeostatPolicyBoundaryDomain.Instance.PolicyID, in attachState,
                            attachReadout.Fingerprint, attachReadout.ReadoutCandidateFingerprint,
                            attachReadout.ReadoutCandidateOccurrenceDigest, attachReadout.Revision))
                        throw new InvalidDataException("fixture recovered attach could not prepare its verified canonical scope");
                    RecoveredHomeostatBoundaryAttachmentOutcomes firstAttach = runtime.AttachRecoveredHomeostatBoundaryTrial(
                        in attemptFunding, in directSettlement, in recoveredAttachReceipt, attachProvenance);
                    RecoveredHomeostatBoundaryAttachmentOutcomes secondAttach = runtime.AttachRecoveredHomeostatBoundaryTrial(
                        in attemptFunding, in directSettlement, in recoveredAttachReceipt, attachProvenance);
                    if (firstAttach != RecoveredHomeostatBoundaryAttachmentOutcomes.Attached
                        || secondAttach != RecoveredHomeostatBoundaryAttachmentOutcomes.Attached)
                        throw new InvalidDataException($"fixture recovered attach was not exact/idempotent: first={firstAttach} second={secondAttach}");
                    PolicyBoundarySeedCustody attachTamper = attachCustody;
                    attachTamper.readoutComparisons = 2;
                    attachTamper.custodyDigest = attachTamper.ComputeDigest();
                    File.WriteAllBytes(attachCustodyPath, attachTamper.Encode());
                    bool attachTamperRejected;
                    try
                    {
                        runtime._policyBoundaryObligations[HomeostatPolicyBoundaryDomain.Instance.PolicyID].DiscardStagedExecutionCorroboration();
                        attachTamperRejected = runtime.AttachRecoveredHomeostatBoundaryTrial(
                            in attemptFunding, in directSettlement, in recoveredAttachReceipt, attachProvenance)
                            != RecoveredHomeostatBoundaryAttachmentOutcomes.Attached;
                    }
                    catch (InvalidDataException) { attachTamperRejected = true; }
                    finally { File.WriteAllBytes(attachCustodyPath, attachCustodyBytes); }
                    if (!attachTamperRejected)
                        throw new InvalidDataException("fixture recovered attach accepted a readout-quality custody mutation");
                    recoveredAttachFixturePassed = true;
                    CortexPolicyTrialQuotaDecision terminalFunding = attemptFunding;
                    (Run mixedGenerationChild, CortexForkMaterializationContract mixedGenerationContract) =
                        runtime.CurrentRun.CreateMaterializedChildRun(CortexForkRailRoles.Candidate,
                            terminalFunding.QuotaDecisionID.ToString(), seed.ColdSeedDigest);
                    bool firstRecovery = runtime.TryReconcilePaidHomeostatBoundaryTrials(in recoveryReadout, horizons,
                        out _, out bool firstTerminalSettled);
                    bool secondRecovery = runtime.TryReconcilePaidHomeostatBoundaryTrials(in recoveryReadout, horizons,
                        out _, out bool secondTerminalSettled);
                    CortexPolicyTrialCompletion terminalSettlement = default;
                    recoveryTerminalSettled = firstRecovery && firstTerminalSettled
                        && runtime._policyTrialCompletionByID.TryGetValue(terminalFunding.QuotaDecisionID, out terminalSettlement)
                        && terminalSettlement.ActualExecutedArmSteps + terminalSettlement.ReclaimedOrUnused == terminalFunding.PlannedArmSteps;
                    recoveryMixedGenerationRejected = recoveryTerminalSettled
                        && terminalSettlement.ActualExecutedArmSteps == 12
                        && File.Exists(Path.Combine(mixedGenerationChild.Dir, CortexForkMaterializationContract.MarkerFileName))
                        && !File.Exists(Path.Combine(mixedGenerationChild.Dir, "policy-boundary.rail.ron"));
                    recoverySecondResumeNoOp = secondRecovery && secondTerminalSettled
                        && runtime._policyTrialCompletions.Count(row => row.QuotaDecisionID.Equals(terminalFunding.QuotaDecisionID)) == 1;
                    string settlementCustodyPath = Path.Combine(runtime.CurrentRun.Dir, PolicyBoundarySeedCustodyDirectory,
                        terminalFunding.QuotaDecisionID.ToString(), "settlement-custody.ron");
                    byte[] settlementCustodyBytes = File.ReadAllBytes(settlementCustodyPath);
                    File.Delete(settlementCustodyPath);
                    runtime.InvalidatePolicyTrialReconcileMemo();
                    bool missingRecovery = runtime.TryReconcilePaidHomeostatBoundaryTrials(in recoveryReadout, horizons,
                        out _, out bool missingRecoveryTerminal);
                    recoverySettlementCustodyMissingRepaired = missingRecovery && missingRecoveryTerminal
                        && File.Exists(settlementCustodyPath);
                    File.WriteAllBytes(settlementCustodyPath, settlementCustodyBytes);
                    int settlementIndex = runtime._policyTrialCompletions.FindIndex(
                        row => row.QuotaDecisionID.Equals(terminalFunding.QuotaDecisionID));
                    if (settlementIndex < 0)
                    throw new InvalidDataException("fixture direct paid success lost its durable settlement row");
                    CortexPolicyTrialCompletion originalSettlement = runtime._policyTrialCompletions[settlementIndex];
                    CortexPolicyTrialCompletion forgedSettlement = originalSettlement with
                    {
                        ActualExecutedArmSteps = checked(originalSettlement.ActualExecutedArmSteps + 1),
                        ReclaimedOrUnused = checked(originalSettlement.ReclaimedOrUnused - 1),
                    };
                    runtime._policyTrialCompletions[settlementIndex] = forgedSettlement;
                    runtime._policyTrialCompletionByID[terminalFunding.QuotaDecisionID] = forgedSettlement;
                    File.Delete(settlementCustodyPath);
                    runtime.InvalidatePolicyTrialReconcileMemo();
                    try
                    {
                        _ = runtime.TryReconcilePaidHomeostatBoundaryTrials(in recoveryReadout, horizons, out _, out _);
                    }
                    catch (InvalidDataException) { recoverySettlementCustodyMissingRejected = true; }
                    runtime._policyTrialCompletions[settlementIndex] = originalSettlement;
                    runtime._policyTrialCompletionByID[terminalFunding.QuotaDecisionID] = originalSettlement;
                    File.WriteAllBytes(settlementCustodyPath, settlementCustodyBytes);
                    PolicyBoundarySettlementCustody forgedSettlementCustody =
                        RonSerializer.Deserialize<PolicyBoundarySettlementCustody>(settlementCustodyBytes);
                    forgedSettlementCustody.generationDigest = new string('0', 64);
                    File.WriteAllBytes(settlementCustodyPath, forgedSettlementCustody.Encode());
                    runtime.InvalidatePolicyTrialReconcileMemo();
                    try
                    {
                        _ = runtime.TryReconcilePaidHomeostatBoundaryTrials(in recoveryReadout, horizons, out _, out _);
                    }
                    catch (InvalidDataException) { recoverySettlementCustodyMutationRejected = true; }
                    File.WriteAllBytes(settlementCustodyPath, settlementCustodyBytes);
                    string terminalCustodyPath = Path.Combine(runtime.CurrentRun.Dir, PolicyBoundarySeedCustodyDirectory,
                        terminalFunding.QuotaDecisionID.ToString(), PolicyBoundarySeedCustodyFile);
                    byte[] terminalCustodyBytes = File.ReadAllBytes(terminalCustodyPath);
                    PolicyBoundarySeedCustody terminalCustody = RonSerializer.Deserialize<PolicyBoundarySeedCustody>(terminalCustodyBytes);
                    terminalCustody.coldSeedDigest = new string('0', 64);
                    File.WriteAllBytes(terminalCustodyPath, terminalCustody.Encode());
                    // The forged custody simulates a cold resume onto tampered disk state; drop the
                    // in-memory verdict so reconcile re-proves durably and fails closed on the forge.
                    runtime.InvalidatePolicyTrialReconcileMemo();
                    try
                    {
                        _ = runtime.TryReconcilePaidHomeostatBoundaryTrials(in recoveryReadout, horizons, out _, out _);
                    }
                    catch (InvalidDataException) { recoveryCustodyMutationRejected = true; }
                    File.WriteAllBytes(terminalCustodyPath, terminalCustodyBytes);
                    runtime._policyTrialQuotaDecisions.RemoveAll(row => row.QuotaDecisionID.Equals(terminalFunding.QuotaDecisionID));
                    runtime._policyTrialQuotaByID.Remove(terminalFunding.QuotaDecisionID);
                    runtime._policyTrialHeldSteps = checked(runtime._policyTrialHeldSteps - terminalFunding.HeldArmSteps);
                    runtime.InvalidatePolicyTrialReconcileMemo();
                    PolicyBoundaryArmReceipt[] brokenArms = continuityReceipt.Arms.ToArray();
                    brokenArms[0] = brokenArms[0] with { ContinuityExact = false };
                    try { (continuityReceipt with { Arms = brokenArms, ContinuityExact = false }).Validate(HomeostatPolicyBoundaryDomain.Instance); }
                    catch (InvalidDataException exception) when (exception.Message.Contains("continuity", StringComparison.Ordinal)
                        || exception.Message.Contains("summary flags", StringComparison.Ordinal)) { nonExactContinuityRejected = true; }
                }
                catch (InvalidDataException exception) when (exception.Message.Contains("policy boundary receipt lacks continuity", StringComparison.Ordinal))
                {
                    nonExactContinuityRejected = true;
                }
                ladderInterventionsOnce = Volatile.Read(ref ladderInterventionCount) == 4;
                try { CortexForkRunReceipt<PolicyBoundaryTrialOutcome> repeatReceipt = CortexForkRunner.RunFork(runtime, seed, arms[0][0], seed.NextStep + 1); }
                catch (IOException) { repeatRejected = true; }

                bool Reject(CortexForkMaterializationContract contract, CortexForkArm<PolicyBoundaryTrialOutcome> arm)
                {
                    try { CortexForkRunReceipt<PolicyBoundaryTrialOutcome> rejectedReceipt = CortexForkRunner.RunFork(runtime, seed, arm, seed.NextStep + 1); return false; }
                    catch (InvalidDataException) { return true; }
                }
                Run wrongFundingRun = runtime.CurrentRun.CreateChildRun(CortexForkRailRoles.Baseline);
                CortexForkMaterializationContract wrongFunding = new(parentID, "WRONG-FUNDING", Path.GetFileName(wrongFundingRun.Dir), seed.ColdSeedDigest);
                wrongFundingRun.WriteAtomic(CortexForkMaterializationContract.MarkerFileName, stream => stream.Write(Encoding.UTF8.GetBytes(wrongFunding.Encode())));
                CortexForkArm<PolicyBoundaryTrialOutcome> wrongFundingArm = new(wrongFundingRun.Dir, () => new Cortex(config),
                    static cortex => new PolicyBoundaryTrialOutcome(0, cortex.Step, true), railRole: CortexForkRailRoles.Baseline,
                    parentRunID: parentID, materializationContract: wrongFunding with { AttemptID = attemptID });
                wrongFundingRejected = Reject(wrongFunding with { AttemptID = attemptID }, wrongFundingArm);

                Run wrongParentRun = runtime.CurrentRun.CreateChildRun(CortexForkRailRoles.Baseline);
                CortexForkMaterializationContract wrongParent = new("WRONG-PARENT", attemptID, Path.GetFileName(wrongParentRun.Dir), seed.ColdSeedDigest);
                wrongParentRun.WriteAtomic(CortexForkMaterializationContract.MarkerFileName, stream => stream.Write(Encoding.UTF8.GetBytes(wrongParent.Encode())));
                CortexForkArm<PolicyBoundaryTrialOutcome> wrongParentArm = new(wrongParentRun.Dir, () => new Cortex(config), static cortex => new PolicyBoundaryTrialOutcome(0, cortex.Step, true),
                    railRole: CortexForkRailRoles.Baseline, parentRunID: parentID, materializationContract: wrongParent);
                wrongParentRejected = Reject(wrongParent, wrongParentArm);

                Run wrongColdRun = runtime.CurrentRun.CreateChildRun(CortexForkRailRoles.Baseline);
                CortexForkMaterializationContract wrongCold = new(parentID, attemptID, Path.GetFileName(wrongColdRun.Dir), new string('0', 64));
                wrongColdRun.WriteAtomic(CortexForkMaterializationContract.MarkerFileName, stream => stream.Write(Encoding.UTF8.GetBytes(wrongCold.Encode())));
                CortexForkArm<PolicyBoundaryTrialOutcome> wrongColdArm = new(wrongColdRun.Dir, () => new Cortex(config), static cortex => new PolicyBoundaryTrialOutcome(0, cortex.Step, true),
                    railRole: CortexForkRailRoles.Baseline, parentRunID: parentID, materializationContract: wrongCold);
                wrongColdRejected = Reject(wrongCold, wrongColdArm);

                bool RejectN(string corruption)
                {
                    CortexForkArm<PolicyBoundaryTrialOutcome>[] nArms = new CortexForkArm<PolicyBoundaryTrialOutcome>[3];
                    for (int index = 0; index < nArms.Length; index++)
                    {
                        Run child = runtime.CurrentRun.CreateChildRun(CortexForkRailRoles.Baseline);
                        string childID = Path.GetFileName(child.Dir);
                        CortexForkMaterializationContract expected = new(parentID, attemptID, childID, seed.ColdSeedDigest);
                        CortexForkMaterializationContract armContract = corruption switch
                        {
                            "parent" => expected with { ParentRunID = "WRONG-PARENT" },
                            "cold" => expected with { ColdSeedDigest = new string('0', 64) },
                            "child" => expected with { ChildRunID = "wrong-child" },
                            _ => expected,
                        };
                        CortexForkMaterializationContract markerContract = corruption == "funding"
                            ? expected with { AttemptID = "WRONG-FUNDING" }
                            : armContract;
                        child.WriteAtomic(CortexForkMaterializationContract.MarkerFileName,
                            stream => stream.Write(Encoding.UTF8.GetBytes(markerContract.Encode())));
                        nArms[index] = CreateFixtureArm(child, 1, PolicyBoundaryArms.Baseline, CortexForkRailRoles.Baseline, armContract);
                    }
                    try
                    {
                        _ = CortexForkRunner.RunMatchedForkNLadder(runtime, seed, [nArms], [seed.NextStep + 1]);
                        return false;
                    }
                    catch (InvalidDataException) { return true; }
                }
                wrongNFundingRejected = RejectN("funding");
                wrongNParentRejected = RejectN("parent");
                wrongNColdRejected = RejectN("cold");
                wrongChildRejected = RejectN("child");

                bool allContractReceipts = true;
                allTerminalReceipts = true;
                foreach (CortexForkArm<PolicyBoundaryTrialOutcome> arm in arms.SelectMany(static row => row))
                {
                    string railPath = Path.Combine(arm.RunDirectory, "policy-boundary.rail.ron");
                    if (!File.Exists(railPath)) { allContractReceipts = false; continue; }
                    allTerminalReceipts &= File.Exists(Path.Combine(arm.RunDirectory, "terminal-verification.ron"))
                        && File.Exists(Path.Combine(arm.RunDirectory, CortexForkTerminalRunReceipt.FileName));
                    PolicyBoundaryRailMetadataDocument rail = RonSerializer.Deserialize<PolicyBoundaryRailMetadataDocument>(File.ReadAllBytes(railPath));
                    allContractReceipts &= rail.materializationParentRunID == parentID
                        && rail.materializationAttemptID == attemptID
                        && rail.materializationChildRunID == Path.GetFileName(arm.RunDirectory)
                        && rail.materializationColdSeedDigest == seed.ColdSeedDigest;
                }
                exact = allContractReceipts && allTerminalReceipts;

                // The TSV rows and rail are mutable together; only the execution tape receipt
                // makes the successor scope authoritative.  Rewrite both surfaces and ensure
                // the forged support digest cannot become an executable successor.
                string successorTamperChild = arms[0][2].RunDirectory;
                string successorTamperRailPath = Path.Combine(successorTamperChild, "policy-boundary.rail.ron");
                string successorTamperOccurrenceCheckPath = Path.Combine(successorTamperChild, PolicyOccurrenceCheckReceiptFile);
                string successorTamperCoveragePath = Path.Combine(successorTamperChild, PolicyOccurrenceCheckCoverageReceiptFile);
                if (File.Exists(successorTamperRailPath) && File.Exists(successorTamperOccurrenceCheckPath)
                    && File.Exists(successorTamperCoveragePath))
                {
                    byte[] originalRail = File.ReadAllBytes(successorTamperRailPath);
                    byte[] originalOccurrenceCheck = File.ReadAllBytes(successorTamperOccurrenceCheckPath);
                    byte[] originalCoverage = File.ReadAllBytes(successorTamperCoveragePath);
                    try
                    {
                        PolicyBoundaryRailMetadataDocument forgedRail = RonSerializer.Deserialize<PolicyBoundaryRailMetadataDocument>(originalRail);
                        ulong forgedCandidate = forgedRail.executedCandidateFingerprint ^ 0x1001UL;
                        ulong forgedSupport = forgedRail.successorSupportDigest ^ 0x2001UL;
                        string candidateText = forgedRail.executedCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture);
                        string supportText = forgedRail.successorSupportDigest.ToString("X16", CultureInfo.InvariantCulture);
                        string verificationText = Encoding.UTF8.GetString(originalOccurrenceCheck)
                            .Replace(candidateText, forgedCandidate.ToString("X16", CultureInfo.InvariantCulture), StringComparison.Ordinal);
                        string coverageText = Encoding.UTF8.GetString(originalCoverage)
                            .Replace(candidateText, forgedCandidate.ToString("X16", CultureInfo.InvariantCulture), StringComparison.Ordinal)
                            .Replace(supportText, forgedSupport.ToString("X16", CultureInfo.InvariantCulture), StringComparison.Ordinal);
                        File.WriteAllText(successorTamperOccurrenceCheckPath, verificationText, Encoding.UTF8);
                        File.WriteAllText(successorTamperCoveragePath, coverageText, Encoding.UTF8);
                        forgedRail.executedCandidateFingerprint = forgedCandidate;
                        forgedRail.successorSupportDigest = forgedSupport;
                        forgedRail.successorOccurrenceCheckDigest = ComputePolicyBoundaryFileSHA256(successorTamperOccurrenceCheckPath);
                        forgedRail.successorOccurrenceCheckCoverageDigest = ComputePolicyBoundaryFileSHA256(successorTamperCoveragePath);
                        File.WriteAllBytes(successorTamperRailPath, RonSerializer.SerializeToUtf8(in forgedRail));
                        successorSupportTamperRejected = !TryVerifySuccessorScopeEvidence(
                            successorTamperChild, forgedRail, HomeostatPolicyBoundaryDomain.Instance);
                    }
                    finally
                    {
                        File.WriteAllBytes(successorTamperRailPath, originalRail);
                        File.WriteAllBytes(successorTamperOccurrenceCheckPath, originalOccurrenceCheck);
                        File.WriteAllBytes(successorTamperCoveragePath, originalCoverage);
                    }
                }

                PolicyBoundaryArmReceipt candidateIdentity = new(PolicyBoundaryArms.Candidate, 1, 0, 1, true, true)
                {
                    ExecutedDecisionID = new CortexPolicyDecisionID(1),
                    ExecutedStep = 1,
                    ExecutedLaunchpadAction = 0,
                    ExecutedRawCandidateAction = 1,
                    ExecutedSelectedCandidateAction = 1,
                    ExecutedAction = 1,
                    ExecutedAuthority = CortexPolicyAuthorities.Grammar,
                    ExecutedSelectionCause = CortexPolicySelectionCauses.GrammarCandidate,
                    ExecutedReadoutFingerprint = 1,
                    ExecutedReadoutRevision = 1,
                };
                executedIdentityCustody = candidateIdentity.HasExecutedDecisionIdentity;
                try { new PolicyBoundaryArmReceipt(PolicyBoundaryArms.Candidate, 1, 0, 1, true, true)
                    .ValidateExecutedDecisionIdentity(HomeostatPolicyBoundaryDomain.Instance, requireGrammar: true); }
                catch (InvalidDataException) { missingExecutedIdentityRejected = true; }
                try { (candidateIdentity with { ExecutedReadoutFingerprint = 0 })
                    .ValidateExecutedDecisionIdentity(HomeostatPolicyBoundaryDomain.Instance, requireGrammar: true); }
                catch (InvalidDataException) { forgedExecutedIdentityRejected = true; }
                rawAliasCandidateRejected = candidateIdentity.ExecutedAction == candidateIdentity.ExecutedRawCandidateAction;

                bool custody = true;
                string custodySource = arms[0][0].RunDirectory;
                foreach (string name in PolicyJournalFileNames)
                {
                    if (!File.Exists(Path.Combine(custodySource, name)))
                    {
                        custody = false;
                        continue;
                    }
                    foreach (CortexForkArm<PolicyBoundaryTrialOutcome> arm in arms.SelectMany(static row => row))
                        custody &= File.Exists(Path.Combine(arm.RunDirectory, name));
                }
                policyJournalCustody = custody;

                CortexSeedSidecarSet missingJournals = CortexSeedSidecarSet.Empty;
                foreach (string name in PolicyJournalFileNames)
                {
                    if (name == PolicyDecisionReceiptFile) continue;
                    string path = Path.Combine(custodySource, name);
                    if (File.Exists(path)) missingJournals = missingJournals.WithReference(name, path);
                }
                // journal.log rides every fork seed (the journal's shed prefix lives only there); this arm
                // deliberately omits ONE policy journal, not the journal record itself.
                string custodyJournalLog = Path.Combine(custodySource, "journal.log");
                if (File.Exists(custodyJournalLog)) missingJournals = missingJournals.WithReference("journal.log", custodyJournalLog);
                CortexForkSeed missingJournalSeed = CortexForkSeed.Materialize(
                    Checkpoint.PeekNextStep(custodySource),
                    File.ReadAllBytes(Path.Combine(custodySource, Checkpoint.FileName)),
                    File.ReadAllBytes(Path.Combine(custodySource, "tape.spanlog")),
                    File.ReadAllBytes(Path.Combine(custodySource, "curve.tsv")),
                    PersistedConfigDigest(Checkpoint.PeekConfig(custodySource)), missingJournals,
                    File.ReadAllBytes(Path.Combine(custodySource, "excursions.txt")),
                    File.ReadAllLines(Path.Combine(custodySource, "excursions.txt")).LongLength - 1);
                (Run missingJournalRun, CortexForkMaterializationContract missingJournalContract) =
                    runtime.CurrentRun.CreateMaterializedChildRun(CortexForkRailRoles.Baseline, attemptID, missingJournalSeed.ColdSeedDigest);
                CortexForkArm<PolicyBoundaryTrialOutcome> missingJournalArm = CreateFixtureArm(
                    missingJournalRun, 1, PolicyBoundaryArms.Baseline, CortexForkRailRoles.Baseline,
                    missingJournalContract);
                try
                {
                    _ = CortexForkRunner.RunFork(runtime, missingJournalSeed, missingJournalArm, missingJournalSeed.NextStep + 1);
                }
                catch (InvalidDataException exception) when (exception.Message.Contains("policy journal", StringComparison.Ordinal))
                {
                    missingPolicyJournalRejected = true;
                }
                (bool focusedPassed, string focusedReceipt) = VerifySourceSuccessorReplayFixture(runtime, seed!, focusedSourceDecision,
                    focusedSourceEventID, in focusedSourceCorroboration, runtime._policyBoundaryObligations[HomeostatPolicyBoundaryDomain.Instance.PolicyID]);
                sourceSuccessorFixturePassed = focusedPassed;
                sourceSuccessorFixtureReceipt = focusedReceipt;
                if (runtime._runtimeTape is not null)
                {
                    runtime._runtimeTape.Append(Encoding.ASCII.GetBytes("malformed-policy-boundary-source"),
                        "policy-boundary:source", Provenances.Execution);
                    try
                    {
                        _ = runtime.TryReadBoundarySourceCorroboration(
                            focusedSourceCorroboration.SourceDecisionID.Value,
                            focusedSourceCorroboration.SourceDecisionEventID.Value,
                            focusedSourceCorroboration.ReadoutRevision,
                            focusedSourceCorroboration.ReadoutFingerprint,
                            focusedSourceCorroboration.CandidateFingerprint,
                            focusedSourceCorroboration.OccurrenceDigest,
                            out _, out _, focusedSourceCorroboration.CanonicalState);
                    }
                    catch (InvalidDataException) { boundaryCorroborationMalformedRejected = true; }
                }
                // Run the denied portrait after ladder/recovery reconciliation so its isolated
                // child cannot perturb the parent funding rows those checks consume.
                runtime.FlushPolicyJournalBuffer();
                (Run deniedPortraitRun, CortexForkMaterializationContract deniedPortraitContract) =
                    runtime.CurrentRun.CreateMaterializedChildRun(CortexForkRailRoles.Calibration, attemptID, seed.ColdSeedDigest);
                CortexForkArm<int> deniedPortraitArm = new(
                    deniedPortraitRun.Dir, () => Cortex.CreateCheckpointRuntime(runConfig),
                    static cortex => cortex.Step,
                    interveneAfterLoad: cortex =>
                    {
                        PrepareFixtureArmSeed(cortex, PolicyBoundaryArms.Candidate, frozen: false,
                            Path.GetFileName(deniedPortraitRun.Dir), deniedPortraitContract);
                        PolicyState state = cortex.GetPolicy(HomeostatPolicyBoundaryDomain.Instance.PolicyID);
                        CortexPolicyDecisionReadout requestReadout = new(
                            0, 1, 1, 1, CortexPolicyAuthorities.Grammar, fixtureRevision,
                            CortexPolicySelectionCauses.GrammarCandidate,
                            fixtureSupportDigest, fixtureSourceCandidateFingerprint);
                        state.TrialExecutionCause = CortexPolicySelectionCauses.GrammarCandidate;
                        state.TrialExecutionOutcome = CortexPolicyTrialExecutionOutcomes.GuardDenied;
                        state.TrialRequestCount = 1;
                        state.TrialGuardAdmittedCount = 0;
                        state.TrialLastRequest = new CortexPolicyDecision(
                            new CortexPolicyDecisionID(0xB747F1FAD2C27D01UL), HomeostatPolicyBoundaryDomain.Instance.PolicyID, requestReadout);
                        state.TrialLastRequestStep = cortex.Step;
                        state.TrialExecutionCorroboration = null;
                        state.TrialExecutionReadoutFingerprint = 0;
                        state.TrialExecutionStep = -1;
                        state.HistoricalTrialExecution = default;
                    },
                    completionMode: CortexForkCompletionModes.ExactAbsoluteStep,
                    railRole: CortexForkRailRoles.Calibration,
                    afterRuntimeBind: (cortex, window) =>
                    {
                        if (!TryReadPolicyBoundarySeedCustodyDocument(parentDirectory!, parentID,
                                deniedPortraitContract.AttemptID, out PolicyBoundarySeedCustody custody)
                            || !TryDecodeCanonicalState(custody.canonicalState, HomeostatPolicyBoundaryDomain.Instance, out PolicyCanonicalStateID canonicalState))
                            throw new InvalidDataException("guard-denied portrait has no authenticated custody");
                        PolicyBoundaryContinuationModes mode = cortex.AuthenticatePolicyBoundaryContinuation(
                            in custody, CortexPolicySelectionCauses.GrammarCandidate, in canonicalState);
                        if (mode != PolicyBoundaryContinuationModes.PreserveGuardDenied)
                            throw new InvalidDataException($"guard-denied portrait classified as {mode}");
                        Interlocked.Increment(ref guardDeniedPortraitAcceptedCount);
                        PolicyState state = cortex.GetPolicy(HomeostatPolicyBoundaryDomain.Instance.PolicyID);
                        state.TrialGuardAdmittedCount = 1;
                        try
                        {
                            _ = cortex.AuthenticatePolicyBoundaryContinuation(
                                in custody, CortexPolicySelectionCauses.GrammarCandidate, in canonicalState);
                        }
                        catch (InvalidDataException)
                        {
                            Interlocked.Increment(ref guardDeniedPortraitMalformedRejectedCount);
                        }
                        finally { state.TrialGuardAdmittedCount = 0; }
                    },
                    parentRunID: parentID, materializationContract: deniedPortraitContract);
                _ = CortexForkRunner.RunFork(runtime, seed, deniedPortraitArm, checked(seed.NextStep + 1));
            });
            bool passed = exit == 0 && exact && authorityIdentityFixturePassed && identityProbeIsolated && executedIdentityCustody && missingExecutedIdentityRejected && forgedExecutedIdentityRejected && rawAliasCandidateRejected && wrongFundingRejected && wrongParentRejected && wrongColdRejected && wrongChildRejected && repeatRejected
                && wrongNFundingRejected && wrongNParentRejected && wrongNColdRejected
                && policyJournalCustody && missingPolicyJournalRejected && nonExactContinuityRejected && ladderInterventionsOnce
                && continuationNotAttemptedCount == 4
                && continuationGuardDeniedCount == 0
                && continuationHistoricalCount == 8
                && continuationRungOneCount == 8
                && continuationRungOneNotAttemptedCount == 0
                && continuationRungOneGuardDeniedCount == 0
                && continuationRungOneHistoricalCount == 8
                && continuationRungOneSeedAncestryCount == 8
                && recoveryOrphanReused && recoveryTerminalSettled && recoverySecondResumeNoOp && directSettlementDurable
                && recoveredAttachFixturePassed
                && recoverySettlementCustodyMissingRepaired && recoverySettlementCustodyMissingRejected && recoverySettlementCustodyMutationRejected
                && recoveryIdentityRejected && recoveryMixedGenerationRejected && recoveryCustodyMutationRejected
                && recoveryMissingSourceEventRejected && recoveryForgedSourceEventRejected && recoveryForeignGenerationRejected
                && verificationStaleIgnored && verificationCurrentRestored && verificationLatestMatchingPassed
                && verificationMissingRejected && verificationConflictRejected && successorSupportTamperRejected;
            passed &= sourceSuccessorFixturePassed;
            passed &= boundaryCorroborationRoundTrip && boundaryCorroborationTamperRejected
                && boundaryCorroborationStaleRejected && boundaryCorroborationMalformedRejected && ordinaryLaunchpadCandidateFree;
            bool publicationBeforeFirstAction = publicationBeforeFirstActionCount == 12;
            bool configuredCauseCoverage = baselineCauseCount == 3 && candidateCauseCount == 3
                && forcedCauseCount == 3 && reflexCauseCount == 3;
            bool forcedExecutionPaidCaptured = forcedExecutionPaidCapturedCount == 3;
            passed &= publicationBeforeFirstAction && configuredCauseCoverage
                && forcedExecutionPaidCaptured && failureCensusFlushedWithoutSettlement
                && failureGenerationTransitionDurable && failureRetryUnsettled
                && failureTamperedCompleteRailRejected
                && terminalOutcomeReconstructed
                && terminalOutcomeOmissionRejected
                && terminalOutcomeDigestRejected
                && terminalOutcomeRungSubstitutionRejected
                && ordinaryOutcomeCustodyCount == 1
                && terminalHistoricalScopeAcceptedCount == 3
                && terminalHistoricalScopeMismatchRejectedCount == 3
                && guardDeniedPortraitAcceptedCount == 1
                && guardDeniedPortraitMalformedRejectedCount == 1;
            bool railAccounting = PolicyBoundaryRailMetadata.VerifyRun89Fixture();
            passed &= railAccounting;
            output.WriteLine($"  policy-boundary materialization fixture · arms={(exact ? "12/12" : "BROKEN")} · recovered-attach={(recoveredAttachFixturePassed ? "agreement+forced-divergence/idempotent/tamper-rejected" : "FAIL")} · publication-before-first-action={publicationBeforeFirstActionCount}/12 · continuation=not-attempted:{continuationNotAttemptedCount},guard-denied:{continuationGuardDeniedCount},historical:{continuationHistoricalCount} · continuation-rung1=not-attempted:{continuationRungOneNotAttemptedCount},guard-denied:{continuationRungOneGuardDeniedCount},historical:{continuationRungOneHistoricalCount},seed-ancestry:{continuationRungOneSeedAncestryCount}/{continuationRungOneCount} · guard-denied-portrait=accepted:{guardDeniedPortraitAcceptedCount}/1,malformed-rejected:{guardDeniedPortraitMalformedRejectedCount}/1 · causes=baseline:{baselineCauseCount}/3,candidate:{candidateCauseCount}/3,forced-null:{forcedCauseCount}/3,reflex:{reflexCauseCount}/3 · forced-execution={(forcedExecutionPaidCaptured ? "paid/captured" : $"FAIL({forcedExecutionPaidCapturedCount}/3)")} · ordinary-outcome={ordinaryOutcomeCustodyCount}/1 · reconstructed-outcome={(terminalOutcomeReconstructed ? "exact" : "LOST")},omission={(terminalOutcomeOmissionRejected ? "rejected" : "ACCEPTED")},digest={(terminalOutcomeDigestRejected ? "rejected" : "ACCEPTED")},rung-substitution={(terminalOutcomeRungSubstitutionRejected ? "rejected" : "ACCEPTED")} · incomplete-census={(failureCensusFlushedWithoutSettlement ? "flushed/no-settlement" : "FAIL")} · incomplete-transition={(failureGenerationTransitionDurable ? "durable" : "FAIL")} · retry-unsettled={(failureRetryUnsettled ? "yes" : "FAIL")} · tampered-rail={(failureTamperedCompleteRailRejected ? "rejected" : "ACCEPTED")} · direct-settlement={(directSettlementDurable ? "custody+journal" : "FAIL")} · source-successor={(sourceSuccessorFixturePassed ? sourceSuccessorFixtureReceipt : "FAIL:" + sourceSuccessorFixtureReceipt)} · boundary-source={(boundaryCorroborationRoundTrip && ordinaryLaunchpadCandidateFree && boundaryCorroborationTamperRejected && boundaryCorroborationStaleRejected && boundaryCorroborationMalformedRejected ? "exact/ordinary-0/0/tamper-rejected/stale-rejected/malformed-rejected" : "FAIL")} · authority-identity={(authorityIdentityFixturePassed && identityProbeIsolated ? authorityIdentityFixtureReceipt : "FAIL:" + authorityIdentityFixtureReceipt)} · verification-history={(verificationStaleIgnored && verificationCurrentRestored && verificationLatestMatchingPassed && verificationMissingRejected && verificationConflictRejected ? "stale-ignored/current-restored/latest-pass/missing-rejected/conflict-rejected" : "FAIL")} · successor-scope={(successorSupportTamperRejected ? "authenticated/tamper-rejected" : "FAIL")} · terminal-scope=(history:{terminalHistoricalScopeAcceptedCount}/3,mismatch-rejected:{terminalHistoricalScopeMismatchRejectedCount}/3) · terminal-custody={(allTerminalReceipts ? "12/12" : "BROKEN")} · recovery-orphan={(recoveryOrphanReused ? "reused/zero" : "FAIL")} · recovery-terminal={(recoveryTerminalSettled ? "settled" : "FAIL")} · recovery-second-resume={(recoverySecondResumeNoOp ? "no-op" : "FAIL")} · recovery-settlement-missing={(recoverySettlementCustodyMissingRepaired ? "repaired" : "FAIL")} · recovery-settlement-mismatch={(recoverySettlementCustodyMissingRejected ? "rejected" : "ACCEPTED")} · recovery-settlement-tamper={(recoverySettlementCustodyMutationRejected ? "rejected" : "ACCEPTED")} · recovery-identity={(recoveryIdentityRejected ? "rejected" : "ACCEPTED")} · recovery-mixed={(recoveryMixedGenerationRejected ? "rejected" : "ACCEPTED")} · recovery-foreign={(recoveryForeignGenerationRejected ? "rejected" : "ACCEPTED")} · recovery-custody={(recoveryCustodyMutationRejected ? "rejected" : "ACCEPTED")} · recovery-missing-event={(recoveryMissingSourceEventRejected ? "rejected" : "ACCEPTED")} · recovery-forged-event={(recoveryForgedSourceEventRejected ? "rejected" : "ACCEPTED")} · executed-identity={(executedIdentityCustody ? "carried" : "LOST")} · missing-identity={(missingExecutedIdentityRejected ? "rejected" : "ACCEPTED")} · forged-identity={(forgedExecutedIdentityRejected ? "rejected" : "ACCEPTED")} · raw-alias={(rawAliasCandidateRejected ? "rejected" : "ACCEPTED")} · continuity={(nonExactContinuityRejected ? "rejected" : "ACCEPTED")} · intervention={(ladderInterventionsOnce ? "fork-only" : $"REPLAYED-{ladderInterventionCount}")} · funding={(wrongFundingRejected && wrongNFundingRejected ? "rejected" : "ACCEPTED")} · parent={(wrongParentRejected ? "rejected" : "ACCEPTED")} · cold={(wrongColdRejected ? "rejected" : "ACCEPTED")} · child={(wrongChildRejected ? "rejected" : "ACCEPTED")} · repeat={(repeatRejected ? "rejected" : "ACCEPTED")} · policy-journal={(policyJournalCustody ? "carried" : "LOST")} · missing-journal={(missingPolicyJournalRejected ? "rejected" : "ACCEPTED")} · rail-accounting={(railAccounting ? "rejected/accepted" : "BROKEN")} · {(passed ? "PASS" : "FAIL")}");
            return passed;
        }
        finally
        {
            if (parentDirectory is not null && Directory.Exists(parentDirectory)) Directory.Delete(parentDirectory, recursive: true);
            if (File.Exists(corpusPath)) File.Delete(corpusPath);
        }
    }

    private static bool VerifyPolicyBoundaryTerminalScope(
        Cortex cortex,
        IPolicyBoundaryDomain domain,
        CortexPolicyQuotaDecisionID fundingID,
        in PolicyBoundaryTrialOutcome outcome,
        out PolicyVerifiedScopeEntry scope)
    {
        if (!cortex.TryReadPolicyTrialExecutionScopeForQuota(domain.PolicyID, fundingID, out scope))
            return false;
        return scope.IsValid
            && scope.State == outcome.ExecutedCanonicalState
            && scope.ReadoutFingerprint == outcome.ExecutedReadoutFingerprint
            && scope.CandidateFingerprint == outcome.ExecutedCandidateFingerprint
            && scope.OccurrenceDigest == outcome.ExecutedReadoutOccurrenceDigest
            && scope.Revision == new GrammarRevisionID(outcome.ExecutedReadoutRevision);
    }

    internal static CortexForkArm<PolicyBoundaryTrialOutcome> CreatePolicyBoundaryArm(
        string path, int step, int horizon, PolicyBoundaryArms armKind, CortexRunConfig config, IPolicyBoundaryDomain domain, CortexPolicyTrialAuthorityIdentity authorityIdentity, CortexPolicyAuthorities authority, bool forced = false, bool frozen = false,
        bool requireOrdinaryOutcome = false,
        CortexForkRailRoles railRole = CortexForkRailRoles.Unknown, string parentRunID = "", CortexForkMaterializationContract? materializationContract = null,
        PolicyBoundaryObligationID obligation = default, PolicyBoundaryRational candidateBoundary = default)
    {
        ArgumentNullException.ThrowIfNull(domain);
        domain.PolicyBinding.Validate();
        domain.ArmTopology.Validate();
        if (!domain.SeedAuthority.IsValid)
            throw new InvalidDataException($"policy-boundary domain '{domain.PolicyID}' has incomplete seed authority");
        if (materializationContract is not CortexForkMaterializationContract contract)
            throw new ArgumentException("policy boundary arms require a typed materialization contract", nameof(materializationContract));
        contract.Validate(path);
        if (!string.Equals(contract.ParentRunID, parentRunID, StringComparison.Ordinal))
            throw new InvalidDataException("policy boundary arm parent identity disagrees with its materialization contract");
        long seedPaid = 0;
        ulong seedGrammar = 0;
        ulong seedTransitions = 0;
        PolicyBoundarySeedCustody? seedCustody = null;
        string parentDirectory = Path.GetFullPath(Path.Combine(path, "..", ".."));
        int generation = ParsePolicyBoundaryChildIndex(Path.GetFileName(path));

        PolicyBoundarySeedCustody ReadSeedCustody()
        {
            if (seedCustody is PolicyBoundarySeedCustody cachedCustody)
                return cachedCustody;
            if (!TryReadPolicyBoundarySeedCustodyDocument(parentDirectory, parentRunID, contract.AttemptID,
                    out PolicyBoundarySeedCustody boundCustody))
                throw new InvalidDataException($"policy boundary arm {contract.ChildRunID} has no authenticated seed custody");
            seedCustody = boundCustody;
            return boundCustody;
        }

        void PrepareArmSeed(Cortex cortex)
        {
            PolicyBoundarySeedCustody boundCustody = ReadSeedCustody();
            CortexPolicyAuthorities preparedAuthority = forced
                ? domain.SeedAuthority.ForcedNullAuthority
                : authority;
            cortex.SetPolicyTrialAuthority(domain.PolicyID, in authorityIdentity, preparedAuthority,
                grammarExecutionQuota: -1,
                forcedDivergenceSeed: forced ? authorityIdentity.CandidateFingerprint.Value ^ 0x9E3779B97F4A7C15UL : null,
                freezeAdaptation: frozen);
            if (forced)
            {
                PolicyCanonicalStateID custodyState = default;
                if (domain.CanonicalScopeMode != PolicyCanonicalScopeModes.None && !TryDecodeCanonicalState(boundCustody.canonicalState, domain, out custodyState))
                    throw new InvalidDataException($"policy boundary arm {contract.ChildRunID} has no canonical custody scope");
                cortex.BindPendingForcedTrialIntent(domain.PolicyID,
                    boundCustody.fundingID, boundCustody.sourceFundingDecision, boundCustody.sourceDecisionID,
                    boundCustody.sourceDecisionEventID, boundCustody.sourceCorroborationEventID,
                    boundCustody.sourceSupportDigest, boundCustody.sourceCandidateFingerprint,
                    boundCustody.readoutFingerprint, boundCustody.candidateFingerprint,
                    new GrammarRevisionID(boundCustody.candidateRevision), in custodyState,
                    obligation.Value, (byte)armKind,
                    domain.BoundaryFeatureID,
                    boundCustody.sourceRunID, boundCustody.custodyDigest);
                cortex.BindPolicyBoundaryForcedCandidate(
                    domain.PolicyID,
                    boundCustody.domainCandidateCanonical,
                    boundCustody.domainCandidateDigest,
                    boundCustody.domainFrontierRevision,
                    boundCustody.domainFrontierAuthoritySHA256);
            }
            cortex.BindActiveTrialQuotaIdentity(domain.PolicyID,
                new CortexPolicyQuotaDecisionID(boundCustody.fundingID), boundCustody.custodyDigest);
        }

        return new CortexForkArm<PolicyBoundaryTrialOutcome>(path, () => Cortex.CreateCheckpointRuntime(config), cortex =>
        {
            PolicyBoundarySeedCustody boundCustody = ReadSeedCustody();
            CortexPolicyRuntimeReceipt policy = cortex.ReadPolicyRuntimeReceipt(domain.PolicyID);
            long paid = checked((long)policy.PaidGrammarOutcomes);
            long grammar = checked((long)policy.GrammarExecutions);
            if (!cortex.TryReadPolicyTrialExecutionReceiptForQuota(domain.PolicyID, new CortexPolicyQuotaDecisionID(boundCustody.fundingID),
                    out CortexPolicyTrialExecutionOutcomes executionOutcome,
                    out long requestCount, out long guardAdmittedCount,
                    out CortexPolicyDecisionReadout lastRequestReadout,
                    out CortexPolicyDecisionID lastRequestDecisionID, out int lastRequestStep,
                    out CortexPolicyDecisionReadout executed,
                    out CortexPolicyDecisionID executedDecisionID, out ulong executedFingerprint, out int executedStep))
                throw new InvalidDataException("policy boundary child completed without an authenticated execution receipt");
            long matchedSpend = checked((long)cortex.Step + 1 - boundCustody.nextStep);
            bool executionObserved = executionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted;
            TapeEventID executedDecisionEventID = forced && executionObserved
                ? cortex.FindPolicyDecisionEvent(executedDecisionID)
                : default;
            CortexPolicyOutcomeEvidence outcomeEvidence = default;
            bool hasOutcomeEvidence = executionObserved
                && cortex.TryReadPolicyOutcomeEvidence(executedDecisionID, out outcomeEvidence);
            if (forced && executionObserved && requireOrdinaryOutcome && !hasOutcomeEvidence)
                throw new InvalidDataException("terminal forced policy boundary child lacks its ordinary POLICY-OUTCOME");
            ulong forcedDivergenceSeed = forced
                ? authorityIdentity.CandidateFingerprint.Value ^ 0x9E3779B97F4A7C15UL
                : 0;
            PolicyCanonicalStateID successorState = default;
            bool successorScopeVerified = !executionObserved;
            if (executionObserved)
            {
                if (!cortex.TryReadPolicyTrialExecutionScopeForQuota(domain.PolicyID, new CortexPolicyQuotaDecisionID(boundCustody.fundingID), out PolicyVerifiedScopeEntry scope))
                    throw new InvalidDataException("policy boundary child completed with execution but no immutable canonical scope");
                successorState = scope.State;
                bool executionScopeCandidateExact = executed.SelectionCause == CortexPolicySelectionCauses.Launchpad
                    ? executed.ReadoutCandidateFingerprint == 0 && executed.ReadoutCandidateOccurrenceDigest == 0
                    : scope.CandidateFingerprint == executed.ReadoutCandidateFingerprint
                        && scope.OccurrenceDigest == executed.ReadoutCandidateOccurrenceDigest;
                successorScopeVerified = scope.IsValid
                    && scope.ReadoutFingerprint == executedFingerprint
                    && executionScopeCandidateExact
                    && scope.Revision == executed.GrammarRevision;
            }
            if (forced && (!executionObserved || executed.SelectionCause != domain.SeedAuthority.ForcedNullSelectionCause
                || executedDecisionEventID.Value <= 0 || forcedDivergenceSeed == 0 || !successorScopeVerified))
                throw new InvalidDataException($"forced policy boundary child lacks its {domain.SeedAuthority.ForcedNullSelectionCause} event or seed custody: observed={executionObserved} cause={executed.SelectionCause} event={executedDecisionEventID.Value} seed={forcedDivergenceSeed:X} scope={successorScopeVerified} state={successorState} readout={executedFingerprint:X16} candidate={executed.ReadoutCandidateFingerprint:X16} support={executed.ReadoutCandidateOccurrenceDigest:X16} revision={executed.GrammarRevision.Value}");
            return new PolicyBoundaryTrialOutcome(checked(paid - seedPaid), matchedSpend, true,
                checked(grammar - (long)seedGrammar), checked((long)policy.TrialAdaptationTransitions - (long)seedTransitions),
                policy.AdaptationEnabled)
            {
                ExecutionOutcome = executionOutcome,
                RequestCount = requestCount,
                GuardAdmittedCount = guardAdmittedCount,
                LastRequestDecisionID = lastRequestDecisionID,
                LastRequestStep = lastRequestStep,
                LastRequestReadout = lastRequestReadout,
                ExecutedDecisionID = executionObserved ? executedDecisionID : default,
                ExecutedStep = executionObserved ? executedStep : -1,
                ExecutedLaunchpadAction = executionObserved ? executed.LaunchpadAction : -1,
                ExecutedRawCandidateAction = executionObserved ? executed.RawCandidateAction : -1,
                ExecutedSelectedCandidateAction = executionObserved ? executed.SelectedCandidateAction : -1,
                ExecutedAction = executionObserved ? executed.ExecutedAction : -1,
                ExecutedAuthority = executionObserved ? executed.Authority : CortexPolicyAuthorities.Launchpad,
                ExecutedSelectionCause = executionObserved ? executed.SelectionCause : CortexPolicySelectionCauses.Launchpad,
                ExecutedReadoutFingerprint = executionObserved ? executedFingerprint : 0,
                ExecutedReadoutRevision = executionObserved ? executed.GrammarRevision.Value : 0,
                ExecutedReadoutOccurrenceDigest = executionObserved ? executed.ReadoutCandidateOccurrenceDigest : 0,
                ExecutedCandidateFingerprint = executionObserved ? executed.ReadoutCandidateFingerprint : 0,
                ExecutedCanonicalState = executionObserved ? successorState : default,
                ExecutedDecisionEventID = executedDecisionEventID,
                ExecutedOutcomeEventID = requireOrdinaryOutcome && hasOutcomeEvidence ? outcomeEvidence.EventID : default,
                ExecutedOutcomePayloadSHA256 = requireOrdinaryOutcome && hasOutcomeEvidence ? outcomeEvidence.PayloadSHA256 : "",
                ForcedDivergenceSeed = forcedDivergenceSeed,
            };
        }, PrepareArmSeed, CortexForkCompletionModes.ExactAbsoluteStep, railRole: railRole,
            afterRuntimeBind: (cortex, _) =>
            {
                PolicyBoundarySeedCustody boundCustody = ReadSeedCustody();
                PolicyCanonicalStateID preparedState = default;
                if (domain.CanonicalScopeMode != PolicyCanonicalScopeModes.None && !TryDecodeCanonicalState(boundCustody.canonicalState, domain, out preparedState))
                    throw new InvalidDataException($"policy boundary child {contract.ChildRunID} has no canonical custody scope");
                CortexPolicySelectionCauses preparedCause = forced
                    ? domain.SeedAuthority.ForcedNullSelectionCause
                    : armKind == PolicyBoundaryArms.Candidate
                        ? domain.SeedAuthority.CandidateSelectionCause
                        : authority switch
                        {
                            CortexPolicyAuthorities.Launchpad => CortexPolicySelectionCauses.Launchpad,
                            CortexPolicyAuthorities.Shadow => CortexPolicySelectionCauses.ShadowCandidate,
                            CortexPolicyAuthorities.Grammar => CortexPolicySelectionCauses.GrammarCandidate,
                            _ => throw new InvalidDataException("policy boundary arm authority has no execution cause"),
                        };
                PolicyBoundaryContinuationModes continuation = cortex.AuthenticatePolicyBoundaryContinuationForDomain(
                    in boundCustody, domain, preparedCause, in preparedState);
                if (continuation == PolicyBoundaryContinuationModes.RestoreNotAttempted)
                {
                    PolicyState preparedPolicy = cortex.GetPolicy(domain.PolicyID);
                    ulong preparedReadoutFingerprint = ReadActivePolicyFingerprint(preparedPolicy);
                    cortex.RestorePaidPolicyTrialEpoch(domain.PolicyID,
                        new CortexPolicyQuotaDecisionID(boundCustody.fundingID), boundCustody.custodyDigest,
                        preparedCause,
                        forced ? authorityIdentity.CandidateFingerprint.Value ^ 0x9E3779B97F4A7C15UL : null,
                        in preparedState, preparedReadoutFingerprint, preparedPolicy.ReadoutCandidateFingerprint,
                        preparedPolicy.ReadoutCandidateOccurrenceDigest, preparedPolicy.ReadoutCandidateRevision);
                }
                if (forced)
                    cortex.BindPolicyBoundaryForcedCandidate(
                        domain.PolicyID,
                        boundCustody.domainCandidateCanonical,
                        boundCustody.domainCandidateDigest,
                        boundCustody.domainFrontierRevision,
                        boundCustody.domainFrontierAuthoritySHA256);
                CortexPolicyRuntimeReceipt policy = cortex.ReadPolicyRuntimeReceipt(domain.PolicyID);
                seedPaid = checked((long)policy.PaidGrammarOutcomes);
                seedGrammar = policy.GrammarExecutions;
                seedTransitions = policy.TrialAdaptationTransitions;
            },
            parentRunID: parentRunID, materializationContract: contract,
            afterCompletedStep: null,
            persistCompletionBeforeLanding: (cortex, _, _, outcome) =>
            {
                PolicyBoundarySeedCustody boundCustody = ReadSeedCustody();
                // This hook runs only after terminal checkpoint verification succeeds; metadata continuity is therefore
                // custody of the child terminal receipt rather than a default on the pre-verification outcome.
                cortex.FlushPolicyJournalBuffer();
                string successorOccurrenceCheckDigest = armKind == PolicyBoundaryArms.ForcedDivergentNull
                    ? ComputePolicyBoundaryFileSHA256(cortex.CurrentRun.PathOf(PolicyOccurrenceCheckReceiptFile))
                    : "";
                string successorOccurrenceCheckCoverageDigest = armKind == PolicyBoundaryArms.ForcedDivergentNull
                    ? ComputePolicyBoundaryFileSHA256(cortex.CurrentRun.PathOf(PolicyOccurrenceCheckCoverageReceiptFile))
                    : "";
                PolicyCanonicalStateID successorState = outcome.ExecutedCanonicalState;
                if (armKind == PolicyBoundaryArms.ForcedDivergentNull
                    && !VerifyPolicyBoundaryTerminalScope(cortex, domain,
                        new CortexPolicyQuotaDecisionID(boundCustody.fundingID), in outcome,
                        out _))
                    throw new InvalidDataException("forced policy boundary metadata lacks the durable verified successor scope");
                PolicyBoundaryRailMetadata metadata = new(step, horizon, armKind, railRole, authorityIdentity.ActiveProgramFingerprint.Value,
                    outcome with { ContinuityExact = true }, cortex.Step, contract,
                    boundCustody.sourceRunID, boundCustody.nextStep, boundCustody.custodyDigest,
                    new CortexForkDigests(boundCustody.checkpointSHA256, boundCustody.tapeSpanlogSHA256,
                        boundCustody.curveSHA256, boundCustody.excursionsSHA256), generation, obligation, candidateBoundary,
                    successorState, outcome.ExecutedReadoutOccurrenceDigest,
                    successorOccurrenceCheckDigest, successorOccurrenceCheckCoverageDigest,
                    requireOrdinaryOutcome);
                byte[] encoded = metadata.Encode(domain);
                cortex.CurrentRun.WriteAtomic("policy-boundary.rail.ron", stream => stream.Write(encoded));
            });
    }

    internal static CortexForkArm<PolicyBoundaryTrialOutcome> CreateHomeostatBoundaryArm(
        string path, int step, int horizon, PolicyBoundaryArms armKind, CortexRunConfig config, CortexPolicyTrialAuthorityIdentity authorityIdentity, CortexPolicyAuthorities authority, bool forced = false, bool frozen = false,
        bool requireOrdinaryOutcome = false,
        CortexForkRailRoles railRole = CortexForkRailRoles.Unknown, string parentRunID = "", CortexForkMaterializationContract? materializationContract = null,
        PolicyBoundaryObligationID obligation = default, PolicyBoundaryRational candidateBoundary = default)
        => CreatePolicyBoundaryArm(path, step, horizon, armKind, config, HomeostatPolicyBoundaryDomain.Instance,
            authorityIdentity, authority, forced, frozen, requireOrdinaryOutcome, railRole, parentRunID,
            materializationContract, obligation, candidateBoundary);

    private void ValidatePolicyBoundaryMaterializationContracts(
        CortexForkSeed seed,
        in CortexPolicyTrialQuotaDecision funding,
        CortexForkArm<PolicyBoundaryTrialOutcome>[] baselineArms,
        CortexForkArm<PolicyBoundaryTrialOutcome>[] candidateArms,
        CortexForkArm<PolicyBoundaryTrialOutcome>[] forcedNullArms,
        CortexForkArm<PolicyBoundaryTrialOutcome>[] reflexArms)
    {
        string parentRunID = Path.GetFileName(CurrentRun.Dir);
        string attemptID = funding.QuotaDecisionID.ToString();
        foreach (CortexForkArm<PolicyBoundaryTrialOutcome> arm in baselineArms
            .Concat(candidateArms).Concat(forcedNullArms).Concat(reflexArms))
        {
            CortexForkRunner.ValidateMaterializationContract(this, seed, arm, Path.GetFullPath(arm.RunDirectory), requireContract: true);
            CortexForkMaterializationContract contract = arm.MaterializationContract!.Value;
            if (!string.Equals(contract.AttemptID, attemptID, StringComparison.Ordinal))
                throw new InvalidDataException("paid policy boundary arm materialization contract disagrees with its funding decision or parent");
        }
    }

    internal bool PolicyBoundaryAllowsProduction(CortexPolicyID policy, ReadOnlySpan<MetricSample> features)
        => ObservePolicyBoundaryGate(policy, features).AllowsProduction;

    internal PolicyBoundaryGateObservation ObservePolicyBoundaryGate(
        CortexPolicyID policy,
        ReadOnlySpan<MetricSample> features)
    {
        IPolicyBoundaryDomain? domain = null;
        if ((_policyBoundaryTrialOverride is PolicyBoundaryTrialOverride trialForDomain && trialForDomain.Policy.Equals(policy))
            || _policyBoundaryObligations.ContainsKey(policy))
            domain = RequirePolicyBoundaryDomain(policy);
        if (_policyBoundaryTrialOverride is PolicyBoundaryTrialOverride trial && trial.Policy.Equals(policy))
        {
            double observed = double.NaN;
            for (int i = 0; i < features.Length; i++)
                if (features[i].MetricID.Value == trial.FeatureID)
                {
                    observed = features[i].Value.Kind switch
                    {
                        NumericKinds.F64 => features[i].Value.GetF64(),
                        NumericKinds.I64 => features[i].Value.GetI64(),
                        NumericKinds.U64 => features[i].Value.GetU64(),
                        _ => double.NaN,
                    };
                    break;
                }
            bool satisfied = double.IsFinite(observed) && PolicyBoundaryRational.FromDouble(observed) <= trial.Boundary;
            return new(true, observed, trial.Boundary, PolicyBoundaryComparisons.LessThanOrEqual, satisfied);
        }
        if (!_policyBoundaryObligations.TryGetValue(policy, out PolicyBoundaryObligation? obligation))
            return default;
        _ = domain ?? throw new InvalidDataException($"no policy-boundary domain is registered for {policy}");
        if (!obligation.TryReadGuard(features, out PolicyBoundaryReadout readout))
            return new(true, double.NaN, default, PolicyBoundaryComparisons.Unknown, false);
        double observedValue = double.NaN;
        int featureID = int.TryParse(obligation.Identity.Feature, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed : -1;
        for (int i = 0; i < features.Length; i++)
            if (features[i].MetricID.Value == featureID)
            {
                observedValue = features[i].Value.Kind switch
                {
                    NumericKinds.F64 => features[i].Value.GetF64(),
                    NumericKinds.I64 => features[i].Value.GetI64(),
                    NumericKinds.U64 => features[i].Value.GetU64(),
                    _ => double.NaN,
                };
                break;
            }
        return new(true, observedValue, readout.Boundary, readout.Comparison, readout.CanActuate);
    }

    private static (int ChildDirectories, int MaterializationMarkers) CountPolicyBoundaryChildren(
        string parentDirectory,
        string attemptID)
    {
        string childrenDirectory = Path.Combine(parentDirectory, "children");
        if (!Directory.Exists(childrenDirectory)) return (0, 0);
        string parentRunID = Path.GetFileName(Path.GetFullPath(parentDirectory));
        int children = 0;
        int markers = 0;
        foreach (string child in Directory.GetDirectories(childrenDirectory))
        {
            string markerPath = Path.Combine(child, CortexForkMaterializationContract.MarkerFileName);
            if (!File.Exists(markerPath) || !TryReadPolicyBoundaryMaterializationContract(markerPath, child, out CortexForkMaterializationContract contract))
                continue;
            if (!string.Equals(contract.ParentRunID, parentRunID, StringComparison.Ordinal)
                || !string.Equals(contract.AttemptID, attemptID, StringComparison.Ordinal)) continue;
            children++;
            markers++;
        }
        return (children, markers);
    }

    /// A settled-terminal reconcile verdict is durable authority proven once per trial-state
    /// epoch: the first reconcile after any funding/settlement mutation re-runs the full durable
    /// verification (seed custody, checkpoint/tape/curve hashes, child receipts); the memo only
    /// replays that already-proven verdict while the in-memory trial state is unchanged, so the
    /// steady-state per-step reconcile is a dictionary lookup instead of a disk walk.  Cold and
    /// cross-process paths always re-prove — the memo is empty at process start and every restore
    /// path bumps the stamp.
    private int _policyTrialReconcileStamp;
    private readonly Dictionary<(ulong Fingerprint, string Horizons, bool RequireReceipt), SettledBoundaryReconcileVerdict> _settledBoundaryReconcileMemo = new();

    private readonly record struct SettledBoundaryReconcileVerdict(
        int Stamp,
        CortexPolicyTrialQuotaDecision Funding,
        PolicyBoundaryForkReceipt Receipt,
        CortexPolicyTrialCompletion Completion);

    private void InvalidatePolicyTrialReconcileMemo() => _policyTrialReconcileStamp++;

    /// Reconcile every durable Homeostat lease before consulting the transient boundary decision.
    /// A marker is only placement intent: terminal-run-receipt + terminal-verification + typed rail
    /// metadata are the terminal authority.  Incomplete generations remain on disk and the next
    /// retry uses the next role index; no cleanup or artifact deletion is permitted.
    private bool TryReconcilePaidHomeostatBoundaryTrials(
        in CortexPolicyReadoutReceipt readout,
        int[] horizons,
        out CortexPolicyTrialQuotaDecision pendingFunding,
        out bool terminalSettled)
        => TryReconcilePaidHomeostatBoundaryTrials(in readout, horizons, out pendingFunding, out terminalSettled,
            out _, out _, out _);

    private bool TryReconcilePaidHomeostatBoundaryTrials(
        in CortexPolicyReadoutReceipt readout,
        int[] horizons,
        out CortexPolicyTrialQuotaDecision pendingFunding,
        out bool terminalSettled,
        out CortexPolicyTrialQuotaDecision recoveredFunding,
        out PolicyBoundaryForkReceipt recoveredReceipt,
        out CortexPolicyTrialCompletion recoveredSettlement,
        bool requireReceipt = true)
    {
        pendingFunding = default;
        terminalSettled = false;
        recoveredFunding = default;
        recoveredReceipt = default;
        recoveredSettlement = default;
        IPolicyBoundaryDomain domain = RequirePolicyBoundaryDomain(HomeostatPolicyBoundaryDomain.Instance.PolicyID);
        CortexPolicyTrialAllocation allocation = ReadPolicyTrialAllocation(domain.PolicyID);
        if (!allocation.IsPresent || allocation.Authority != CortexPolicyAuthorities.Grammar)
            return false;
        (ulong, string, bool) memoKey = (readout.Fingerprint, string.Join(',', horizons), requireReceipt);
        if (_settledBoundaryReconcileMemo.TryGetValue(memoKey, out SettledBoundaryReconcileVerdict verdict)
            && verdict.Stamp == _policyTrialReconcileStamp)
        {
            pendingFunding = verdict.Funding;
            recoveredFunding = verdict.Funding;
            recoveredReceipt = verdict.Receipt;
            recoveredSettlement = verdict.Completion;
            terminalSettled = true;
            return true;
        }
        bool restrictToReadout = readout.Fingerprint != 0;
        bool liveBoundaryReceipt = _policyBoundaryObligations.ContainsKey(domain.PolicyID);
        HashSet<CortexPolicyQuotaDecisionID> seen = new();
        // A prior recovery may have settled the lease just before the parent process died.  If
        // its terminal generation is still present, treat the next resume as a no-op rather than
        // minting a new funding identity for the same obligation.
        foreach (CortexPolicyTrialCompletion settlement in _policyTrialCompletions)
        {
            if (!_policyTrialQuotaByID.TryGetValue(settlement.QuotaDecisionID, out CortexPolicyTrialQuotaDecision settledFunding)
                || !settledFunding.Policy.Equals(domain.PolicyID)
                || restrictToReadout && settledFunding.ReadoutFingerprint != readout.Fingerprint
                || settledFunding.RequestedHorizonSteps != horizons[^1]) continue;
            if (TryReadTerminalHomeostatBoundaryGeneration(settledFunding, settledFunding.ReadoutFingerprint, horizons,
                    out long settledActual, out string settledGenerationDigest, out PolicyBoundaryForkReceipt settledReceipt,
                    requireReceipt && liveBoundaryReceipt))
            {
                string settlementCustodyPath = Path.Combine(CurrentRun.Dir, PolicyBoundarySeedCustodyDirectory,
                    settledFunding.QuotaDecisionID.ToString(), "settlement-custody.ron");
                bool settlementCustodyPresent = File.Exists(settlementCustodyPath);
                if (!TryReadPolicyBoundarySettlementCustody(in settledFunding, out PolicyBoundarySettlementCustody settledCustody))
                {
                    if (settlementCustodyPresent
                        || settlement.ActualExecutedArmSteps != settledActual
                        || settlement.ReclaimedOrUnused != checked(settledFunding.PlannedArmSteps - settledActual)
                        || settlement.VerifierOutcome != CortexPolicyVerifierOutcomes.Passed
                        || requireReceipt && liveBoundaryReceipt && !settledReceipt.QuotaDecisionID.Equals(settledFunding.QuotaDecisionID))
                        throw new InvalidDataException($"settled policy trial {settledFunding.QuotaDecisionID} has invalid generation custody");
                    // A process may have persisted the Passed settlement after the
                    // custody sidecar write but before the sidecar became visible.
                    // Recover that narrow transaction only after the terminal rails
                    // and accounting independently authenticate the exact generation.
                    WritePolicyBoundarySettlementCustody(in settledFunding, settledGenerationDigest, settledActual);
                    FlushPolicyJournalBuffer();
                    if (!TryReadPolicyBoundarySettlementCustody(in settledFunding, out settledCustody))
                        throw new InvalidDataException($"settled policy trial {settledFunding.QuotaDecisionID} could not repair its generation custody");
                }
                if (settledCustody.generationDigest != settledGenerationDigest
                    || settledCustody.actualExecutedArmSteps != settledActual
                    || settlement.ActualExecutedArmSteps != settledActual
                    || settlement.ReclaimedOrUnused != checked(settledFunding.PlannedArmSteps - settledActual)
                    || settlement.VerifierOutcome != CortexPolicyVerifierOutcomes.Passed
                    || requireReceipt && liveBoundaryReceipt && !settledReceipt.QuotaDecisionID.Equals(settledFunding.QuotaDecisionID))
                    throw new InvalidDataException($"settled policy trial {settledFunding.QuotaDecisionID} lacks its generation custody");
                if (!EnsurePolicyTrialQuotaPredecessor(in settledFunding,
                        out CortexPolicyTrialQuotaDecision settledTapeFunding))
                    throw new InvalidDataException($"settled policy trial {settledFunding.QuotaDecisionID} lacks its authenticated funding predecessor");
                EnsurePolicyTrialCompletionDurable(in settlement);
                pendingFunding = settledFunding;
                recoveredFunding = settledTapeFunding;
                recoveredReceipt = settledReceipt;
                recoveredSettlement = settlement;
                terminalSettled = true;
                _settledBoundaryReconcileMemo[memoKey] = new SettledBoundaryReconcileVerdict(
                    _policyTrialReconcileStamp, settledTapeFunding, settledReceipt, settlement);
                return true;
            }
            if (settlement.VerifierOutcome == CortexPolicyVerifierOutcomes.Passed)
                throw new InvalidDataException($"settled policy trial {settlement.QuotaDecisionID} lacks a complete authenticated terminal generation");
        }
        bool hasPendingFunding = false;
        CortexPolicyTrialQuotaDecision firstPendingFunding = default;
        foreach (CortexPolicyTrialQuotaDecision funding in _policyTrialQuotaDecisions)
        {
            if (funding.Decision != CortexPolicyQuotaDecisions.Paid
                || !funding.Policy.Equals(domain.PolicyID)
                || !seen.Add(funding.QuotaDecisionID)
                || _policyTrialCompletionByID.ContainsKey(funding.QuotaDecisionID)) continue;
            if (restrictToReadout && funding.ReadoutFingerprint != readout.Fingerprint
                || funding.RequestedHorizonSteps != horizons[^1]
                || funding.ArmCount != 4
                || funding.PlannedArmSteps != checked((long)horizons[^1] * 4)
                || funding.HeldArmSteps != funding.PlannedArmSteps
                || funding.AllocationArmSteps != allocation.ArmSteps
                || !string.Equals(funding.AllocationIdentity, allocation.Identity, StringComparison.Ordinal)
                || !string.Equals(funding.AllocationDigest, allocation.Digest, StringComparison.Ordinal))
                throw new InvalidDataException($"paid Homeostat policy trial {funding.QuotaDecisionID} disagrees with its durable allocation identity");
            if (TryReadTerminalHomeostatBoundaryGeneration(funding, funding.ReadoutFingerprint, horizons, out long actual, out string generationDigest, out PolicyBoundaryForkReceipt receipt, requireReceipt))
            {
                if (!EnsurePolicyTrialQuotaPredecessor(in funding,
                        out CortexPolicyTrialQuotaDecision tapeFunding))
                    throw new InvalidDataException($"paid policy trial {funding.QuotaDecisionID} lacks its authenticated funding predecessor");
                CortexPolicyTrialCompletion settlement = SettleAuthenticatedPolicyBoundary(in funding, horizons,
                    out receipt);
                pendingFunding = funding;
                recoveredFunding = tapeFunding;
                recoveredReceipt = receipt;
                recoveredSettlement = settlement;
                Trace.Cortex.Boundary("policy.trial-recovered-terminal",
                    $"id={funding.QuotaDecisionID} actual={settlement.ActualExecutedArmSteps} refund={settlement.ReclaimedOrUnused}");
                terminalSettled = true;
                _settledBoundaryReconcileMemo[memoKey] = new SettledBoundaryReconcileVerdict(
                    _policyTrialReconcileStamp, tapeFunding, receipt, settlement);
                return true;
            }
            if (hasPendingFunding)
                throw new InvalidDataException(
                    $"Homeostat policy recovery found multiple pending paid trials for one readout: {firstPendingFunding.QuotaDecisionID} and {funding.QuotaDecisionID}");
            firstPendingFunding = funding;
            hasPendingFunding = true;
        }
        if (hasPendingFunding)
        {
            pendingFunding = firstPendingFunding;
            return true;
        }
        return terminalSettled;
    }

    private static bool TryVerifyForcedTrialOverrideEvent(
        string childDirectory,
        PolicyBoundaryRailMetadataDocument rail,
        ulong sourceCandidateFingerprint,
        IPolicyBoundaryDomain domain)
    {
        if (rail.ordinaryOutcomeRequired
            && (rail.arm != PolicyBoundaryArms.ForcedDivergentNull
                || rail.executionOutcome != CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted
                || rail.executedOutcomeEventID <= 0
                || rail.executedOutcomePayloadSHA256.Length != 64
                || rail.executedOutcomePayloadSHA256.Any(static c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))))
            return false;
        if (!rail.ordinaryOutcomeRequired
            && (rail.executedOutcomeEventID != 0 || rail.executedOutcomePayloadSHA256.Length != 0))
            return false;
        if (rail.executedSelectionCause != CortexPolicySelectionCauses.TrialOverride
            || rail.executedDecisionID == 0 || rail.executedDecisionEventID <= 0
            || rail.forcedDivergenceSeed == 0
            || sourceCandidateFingerprint == 0
            || rail.forcedDivergenceSeed != (sourceCandidateFingerprint ^ 0x9E3779B97F4A7C15UL))
            return false;
        using CortexPolicyOccurrenceCheckBundle bundle = new(childDirectory);
        Tape tape;
        try { tape = bundle.Tape; }
        catch (Exception error) when (error is InvalidDataException or IOException) { return false; }
        TapeEventID eventID = new(rail.executedDecisionEventID);
        if (!tape.TryGetEventView(eventID, out TapeEventView view)
            || !string.Equals(view.Source, domain.PolicyBinding.PolicyPacketSource, StringComparison.Ordinal)
            || view.Provenance != Provenances.Execution
            || !tape.Resolve(eventID, out byte[] payload))
            return false;
        CortexPolicyDecisionPacket packet;
        try { packet = TapePacketCreator.DecodePolicyDecision(payload); }
        catch (InvalidDataException) { return false; }
        if (packet.DecisionID.Value != rail.executedDecisionID
            || packet.Readout.GrammarRevision.Value != rail.executedReadoutRevision
            || packet.Readout.ReadoutCandidateFingerprint != rail.executedCandidateFingerprint
            || packet.Readout.ReadoutCandidateOccurrenceDigest != rail.executedReadoutOccurrenceDigest
            || packet.Readout.SelectionCause != CortexPolicySelectionCauses.TrialOverride
            || packet.Readout.Authority != CortexPolicyAuthorities.Grammar
            || packet.Readout.SelectedCandidateAction != packet.Readout.ExecutedAction
            || packet.Readout.SelectedCandidateAction == packet.Readout.RawCandidateAction)
            return false;
        CortexPolicyOutcomePacket outcomePacket = default;
        byte[]? outcomePayload = null;
        if (rail.ordinaryOutcomeRequired)
        {
            if (rail.executedOutcomeEventID <= 0 || rail.executedOutcomePayloadSHA256.Length != 64)
                return false;
            TapeEventID outcomeEventID = new(rail.executedOutcomeEventID);
            int outcomeCount = 0;
            foreach (TapeEventView outcomeView in tape.GetEventViews())
            {
                if (!string.Equals(outcomeView.Source, domain.PolicyBinding.PolicyPacketSource, StringComparison.Ordinal)
                    || outcomeView.Provenance != Provenances.Execution
                    || !tape.Resolve(outcomeView.Id, out byte[] candidatePayload)
                    || !TapePacketCreator.TryDecodePolicyOutcome(candidatePayload, out CortexPolicyOutcomePacket candidateOutcome)
                    || !candidateOutcome.DecisionID.Equals(packet.DecisionID)) continue;
                outcomeCount++;
                if (outcomeView.Id == outcomeEventID)
                {
                    outcomePacket = candidateOutcome;
                    outcomePayload = candidatePayload;
                }
            }
            if (outcomeCount != 1 || outcomePayload is null
                || !string.Equals(TapePacketCreator.DigestPolicyOutcomePayload(outcomePayload), rail.executedOutcomePayloadSHA256, StringComparison.Ordinal))
                return false;
            outcomePacket.Validate(domain.Schema);
            if (outcomePacket.Outcomes.Length != 2 || outcomePacket.Outcomes[0].MetricID.Value != 500 || outcomePacket.Outcomes[1].MetricID.Value != 501)
                return false;
        }
        if (rail.ordinaryOutcomeRequired && !RunAuthority.LoadIdentity(childDirectory).Complete)
            return false;
        string eventText = "s" + rail.executedDecisionEventID.ToString(CultureInfo.InvariantCulture);
        foreach (string line in bundle.DecisionReceiptLines.Skip(1))
        {
            string[] columns = line.Split('\t');
            if (columns.Length == 14 && string.Equals(columns[1], eventText, StringComparison.Ordinal)
                && string.Equals(columns[2], rail.executedDecisionID.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                if (!rail.ordinaryOutcomeRequired)
                    return true;
                int journalMatches = 0;
                string outcomeEventText = new TapeEventID(rail.executedOutcomeEventID).ToString();
                foreach (string journalLine in bundle.JournalLines)
                {
                    string[] journalColumns = journalLine.Split('\t');
                    if (journalColumns.Length < 4 || journalColumns[1] != "policy-outcome"
                        || journalColumns[2] != outcomeEventText
                        || journalColumns[3] != domain.PolicyBinding.PolicyPacketSource)
                        continue;
                    if (outcomePayload is null
                        || !TapePacketCreator.TryReadPolicyOutcomeJournalRow(journalLine,
                            new(rail.executedOutcomeEventID), domain.PolicyBinding.PolicyPacketSource,
                            in outcomePacket, rail.executedOutcomePayloadSHA256, outcomePayload.Length, out _))
                        return false;
                    journalMatches++;
                }
                return journalMatches == 1;
            }
        }
        return false;
    }

    private static bool TryVerifySuccessorScopeEvidence(
        string childDirectory,
        PolicyBoundaryRailMetadataDocument rail,
        IPolicyBoundaryDomain domain)
    {
        if (rail.successorOccurrenceCheckDigest.Length != 64
            || rail.successorCanonicalPolicy != domain.PolicyID.Value
            || rail.successorCanonicalKind != (byte)domain.CanonicalStateKind
            || rail.successorCanonicalVersion == 0
            || rail.successorSupportDigest == 0
            || rail.successorOccurrenceCheckCoverageDigest.Length != 64)
            return false;
        if (rail.executedCanonicalPolicy != rail.successorCanonicalPolicy
            || rail.executedCanonicalKind != rail.successorCanonicalKind
            || rail.executedCanonicalVersion != rail.successorCanonicalVersion
            || rail.executedCanonicalValue != rail.successorCanonicalValue)
            return false;
        if (!TryVerifySuccessorScopeTapeReceipt(childDirectory, rail, domain))
            return false;
        string path = Path.Combine(childDirectory, PolicyOccurrenceCheckReceiptFile);
        if (!File.Exists(path) || ComputePolicyBoundaryFileSHA256(path) != rail.successorOccurrenceCheckDigest)
            return false;
        string[] lines;
        try { lines = File.ReadAllLines(path, Encoding.UTF8); }
        catch (IOException) { return false; }
        if (lines.Length == 0 || !lines[0].TrimStart('\uFEFF').Equals(PolicyOccurrenceCheckReceiptHeader, StringComparison.Ordinal))
            return false;
        bool verificationRowFound = false;
        for (int i = 1; i < lines.Length; i++)
        {
            string[] columns = lines[i].Split('\t');
            if (columns.Length != 19 || columns[0] != domain.PolicyID.Value
                || !ulong.TryParse(columns[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong readout)
                || !ulong.TryParse(columns[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong candidate)
                || !ulong.TryParse(columns[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong revision)
                || columns[7] != "1"
                || columns[15] != rail.successorCanonicalPolicy
                || !byte.TryParse(columns[16], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte kind)
                || !ushort.TryParse(columns[17], NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort version)
                || !ulong.TryParse(columns[18], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong value))
                continue;
            if (readout == rail.executedReadoutFingerprint
                && candidate == rail.executedCandidateFingerprint
                && revision == rail.executedReadoutRevision
                && kind == rail.successorCanonicalKind
                && version == rail.successorCanonicalVersion
                && value == rail.successorCanonicalValue)
                verificationRowFound = true;
        }
        if (!verificationRowFound)
            return false;
        string coveragePath = Path.Combine(childDirectory, PolicyOccurrenceCheckCoverageReceiptFile);
        if (!File.Exists(coveragePath) || ComputePolicyBoundaryFileSHA256(coveragePath) != rail.successorOccurrenceCheckCoverageDigest)
            return false;
        try { lines = File.ReadAllLines(coveragePath, Encoding.UTF8); }
        catch (IOException) { return false; }
        if (lines.Length == 0 || !lines[0].TrimStart('\uFEFF').Equals(PolicyOccurrenceCheckCoverageReceiptHeader, StringComparison.Ordinal))
            return false;
        for (int i = 1; i < lines.Length; i++)
        {
            string[] columns = lines[i].Split('\t');
            if (columns.Length != 26 || columns[0] != domain.PolicyID.Value
                || !ulong.TryParse(columns[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong readout)
                || !byte.TryParse(columns[13], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte kind)
                || !ushort.TryParse(columns[14], NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort version)
                || !ulong.TryParse(columns[15], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong value)
                || columns[16] != "1"
                || !ulong.TryParse(columns[18], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong candidate)
                || !ulong.TryParse(columns[19], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong support)
                || !ulong.TryParse(columns[20], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong revision))
                continue;
            if (readout == rail.executedReadoutFingerprint
                && candidate == rail.executedCandidateFingerprint
                && support == rail.successorSupportDigest
                && revision == rail.executedReadoutRevision
                && kind == rail.successorCanonicalKind
                && version == rail.successorCanonicalVersion
                && value == rail.successorCanonicalValue)
                return true;
        }
        return false;
    }

    private static bool TryVerifySuccessorScopeTapeReceipt(
        string childDirectory,
        PolicyBoundaryRailMetadataDocument rail,
        IPolicyBoundaryDomain domain)
    {
        using CortexPolicyOccurrenceCheckBundle bundle = new(childDirectory);
        Tape tape;
        try { tape = bundle.Tape; }
        catch (Exception error) when (error is InvalidDataException or IOException) { return false; }
        foreach (TapeEventView view in tape.GetEventViews())
        {
            if (view.Provenance != Provenances.Execution
                || !string.Equals(view.Source, domain.PolicyBinding.PolicyPacketSource, StringComparison.Ordinal)
                || !tape.Resolve(view.Id, out byte[] payload)) continue;
            string text = Encoding.UTF8.GetString(payload);
            if (!text.StartsWith("POLICY-VERIFICATION-SCOPE\t", StringComparison.Ordinal)) continue;
            Dictionary<string, string> fields = new(StringComparer.Ordinal);
            string[] parts = text.Split('\t');
            bool valid = true;
            for (int i = 1; i < parts.Length; i++)
            {
                int separator = parts[i].IndexOf('=');
                if (separator <= 0 || !fields.TryAdd(parts[i][..separator], parts[i][(separator + 1)..]))
                {
                    valid = false;
                    break;
                }
            }
            if (!valid || fields.GetValueOrDefault("policy") != domain.PolicyID.Value
                || !ulong.TryParse(fields.GetValueOrDefault("readout"), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong readout)
                || !ulong.TryParse(fields.GetValueOrDefault("candidate"), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong candidate)
                || !ulong.TryParse(fields.GetValueOrDefault("support"), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong support)
                || !ulong.TryParse(fields.GetValueOrDefault("revision"), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong revision)
                || !byte.TryParse(fields.GetValueOrDefault("state_kind"), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte kind)
                || !ushort.TryParse(fields.GetValueOrDefault("state_version"), NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort version)
                || !ulong.TryParse(fields.GetValueOrDefault("state_value"), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong value))
                continue;
            if (readout == rail.executedReadoutFingerprint
                && candidate == rail.executedCandidateFingerprint
                && support == rail.successorSupportDigest
                && revision == rail.executedReadoutRevision
                && fields.GetValueOrDefault("state_policy") == rail.successorCanonicalPolicy
                && kind == rail.successorCanonicalKind
                && version == rail.successorCanonicalVersion
                && value == rail.successorCanonicalValue)
                return true;
        }
        return false;
    }

    private bool TryReadTerminalHomeostatBoundaryGeneration(
        in CortexPolicyTrialQuotaDecision funding,
        ulong readoutFingerprint,
        int[] horizons,
        out long actual,
        out string generationDigest,
        out PolicyBoundaryForkReceipt receipt,
        bool requireReceipt = true)
    {
        LoopClosurePolicyBinding policy = RequirePolicyBoundaryDomain(funding.Policy).PolicyBinding;
        return TryReadTerminalPolicyBoundaryGeneration(in policy, in funding,
            readoutFingerprint, horizons, out actual, out generationDigest, out receipt,
            out _, out _, out _, requireReceipt);
    }

    private bool TryReadTerminalPolicyBoundaryGeneration(
        in LoopClosurePolicyBinding policy,
        in CortexPolicyTrialQuotaDecision funding,
        ulong readoutFingerprint,
        int[] horizons,
        out long actual,
        out string generationDigest,
        out PolicyBoundaryForkReceipt receipt,
        bool requireReceipt = true)
        => TryReadTerminalPolicyBoundaryGeneration(in policy, in funding, readoutFingerprint, horizons,
            out actual, out generationDigest, out receipt, out _, out _, out _, requireReceipt);

    private bool TryReadTerminalHomeostatBoundaryGeneration(
        in CortexPolicyTrialQuotaDecision funding,
        ulong readoutFingerprint,
        int[] horizons,
        out long actual,
        out string generationDigest,
        out PolicyBoundaryForkReceipt receipt,
        out PolicyBoundaryGenerationStates generationState,
        out string generationStateReason,
        out PolicyBoundaryGenerationCensus generationCensus,
        bool requireReceipt = true)
    {
        LoopClosurePolicyBinding policy = RequirePolicyBoundaryDomain(funding.Policy).PolicyBinding;
        return TryReadTerminalPolicyBoundaryGeneration(in policy, in funding,
            readoutFingerprint, horizons, out actual, out generationDigest, out receipt,
            out generationState, out generationStateReason, out generationCensus, requireReceipt);
    }

    private bool TryReadTerminalPolicyBoundaryGeneration(
        in LoopClosurePolicyBinding policy,
        in CortexPolicyTrialQuotaDecision funding,
        ulong readoutFingerprint,
        int[] horizons,
        out long actual,
        out string generationDigest,
        out PolicyBoundaryForkReceipt receipt,
        out PolicyBoundaryGenerationStates generationState,
        out string generationStateReason,
        out PolicyBoundaryGenerationCensus generationCensus,
        bool requireReceipt = true)
    {
        policy.Validate();
        IPolicyBoundaryDomain domain = RequirePolicyBoundaryDomain(funding.Policy);
        actual = 0;
        generationDigest = "";
        receipt = default;
        generationState = PolicyBoundaryGenerationStates.Incomplete;
        generationStateReason = "no-complete-generation";
        generationCensus = default;
        if (!policy.MatchesPolicy(funding.Policy)) return false;
        try { ValidatePolicyBoundarySeedCustody(in funding, domain); }
        catch (Exception error) when (error is InvalidDataException or IOException or ArgumentException)
        {
            Trace.Cortex.Boundary("policy.boundary.terminal-reject",
                $"funding={funding.QuotaDecisionID} reason=validate message={error.Message}");
            throw;
        }
        if (!TryLoadPolicyBoundarySeed(in funding, out CortexForkSeed seed))
        {
            Trace.Cortex.Boundary("policy.boundary.terminal-reject", $"funding={funding.QuotaDecisionID} reason=seed-load");
            return false;
        }
        if (!TryReadPolicyBoundarySeedCustodyDocument(CurrentRun.Dir, Path.GetFileName(CurrentRun.Dir),
                funding.QuotaDecisionID.ToString(), out PolicyBoundarySeedCustody custody))
        {
            Trace.Cortex.Boundary("policy.boundary.terminal-reject", $"funding={funding.QuotaDecisionID} reason=custody-document");
            return false;
        }
        if (!TryDecodeCanonicalState(custody.canonicalState, out PolicyCanonicalStateID custodyState)
            || !custodyState.Policy.Equals(domain.PolicyID))
            return false;
        // Terminal aggregation must authenticate the same source lease that
        // custody records.  A valid sidecar alone cannot make a deleted or
        // conflicting root Paid row executable.
        CortexPolicyPendingForcedTrialIntent sourceLease = new(
            domain.PolicyID, funding.QuotaDecisionID.Value, custody.sourceFundingDecision, 1,
            custody.sourceDecisionID, custody.sourceDecisionEventID, custody.sourceCorroborationEventID,
            custody.sourceSupportDigest, custody.sourceCandidateFingerprint, custody.candidateFingerprint,
            custody.readoutFingerprint, new GrammarRevisionID(custody.candidateRevision), custodyState,
            custody.readoutFingerprint, custody.candidateFingerprint, new GrammarRevisionID(custody.candidateRevision),
            custody.sourceSupportDigest, custodyState, "terminal", (byte)PolicyBoundaryArms.ForcedDivergentNull,
            domain.BoundaryFeatureID, custody.sourceRunID,
            custody.custodyDigest);
        bool rootFundingJoined = TryReadRootPolicyFundingDecision(CurrentRun.Dir, custody.sourceRunID, in sourceLease, in custody,
            out CortexPolicyTrialQuotaDecision rootFunding, out _)
            && rootFunding.QuotaDecisionID.Equals(funding.QuotaDecisionID)
            && rootFunding.Decision == custody.sourceFundingDecision
            && rootFunding.CandidateFingerprint == funding.CandidateFingerprint
            && rootFunding.ReadoutFingerprint == funding.ReadoutFingerprint
            && rootFunding.CandidateRevision == funding.CandidateRevision
            && rootFunding.QuotaStep == custody.fundingStep
            && string.Equals(rootFunding.SeedAuditOnlyDigest, custody.custodyDigest, StringComparison.Ordinal);
        if (!rootFundingJoined)
        {
            Trace.Cortex.Boundary("policy.boundary.root-funding-reject",
                $"id={funding.QuotaDecisionID} source={custody.sourceRunID} root={CurrentRun.Dir} root_id={(rootFunding.QuotaDecisionID.Value == 0 ? "none" : rootFunding.QuotaDecisionID.ToString())}");
            return false;
        }
        string parentDirectory = CurrentRun.Dir;
        string childrenDirectory = Path.Combine(parentDirectory, "children");
        if (!Directory.Exists(childrenDirectory))
        {
            Trace.Cortex.Boundary("policy.boundary.terminal-reject", $"funding={funding.QuotaDecisionID} reason=children-missing path={childrenDirectory}");
            return false;
        }
        string parentRunID = Path.GetFileName(Path.GetFullPath(parentDirectory));
        HashSet<int> registeredHorizons = horizons.ToHashSet();
        Dictionary<int, Dictionary<int, Dictionary<PolicyBoundaryArms, (long Spend, string Evidence, string Tuple, PolicyBoundaryRailMetadataDocument Rail, CortexForkTerminalRunReceipt Terminal)>>> generations = new();
        int childDirectories = 0;
        int materializationMarkers = 0;
        int completeRails = 0;
        int invalidArtifacts = 0;
        string firstInvalidReason = "";
        int nextGeneration = 0;
        foreach (string child in Directory.GetDirectories(childrenDirectory))
        {
            string markerPath = Path.Combine(child, CortexForkMaterializationContract.MarkerFileName);
            if (!File.Exists(markerPath)) continue;
            if (!TryReadPolicyBoundaryMaterializationContract(markerPath, child, out CortexForkMaterializationContract contract))
            {
                invalidArtifacts++;
                if (firstInvalidReason.Length == 0) firstInvalidReason = "marker";
                Trace.Cortex.Boundary("policy.boundary.terminal-reject",
                    $"funding={funding.QuotaDecisionID} child={Path.GetFileName(child)} reason=marker");
                continue;
            }
            if (!string.Equals(contract.ParentRunID, parentRunID, StringComparison.Ordinal)
                || !string.Equals(contract.AttemptID, funding.QuotaDecisionID.ToString(), StringComparison.Ordinal)) continue;
            childDirectories++;
            materializationMarkers++;
            nextGeneration = Math.Max(nextGeneration, ParsePolicyBoundaryChildIndex(Path.GetFileName(child)) + 1);
            string railPath = Path.Combine(child, "policy-boundary.rail.ron");
            string verificationPath = Path.Combine(child, "terminal-verification.ron");
            if (!File.Exists(railPath) || !File.Exists(verificationPath)
                || !File.Exists(Path.Combine(child, CortexForkTerminalRunReceipt.FileName))) continue;
            try
            {
                PolicyBoundaryRailMetadataDocument rail = RonSerializer.Deserialize<PolicyBoundaryRailMetadataDocument>(File.ReadAllBytes(railPath));
                CortexPolicyTrialExecutionOutcomes executionOutcome = PolicyBoundaryRailMetadata.ResolveExecutionOutcome(rail);
                bool accountingValid = Enum.IsDefined(executionOutcome) && rail.requestCount >= 0 && rail.guardAdmittedCount >= 0
                    && rail.guardAdmittedCount <= rail.requestCount
                    && (executionOutcome != CortexPolicyTrialExecutionOutcomes.NotAttempted || rail.requestCount == 0 && rail.guardAdmittedCount == 0)
                    && (executionOutcome != CortexPolicyTrialExecutionOutcomes.GuardDenied || rail.requestCount > 0 && rail.guardAdmittedCount == 0)
                    && (rail.requestCount == 0
                        ? rail.lastRequestDecisionID == 0 && rail.lastRequestStep == -1
                        : rail.lastRequestDecisionID != 0 && rail.lastRequestStep >= 0);
                bool executionIdentityRequired = executionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted
                    || rail.guardAdmittedCount > 0;
                bool launchpadExecution = rail.executedSelectionCause == CortexPolicySelectionCauses.Launchpad;
                bool executionCandidateIdentityValid = launchpadExecution
                    ? rail.executedReadoutOccurrenceDigest == 0 && rail.executedCandidateFingerprint == 0
                    : rail.executedReadoutOccurrenceDigest != 0 && rail.executedCandidateFingerprint != 0;
                bool noExecutionIdentity = rail.executedDecisionID == 0 && rail.executedStep == -1 && rail.executedLaunchpadAction == -1
                    && rail.executedRawCandidateAction == -1 && rail.executedSelectedCandidateAction == -1 && rail.executedAction == -1
                    && rail.executedAuthority == CortexPolicyAuthorities.Launchpad && rail.executedSelectionCause == CortexPolicySelectionCauses.Launchpad
                    && rail.executedReadoutFingerprint == 0 && rail.executedReadoutRevision == 0
                    && rail.executedReadoutOccurrenceDigest == 0 && rail.executedCandidateFingerprint == 0
                    && rail.executedCanonicalPolicy.Length == 0 && rail.executedCanonicalVersion == 0 && rail.executedCanonicalValue == 0;
                bool executionIdentityValid = executionIdentityRequired
                    ? rail.schemaVersion == 11 && rail.executedDecisionID != 0 && rail.executedStep >= rail.sourceNextStep && rail.executedStep <= rail.terminalStep
                        && rail.executedReadoutFingerprint != 0 && rail.executedReadoutRevision != 0
                        && executionCandidateIdentityValid
                        && rail.executedCanonicalPolicy == domain.PolicyID.Value
                        && rail.executedCanonicalKind == (byte)domain.CanonicalStateKind
                        && rail.executedCanonicalVersion != 0
                    : noExecutionIdentity;
                string rejectionReason = rail.schemaVersion != 11 ? "schema" :
                    rail.readoutFingerprint != readoutFingerprint ? "readout" :
                    rail.readoutFingerprint != funding.ReadoutFingerprint ? "funding-readout-fingerprint" :
                    rail.arm == PolicyBoundaryArms.Candidate
                        && executionIdentityRequired && rail.executedCandidateFingerprint != funding.CandidateFingerprint ? "funding-candidate-fingerprint" :
                    rail.materializationParentRunID != parentRunID ? "parent" :
                    rail.materializationAttemptID != contract.AttemptID ? "attempt" :
                    rail.materializationChildRunID != contract.ChildRunID ? "child" :
                    rail.materializationColdSeedDigest != contract.ColdSeedDigest ? "contract-cold" :
                    rail.materializationColdSeedDigest != custody.coldSeedDigest ? "custody-cold" :
                    !registeredHorizons.Contains(rail.horizon) ? "horizon" :
                    !rail.continuityExact ? "continuity" :
                    !accountingValid ? "execution-accounting" :
                    rail.requestCount > 0 && rail.lastRequestRevision == 0 ? "zero-request-revision" :
                    !executionIdentityValid ? "executed-identity" :
                    executionIdentityRequired && rail.executedReadoutRevision < funding.CandidateRevision.Value ? "executed-revision-rewind" :
                    executionIdentityRequired && rail.executedLaunchpadAction < 0 ? "executed-launchpad-action" :
                    executionIdentityRequired && (rail.executedRawCandidateAction == -1) != (rail.executedSelectedCandidateAction == -1) ? "executed-candidate-presence" :
                    executionIdentityRequired && !Enum.IsDefined(rail.executedAuthority) ? "executed-authority" :
                    executionIdentityRequired && !Enum.IsDefined(rail.executedSelectionCause) ? "executed-selection-cause" :
                    _loopLineageEnabled && rail.arm == PolicyBoundaryArms.ForcedDivergentNull
                        && rail.horizon == horizons[^1] && !rail.ordinaryOutcomeRequired ? "terminal-ordinary-outcome-required" :
                    !PolicyBoundaryRailMetadata.IsOrdinaryOutcomeMarkerValid(rail, executionOutcome) ? "ordinary-outcome-marker" :
                    rail.arm == PolicyBoundaryArms.ForcedDivergentNull && rail.ordinaryOutcomeRequired && executionIdentityRequired
                        && (rail.executedOutcomeEventID <= 0 || rail.executedOutcomePayloadSHA256.Length != 64) ? "ordinary-outcome-custody" :
                    rail.arm == PolicyBoundaryArms.ForcedDivergentNull && executionIdentityRequired
                        && !TryVerifyForcedTrialOverrideEvent(child, rail, funding.CandidateFingerprint, domain) ? "forced-execution-event" :
                    rail.arm == PolicyBoundaryArms.ForcedDivergentNull && executionIdentityRequired
                        && !TryVerifySuccessorScopeEvidence(child, rail, domain) ? "successor-scope-evidence" :
                    custody.schemaVersion >= 6 && rail.obligationID != custody.obligation ? "custody-obligation" :
                    custody.schemaVersion >= 6 && rail.candidateBoundary != custody.boundary ? "custody-boundary" :
                    rail.sourceRunID != custody.sourceRunID ? "source-run" :
                    rail.sourceNextStep != seed.NextStep ? "source-next" :
                    rail.custodyDigest != custody.custodyDigest ? "custody-digest" :
                    rail.sourceCheckpointSHA256 != custody.checkpointSHA256 ? "checkpoint" :
                    rail.sourceTapeSpanlogSHA256 != custody.tapeSpanlogSHA256 ? "tape" :
                    rail.sourceCurveSHA256 != custody.curveSHA256 ? "curve" :
                    rail.sourceExcursionsSHA256 != custody.excursionsSHA256 ? "excursions" :
                    rail.generation != ParsePolicyBoundaryChildIndex(Path.GetFileName(child)) ? "generation" : "";
                if (rejectionReason.Length == 0 && requireReceipt && string.IsNullOrWhiteSpace(rail.obligationID))
                    rejectionReason = "obligation";
                if (rejectionReason.Length == 0 && requireReceipt && string.IsNullOrWhiteSpace(rail.candidateBoundary))
                    rejectionReason = "candidate-boundary";
                if (rejectionReason.Length != 0)
                {
                    invalidArtifacts++;
                    if (firstInvalidReason.Length == 0) firstInvalidReason = rejectionReason;
                    Trace.Cortex.Boundary("policy.boundary.terminal-reject",
                        $"funding={funding.QuotaDecisionID} child={Path.GetFileName(child)} arm={rail.arm} reason={rejectionReason} attempt={contract.AttemptID}");
                    continue;
                }
                CortexForkRailRoles expectedRole = rail.arm switch
                {
                    PolicyBoundaryArms.Baseline => CortexForkRailRoles.Baseline,
                    PolicyBoundaryArms.Candidate => CortexForkRailRoles.Candidate,
                    PolicyBoundaryArms.ForcedDivergentNull => CortexForkRailRoles.ForcedNull,
                    PolicyBoundaryArms.ReflexFrozenControl => CortexForkRailRoles.ReflexFrozen,
                    _ => CortexForkRailRoles.Unknown,
                };
                if (expectedRole == CortexForkRailRoles.Unknown || rail.railRole != expectedRole)
                {
                    invalidArtifacts++;
                    if (firstInvalidReason.Length == 0) firstInvalidReason = "rail-role";
                    Trace.Cortex.Boundary("policy.boundary.terminal-reject",
                        $"funding={funding.QuotaDecisionID} child={Path.GetFileName(child)} arm={rail.arm} reason=rail-role expected={expectedRole} actual={rail.railRole}");
                    continue;
                }
                CortexForkTerminalOccurrenceCheckReceipt verification = CortexForkTerminalRunReceipt.ReadTerminalOccurrenceCheckDocument(
                    verificationPath, expectedRole switch
                    {
                        CortexForkRailRoles.Baseline => CortexForkPreparationRoles.Baseline,
                        CortexForkRailRoles.Candidate => CortexForkPreparationRoles.Candidate,
                        CortexForkRailRoles.ForcedNull => CortexForkPreparationRoles.ForcedNull,
                        CortexForkRailRoles.ReflexFrozen => CortexForkPreparationRoles.ReflexFrozen,
                        _ => CortexForkPreparationRoles.Unknown,
                    }).Receipt;
                verification.Validate(Path.GetFileName(child), contract.ColdSeedDigest);
                CortexForkTerminalRunReceipt terminal = CortexForkTerminalRunReceipt.Read(child);
                CortexForkAdoptionHopDocument? rootAdoption = terminal.adoptionAncestry.Count > 0
                    ? terminal.adoptionAncestry[0] : null;
                CortexForkAdoptionHopDocument? sourceAdoption = terminal.adoptionAncestry.Count > 0
                    ? terminal.adoptionAncestry[^1] : null;
                long terminalMatchedSpend = checked((long)terminal.actualNextStep - custody.nextStep);
                string terminalRejectionReason = terminal.role != expectedRole ? "terminal-role" :
                    terminal.parentRunID != parentRunID ? "terminal-parent" :
                    terminal.childRunID != Path.GetFileName(child) ? "terminal-child" :
                    terminal.coldSeedDigest != contract.ColdSeedDigest ? "terminal-contract-cold" :
                    terminal.coldSeedDigest != custody.coldSeedDigest ? "terminal-custody-cold" :
                    rootAdoption is null ? "terminal-adoption-missing" :
                    rootAdoption.originRunID != custody.sourceRunID ? "terminal-root-run" :
                    rootAdoption.sourceNextStep != custody.nextStep ? "terminal-root-next" :
                    rootAdoption.sourceSeedDigest != custody.coldSeedDigest ? "terminal-root-seed" :
                    rootAdoption.parentBindingDigest != custody.coldSeedDigest ? "terminal-root-binding" :
                    sourceAdoption!.childRunID != terminal.childRunID ? "terminal-source-child" :
                    sourceAdoption.originRunID != terminal.sourceRunID ? "terminal-source-run" :
                    sourceAdoption.sourceNextStep != terminal.sourceNextStep ? "terminal-source-next" :
                    sourceAdoption.sourceSeedDigest != terminal.sourceSeedDigest ? "terminal-source-seed" :
                    sourceAdoption.persistedConfigDigest != terminal.persistedConfigDigest ? "terminal-source-config" :
                    terminal.actualNextStep != rail.terminalStep + 1 ? "terminal-step" :
                    terminal.plannedNextStep - custody.nextStep != rail.horizon ? "terminal-horizon" :
                    terminal.actualNextStep != terminal.plannedNextStep ? "terminal-partial" :
                    terminal.runtimeStopRequested ? "terminal-runtime-stop" :
                    terminalMatchedSpend < 0 || terminalMatchedSpend != rail.matchedSpend ? "terminal-spend" :
                    terminal.exitCode != 0 ? "terminal-exit" :
                    !terminal.terminalCheckpointExact ? "terminal-checkpoint-exact" :
                    !terminal.terminalOccurrenceCheckExact ? "terminal-verification-exact" :
                    !terminal.terminalOccurrenceCheckAttempted ? "terminal-verification-missing" :
                    !verification.verified ? "verification-unverified" : "";
                if (terminalRejectionReason.Length != 0)
                {
                    bool retryableTerminal = terminalRejectionReason is "terminal-partial" or "terminal-runtime-stop"
                        or "terminal-exit" or "terminal-checkpoint-exact" or "terminal-verification-exact"
                        or "terminal-verification-missing";
                    if (!retryableTerminal)
                    {
                        invalidArtifacts++;
                        if (firstInvalidReason.Length == 0) firstInvalidReason = terminalRejectionReason;
                    }
                    Trace.Cortex.Boundary("policy.boundary.terminal-reject",
                        $"funding={funding.QuotaDecisionID} child={Path.GetFileName(child)} arm={rail.arm} reason={terminalRejectionReason}");
                    continue;
                }
                int index = ParsePolicyBoundaryChildIndex(Path.GetFileName(child));
                if (index < 0) continue;
                string tuple = string.Join('|', rail.sourceRunID, rail.sourceNextStep,
                    rail.materializationColdSeedDigest, rail.sourceCheckpointSHA256, rail.sourceTapeSpanlogSHA256,
                    rail.sourceCurveSHA256, rail.sourceExcursionsSHA256, rail.custodyDigest, rail.generation);
                string evidence = string.Join('|', Path.GetFileName(child),
                    ComputePolicyBoundaryFileSHA256(railPath), ComputePolicyBoundaryFileSHA256(verificationPath),
                    ComputePolicyBoundaryFileSHA256(Path.Combine(child, CortexForkTerminalRunReceipt.FileName)), tuple);
                if (!generations.TryGetValue(index, out Dictionary<int, Dictionary<PolicyBoundaryArms, (long Spend, string Evidence, string Tuple, PolicyBoundaryRailMetadataDocument Rail, CortexForkTerminalRunReceipt Terminal)>>? generationByHorizon))
                    generations[index] = generationByHorizon = new();
                if (!generationByHorizon.TryGetValue(rail.horizon, out Dictionary<PolicyBoundaryArms, (long Spend, string Evidence, string Tuple, PolicyBoundaryRailMetadataDocument Rail, CortexForkTerminalRunReceipt Terminal)>? generation))
                    generationByHorizon[rail.horizon] = generation = new();
                if (!generation.ContainsKey(rail.arm)) generation.Add(rail.arm, (terminalMatchedSpend, evidence, tuple, rail, terminal));
                completeRails++;
            }
            catch (Exception error) when (error is InvalidDataException or FormatException)
            {
                invalidArtifacts++;
                if (firstInvalidReason.Length == 0) firstInvalidReason = "receipt";
                Trace.Cortex.Boundary("policy.boundary.terminal-reject",
                    $"funding={funding.QuotaDecisionID} child={Path.GetFileName(child)} reason=receipt:{error.Message}");
            }
            catch (IOException error)
            {
                invalidArtifacts++;
                if (firstInvalidReason.Length == 0) firstInvalidReason = "io";
                Trace.Cortex.Boundary("policy.boundary.terminal-reject",
                    $"funding={funding.QuotaDecisionID} child={Path.GetFileName(child)} reason=io:{error.Message}");
            }
            catch (Exception error)
            {
                invalidArtifacts++;
                if (firstInvalidReason.Length == 0) firstInvalidReason = "receipt";
                Trace.Cortex.Boundary("policy.boundary.terminal-reject",
                    $"funding={funding.QuotaDecisionID} child={Path.GetFileName(child)} reason=receipt:{error.Message}");
            }
        }
        Dictionary<int, int> selectedGenerationByHorizon = new();
        int[] selectedGenerations = new int[horizons.Length];
        if (!TrySelectGeneration(horizons.Length - 1))
        {
            generationState = invalidArtifacts > 0 ? PolicyBoundaryGenerationStates.Invalid : PolicyBoundaryGenerationStates.Incomplete;
            generationStateReason = invalidArtifacts > 0 ? firstInvalidReason : "no-complete-generation";
            generationCensus = new(generationState, generationStateReason, childDirectories, materializationMarkers,
                completeRails, invalidArtifacts, nextGeneration);
            return false;
        }
        generationState = PolicyBoundaryGenerationStates.Complete;
        generationStateReason = "authenticated-generation";
        generationCensus = new(generationState, generationStateReason, childDirectories, materializationMarkers,
            completeRails, invalidArtifacts, nextGeneration);
        for (int index = 0; index < horizons.Length; index++)
            selectedGenerationByHorizon[horizons[index]] = selectedGenerations[index];
        IEnumerable<(long Spend, string Evidence, string Tuple, PolicyBoundaryRailMetadataDocument Rail, CortexForkTerminalRunReceipt Terminal)> selectedRows = horizons.SelectMany(horizon => generations[selectedGenerationByHorizon[horizon]][horizon].Values);
        string[] evidenceRows = selectedRows.Select(static value => value.Evidence).OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        generationDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            funding.QuotaDecisionID.Value, string.Join(',', horizons.Select(horizon => $"{horizon}:{selectedGenerationByHorizon[horizon]}")), string.Join(';', evidenceRows), "policy-boundary-generation-v3"))));
        foreach ((long spend, _, _, _, _) in generations[selectedGenerations[^1]][horizons[^1]].Values)
        {
            if (spend < 0)
            {
                generationState = PolicyBoundaryGenerationStates.Invalid;
                generationStateReason = "negative-spend";
                generationCensus = new(generationState, generationStateReason, childDirectories, materializationMarkers,
                    completeRails, invalidArtifacts, nextGeneration);
                return false;
            }
            actual = checked(actual + spend);
        }
        if (actual > funding.PlannedArmSteps)
        {
            generationState = PolicyBoundaryGenerationStates.Invalid;
            generationStateReason = "planned-spend";
            generationCensus = new(generationState, generationStateReason, childDirectories, materializationMarkers,
                completeRails, invalidArtifacts, nextGeneration);
            return false;
        }
        if (!requireReceipt) return true;
        if (!_policyBoundaryObligations.TryGetValue(funding.Policy, out PolicyBoundaryObligation? obligation)
            || !obligation.Identity.Policy.Equals(domain.PolicyID))
        {
            generationState = PolicyBoundaryGenerationStates.Invalid;
            generationStateReason = "obligation-missing";
            generationCensus = new(generationState, generationStateReason, childDirectories, materializationMarkers,
                completeRails, invalidArtifacts, nextGeneration);
            return false;
        }
        if (requireReceipt && selectedRows.Any(row => !string.Equals(row.Rail.obligationID, obligation.ID.Value, StringComparison.Ordinal)))
        {
            generationState = PolicyBoundaryGenerationStates.Invalid;
            generationStateReason = "obligation-mismatch";
            generationCensus = new(generationState, generationStateReason, childDirectories, materializationMarkers,
                completeRails, invalidArtifacts, nextGeneration);
            return false;
        }
        string[] candidateBoundaries = selectedRows.Select(static row => row.Rail.candidateBoundary).Distinct(StringComparer.Ordinal).ToArray();
        if (candidateBoundaries.Length != 1) return false;
        PolicyBoundaryRational candidateBoundary = PolicyBoundaryRational.Parse(candidateBoundaries[0]);
        PolicyBoundaryArmReceipt[] rows = new PolicyBoundaryArmReceipt[horizons.Length * 4];
        for (int i = 0; i < horizons.Length; i++)
        {
            if (!generations[selectedGenerationByHorizon[horizons[i]]].TryGetValue(horizons[i], out Dictionary<PolicyBoundaryArms, (long Spend, string Evidence, string Tuple, PolicyBoundaryRailMetadataDocument Rail, CortexForkTerminalRunReceipt Terminal)>? generation)
                || !generation.TryGetValue(PolicyBoundaryArms.Baseline, out var baseline)
                || !generation.TryGetValue(PolicyBoundaryArms.Candidate, out var candidateRail)
                || !generation.TryGetValue(PolicyBoundaryArms.ForcedDivergentNull, out var nullRail)
                || !generation.TryGetValue(PolicyBoundaryArms.ReflexFrozenControl, out var reflexRail)) return false;
            PolicyBoundaryRailMetadataDocument[] rails = [baseline.Rail, candidateRail.Rail, nullRail.Rail, reflexRail.Rail];
            for (int armIndex = 0; armIndex < rails.Length; armIndex++)
            {
                PolicyBoundaryRailMetadataDocument rail = rails[armIndex];
                PolicyBoundaryArmReceipt row = PolicyBoundaryRailMetadata.CreateArmReceipt(
                    rail, generation[(PolicyBoundaryArms)armIndex].Spend,
                    domain);
                row.Validate(domain);
                row.ValidateExecutedDecisionIdentity(domain);
                rows[i * 4 + armIndex] = row;
            }
        }
        bool continuity = rows.All(static row => row.ContinuityExact);
        bool matchedSpend = rows.GroupBy(static row => row.Horizon).All(static group =>
        {
            long spend = group.First(static row => row.Arm == PolicyBoundaryArms.Baseline).MatchedSpend;
            return group.Count() == 4 && group.All(row => row.MatchedSpend == spend);
        });
        int terminalHorizon = horizons[^1];
        bool forcedNullBehaviorExecuted = rows.Where(row => row.Arm == PolicyBoundaryArms.ForcedDivergentNull && row.Horizon == terminalHorizon)
            .All(static row => row.BehaviorallyExecuted);
        bool reflexFrozen = rows.Where(static row => row.Arm == PolicyBoundaryArms.ReflexFrozenControl)
            .All(static row => row.GrammarExecutionsDelta == 0 && row.TrialAdaptationTransitions == 0 && !row.AdaptationEnabled);
        receipt = new PolicyBoundaryForkReceipt(obligation.ID, PolicyBoundaryRational.Zero, candidateBoundary,
            [.. horizons], rows, continuity, matchedSpend, forcedNullBehaviorExecuted,
            continuity && matchedSpend && rows.All(static row => row.ChildProcessCompleted)
                && forcedNullBehaviorExecuted
                && rows.Where(row => row.Arm == PolicyBoundaryArms.ForcedDivergentNull && row.Horizon == terminalHorizon).All(static row => row.Diverged)
                && reflexFrozen && rows.GroupBy(static row => row.Horizon).All(static group =>
                {
                    long baseline = group.First(static row => row.Arm == PolicyBoundaryArms.Baseline).PaidCloseDelta;
                    long candidate = group.First(static row => row.Arm == PolicyBoundaryArms.Candidate).PaidCloseDelta;
                    return candidate >= baseline;
                }),
            funding.ReadoutFingerprint, funding.CandidateRevision.Value)
        {
            QuotaDecisionID = funding.QuotaDecisionID,
            SourceDecisionCandidateFingerprint = funding.CandidateFingerprint,
        };
                receipt.Validate(domain);
        return true;

        bool TrySelectGeneration(int horizonIndex)
        {
            int horizon = horizons[horizonIndex];
            foreach (int generationIndex in generations.Keys.OrderByDescending(static value => value))
            {
                if (!generations[generationIndex].TryGetValue(horizon,
                        out Dictionary<PolicyBoundaryArms, (long Spend, string Evidence, string Tuple, PolicyBoundaryRailMetadataDocument Rail, CortexForkTerminalRunReceipt Terminal)>? current)
                    || current.Count != 4)
                    continue;
                if (horizonIndex < horizons.Length - 1)
                {
                    int nextGenerationIndex = selectedGenerations[horizonIndex + 1];
                    Dictionary<PolicyBoundaryArms, (long Spend, string Evidence, string Tuple, PolicyBoundaryRailMetadataDocument Rail, CortexForkTerminalRunReceipt Terminal)> next =
                        generations[nextGenerationIndex][horizons[horizonIndex + 1]];
                    if (!ContinuesEveryArm(current, next)) continue;
                }
                selectedGenerations[horizonIndex] = generationIndex;
                if (horizonIndex == 0 || TrySelectGeneration(horizonIndex - 1)) return true;
            }
            return false;
        }

        static bool ContinuesEveryArm(
            Dictionary<PolicyBoundaryArms, (long Spend, string Evidence, string Tuple, PolicyBoundaryRailMetadataDocument Rail, CortexForkTerminalRunReceipt Terminal)> prior,
            Dictionary<PolicyBoundaryArms, (long Spend, string Evidence, string Tuple, PolicyBoundaryRailMetadataDocument Rail, CortexForkTerminalRunReceipt Terminal)> next)
        {
            foreach (PolicyBoundaryArms arm in Enum.GetValues<PolicyBoundaryArms>())
            {
                if (!prior.TryGetValue(arm, out var priorRow) || !next.TryGetValue(arm, out var nextRow)) return false;
                List<CortexForkAdoptionHopDocument> ancestry = nextRow.Terminal.adoptionAncestry;
                if (!string.Equals(nextRow.Terminal.sourceRunID, priorRow.Terminal.childRunID, StringComparison.Ordinal)
                    || ancestry.Count < 2
                    || !string.Equals(ancestry[^2].childRunID, priorRow.Terminal.childRunID, StringComparison.Ordinal)
                    || !string.Equals(ancestry[^1].originRunID, priorRow.Terminal.childRunID, StringComparison.Ordinal)
                    || !string.Equals(ancestry[^1].childRunID, nextRow.Terminal.childRunID, StringComparison.Ordinal))
                    return false;
            }
            return true;
        }
    }

    private static int ParsePolicyBoundaryChildIndex(string childID)
    {
        int separator = childID.LastIndexOf('_');
        return separator >= 0 && int.TryParse(childID.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int index)
            ? index : -1;
    }

    private static string ComputePolicyBoundaryFileSHA256(string path)
        => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static bool TryReadPolicyBoundaryMaterializationContract(
        string markerPath, string childDirectory, out CortexForkMaterializationContract contract)
    {
        contract = default;
        try
        {
            byte[] bytes = File.ReadAllBytes(markerPath);
            string[] lines = Encoding.UTF8.GetString(bytes)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length != 4) return false;
            string? parent = null;
            string? attempt = null;
            string? child = null;
            string? cold = null;
            foreach (string line in lines)
            {
                int separator = line.IndexOf('=');
                if (separator <= 0 || separator == line.Length - 1) return false;
                string key = line[..separator];
                string value = line[(separator + 1)..];
                switch (key)
                {
                    case "parent" when parent is null: parent = value; break;
                    case "attempt" when attempt is null: attempt = value; break;
                    case "child" when child is null: child = value; break;
                    case "cold" when cold is null: cold = value; break;
                    default: return false;
                }
            }
            if (parent is null || attempt is null || child is null || cold is null) return false;
            contract = new CortexForkMaterializationContract(parent, attempt, child, cold);
            contract.Validate(childDirectory);
            return bytes.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(contract.Encode()));
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (InvalidDataException) { return false; }
    }

    private void AppendPolicyBoundaryAdmissionCensus(
        in CortexPolicyTrialQuotaDecision funding,
        int beforeChildDirectories,
        int beforeMaterializationMarkers,
        (int ChildDirectories, int MaterializationMarkers) after,
        PolicyBoundaryAdmissionCensusStatuses beforeStatus = PolicyBoundaryAdmissionCensusStatuses.Counted,
        PolicyBoundaryAdmissionCensusStatuses afterStatus = PolicyBoundaryAdmissionCensusStatuses.Counted)
    {
        if (_runtimeRun is null) return;
        string row = string.Join('\t',
            funding.QuotaDecisionID.ToString(), funding.Decision.ToString(),
            beforeChildDirectories.ToString(CultureInfo.InvariantCulture),
            beforeMaterializationMarkers.ToString(CultureInfo.InvariantCulture),
            after.ChildDirectories.ToString(CultureInfo.InvariantCulture),
            after.MaterializationMarkers.ToString(CultureInfo.InvariantCulture),
            beforeStatus.ToString(), afterStatus.ToString());
        _policyJournalBuffer.Append(_runtimeRun.PathOf(PolicyBoundaryAdmissionCensusFile), PolicyBoundaryAdmissionCensusHeader, row);
    }

    private void AppendPolicyBoundaryGenerationTransition(
        in CortexPolicyTrialQuotaDecision funding,
        in PolicyBoundaryGenerationCensus census)
    {
        if (_runtimeRun is null) return;
        string row = string.Join('\t',
            funding.QuotaDecisionID.ToString(),
            census.State.ToString(),
            census.Reason,
            census.ChildDirectories.ToString(CultureInfo.InvariantCulture),
            census.MaterializationMarkers.ToString(CultureInfo.InvariantCulture),
            census.CompleteRails.ToString(CultureInfo.InvariantCulture),
            census.InvalidArtifacts.ToString(CultureInfo.InvariantCulture),
            census.NextGeneration.ToString(CultureInfo.InvariantCulture));
        _policyJournalBuffer.Append(_runtimeRun.PathOf(PolicyBoundaryGenerationTransitionFile),
            PolicyBoundaryGenerationTransitionHeader, row);
    }

    private void AppendPolicyBoundaryOpportunityCensus(
        bool obligationAvailable,
        bool readoutAvailable,
        bool exactReadout,
        bool readyReadout,
        in CortexPolicyReadoutReceipt readout)
    {
        if (_runtimeRun is null) return;
        string row = string.Join('\t',
            Step.ToString(CultureInfo.InvariantCulture),
            obligationAvailable ? "1" : "0",
            readoutAvailable ? "1" : "0",
            exactReadout ? "1" : "0",
            readyReadout ? "1" : "0",
            readout.Revision.Value.ToString(CultureInfo.InvariantCulture),
            readout.Fingerprint.ToString("X16", CultureInfo.InvariantCulture),
            readout.CanonicalCoverage.RequiredStateCount.ToString(CultureInfo.InvariantCulture),
            readout.CanonicalCoverage.CoveredStateCount.ToString(CultureInfo.InvariantCulture),
            readout.CanonicalCoverage.MissingStateCount.ToString(CultureInfo.InvariantCulture),
            readout.CanonicalCoverage.RequiredStatesDigest.ToString("X16", CultureInfo.InvariantCulture),
            readout.CanonicalCoverage.CoveredStatesDigest.ToString("X16", CultureInfo.InvariantCulture),
            readout.CanonicalCoverage.MissingStatesDigest.ToString("X16", CultureInfo.InvariantCulture),
            readout.CanonicalCoverage.Attribution.ToString());
        _policyJournalBuffer.Append(_runtimeRun.PathOf(PolicyBoundaryOpportunityCensusFile), PolicyBoundaryOpportunityCensusHeader, row);
    }

    private void TruncatePolicyBoundaryOpportunityCensus()
    {
        FlushPolicyJournalBuffer();
        if (_runtimeRun is null) return;
        string path = _runtimeRun.PathOf(PolicyBoundaryOpportunityCensusFile);
        if (!File.Exists(path)) return;
        string[] lines = File.ReadAllText(path, Encoding.UTF8).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return;
        if (!string.Equals(lines[0], PolicyBoundaryOpportunityCensusHeader, StringComparison.Ordinal))
            throw new InvalidDataException("policy boundary opportunity census header changed");
        StringBuilder kept = new(PolicyBoundaryOpportunityCensusHeader + "\n");
        for (int i = 1; i < lines.Length; i++)
        {
            string[] columns = lines[i].Split('\t');
            if (columns.Length != 14 || !int.TryParse(columns[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int step))
                throw new InvalidDataException("policy boundary opportunity census row has the wrong shape");
            if (step <= Step) kept.Append(lines[i]).Append('\n');
        }
        File.WriteAllText(path, kept.ToString(), Encoding.UTF8);
    }

    private void TruncatePolicyBoundaryReceiptFile()
    {
        FlushPolicyJournalBuffer();
        if (_runtimeRun is null) return;
        string path = _runtimeRun.PathOf(PolicyBoundaryReceiptFile);
        if (!File.Exists(path)) return;
        string[] lines = File.ReadAllLines(path);
        if (lines.Length == 0) return;
        if (!string.Equals(lines[0], PolicyBoundaryReceiptHeader, StringComparison.Ordinal))
            throw new InvalidDataException("policy boundary receipt header changed");
        StringBuilder kept = new(PolicyBoundaryReceiptHeader + "\n");
        for (int i = 1; i < lines.Length; i++)
        {
            string[] columns = lines[i].Split('\t');
            if (columns.Length != PolicyBoundaryReceiptColumnCount || !int.TryParse(columns[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int step))
                throw new InvalidDataException("policy boundary receipt row has the wrong shape");
            if (step <= Step) kept.Append(lines[i]).Append('\n');
        }
        File.WriteAllText(path, kept.ToString(), Encoding.UTF8);
    }

    private void TruncatePolicyBoundaryAdmissionCensus()
    {
        FlushPolicyJournalBuffer();
        if (_runtimeRun is null) return;
        string path = _runtimeRun.PathOf(PolicyBoundaryAdmissionCensusFile);
        if (!File.Exists(path)) return;
        string[] lines = File.ReadAllLines(path);
        if (lines.Length == 0) return;
        if (!string.Equals(lines[0], PolicyBoundaryAdmissionCensusHeader, StringComparison.Ordinal))
            throw new InvalidDataException("policy boundary admission census header changed");
        StringBuilder kept = new(PolicyBoundaryAdmissionCensusHeader + "\n");
        for (int i = 1; i < lines.Length; i++)
        {
            string[] columns = lines[i].Split('\t');
            if (columns.Length != 6
                || !ulong.TryParse(columns[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong fundingID))
                throw new InvalidDataException("policy boundary admission census row has the wrong shape");
            if (_policyTrialQuotaByID.ContainsKey(new CortexPolicyQuotaDecisionID(fundingID)))
                kept.Append(lines[i]).Append('\n');
        }
        File.WriteAllText(path, kept.ToString(), Encoding.UTF8);
    }

    internal bool TryEmitPolicyBoundaryReceipt(
        CortexPolicyID policy,
        in PolicyBoundaryForkReceipt receipt,
        out TapeEventID eventID,
        out byte[] payload)
    {
        eventID = default;
        payload = [];
        if (_runtimeTape is null || _runtimeJournal is null) return false;
        eventID = AppendPolicyBoundaryReceipt(policy, in receipt, out payload);
        return eventID.Value > 0 && payload.Length > 0;
    }

    private TapeEventID AppendPolicyBoundaryReceipt(
        CortexPolicyID policy,
        in PolicyBoundaryForkReceipt receipt,
        out byte[] payload)
    {
        payload = [];
        string digest = PolicyBoundaryObligation.ComputeReceiptDigest(in receipt);
        if (_runtimeRun is not null)
        {
            string armEvidence = string.Join(';', receipt.Arms.Select(static arm =>
                    string.Join(',', (byte)arm.Arm, arm.Horizon, arm.PaidCloseDelta, arm.MatchedSpend,
                    arm.ContinuityExact ? 1 : 0, arm.ChildProcessCompleted ? 1 : 0, arm.GrammarExecutionsDelta, arm.TrialAdaptationTransitions, arm.AdaptationEnabled ? 1 : 0,
                    (byte)arm.ExecutionOutcome, arm.RequestCount, arm.GuardAdmittedCount, arm.LastRequestDecisionID.Value, arm.LastRequestStep,
                    arm.LastRequestReadout.LaunchpadAction, arm.LastRequestReadout.RawCandidateAction, arm.LastRequestReadout.SelectedCandidateAction,
                    arm.LastRequestReadout.ExecutedAction, (byte)arm.LastRequestReadout.Authority, arm.LastRequestReadout.GrammarRevision.Value,
                    (byte)arm.LastRequestReadout.SelectionCause, arm.LastRequestReadout.ReadoutCandidateOccurrenceDigest.ToString("X16", CultureInfo.InvariantCulture),
                    arm.LastRequestReadout.ReadoutCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture),
                    arm.ExecutedDecisionID.Value, arm.ExecutedStep, arm.ExecutedLaunchpadAction, arm.ExecutedRawCandidateAction, arm.ExecutedSelectedCandidateAction,
                    arm.ExecutedAction, (byte)arm.ExecutedAuthority, (byte)arm.ExecutedSelectionCause,
                    arm.ExecutedReadoutFingerprint.ToString("X16", CultureInfo.InvariantCulture), arm.ExecutedReadoutRevision,
                    arm.ExecutedReadoutOccurrenceDigest.ToString("X16", CultureInfo.InvariantCulture), arm.ExecutedCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture),
                    arm.ExecutedCanonicalState.Version == 0 ? "" : arm.ExecutedCanonicalState.Policy.Value,
                    (byte)arm.ExecutedCanonicalState.Kind, arm.ExecutedCanonicalState.Version,
                    arm.ExecutedCanonicalState.Value.ToString("X16", CultureInfo.InvariantCulture), arm.ExecutedDecisionEventID.Value,
                    arm.ExecutedOutcomeEventID.Value, arm.ExecutedOutcomePayloadSHA256,
                    arm.ForcedDivergenceSeed.ToString("X16", CultureInfo.InvariantCulture), arm.Diverged ? 1 : 0)));
            string row = string.Join('\t',
                Step.ToString(CultureInfo.InvariantCulture), policy.Value, receipt.Obligation.Value,
                receipt.CandidateBoundary.ToString(), receipt.BaselineBoundary.ToString(), string.Join(',', receipt.Horizons),
                receipt.ContinuityExact ? "1" : "0", receipt.MatchedSpend ? "1" : "0",
                receipt.ForcedNullBehaviorExecuted ? "1" : "0", receipt.Verified ? "1" : "0", armEvidence, digest,
                receipt.SourceDecisionReadoutFingerprint.ToString("X16", CultureInfo.InvariantCulture),
                receipt.SourceDecisionCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture),
                receipt.SourceDecisionReadoutRevision.ToString(CultureInfo.InvariantCulture),
                receipt.QuotaDecisionID.Value.ToString("X16", CultureInfo.InvariantCulture));
            _policyJournalBuffer.Append(_runtimeRun.PathOf(PolicyBoundaryReceiptFile), PolicyBoundaryReceiptHeader, row);
        }
        if (_runtimeTape is not null && _runtimeJournal is not null)
        {
            payload = TapePacketCreator.EncodePolicyBoundaryReceipt(policy, RequirePolicyBoundaryDomain(policy), in receipt);
            return TapePacketCreator.AppendPolicyBoundaryReceipt(_runtimeTape, _runtimeJournal, Step, policy,
                RequirePolicyBoundaryDomain(policy), in receipt);
        }
        return default;
    }

    private void SavePolicyBoundaryState(CkptWriter writer)
    {
        writer.Section(PolicyBoundaryCheckpointTag);
        writer.U32(PolicyBoundaryCheckpointVersion);
        List<KeyValuePair<CortexPolicyID, PolicyBoundaryObligation>> rows = new(_policyBoundaryObligations);
        rows.Sort(static (left, right) => left.Key.CompareTo(right.Key));
        writer.I32(rows.Count);
        foreach (KeyValuePair<CortexPolicyID, PolicyBoundaryObligation> row in rows) row.Value.Save(writer);
        List<KeyValuePair<CortexPolicyID, (PolicyBoundaryTrainingReceipt Training, PolicyBoundaryMountReceipt Mount)>> lineage = new(_mountedPolicyBoundaryLineage);
        lineage.Sort(static (left, right) => left.Key.CompareTo(right.Key));
        writer.I32(lineage.Count);
        foreach (var row in lineage)
        {
            writer.Str(row.Key.Value);
            PolicyBoundaryTrainingReceipt training = row.Value.Training;
            PolicyBoundaryMountReceipt mount = row.Value.Mount;
            IPolicyBoundaryDomain domain = RequirePolicyBoundaryDomain(row.Key);
            writer.Bytes(PolicyBoundaryTrainingReceipt.Encode(in training, domain));
            writer.Bytes(PolicyBoundaryMountReceipt.Encode(in mount, in training, domain));
        }
    }

    private void LoadPolicyBoundaryState(CkptReader reader)
    {
        reader.Expect(PolicyBoundaryCheckpointTag);
        uint version = reader.U32();
        if (version != PolicyBoundaryCheckpointVersion)
            throw new InvalidDataException("policy boundary checkpoint schema is unsupported");
        int count = reader.I32();
        if (count < 0 || count > 1024) throw new InvalidDataException("invalid policy boundary obligation count");
        _policyBoundaryObligations.Clear();
        for (int i = 0; i < count; i++)
        {
            PolicyBoundaryObligation obligation = PolicyBoundaryObligation.Load(reader, RequirePolicyBoundaryDomain, readTeacherCorroboration: true, readExecutedDecision: true, readFundingDecision: true, readExecutionCorroboration: true, readExecutedStep: true, readExecutionAccounting: true, readSplitIdentity: true, readForcedCustody: true, readCanonicalScope: true, legacyExecutionOutcome: false);
            if (!_policies.ContainsKey(obligation.Identity.Policy))
                throw new InvalidDataException($"checkpoint boundary obligation addresses unknown policy '{obligation.Identity.Policy}'");
            if (!_policyBoundaryObligations.TryAdd(obligation.Identity.Policy, obligation))
                throw new InvalidDataException($"duplicate checkpoint boundary obligation for '{obligation.Identity.Policy}'");
        }
        int lineageCount = reader.I32();
        if (lineageCount < 0 || lineageCount > 1024) throw new InvalidDataException("invalid policy boundary lineage count");
        _mountedPolicyBoundaryLineage.Clear();
        for (int i = 0; i < lineageCount; i++)
        {
            CortexPolicyID policy = new(reader.Str());
            IPolicyBoundaryDomain domain = RequirePolicyBoundaryDomain(policy);
            PolicyBoundaryTrainingReceipt training = PolicyBoundaryTrainingReceipt.Decode(reader.Bytes(1_000_000), domain);
            PolicyBoundaryMountReceipt mount = PolicyBoundaryMountReceipt.Decode(reader.Bytes(1_000_000), in training, domain);
            if (!string.Equals(policy.Value, training.Policy, StringComparison.Ordinal)) throw new InvalidDataException("policy boundary lineage policy mismatch");
            _mountedPolicyBoundaryLineage[policy] = (training, mount);
        }
    }
}

/// Typed placement metadata for policy-boundary rails.  The run directory is only an immediate role child;
/// step and horizon remain explicit receipt fields instead of becoming a hidden path taxonomy.
internal readonly record struct PolicyBoundaryRailMetadata(
    int Step,
    int Horizon,
    PolicyBoundaryArms Arm,
    CortexForkRailRoles RailRole,
    ulong ReadoutFingerprint,
    PolicyBoundaryTrialOutcome Outcome,
    int TerminalStep,
    CortexForkMaterializationContract MaterializationContract,
    string SourceRunID = "",
    int SourceNextStep = -1,
    string AuditOnlyDigest = "",
    CortexForkDigests SourceDigests = default,
    int Generation = -1,
    PolicyBoundaryObligationID Obligation = default,
    PolicyBoundaryRational CandidateBoundary = default,
    PolicyCanonicalStateID SuccessorCanonicalState = default,
    ulong SuccessorOccurrenceDigest = 0,
    string SuccessorOccurrenceCheckDigest = "",
    string SuccessorOccurrenceCheckCoverageDigest = "",
    bool RequireOrdinaryOutcome = false)
{
    private static bool IsLowerHexDigest(string value)
        => value.Length == 64 && value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public byte[] Encode(IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        Outcome.Validate(domain);
        Outcome.ValidateExecutionIdentity(domain);
        bool executionIdentityRequired = Outcome.ExecutionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted
            || Outcome.GuardAdmittedCount > 0;
        PolicyCanonicalStateID executedCanonicalState = Outcome.ExecutedCanonicalState;
        if (executionIdentityRequired && !Outcome.HasExecutedDecisionIdentity)
            throw new InvalidDataException("policy-boundary rail metadata requires its executed decision identity after guard admission");
        if (Outcome.ExecutionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted
            && !domain.ValidateCanonicalState(in executedCanonicalState))
            throw new InvalidDataException("policy-boundary rail metadata requires its immutable execution scope");
        if (Outcome.ExecutedSelectionCause == CortexPolicySelectionCauses.TrialOverride
            && (Outcome.ExecutedDecisionEventID.Value <= 0 || Outcome.ForcedDivergenceSeed == 0))
            throw new InvalidDataException("policy-boundary TrialOverride rail metadata lacks event or forced-seed custody");
        if (RequireOrdinaryOutcome && Arm != PolicyBoundaryArms.ForcedDivergentNull)
            throw new InvalidDataException("ordinary outcome custody is only legal on the forced divergent null rail");
        if (RequireOrdinaryOutcome
            && (Outcome.ExecutionOutcome != CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted
                || Outcome.ExecutedSelectionCause != CortexPolicySelectionCauses.TrialOverride
                || Outcome.ExecutedOutcomeEventID.Value <= 0
                || !IsLowerHexDigest(Outcome.ExecutedOutcomePayloadSHA256)))
            throw new InvalidDataException("required ordinary outcome custody is not an executed TrialOverride outcome");
        if (!RequireOrdinaryOutcome
            && (Outcome.ExecutedOutcomeEventID.Value != 0 || Outcome.ExecutedOutcomePayloadSHA256.Length != 0))
            throw new InvalidDataException("policy-boundary rail metadata carries ordinary outcome custody without its requirement marker");
        PolicyCanonicalStateID successorCanonicalState = SuccessorCanonicalState;
        if (Arm == PolicyBoundaryArms.ForcedDivergentNull
            && Outcome.ExecutionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted
            && (!domain.ValidateCanonicalState(in successorCanonicalState)
                || SuccessorOccurrenceDigest == 0
                || SuccessorOccurrenceCheckDigest.Length != 64
                || SuccessorOccurrenceCheckDigest.Any(static c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
                || SuccessorOccurrenceCheckCoverageDigest.Length != 64
                || SuccessorOccurrenceCheckCoverageDigest.Any(static c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))))
            throw new InvalidDataException("forced policy-boundary rail metadata lacks successor scope evidence");
        PolicyBoundaryRailMetadataDocument document = new()
        {
            executionAccountingMarker = 1,
            step = Step,
            horizon = Horizon,
            arm = Arm,
            railRole = RailRole,
            readoutFingerprint = ReadoutFingerprint,
            paidCloseDelta = Outcome.PaidCloseDelta,
            matchedSpend = Outcome.MatchedSpend,
            continuityExact = Outcome.ContinuityExact,
            childProcessCompleted = Outcome.ChildProcessCompleted,
            terminalStep = TerminalStep,
            grammarExecutionsDelta = Outcome.GrammarExecutionsDelta,
            trialAdaptationTransitions = Outcome.TrialAdaptationTransitions,
            adaptationEnabled = Outcome.AdaptationEnabled,
            executionOutcome = Outcome.ExecutionOutcome,
            requestCount = Outcome.RequestCount,
            guardAdmittedCount = Outcome.GuardAdmittedCount,
            lastRequestDecisionID = Outcome.LastRequestDecisionID.Value,
            lastRequestStep = Outcome.LastRequestStep,
            lastRequestLaunchpadAction = Outcome.LastRequestReadout.LaunchpadAction,
            lastRequestRawCandidateAction = Outcome.LastRequestReadout.RawCandidateAction,
            lastRequestSelectedCandidateAction = Outcome.LastRequestReadout.SelectedCandidateAction,
            lastRequestExecutedAction = Outcome.LastRequestReadout.ExecutedAction,
            lastRequestAuthority = Outcome.LastRequestReadout.Authority,
            lastRequestRevision = Outcome.LastRequestReadout.GrammarRevision.Value,
            lastRequestSelectionCause = Outcome.LastRequestReadout.SelectionCause,
            lastRequestSupportDigest = Outcome.LastRequestReadout.ReadoutCandidateOccurrenceDigest,
            lastRequestCandidateFingerprint = Outcome.LastRequestReadout.ReadoutCandidateFingerprint,
            obligationID = Obligation.Value,
            candidateBoundary = CandidateBoundary.ToString(),
            executedDecisionID = Outcome.ExecutedDecisionID.Value,
            executedStep = Outcome.ExecutedStep,
            executedLaunchpadAction = Outcome.ExecutedLaunchpadAction,
            executedRawCandidateAction = Outcome.ExecutedRawCandidateAction,
            executedSelectedCandidateAction = Outcome.ExecutedSelectedCandidateAction,
            executedAction = Outcome.ExecutedAction,
            executedAuthority = Outcome.ExecutedAuthority,
            executedSelectionCause = Outcome.ExecutedSelectionCause,
            executedReadoutFingerprint = Outcome.ExecutedReadoutFingerprint,
            executedReadoutRevision = Outcome.ExecutedReadoutRevision,
            executedReadoutOccurrenceDigest = Outcome.ExecutedReadoutOccurrenceDigest,
            executedCandidateFingerprint = Outcome.ExecutedCandidateFingerprint,
            executedDecisionEventID = Outcome.ExecutedDecisionEventID.Value,
            executedOutcomeEventID = Outcome.ExecutedOutcomeEventID.Value,
            executedOutcomePayloadSHA256 = Outcome.ExecutedOutcomePayloadSHA256,
            ordinaryOutcomeRequired = RequireOrdinaryOutcome,
            forcedDivergenceSeed = Outcome.ForcedDivergenceSeed,
            executedCanonicalPolicy = Outcome.ExecutedCanonicalState.Policy.Value,
            executedCanonicalKind = (byte)Outcome.ExecutedCanonicalState.Kind,
            executedCanonicalVersion = Outcome.ExecutedCanonicalState.Version,
            executedCanonicalValue = Outcome.ExecutedCanonicalState.Value,
            successorCanonicalPolicy = SuccessorCanonicalState.Policy.Value,
            successorCanonicalKind = (byte)SuccessorCanonicalState.Kind,
            successorCanonicalVersion = SuccessorCanonicalState.Version,
            successorCanonicalValue = SuccessorCanonicalState.Value,
            successorSupportDigest = SuccessorOccurrenceDigest,
            successorOccurrenceCheckDigest = SuccessorOccurrenceCheckDigest,
            successorOccurrenceCheckCoverageDigest = SuccessorOccurrenceCheckCoverageDigest,
            materializationParentRunID = MaterializationContract.ParentRunID,
            materializationAttemptID = MaterializationContract.AttemptID,
            materializationChildRunID = MaterializationContract.ChildRunID,
            materializationColdSeedDigest = MaterializationContract.ColdSeedDigest,
            sourceRunID = SourceRunID,
            sourceNextStep = SourceNextStep,
            custodyDigest = AuditOnlyDigest,
            sourceCheckpointSHA256 = SourceDigests.CheckpointSHA256,
            sourceTapeSpanlogSHA256 = SourceDigests.TapeSpanlogSHA256,
            sourceCurveSHA256 = SourceDigests.CurveSHA256,
            sourceExcursionsSHA256 = SourceDigests.ExcursionsSHA256,
            generation = Generation,
        };
        byte[] first = RonSerializer.SerializeToUtf8(in document);
        PolicyBoundaryRailMetadataDocument restored = RonSerializer.Deserialize<PolicyBoundaryRailMetadataDocument>(first);
        if (restored.schemaVersion != 11 || restored.executionAccountingMarker != 1 || !restored.continuityExact
            || restored.ordinaryOutcomeRequired != RequireOrdinaryOutcome)
            throw new InvalidDataException("policy-boundary rail metadata lost its execution accounting schema");
        if (restored.executionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted
            && (restored.executedCanonicalPolicy.Length == 0
                || restored.executedCanonicalVersion == 0))
            throw new InvalidDataException("policy-boundary rail metadata lost immutable execution scope");
        if (Arm == PolicyBoundaryArms.ForcedDivergentNull
            && Outcome.ExecutionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted
            && (restored.successorCanonicalPolicy != SuccessorCanonicalState.Policy.Value
                || restored.successorCanonicalKind != (byte)SuccessorCanonicalState.Kind
                || restored.successorCanonicalVersion != SuccessorCanonicalState.Version
                || restored.successorCanonicalValue != SuccessorCanonicalState.Value
                || restored.successorOccurrenceCheckDigest != SuccessorOccurrenceCheckDigest
                || restored.successorOccurrenceCheckCoverageDigest != SuccessorOccurrenceCheckCoverageDigest
                || restored.successorSupportDigest != SuccessorOccurrenceDigest))
            throw new InvalidDataException("policy-boundary rail metadata lost successor scope custody");
        if (!Enum.IsDefined(restored.executionOutcome) || restored.requestCount < 0 || restored.guardAdmittedCount < 0
            || restored.guardAdmittedCount > restored.requestCount
            || restored.executionOutcome == CortexPolicyTrialExecutionOutcomes.NotAttempted && (restored.requestCount != 0 || restored.guardAdmittedCount != 0)
            || restored.executionOutcome == CortexPolicyTrialExecutionOutcomes.GuardDenied && (restored.requestCount == 0 || restored.guardAdmittedCount != 0))
            throw new InvalidDataException("policy-boundary rail metadata carries invalid execution accounting");
        if (restored.requestCount == 0 && (restored.lastRequestDecisionID != 0 || restored.lastRequestStep != -1)
            || restored.requestCount > 0 && (restored.lastRequestDecisionID == 0 || restored.lastRequestStep < 0))
            throw new InvalidDataException("policy-boundary rail metadata carries invalid last-request identity");
        bool restoredIdentityRequired = restored.executionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted
            || restored.guardAdmittedCount > 0;
        if (restoredIdentityRequired && (restored.executedDecisionID == 0 || restored.executedStep < restored.sourceNextStep
            || restored.executedStep > restored.terminalStep || restored.executedReadoutFingerprint == 0 || restored.executedReadoutRevision == 0))
            throw new InvalidDataException("policy-boundary rail metadata lost its executed decision identity");
        if (restored.executedSelectionCause == CortexPolicySelectionCauses.TrialOverride
            && (restored.executedDecisionEventID <= 0 || restored.forcedDivergenceSeed == 0))
            throw new InvalidDataException("policy-boundary rail metadata lost TrialOverride event or forced-seed custody");
        if ((restored.executedOutcomeEventID == 0) != (restored.executedOutcomePayloadSHA256.Length == 0)
            || restored.executedOutcomeEventID < 0
            || restored.executedOutcomePayloadSHA256.Length != 0
                && (restored.executedOutcomePayloadSHA256.Length != 64
                    || restored.executedOutcomePayloadSHA256.Any(static c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))))
            throw new InvalidDataException("policy-boundary rail metadata carries malformed ordinary outcome identity");
        if (restored.ordinaryOutcomeRequired
            && (restored.executedOutcomeEventID <= 0 || restored.executedOutcomePayloadSHA256.Length != 64))
            throw new InvalidDataException("policy-boundary rail metadata lost required ordinary outcome custody");
        if (!restored.ordinaryOutcomeRequired
            && (restored.executedOutcomeEventID != 0 || restored.executedOutcomePayloadSHA256.Length != 0))
            throw new InvalidDataException("policy-boundary rail metadata carries unmarked ordinary outcome custody");
        byte[] second = RonSerializer.SerializeToUtf8(in restored);
        if (!first.AsSpan().SequenceEqual(second))
            throw new InvalidDataException("policy-boundary rail metadata SaveLoadSave drifted");
        return first;
    }

    internal static CortexPolicyTrialExecutionOutcomes ResolveExecutionOutcome(PolicyBoundaryRailMetadataDocument document)
    {
        if (document.schemaVersion != 11 || document.executionAccountingMarker != 1)
            throw new InvalidDataException("policy-boundary rail metadata lacks its execution-accounting marker");
        return document.executionOutcome;
    }

    internal static bool ResolveChildProcessCompleted(PolicyBoundaryRailMetadataDocument document)
        => document.childProcessCompleted;

    internal static bool IsOrdinaryOutcomeMarkerValid(
        PolicyBoundaryRailMetadataDocument document,
        CortexPolicyTrialExecutionOutcomes executionOutcome)
    {
        bool carriesOutcomeIdentity = document.executedOutcomeEventID != 0
            || document.executedOutcomePayloadSHA256.Length != 0;
        if (!document.ordinaryOutcomeRequired) return !carriesOutcomeIdentity;
        return document.arm == PolicyBoundaryArms.ForcedDivergentNull
            && executionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted
            && document.executedSelectionCause == CortexPolicySelectionCauses.TrialOverride
            && document.executedOutcomeEventID > 0
            && IsLowerHexDigest(document.executedOutcomePayloadSHA256);
    }

    /// Convert one durable rail document into the receipt consumed by settlement and historical
    /// verification. Matched spend is supplied by the caller's authenticated accounting owner;
    /// the rail document is not allowed to override that authority during reconstruction.
    internal static PolicyBoundaryArmReceipt CreateArmReceipt(
        PolicyBoundaryRailMetadataDocument document,
        long matchedSpend,
        IPolicyBoundaryDomain domain)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(domain);
        if (matchedSpend < 0)
            throw new InvalidDataException("policy-boundary rail receipt has negative authenticated spend");

        // Schema 2 sealed the executed decision itself as the configured-execution marker.
        // Later schemas own an explicit execution outcome and accounting marker.
        CortexPolicyTrialExecutionOutcomes executionOutcome = document.schemaVersion == 2
            ? CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted
            : ResolveExecutionOutcome(document);
        if (document.schemaVersion != 2 && !IsOrdinaryOutcomeMarkerValid(document, executionOutcome))
            throw new InvalidDataException("policy-boundary rail metadata has invalid ordinary outcome custody");

        PolicyCanonicalStateID executedCanonicalState = ResolveExecutedCanonicalState(document, domain);
        return new PolicyBoundaryArmReceipt(
            document.arm,
            document.horizon,
            document.paidCloseDelta,
            matchedSpend,
            document.continuityExact,
            ResolveChildProcessCompleted(document),
            document.grammarExecutionsDelta,
            document.trialAdaptationTransitions,
            document.adaptationEnabled)
        {
            ExecutionOutcome = executionOutcome,
            RequestCount = document.requestCount,
            GuardAdmittedCount = document.guardAdmittedCount,
            LastRequestDecisionID = new CortexPolicyDecisionID(document.lastRequestDecisionID),
            LastRequestStep = document.lastRequestStep,
            LastRequestReadout = new CortexPolicyDecisionReadout(
                document.lastRequestLaunchpadAction,
                document.lastRequestRawCandidateAction,
                document.lastRequestSelectedCandidateAction,
                document.lastRequestExecutedAction,
                document.lastRequestAuthority,
                new GrammarRevisionID(document.lastRequestRevision),
                document.lastRequestSelectionCause,
                document.lastRequestSupportDigest,
                document.lastRequestCandidateFingerprint),
            ExecutedDecisionID = new CortexPolicyDecisionID(document.executedDecisionID),
            ExecutedStep = document.executedStep,
            ExecutedLaunchpadAction = document.executedLaunchpadAction,
            ExecutedRawCandidateAction = document.executedRawCandidateAction,
            ExecutedSelectedCandidateAction = document.executedSelectedCandidateAction,
            ExecutedAction = document.executedAction,
            ExecutedAuthority = document.executedAuthority,
            ExecutedSelectionCause = document.executedSelectionCause,
            ExecutedReadoutFingerprint = document.executedReadoutFingerprint,
            ExecutedReadoutRevision = document.executedReadoutRevision,
            ExecutedReadoutOccurrenceDigest = document.executedReadoutOccurrenceDigest,
            ExecutedCandidateFingerprint = document.executedCandidateFingerprint,
            ExecutedCanonicalState = executedCanonicalState,
            ExecutedDecisionEventID = new TapeEventID(document.executedDecisionEventID),
            ExecutedOutcomeEventID = new TapeEventID(document.executedOutcomeEventID),
            ExecutedOutcomePayloadSHA256 = document.executedOutcomePayloadSHA256,
            ForcedDivergenceSeed = document.forcedDivergenceSeed,
            Diverged = document.arm == PolicyBoundaryArms.ForcedDivergentNull
                && executionOutcome == CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted
                && document.guardAdmittedCount > 0
                && document.executedAction >= 0
                && document.executedAction != document.executedLaunchpadAction
                && document.executedAction != document.executedRawCandidateAction,
        };
    }

    private static PolicyCanonicalStateID ResolveExecutedCanonicalState(
        PolicyBoundaryRailMetadataDocument document,
        IPolicyBoundaryDomain domain)
    {
        if (document.executedCanonicalVersion == 0)
        {
            if (document.executedCanonicalPolicy.Length != 0
                || document.executedCanonicalKind != 0
                || document.executedCanonicalValue != 0)
                throw new InvalidDataException("policy-boundary rail metadata carries partial executed canonical state");
            return default;
        }
        if (document.executedCanonicalPolicy.Length == 0
            || !Enum.IsDefined((PolicyCanonicalStateKinds)document.executedCanonicalKind))
            throw new InvalidDataException("policy-boundary rail metadata carries malformed executed canonical state");
        try
        {
            PolicyCanonicalStateID state = new(
                new CortexPolicyID(document.executedCanonicalPolicy),
                (PolicyCanonicalStateKinds)document.executedCanonicalKind,
                document.executedCanonicalVersion,
                document.executedCanonicalValue);
            if (!domain.ValidateCanonicalState(in state))
                throw new InvalidDataException("policy-boundary rail metadata carries foreign executed canonical state");
            return state;
        }
        catch (ArgumentException error)
        {
            throw new InvalidDataException("policy-boundary rail metadata carries malformed executed canonical state", error);
        }
    }

    internal static bool VerifyRun89Fixture()
    {
        CortexPolicyDecisionReadout requestReadout = new(0, -1, -1, 0, CortexPolicyAuthorities.Launchpad,
            GrammarRevisionID.Zero, CortexPolicySelectionCauses.Launchpad);
        PolicyBoundaryTrialOutcome denied = new(0, 1, true)
        {
            ExecutionOutcome = CortexPolicyTrialExecutionOutcomes.GuardDenied,
            RequestCount = 1,
            GuardAdmittedCount = 0,
            LastRequestDecisionID = new CortexPolicyDecisionID(89),
            LastRequestStep = 270,
            LastRequestReadout = requestReadout,
        };
        PolicyBoundaryRailMetadata metadata = new(270, 16, PolicyBoundaryArms.Candidate, CortexForkRailRoles.Candidate,
            1, denied, 286, default, SourceRunID: "run89", SourceNextStep: 270);
        bool deniedRoundTrip = true;
        try { _ = metadata.Encode(HomeostatPolicyBoundaryDomain.Instance); } catch (InvalidDataException) { deniedRoundTrip = false; }
        PolicyBoundaryTrialOutcome launchpadSuppressedRawCandidate = new(0, 1, true)
        {
            ExecutionOutcome = CortexPolicyTrialExecutionOutcomes.NotAttempted,
            RequestCount = 0,
            GuardAdmittedCount = 0,
            LastRequestDecisionID = default,
            LastRequestStep = -1,
            LastRequestReadout = requestReadout,
        };
        PolicyBoundaryRailMetadata launchpadMetadata = new(270, 16, PolicyBoundaryArms.Baseline,
            CortexForkRailRoles.Baseline, 1, launchpadSuppressedRawCandidate, 286, default,
            SourceRunID: "launchpad", SourceNextStep: 270);
        bool launchpadZeroAccountingRoundTrip = false;
        try
        {
            byte[] encoded = launchpadMetadata.Encode(HomeostatPolicyBoundaryDomain.Instance);
            PolicyBoundaryRailMetadataDocument restoredLaunchpad = RonSerializer.Deserialize<PolicyBoundaryRailMetadataDocument>(encoded);
            launchpadZeroAccountingRoundTrip = restoredLaunchpad.executionOutcome == CortexPolicyTrialExecutionOutcomes.NotAttempted
                && restoredLaunchpad.requestCount == 0
                && restoredLaunchpad.guardAdmittedCount == 0
                && restoredLaunchpad.lastRequestDecisionID == 0
                && restoredLaunchpad.lastRequestStep == -1;
        }
        catch (InvalidDataException) { }
        bool rejectsAdmissionWithoutCorroboration = false;
        try { _ = (metadata with { Outcome = denied with { GuardAdmittedCount = 1 } }).Encode(HomeostatPolicyBoundaryDomain.Instance); }
        catch (InvalidDataException) { rejectsAdmissionWithoutCorroboration = true; }
        bool rejectsConfiguredWithoutCorroboration = false;
        try { _ = (metadata with { Outcome = denied with { ExecutionOutcome = CortexPolicyTrialExecutionOutcomes.ConfiguredCauseExecuted } }).Encode(HomeostatPolicyBoundaryDomain.Instance); }
        catch (InvalidDataException) { rejectsConfiguredWithoutCorroboration = true; }
        bool rejectsPartialIdentity = false;
        try { _ = (metadata with { Outcome = denied with { ExecutedDecisionID = new CortexPolicyDecisionID(89) } }).Encode(HomeostatPolicyBoundaryDomain.Instance); }
        catch (InvalidDataException) { rejectsPartialIdentity = true; }
        PolicyBoundaryRailMetadataDocument legacy = new() { schemaVersion = 4, executedDecisionID = 89 };
        bool legacyRejected = false;
        try { _ = ResolveExecutionOutcome(legacy); }
        catch (InvalidDataException) { legacyRejected = true; }
        bool rejectsStrippedLegacy = false;
        try { _ = ResolveExecutionOutcome(new PolicyBoundaryRailMetadataDocument { schemaVersion = 4 }); }
        catch (InvalidDataException) { rejectsStrippedLegacy = true; }
        bool rejectsOmittedAccountingMarker = false;
        try { _ = ResolveExecutionOutcome(new PolicyBoundaryRailMetadataDocument { schemaVersion = 5, executionOutcome = CortexPolicyTrialExecutionOutcomes.GuardDenied }); }
        catch (InvalidDataException) { rejectsOmittedAccountingMarker = true; }
        return deniedRoundTrip && launchpadZeroAccountingRoundTrip && rejectsAdmissionWithoutCorroboration && rejectsConfiguredWithoutCorroboration && rejectsPartialIdentity
            && legacyRejected && rejectsStrippedLegacy && rejectsOmittedAccountingMarker
            && TapePacketCreator.VerifyPolicyOutcomeCodecFixture();
    }
}

[RonObject]
internal partial class PolicyBoundaryRailMetadataDocument
{
    public int schemaVersion = 11;
    public string obligationID = "";
    public string candidateBoundary = "0";
    public int step;
    public int horizon;
    public PolicyBoundaryArms arm;
    public CortexForkRailRoles railRole;
    public ulong readoutFingerprint;
    public long paidCloseDelta;
    public long matchedSpend;
    public bool continuityExact;
    [RonAlias("powered")]
    public bool childProcessCompleted;
    public int terminalStep;
    public long grammarExecutionsDelta;
    public long trialAdaptationTransitions;
    public bool adaptationEnabled;
    public int executionAccountingMarker;
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
    public long executedDecisionEventID;
    public long executedOutcomeEventID;
    public string executedOutcomePayloadSHA256 = "";
    public bool ordinaryOutcomeRequired;
    public ulong forcedDivergenceSeed;
    public string executedCanonicalPolicy = "";
    public byte executedCanonicalKind;
    public ushort executedCanonicalVersion;
    public ulong executedCanonicalValue;
    public ulong successorSupportDigest;
    public string successorCanonicalPolicy = "";
    public byte successorCanonicalKind;
    public ushort successorCanonicalVersion;
    public ulong successorCanonicalValue;
    public string successorOccurrenceCheckDigest = "";
    public string successorOccurrenceCheckCoverageDigest = "";
    public string materializationParentRunID = "";
    public string materializationAttemptID = "";
    public string materializationChildRunID = "";
    public string materializationColdSeedDigest = "";
    public string sourceRunID = "";
    public int sourceNextStep = -1;
    public string custodyDigest = "";
    public string sourceCheckpointSHA256 = "";
    public string sourceTapeSpanlogSHA256 = "";
    public string sourceCurveSHA256 = "";
    public string sourceExcursionsSHA256 = "";
    public int generation = -1;
}
