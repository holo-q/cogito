namespace Cogito;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// The native crawler's preference comparison is a durable measurement.  It is
/// intentionally separate from a loop link: an agreement or an unavailable
/// learned candidate is liveness evidence, while only a present candidate whose
/// species, canonical action, and digest differ may become PreferenceDivergence.
public enum RepositoryPreferenceComparisonOutcomes : byte
{
    ComparisonNotAttempted,
    CandidateUnavailable,
    ReflexAgreement,
    Diverged,
}

public readonly record struct RepositorySelectionReceipt(
    int Step,
    CortexPolicyID PolicyID,
    CortexPolicyDecisionID DecisionID,
    TapeEventID DecisionEventID,
    string DecisionPayloadSHA256,
    ulong ReadoutFingerprint,
    ulong ReadoutCandidateFingerprint,
    RepositoryFrontierRevision FrontierRevision,
    string FrontierAuthoritySHA256,
    int SelectionOrdinal,
    RepositoryCandidateSpecies CandidateSpecies,
    string CandidateCanonical,
    RepositoryCandidateDigest CandidateDigest,
    string ReceiptSHA256) : IRepositoryLineageReceipt
{
    public string Kind => "selection";
    public string Canonical => RepositoryLineageReceiptCodec.Join(
        RepositoryLineageReceiptCodec.I(Step), PolicyID.Value,
        DecisionID.Value.ToString(CultureInfo.InvariantCulture), DecisionEventID.Value.ToString(CultureInfo.InvariantCulture),
        DecisionPayloadSHA256, ReadoutFingerprint.ToString(CultureInfo.InvariantCulture),
        ReadoutCandidateFingerprint.ToString(CultureInfo.InvariantCulture), FrontierRevision.Value.ToString(CultureInfo.InvariantCulture),
        FrontierAuthoritySHA256, SelectionOrdinal.ToString(CultureInfo.InvariantCulture), CandidateSpecies.ToString(),
        CandidateCanonical, CandidateDigest.ToString());

    public static RepositorySelectionReceipt Create(
        int step, CortexPolicyID policyID, in CortexPolicyDecision decision, TapeEventID decisionEventID,
        string decisionPayloadSHA256, RepositoryCandidateProposal proposal, string frontierAuthoritySHA256,
        int selectionOrdinal)
    {
        RepositorySelectionReceipt receipt = new(step, policyID, decision.DecisionID, decisionEventID,
            decisionPayloadSHA256, decision.ReadoutIdentity.Value, decision.Readout.CandidateFingerprint,
            proposal.Revision, frontierAuthoritySHA256, selectionOrdinal, proposal.Candidate.Species,
            proposal.Candidate.Canonical, proposal.Candidate.Digest, "");
        receipt = receipt with { ReceiptSHA256 = RepositoryLineageReceiptCodec.Digest(receipt.Kind, receipt.Canonical) };
        receipt.Validate();
        return receipt;
    }

    public void Validate()
    {
        if (Step < 0 || string.IsNullOrWhiteSpace(PolicyID.Value) || DecisionID.Value == 0 || DecisionEventID.Value <= 0
            || !RepositoryLineageReceiptCodec.IsSHA(DecisionPayloadSHA256) || ReadoutFingerprint == 0
            || !FrontierRevision.IsValid || !RepositoryLineageReceiptCodec.IsSHA(FrontierAuthoritySHA256) || SelectionOrdinal < 0
            || !Enum.IsDefined(CandidateSpecies) || string.IsNullOrWhiteSpace(CandidateCanonical) || !CandidateDigest.IsValid)
            throw new InvalidDataException("repository selection receipt identity is malformed");
        if (!RepositoryCandidate.TryParseCanonical(CandidateCanonical, out RepositoryCandidate candidate)
            || candidate.Species != CandidateSpecies || candidate.Digest != CandidateDigest)
            throw new InvalidDataException("repository selection candidate identity diverges");
        RepositoryLineageReceiptCodec.RequireSHA(ReceiptSHA256, "repository selection receipt");
        if (ReceiptSHA256 != RepositoryLineageReceiptCodec.Digest(Kind, Canonical))
            throw new InvalidDataException("repository selection receipt digest diverges");
    }

    internal static bool TryDecode(ReadOnlySpan<byte> payload, out RepositorySelectionReceipt receipt)
    {
        receipt = default;
        if (!TapePacketCreator.TryReadRepositoryLineageReceipt(payload, out string kind, out string canonical, out string digest)
            || kind != "selection" || !RepositoryLineageReceiptCodec.TrySplit(canonical, out string[] fields) || fields.Length != 13)
            return false;
        try
        {
            if (!int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int step)
                || !ulong.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out ulong decision)
                || !long.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long decisionEvent)
                || !ulong.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out ulong readout)
                || !ulong.TryParse(fields[6], NumberStyles.None, CultureInfo.InvariantCulture, out ulong candidateFingerprint)
                || !ulong.TryParse(fields[7], NumberStyles.None, CultureInfo.InvariantCulture, out ulong revision)
                || !int.TryParse(fields[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ordinal)
                || !Enum.TryParse(fields[10], out RepositoryCandidateSpecies species)
                // A candidate digest renders X16 (RepositoryCandidateDigest.ToString); decoding it as
                // DECIMAL refuses every receipt whose digest carries a hex letter — roughly all of
                // them — and the refusal only surfaces at the terminal capture, a whole run later.
                || !ulong.TryParse(fields[12], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong candidateDigest))
                return false;
            receipt = new(step, new CortexPolicyID(fields[1]), new CortexPolicyDecisionID(decision), new TapeEventID(decisionEvent),
                fields[4], readout, candidateFingerprint, new RepositoryFrontierRevision(revision), fields[8], ordinal,
                species, fields[11], new RepositoryCandidateDigest(candidateDigest), digest);
            receipt.Validate();
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or FormatException or OverflowException)
        {
            receipt = default;
            return false;
        }
    }
}

