namespace Cogito;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Cogito.Grammar;

public enum OrganicComparisonOutcomeKinds : byte
{
    ReadoutQuotaDenied,
    ReadoutCompletedNoMatch,
    CandidateAgreement,
    CandidateDivergence,
}

/// Custody for one ordinary Homeostat policy comparison. This receipt is a
/// measurement/custody companion to POLICY-DECISION; it is never grammar input.
public readonly record struct OrganicComparisonReceipt(
    int Step,
    CortexPolicyID Policy,
    CortexPolicyDecisionID DecisionID,
    TapeEventID SourceDecisionEventID,
    string SourceDecisionPayloadSHA256,
    string SourceDecisionJournalSHA256,
    GrammarRevisionID ReadoutRevision,
    ulong ReadoutFingerprint,
    ulong CandidateFingerprint,
    ulong CandidateOccurrenceDigest,
    int LaunchpadAction,
    int RawCandidateAction,
    int SelectedCandidateAction,
    OrganicComparisonOutcomeKinds Outcome,
    CortexPolicyQuotaDecisionID? QuotaDecisionID,
    CortexPolicyQuotaDecisions? FundingDecision,
    string FundingJournalRowSHA256,
    string SettlementJournalRowSHA256,
    string CanonicalReceiptSHA256)
{
    public bool HasFundingDecision => QuotaDecisionID is { Value: > 0 };

    public void Validate()
    {
        if (Step < 0 || Policy.Value.Length == 0 || DecisionID.Value == 0 || SourceDecisionEventID.Value < 0)
            throw new InvalidDataException("organic comparison identity is malformed");
        ValidateSHA256(SourceDecisionPayloadSHA256, nameof(SourceDecisionPayloadSHA256));
        ValidateSHA256(SourceDecisionJournalSHA256, nameof(SourceDecisionJournalSHA256));
        ValidateSHA256(CanonicalReceiptSHA256, nameof(CanonicalReceiptSHA256));
        bool candidate = RawCandidateAction >= 0 || SelectedCandidateAction >= 0;
        if (candidate != (RawCandidateAction >= 0 && SelectedCandidateAction >= 0))
            throw new InvalidDataException("organic comparison candidate actions must be jointly present");
        if (candidate)
        {
            if (CandidateFingerprint == 0 || CandidateOccurrenceDigest == 0)
                throw new InvalidDataException("organic comparison candidate identity is incomplete");
        }
        else if (CandidateFingerprint != 0 || CandidateOccurrenceDigest != 0)
            throw new InvalidDataException("organic comparison candidate identity is present without a candidate");
        if (LaunchpadAction < 0 || (candidate && (RawCandidateAction < 0 || SelectedCandidateAction < 0)))
            throw new InvalidDataException("organic comparison actions are malformed");
        if (QuotaDecisionID is { Value: 0 })
            throw new InvalidDataException("organic comparison funding identity is zero");
        if (HasFundingDecision && FundingJournalRowSHA256.Length != 64)
            throw new InvalidDataException("organic comparison funding identity lacks its journal row digest");
        if (!HasFundingDecision && FundingDecision is not null)
            throw new InvalidDataException("organic comparison funding status lacks its funding identity");
        switch (Outcome)
        {
            case OrganicComparisonOutcomeKinds.ReadoutQuotaDenied:
                if (candidate || FundingDecision != CortexPolicyQuotaDecisions.Denied || !HasFundingDecision
                    || FundingJournalRowSHA256.Length != 64 || SettlementJournalRowSHA256.Length != 0)
                    throw new InvalidDataException("funding-denied comparison does not bind its denied row");
                break;
            case OrganicComparisonOutcomeKinds.ReadoutCompletedNoMatch:
                if (candidate || FundingDecision is not (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused)
                    || !HasFundingDecision || FundingJournalRowSHA256.Length != 64 || SettlementJournalRowSHA256.Length != 64)
                    throw new InvalidDataException("completed-no-match comparison does not bind funding and settlement");
                break;
            case OrganicComparisonOutcomeKinds.CandidateAgreement:
                if (!candidate || RawCandidateAction != LaunchpadAction || FundingDecision == CortexPolicyQuotaDecisions.Denied
                    || SettlementJournalRowSHA256.Length != 0)
                    throw new InvalidDataException("candidate agreement is not a raw-vs-launchpad agreement");
                break;
            case OrganicComparisonOutcomeKinds.CandidateDivergence:
                if (!candidate || RawCandidateAction == LaunchpadAction || FundingDecision == CortexPolicyQuotaDecisions.Denied
                    || SettlementJournalRowSHA256.Length != 0)
                    throw new InvalidDataException("candidate divergence is not a raw-vs-launchpad divergence");
                break;
            default:
                throw new InvalidDataException("unknown organic comparison outcome");
        }
        if (FundingJournalRowSHA256.Length is not (0 or 64) || SettlementJournalRowSHA256.Length is not (0 or 64))
            throw new InvalidDataException("organic comparison journal row digest is malformed");
        if (!string.Equals(CanonicalReceiptSHA256, ComputeCanonicalReceiptSHA256(this), StringComparison.Ordinal))
            throw new InvalidDataException("organic comparison canonical receipt digest does not match its fields");
    }

    internal static string ComputeCanonicalReceiptSHA256(in OrganicComparisonReceipt receipt)
    {
        string material = string.Join('|', receipt.Step.ToString(CultureInfo.InvariantCulture), receipt.Policy.Value,
            receipt.DecisionID.Value.ToString(CultureInfo.InvariantCulture), receipt.SourceDecisionEventID.Value.ToString(CultureInfo.InvariantCulture),
            receipt.SourceDecisionPayloadSHA256, receipt.SourceDecisionJournalSHA256, receipt.ReadoutRevision.Value.ToString(CultureInfo.InvariantCulture),
            receipt.ReadoutFingerprint.ToString("X16", CultureInfo.InvariantCulture), receipt.CandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture),
            receipt.CandidateOccurrenceDigest.ToString("X16", CultureInfo.InvariantCulture), receipt.LaunchpadAction.ToString(CultureInfo.InvariantCulture),
            receipt.RawCandidateAction.ToString(CultureInfo.InvariantCulture), receipt.SelectedCandidateAction.ToString(CultureInfo.InvariantCulture),
            receipt.Outcome, receipt.QuotaDecisionID?.Value.ToString(CultureInfo.InvariantCulture) ?? "", receipt.FundingDecision,
            receipt.FundingJournalRowSHA256, receipt.SettlementJournalRowSHA256, "organic-comparison-v1");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    internal static void ValidateSHA256(string value, string name)
    {
        if (value.Length != 64 || !value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F'))
            throw new InvalidDataException($"{name} is not a SHA-256 digest");
    }
}