public readonly record struct RepositoryPreferenceComparisonReceipt(
    int Step,
    CortexPolicyID PolicyID,
    CortexPolicyDecisionID DecisionID,
    RepositoryFrontierRevision FrontierRevision,
    string FrontierAuthoritySHA256,
    RepositoryCandidateSpecies LaunchpadSpecies,
    string LaunchpadCanonical,
    RepositoryCandidateDigest LaunchpadDigest,
    bool LearnedCandidatePresent,
    RepositoryCandidateSpecies LearnedSpecies,
    string LearnedCanonical,
    RepositoryCandidateDigest LearnedDigest,
    TapeEventID SelectionEventID,
    string SelectionPayloadSHA256,
    string SelectionJournalSHA256,
    RepositoryPreferenceComparisonOutcomes Outcome,
    string ReceiptSHA256) : IRepositoryLineageReceipt
{
    public string Kind => "preference-comparison";

    public string Canonical => RepositoryLineageReceiptCodec.Join(
        RepositoryLineageReceiptCodec.I(Step), PolicyID.Value,
        DecisionID.Value.ToString(CultureInfo.InvariantCulture), FrontierRevision.Value.ToString(CultureInfo.InvariantCulture),
        FrontierAuthoritySHA256, LaunchpadSpecies.ToString(), LaunchpadCanonical, LaunchpadDigest.ToString(),
        LearnedCandidatePresent ? "1" : "0", LearnedSpecies.ToString(), LearnedCanonical, LearnedDigest.ToString(),
        SelectionEventID.Value.ToString(CultureInfo.InvariantCulture), SelectionPayloadSHA256, SelectionJournalSHA256, Outcome.ToString());

    public bool IsPreferenceDivergence => Outcome == RepositoryPreferenceComparisonOutcomes.Diverged
        && LearnedCandidatePresent
        && (LaunchpadSpecies != LearnedSpecies || !string.Equals(LaunchpadCanonical, LearnedCanonical, StringComparison.Ordinal)
            || LaunchpadDigest != LearnedDigest);

    public static RepositoryPreferenceComparisonReceipt Create(
        int step,
        CortexPolicyID policyID,
        CortexPolicyDecisionID decisionID,
        RepositoryFrontierRevision frontierRevision,
        string frontierAuthoritySHA256,
        in RepositoryCandidate launchpad,
        RepositoryCandidate? learned,
        TapeEventID selectionEventID,
        string selectionPayloadSHA256,
        string selectionJournalSHA256,
        RepositoryPreferenceComparisonOutcomes outcome)
    {
        RepositoryCandidateDigest learnedDigest = learned?.Digest ?? RepositoryCandidateDigest.Zero;
        RepositoryPreferenceComparisonReceipt receipt = new(step, policyID, decisionID, frontierRevision,
            frontierAuthoritySHA256, launchpad.Species, launchpad.Canonical, launchpad.Digest,
            learned is not null, learned?.Species ?? default, learned?.Canonical ?? "", learnedDigest,
            selectionEventID, selectionPayloadSHA256, selectionJournalSHA256, outcome, "");
        receipt = receipt with { ReceiptSHA256 = RepositoryLineageReceiptCodec.Digest(receipt.Kind, receipt.Canonical) };
        receipt.Validate();
        return receipt;
    }

    public void Validate()
    {
        if (Step < 0 || string.IsNullOrWhiteSpace(PolicyID.Value) || DecisionID.Value == 0
            || !FrontierRevision.IsValid || !RepositoryLineageReceiptCodec.IsSHA(FrontierAuthoritySHA256)
            || !Enum.IsDefined(LaunchpadSpecies) || string.IsNullOrWhiteSpace(LaunchpadCanonical) || !LaunchpadDigest.IsValid
            || SelectionEventID.Value <= 0 || !RepositoryLineageReceiptCodec.IsSHA(SelectionPayloadSHA256)
            || !RepositoryLineageReceiptCodec.IsSHA(SelectionJournalSHA256)
            || !Enum.IsDefined(Outcome))
            throw new InvalidDataException("repository preference comparison identity is malformed");
        if (!RepositoryCandidate.TryParseCanonical(LaunchpadCanonical, out RepositoryCandidate parsedLaunchpad))
            throw new InvalidDataException("repository preference launchpad canonical is not parseable");
        if (parsedLaunchpad.Species != LaunchpadSpecies || parsedLaunchpad.Digest != LaunchpadDigest)
            throw new InvalidDataException("repository preference launchpad candidate identity diverges");
        if (LearnedCandidatePresent)
        {
            if (!Enum.IsDefined(LearnedSpecies) || string.IsNullOrWhiteSpace(LearnedCanonical) || !LearnedDigest.IsValid)
                throw new InvalidDataException("repository preference learned candidate identity is malformed");
            if (!RepositoryCandidate.TryParseCanonical(LearnedCanonical, out RepositoryCandidate parsedLearned))
                throw new InvalidDataException("repository preference learned canonical is not parseable");
            if (parsedLearned.Species != LearnedSpecies || parsedLearned.Digest != LearnedDigest)
                throw new InvalidDataException("repository preference learned candidate identity diverges");
        }
        else if (LearnedSpecies != default || LearnedCanonical.Length != 0 || LearnedDigest.IsValid
            || Outcome == RepositoryPreferenceComparisonOutcomes.Diverged)
            throw new InvalidDataException("repository preference absent candidate carries learned identity");
        bool differs = LearnedCandidatePresent && (LaunchpadSpecies != LearnedSpecies
            || !string.Equals(LaunchpadCanonical, LearnedCanonical, StringComparison.Ordinal) || LaunchpadDigest != LearnedDigest);
        if (Outcome == RepositoryPreferenceComparisonOutcomes.Diverged != differs)
            throw new InvalidDataException("repository preference outcome does not match candidate identity relation");
        if (Outcome == RepositoryPreferenceComparisonOutcomes.ReflexAgreement && (!LearnedCandidatePresent || differs)
            || Outcome == RepositoryPreferenceComparisonOutcomes.CandidateUnavailable && LearnedCandidatePresent)
            throw new InvalidDataException("repository preference typed liveness outcome is inconsistent");
        RepositoryLineageReceiptCodec.RequireSHA(ReceiptSHA256, "preference comparison receipt");
        if (ReceiptSHA256 != RepositoryLineageReceiptCodec.Digest(Kind, Canonical))
            throw new InvalidDataException("repository preference comparison receipt digest diverges");
    }

    internal static bool TryDecode(ReadOnlySpan<byte> payload, out RepositoryPreferenceComparisonReceipt receipt)
    {
        receipt = default;
        if (!TapePacketCreator.TryReadRepositoryLineageReceipt(payload, out string kind, out string canonical, out string digest)
            || !string.Equals(kind, "preference-comparison", StringComparison.Ordinal)
            || !RepositoryLineageReceiptCodec.TrySplit(canonical, out string[] fields) || fields.Length != 16)
            return false;
        try
        {
            if (!int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int step)
                || !ulong.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out ulong decision)
                || !ulong.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out ulong revision)
                || !Enum.TryParse(fields[5], out RepositoryCandidateSpecies launchpadSpecies)
                || !ulong.TryParse(fields[7], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong launchpadDigest)
                || (fields[8] != "0" && fields[8] != "1")
                || !Enum.TryParse(fields[9], out RepositoryCandidateSpecies learnedSpecies)
                || !ulong.TryParse(fields[11], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong learnedDigest)
                || !long.TryParse(fields[12], NumberStyles.Integer, CultureInfo.InvariantCulture, out long selectionEvent)
                || !Enum.TryParse(fields[15], out RepositoryPreferenceComparisonOutcomes outcome))
                return false;
            bool learnedPresent = fields[8] == "1";
            receipt = new(step, new CortexPolicyID(fields[1]), new CortexPolicyDecisionID(decision),
                new RepositoryFrontierRevision(revision), fields[4], launchpadSpecies, fields[6],
                new RepositoryCandidateDigest(launchpadDigest), learnedPresent, learnedSpecies, fields[10],
                new RepositoryCandidateDigest(learnedDigest), new TapeEventID(selectionEvent), fields[13], fields[14], outcome, digest);
            receipt.Validate();
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or FormatException or OverflowException)
        {
            receipt = default;
            return false;
        }
    }
}
