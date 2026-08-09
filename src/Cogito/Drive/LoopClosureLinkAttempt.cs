namespace Cogito;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ronmamon;

/// One append-only observation of a loop-closure gate. The event, journal,
/// fork, and divergence identities are carried together so a report can derive
/// lifetime liveness from durable custody rather than trial-local state.
/// Child custody is a separate namespace from the parent tape. The parent
/// link may point at a typed reference event, while this record identifies the
/// ordinary outcome that actually ran in the child arm.
public readonly record struct LoopClosureChildOutcomeReference(
    string RunID,
    string RelativePath,
    LoopClosureDigest AuthoritySHA256,
    LoopClosureDigest RailSHA256,
    CortexPolicyDecisionID ForcedDecisionID,
    TapeEventID OutcomeEventID,
    LoopClosureDigest OutcomePayloadSHA256,
    bool BeforeSeal)
{
    public bool IsPresent => !string.IsNullOrWhiteSpace(RunID) || !string.IsNullOrWhiteSpace(RelativePath)
        || AuthoritySHA256.IsValid || RailSHA256.IsValid || ForcedDecisionID.Value != 0
        || OutcomeEventID.Value != 0 || OutcomePayloadSHA256.IsValid || BeforeSeal;

    public void Validate(bool required)
    {
        if (!required && !IsPresent) return;
        if (string.IsNullOrWhiteSpace(RunID) || string.IsNullOrWhiteSpace(RelativePath)
            || !AuthoritySHA256.IsValid || !RailSHA256.IsValid || ForcedDecisionID.Value == 0
            || OutcomeEventID.Value <= 0 || !OutcomePayloadSHA256.IsValid || !BeforeSeal)
            throw new InvalidDataException("child outcome reference omits authenticated pre-seal custody");
        if (Path.IsPathRooted(RelativePath) || RelativePath.Contains("..", StringComparison.Ordinal))
        throw new InvalidDataException("child outcome reference path escapes the parent run");
    }

    internal static LoopClosureChildOutcomeReference Canonicalize(LoopClosureChildOutcomeReference value)
        => !value.IsPresent
            ? default
            : new(value.RunID ?? "", value.RelativePath ?? "",
                new(value.AuthoritySHA256.Value ?? ""), new(value.RailSHA256.Value ?? ""), value.ForcedDecisionID,
                value.OutcomeEventID, new(value.OutcomePayloadSHA256.Value ?? ""), value.BeforeSeal);
}

public readonly record struct LoopClosureLinkAttempt(
    string RecordID,
    string RunID,
    LoopClosureLinkSpecies Species,
    LoopClosureLinkPaths Path,
    LoopClosureLinkStates State,
    int Step,
    TapeEventID EventID,
    LoopClosureDigest EvidenceSHA256,
    long PredecessorEventID,
    LoopClosureDigest PredecessorEvidenceSHA256,
    LoopClosureDigest JournalSHA256,
    LoopClosureGateDenialReasons DenialReason,
    bool HasDenialReason,
    LoopClosureQuotaID QuotaID,
    LoopClosureDigest ForkReceiptSHA256,
    LoopClosureDigest DivergenceEvidenceSHA256,
    Grammar.GrammarRevisionID GrammarRevision,
    LoopClosureDigest AttemptSHA256 = default,
    LoopClosureDigest PredecessorAttemptSHA256 = default,
    string EvidenceRunID = "",
    string EvidenceRelativePath = "",
    LoopClosureDigest EvidenceAuthoritySHA256 = default,
    LoopClosureDigest EvidenceRailSHA256 = default,
    LoopClosureChildOutcomeReference ChildOutcome = default,
    TapeEventID LinkEventID = default,
    LoopClosureDigest LinkPacketSHA256 = default,
    LoopClosureDigest LinkJournalSHA256 = default) : IRepositoryLineageReceipt
{
    public const int SchemaVersion = 2;
    /// Seal one attempt after all custody fields are known. The digest covers
    /// the declaration itself; it is not a self-authored substitute for the
    /// tape/journal/authority checks performed by the adjudicator.
    public static LoopClosureLinkAttempt Create(
        string recordID,
        string runID,
        LoopClosureLinkSpecies species,
        LoopClosureLinkPaths path,
        LoopClosureLinkStates state,
        int step,
        TapeEventID eventID,
        LoopClosureDigest evidenceSHA256,
        long predecessorEventID,
        LoopClosureDigest predecessorEvidenceSHA256,
        LoopClosureDigest journalSHA256,
        LoopClosureGateDenialReasons denialReason,
        bool hasDenialReason,
        LoopClosureQuotaID fundingID,
        LoopClosureDigest forkReceiptSHA256,
        LoopClosureDigest dissentEvidenceSHA256,
        Grammar.GrammarRevisionID grammarRevision,
        LoopClosureDigest predecessorAttemptSHA256 = default,
        string evidenceRunID = "",
        string evidenceRelativePath = "",
        LoopClosureDigest evidenceAuthoritySHA256 = default,
        LoopClosureDigest evidenceRailSHA256 = default,
        LoopClosureChildOutcomeReference childOutcome = default)
    {
        predecessorEvidenceSHA256 = new(predecessorEvidenceSHA256.Value ?? "");
        fundingID = new(fundingID.Value ?? "");
        forkReceiptSHA256 = new(forkReceiptSHA256.Value ?? "");
        dissentEvidenceSHA256 = new(dissentEvidenceSHA256.Value ?? "");
        predecessorAttemptSHA256 = new(predecessorAttemptSHA256.Value ?? "");
        evidenceRunID ??= "";
        evidenceRelativePath ??= "";
        evidenceAuthoritySHA256 = new(evidenceAuthoritySHA256.Value ?? "");
        evidenceRailSHA256 = new(evidenceRailSHA256.Value ?? "");
        childOutcome = LoopClosureChildOutcomeReference.Canonicalize(childOutcome);
        LoopClosureLinkAttempt attempt = new(recordID, runID, species, path, state, step, eventID,
            evidenceSHA256, predecessorEventID, predecessorEvidenceSHA256, journalSHA256, denialReason,
            hasDenialReason, fundingID, forkReceiptSHA256, dissentEvidenceSHA256, grammarRevision,
            default, predecessorAttemptSHA256, evidenceRunID, evidenceRelativePath,
            evidenceAuthoritySHA256, evidenceRailSHA256, childOutcome);
        attempt = attempt with { AttemptSHA256 = ComputeDigest(in attempt) };
        attempt.Validate();
        return attempt;
    }

    public string Kind => "loop-link";

    /// The packet canonical is the complete typed transition declaration.  Its
    /// digest is separate from the historical arm-attempt digest so old sealed
    /// attempt files remain readable while the in-run event gains full custody.
    public string Canonical => BuildCanonical(in this);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RecordID) || string.IsNullOrWhiteSpace(RunID)
            || !Enum.IsDefined(Species) || !Enum.IsDefined(Path) || !Enum.IsDefined(State)
            || Step < 0 || EventID.Value < 0 || !EvidenceSHA256.IsValid || !JournalSHA256.IsValid)
            throw new InvalidDataException("loop-closure link attempt identity is malformed");
        if (!string.IsNullOrWhiteSpace(EvidenceRunID)
            && (string.IsNullOrWhiteSpace(EvidenceRelativePath) || !EvidenceAuthoritySHA256.IsValid || !EvidenceRailSHA256.IsValid))
            throw new InvalidDataException("child-evidence link attempt omits its run, rail, or authority identity");
        if ((LinkEventID.Value == 0) != !LinkPacketSHA256.IsValid
            || (LinkEventID.Value == 0) != !LinkJournalSHA256.IsValid)
            throw new InvalidDataException("loop-closure link packet custody is incomplete");
        if (!AttemptSHA256.IsValid || AttemptSHA256 != ComputeDigest(in this))
            throw new InvalidDataException("loop-closure link attempt declaration digest is malformed or stale");
        ChildOutcome.Validate(Species == LoopClosureLinkSpecies.ExecutedDivergence && State == LoopClosureLinkStates.Admitted);
        if (Species == LoopClosureLinkSpecies.PreferenceDivergence)
        {
            if (Path != LoopClosureLinkPaths.Organic || PredecessorEventID >= 0 || !string.IsNullOrEmpty(PredecessorEvidenceSHA256.Value))
                throw new InvalidDataException("organic preference link carries a forced path or predecessor");
        }
        else
        {
            if (Path != LoopClosureLinkPaths.Forced || PredecessorEventID < 0 || !PredecessorEvidenceSHA256.IsValid
                || (Species != LoopClosureLinkSpecies.InterventionDivergence && !PredecessorAttemptSHA256.IsValid))
                throw new InvalidDataException("forced loop-closure link omits its chronological predecessor");
        }
        if (State == LoopClosureLinkStates.Admitted && HasDenialReason)
            throw new InvalidDataException("admitted loop-closure link carries a denial reason");
        if (State == LoopClosureLinkStates.Denied && (!HasDenialReason || !Enum.IsDefined(DenialReason)))
            throw new InvalidDataException("denied loop-closure link omits its typed denial reason");
        if (State == LoopClosureLinkStates.Denied && !LoopClosureGateLiveness.IsAllowedDenialReason(Species, DenialReason))
            throw new InvalidDataException("loop-closure link denial reason does not belong to its gate species");
        if (Species is LoopClosureLinkSpecies.AuthorityEligible or LoopClosureLinkSpecies.BoundaryAdmitted or LoopClosureLinkSpecies.ExecutedDivergence)
        {
            if (!QuotaID.IsValid || GrammarRevision.Value == 0)
                throw new InvalidDataException("forced loop-closure link omits funding/revision custody");
        }
        if (State == LoopClosureLinkStates.Admitted
            && Species is (LoopClosureLinkSpecies.BoundaryAdmitted or LoopClosureLinkSpecies.ExecutedDivergence))
        {
            if (!ForkReceiptSHA256.IsValid) throw new InvalidDataException("boundary link omits fork receipt custody");
        }
        if (State == LoopClosureLinkStates.Admitted && Species == LoopClosureLinkSpecies.ExecutedDivergence && !DivergenceEvidenceSHA256.IsValid)
            throw new InvalidDataException("executed divergence link omits divergence evidence custody");
        if (Species == LoopClosureLinkSpecies.ExecutedDivergence && State == LoopClosureLinkStates.Admitted
            && string.IsNullOrWhiteSpace(EvidenceRunID))
            throw new InvalidDataException("executed divergence link omits its child run custody");
    }

    internal LoopClosureLinkReceipt ToReceipt()
    {
        Validate();
        return new(Species, Path, State, AttemptSHA256,
            PredecessorAttemptSHA256, EventID.Value, ChildOutcome);
    }

    internal static LoopClosureDigest ComputeDigest(in LoopClosureLinkAttempt attempt)
    {
        string canonical = string.Join('\u001f',
            "loop-closure-link-attempt-v1", attempt.RecordID, attempt.RunID, LoopClosureLinkSpeciesWire.Format(attempt.Species),
            attempt.Path, attempt.State, attempt.Step, attempt.EventID.Value, attempt.EvidenceSHA256.Value,
            attempt.PredecessorEventID, attempt.PredecessorEvidenceSHA256.Value, attempt.JournalSHA256.Value,
            attempt.DenialReason, attempt.HasDenialReason ? 1 : 0, attempt.QuotaID.Value,
            attempt.ForkReceiptSHA256.Value, attempt.DivergenceEvidenceSHA256.Value, attempt.GrammarRevision.Value,
            attempt.PredecessorAttemptSHA256.Value, attempt.EvidenceRunID, attempt.EvidenceRelativePath,
            attempt.EvidenceAuthoritySHA256.Value, attempt.EvidenceRailSHA256.Value,
            attempt.ChildOutcome.RunID, attempt.ChildOutcome.RelativePath,
            attempt.ChildOutcome.AuthoritySHA256.Value, attempt.ChildOutcome.RailSHA256.Value,
            attempt.ChildOutcome.ForcedDecisionID.Value, attempt.ChildOutcome.OutcomeEventID.Value,
            attempt.ChildOutcome.OutcomePayloadSHA256.Value, attempt.ChildOutcome.BeforeSeal ? 1 : 0);
        return new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
    }

    private static string BuildCanonical(in LoopClosureLinkAttempt attempt)
    {
        return RepositoryLineageReceiptCodec.Join(
            "loop-closure-link-v1", attempt.RecordID, attempt.RunID, LoopClosureLinkSpeciesWire.Format(attempt.Species),
            attempt.Path.ToString(), attempt.State.ToString(), attempt.Step.ToString(CultureInfo.InvariantCulture), attempt.EventID.Value.ToString(CultureInfo.InvariantCulture), attempt.EvidenceSHA256.Value,
            attempt.PredecessorEventID.ToString(CultureInfo.InvariantCulture), attempt.PredecessorEvidenceSHA256.Value, attempt.JournalSHA256.Value,
            attempt.DenialReason.ToString(), (attempt.HasDenialReason ? 1 : 0).ToString(CultureInfo.InvariantCulture), attempt.QuotaID.Value,
            attempt.ForkReceiptSHA256.Value, attempt.DivergenceEvidenceSHA256.Value, attempt.GrammarRevision.Value.ToString(CultureInfo.InvariantCulture),
            attempt.PredecessorAttemptSHA256.Value, attempt.EvidenceRunID, attempt.EvidenceRelativePath,
            attempt.EvidenceAuthoritySHA256.Value, attempt.EvidenceRailSHA256.Value,
            attempt.ChildOutcome.RunID, attempt.ChildOutcome.RelativePath,
            attempt.ChildOutcome.AuthoritySHA256.Value, attempt.ChildOutcome.RailSHA256.Value,
            attempt.ChildOutcome.ForcedDecisionID.Value.ToString(CultureInfo.InvariantCulture), attempt.ChildOutcome.OutcomeEventID.Value.ToString(CultureInfo.InvariantCulture),
            attempt.ChildOutcome.OutcomePayloadSHA256.Value, (attempt.ChildOutcome.BeforeSeal ? 1 : 0).ToString(CultureInfo.InvariantCulture));
    }

}

internal static class LoopClosureLinkAttemptStore
{
    private const string Root = "loop-closure/link-attempts";

    internal static string RelativePath(string recordID) => Path.Combine(Root, recordID + ".ron");

    internal static void Write(Run run, in LoopClosureLinkAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(run);
        attempt.Validate();
        if (!string.Equals(attempt.RunID, Path.GetFileName(Path.GetFullPath(run.Dir)), StringComparison.Ordinal))
            throw new InvalidDataException("loop-closure link attempt run identity disagrees with its destination arm");
        if (attempt.RecordID.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || attempt.RecordID.Contains('/') || attempt.RecordID.Contains('\\'))
            throw new InvalidDataException("loop-closure link attempt record ID is not a safe filename");
        LoopClosureLinkAttemptRON document = ToRON(in attempt);
        byte[] first = RonSerializer.SerializeToUtf8(in document);
        byte[] second = RonSerializer.SerializeToUtf8(in document);
        if (!first.AsSpan().SequenceEqual(second)) throw new InvalidDataException("loop-closure link attempt encoding is nondeterministic");
        string relative = RelativePath(attempt.RecordID);
        string path = run.PathOf(relative);
        if (File.Exists(path))
        {
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(first))
                throw new IOException($"loop-closure link attempt already exists with different bytes: {relative}");
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        run.WriteAtomic(relative, stream => stream.Write(first));
    }

    internal static bool TryWrite(Run run, in LoopClosureLinkAttempt attempt)
    {
        Write(run, in attempt);
        return true;
    }

    internal static IReadOnlyList<LoopClosureLinkAttempt> Read(string directory, string runID)
    {
        string root = Path.Combine(Path.GetFullPath(directory), Root);
        if (!Directory.Exists(root)) return [];
        List<LoopClosureLinkAttempt> attempts = [];
        foreach (string path in Directory.EnumerateFiles(root, "*.ron", SearchOption.TopDirectoryOnly).OrderBy(static path => path, StringComparer.Ordinal))
        {
            LoopClosureLinkAttemptRON document = RonSerializer.Deserialize<LoopClosureLinkAttemptRON>(File.ReadAllBytes(path));
            if (document.schemaVersion != LoopClosureLinkAttempt.SchemaVersion)
                throw new InvalidDataException("loop-closure link attempt schema is unsupported");
            if (document.recordID != Path.GetFileNameWithoutExtension(path) || document.runID != runID)
                throw new InvalidDataException("loop-closure link attempt file identity disagrees with its arm");
            LoopClosureLinkAttempt attempt = FromRON(document);
            attempt.Validate();
            attempts.Add(attempt);
        }
        // The tape-identity key must carry EvidenceRunID: child tapes number their
        // events from zero, so a child-tape attempt and a parent-tape attempt of the
        // same species (InterventionDivergence exists in both forms) can collide on a
        // numeric EventID while naming entirely different events.  Keying on the run
        // namespace keeps those distinct instead of falsely rejecting one as a dup.
        if (attempts.Select(static attempt => attempt.RecordID).Distinct(StringComparer.Ordinal).Count() != attempts.Count
            || attempts.Select(static attempt => (attempt.Species, attempt.EvidenceRunID, attempt.EventID.Value)).Distinct().Count() != attempts.Count)
            throw new InvalidDataException("loop-closure link attempts contain duplicate record or tape event identities");
        return attempts;
    }

    internal static string DigestPayload(ReadOnlySpan<byte> payload)
        => Convert.ToHexStringLower(SHA256.HashData(payload));

    internal static bool TryReadTerminalPolicyOutcome(
        Tape tape,
        string journalPath,
        in LoopClosurePolicyBinding policy,
        CortexPolicyDecisionID decisionID,
        TapeEventID expectedEventID,
        string expectedPayloadSHA256)
        => TryReadTerminalPolicyOutcome(tape, journalPath, decisionID, expectedEventID,
            expectedPayloadSHA256, in policy);

    internal static bool TryReadTerminalPolicyOutcome(
        Tape tape,
        string journalPath,
        CortexPolicyDecisionID decisionID,
        TapeEventID expectedEventID,
        string expectedPayloadSHA256,
        in LoopClosurePolicyBinding policy)
    {
        policy.Validate();
        int matches = 0;
        bool identityMatches = false;
        CortexPolicyOutcomePacket outcomePacket = default;
        byte[]? outcomePayload = null;
        foreach (TapeEventView view in tape.GetEventViews())
        {
            if (!policy.MatchesSource(view.Source)
                || view.Provenance != Provenances.Execution
                || !tape.Resolve(view.Id, out byte[] payload)
                || !TapePacketCreator.TryDecodePolicyOutcome(payload, out CortexPolicyOutcomePacket packet)
                || !packet.DecisionID.Equals(decisionID)) continue;
            matches++;
            if (view.Id != expectedEventID
                || !string.Equals(TapePacketCreator.DigestPolicyOutcomePayload(payload), expectedPayloadSHA256, StringComparison.Ordinal))
                continue;
            identityMatches = true;
            outcomePacket = packet;
            outcomePayload = payload;
        }
        if (matches != 1 || !identityMatches || outcomePayload is null) return false;
        string expectedSource = policy.PolicyPacketSource;
        int journalMatches = 0;
        foreach (string line in File.ReadLines(journalPath))
        {
            string[] columns = line.Split('\t');
            if (columns.Length < 4 || columns[1] != "policy-outcome"
                || columns[2] != expectedEventID.ToString() || columns[3] != expectedSource)
                continue;
            if (!TapePacketCreator.TryReadPolicyOutcomeJournalRow(line, expectedEventID, expectedSource,
                    in outcomePacket, expectedPayloadSHA256, outcomePayload.Length, out _))
                return false;
            journalMatches++;
        }
        return journalMatches == 1;
    }

    /// Decode the forced child arm only when the packet and the sealed child rail
    /// describe the same authenticated TrialOverride execution. Organic readout
    /// decoding deliberately does not accept this cause; a child arm is evidence
    /// for the forced link, never a top-level divergence proof by itself.
    internal static bool TryDecodeForcedChildReadout(
        ReadOnlySpan<byte> payload,
        PolicyBoundaryRailMetadataDocument rail,
        TapeEventID eventID,
        int step,
        string fundingID,
        out CortexPolicyDecisionReadout readout)
    {
        readout = default;
        if (rail.arm != PolicyBoundaryArms.ForcedDivergentNull
            || rail.materializationAttemptID != fundingID
            || rail.executedDecisionEventID != eventID.Value
            || rail.executedStep != step
            || rail.executedDecisionID == 0
            || rail.forcedDivergenceSeed == 0)
            return false;
        try
        {
            CortexPolicyDecisionPacket packet = TapePacketCreator.DecodePolicyDecision(payload);
            readout = packet.Readout;
            return packet.DecisionID.Value == rail.executedDecisionID
                && readout.GrammarRevision.Value == rail.executedReadoutRevision
                && readout.ReadoutCandidateOccurrenceDigest == rail.executedReadoutOccurrenceDigest
                && readout.ReadoutCandidateFingerprint == rail.executedCandidateFingerprint
                && readout.LaunchpadAction == rail.executedLaunchpadAction
                && readout.RawCandidateAction == rail.executedRawCandidateAction
                && readout.SelectedCandidateAction == rail.executedSelectedCandidateAction
                && readout.ExecutedAction == rail.executedAction
                && readout.Authority == rail.executedAuthority
                && readout.SelectionCause == CortexPolicySelectionCauses.TrialOverride
                && rail.executedSelectionCause == CortexPolicySelectionCauses.TrialOverride
                && readout.SelectedCandidateAction == readout.ExecutedAction
                && readout.SelectedCandidateAction != readout.RawCandidateAction
                && readout.SelectedCandidateAction != readout.LaunchpadAction;
        }
        catch (InvalidDataException)
        {
            readout = default;
            return false;
        }
    }

    // A link keeps the immutable identity of its own journal row.  Hashing the
    // whole journal file made an earlier attempt stale as soon as a resumed run
    // appended the next row; this receipt is stable under append.
    internal static LoopClosureDigest DigestJournalReceipt(int step, string kind, long eventID)
    {
        string canonical = string.Join('|', "loop-closure-journal-row-v1", step, kind, eventID);
        return new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
    }

    internal static LoopClosureDigest DigestLoopClosureLinkJournalReceipt(int step, long eventID, int payloadLength)
    {
        string canonical = string.Join('|', "loop-closure-link-journal-row-v1", step, "mint", eventID,
            "repository:loop-link", payloadLength);
        return new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
    }

    internal static LoopClosureLinkAttemptRON ToRON(in LoopClosureLinkAttempt attempt)
        => new()
        {
            schemaVersion = LoopClosureLinkAttempt.SchemaVersion,
            recordID = attempt.RecordID, runID = attempt.RunID, species = LoopClosureLinkSpeciesWire.Format(attempt.Species), path = attempt.Path.ToString(), state = attempt.State.ToString(),
            step = attempt.Step, eventID = attempt.EventID.Value, evidenceSHA256 = attempt.EvidenceSHA256.Value, predecessorEventID = attempt.PredecessorEventID,
            predecessorEvidenceSHA256 = attempt.PredecessorEvidenceSHA256.Value, journalSHA256 = attempt.JournalSHA256.Value, denialReason = attempt.DenialReason.ToString(),
            hasDenialReason = attempt.HasDenialReason, fundingID = attempt.QuotaID.Value, forkReceiptSHA256 = attempt.ForkReceiptSHA256.Value,
            dissentEvidenceSHA256 = attempt.DivergenceEvidenceSHA256.Value, grammarRevision = attempt.GrammarRevision.Value,
            attemptSHA256 = attempt.AttemptSHA256.Value,
            predecessorAttemptSHA256 = attempt.PredecessorAttemptSHA256.Value,
            evidenceRunID = attempt.EvidenceRunID,
            evidenceRelativePath = attempt.EvidenceRelativePath,
            evidenceAuthoritySHA256 = attempt.EvidenceAuthoritySHA256.Value,
            evidenceRailSHA256 = attempt.EvidenceRailSHA256.Value,
            childOutcomeRunID = attempt.ChildOutcome.RunID,
            childOutcomeRelativePath = attempt.ChildOutcome.RelativePath,
            childOutcomeAuthoritySHA256 = attempt.ChildOutcome.AuthoritySHA256.Value,
            childOutcomeRailSHA256 = attempt.ChildOutcome.RailSHA256.Value,
            childOutcomeForcedDecisionID = attempt.ChildOutcome.ForcedDecisionID.Value,
            childOutcomeEventID = attempt.ChildOutcome.OutcomeEventID.Value,
            childOutcomePayloadSHA256 = attempt.ChildOutcome.OutcomePayloadSHA256.Value,
            childOutcomeBeforeSeal = attempt.ChildOutcome.BeforeSeal,
            linkEventID = attempt.LinkEventID.Value,
            linkPacketSHA256 = attempt.LinkPacketSHA256.Value,
            linkJournalSHA256 = attempt.LinkJournalSHA256.Value,
        };

    internal static LoopClosureLinkAttempt FromRON(LoopClosureLinkAttemptRON document)
    {
        if (document.schemaVersion != LoopClosureLinkAttempt.SchemaVersion)
            throw new InvalidDataException("loop-closure link attempt schema is unsupported");
        return new(document.recordID, document.runID, ParseSpecies(document.species), Parse<LoopClosureLinkPaths>(document.path), Parse<LoopClosureLinkStates>(document.state),
            document.step, new TapeEventID(document.eventID), new(document.evidenceSHA256), document.predecessorEventID, new(document.predecessorEvidenceSHA256), new(document.journalSHA256),
            Parse<LoopClosureGateDenialReasons>(document.denialReason), document.hasDenialReason, new(document.fundingID), new(document.forkReceiptSHA256), new(document.dissentEvidenceSHA256), new(document.grammarRevision), new(document.attemptSHA256), new(document.predecessorAttemptSHA256), document.evidenceRunID, document.evidenceRelativePath, new(document.evidenceAuthoritySHA256), new(document.evidenceRailSHA256),
            LoopClosureChildOutcomeReference.Canonicalize(new LoopClosureChildOutcomeReference(document.childOutcomeRunID, document.childOutcomeRelativePath,
                new(document.childOutcomeAuthoritySHA256), new(document.childOutcomeRailSHA256),
                new CortexPolicyDecisionID(document.childOutcomeForcedDecisionID), new TapeEventID(document.childOutcomeEventID),
                new(document.childOutcomePayloadSHA256), document.childOutcomeBeforeSeal)),
            new TapeEventID(document.linkEventID), new(document.linkPacketSHA256), new(document.linkJournalSHA256));
    }

    internal static byte[] EncodeCheckpoint(in LoopClosureLinkAttempt attempt)
    {
        attempt.Validate();
        LoopClosureLinkAttemptRON document = ToRON(in attempt);
        return RonSerializer.SerializeToUtf8(in document);
    }

    internal static LoopClosureLinkAttempt DecodeCheckpoint(ReadOnlySpan<byte> bytes)
    {
        LoopClosureLinkAttemptRON document = RonSerializer.Deserialize<LoopClosureLinkAttemptRON>(bytes.ToArray());
        LoopClosureLinkAttempt attempt = FromRON(document);
        attempt.Validate();
        return attempt;
    }

    private static T Parse<T>(string value) where T : struct, Enum
        => Enum.TryParse(value, out T result) ? result : throw new InvalidDataException($"loop-closure link attempt carries unknown {typeof(T).Name}");

    private static LoopClosureLinkSpecies ParseSpecies(string value)
        => LoopClosureLinkSpeciesWire.TryParse(value, out LoopClosureLinkSpecies species)
            ? species
            : throw new InvalidDataException("loop-closure link attempt carries unknown LoopClosureLinkSpecies");

    internal static bool VerifyCodecFixture()
    {
        string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("attempt")));
        string journal = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("journal")));
        LoopClosureLinkAttempt source = LoopClosureLinkAttempt.Create("attempt-0", "run", LoopClosureLinkSpecies.PreferenceDivergence, LoopClosureLinkPaths.Organic,
            LoopClosureLinkStates.Denied, 4, new TapeEventID(12), new(digest), -1, default, new(journal), LoopClosureGateDenialReasons.ReflexAgreement,
            true, default, default, default, default);
        LoopClosureLinkAttemptRON ron = ToRON(in source);
        byte[] first = RonSerializer.SerializeToUtf8(in ron);
        byte[] second = RonSerializer.SerializeToUtf8(in ron);
        LoopClosureLinkAttempt restored = FromRON(RonSerializer.Deserialize<LoopClosureLinkAttemptRON>(first));
        restored.Validate();
        LoopClosureLinkAttempt tampered = restored with { EventID = new TapeEventID(restored.EventID.Value + 1) };
        bool mutationRejected;
        try { tampered.Validate(); mutationRejected = false; }
        catch (InvalidDataException) { mutationRejected = true; }
        LoopClosureChildOutcomeReference child = new("fixture-child", "children/fixture-child",
            new LoopClosureDigest(new string('a', 64)), new LoopClosureDigest(new string('b', 64)),
            new CortexPolicyDecisionID(91), new TapeEventID(92), new LoopClosureDigest(new string('c', 64)), true);
        LoopClosureLinkAttempt populated = LoopClosureLinkAttempt.Create("attempt-populated", "run",
            LoopClosureLinkSpecies.ExecutedDivergence, LoopClosureLinkPaths.Forced, LoopClosureLinkStates.Admitted,
            5, new TapeEventID(13), new(digest), 12, new(journal), new(journal), default, false,
            new LoopClosureQuotaID("fixture-funding"), new(digest), new(journal), new Grammar.GrammarRevisionID(1),
            new(journal), "fixture-child", "children/fixture-child", child.AuthoritySHA256, child.RailSHA256, child);
        LoopClosureLinkAttemptRON populatedRon = ToRON(in populated);
        byte[] populatedFirst = RonSerializer.SerializeToUtf8(in populatedRon);
        byte[] populatedSecond = RonSerializer.SerializeToUtf8(in populatedRon);
        LoopClosureLinkAttempt populatedRestored = FromRON(RonSerializer.Deserialize<LoopClosureLinkAttemptRON>(populatedFirst));
        populatedRestored.Validate();
        bool populatedStable = populatedFirst.AsSpan().SequenceEqual(populatedSecond);
        bool populatedExact = populatedRestored == populated;
        bool stable = first.AsSpan().SequenceEqual(second);
        bool exact = restored == source;
        Console.WriteLine($"  link-attempt codec · stable={stable} exact={exact} mutation-rejected={mutationRejected}"
            + $" populated-stable={populatedStable} populated-exact={populatedExact}");
        return stable && exact && mutationRejected && populatedStable && populatedExact;
    }

    internal static bool VerifyCustodyFixture()
    {
        using Tape tape = new();
        Journal journal = new();
        CortexPolicyID policy = Homeostat.PolicyID;
        LoopClosurePolicyBinding policyBinding = HomeostatPolicyBoundaryDomain.Instance.PolicyBinding;
        LoopClosureQuotaID fundingID = new("fixture-funding");
        // Frozen fixture digest seed; identifier-side name is Divergence.
        LoopClosureDigest divergenceDigest = Digest("fixture-dissent");
        LoopClosureDigest forkDigest = Digest("fixture-fork");
        Grammar.GrammarRevisionID revision = new(1);
        (TapeEventID EventID, byte[] Payload) Append(string source, string kind, int step, string payload)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(payload);
            TapeEventID eventID = tape.Append(bytes, source, Provenances.Execution);
            journal.RecordLoopClosureLinkEvent(step, eventID, kind, source, bytes);
            return (eventID, bytes);
        }

        CortexPolicyDecisionReadout agreementReadout = new(
            1, 1, 1, 1, CortexPolicyAuthorities.Shadow, revision,
            CortexPolicySelectionCauses.ShadowCandidate,
            ReadoutCandidateOccurrenceDigest: 1, ReadoutCandidateFingerprint: 1);
        CortexPolicyDecision agreementDecision = new(new CortexPolicyDecisionID(10087), policy, agreementReadout);
        TapeEventID agreementID = TapePacketCreator.AppendPolicyDecision(
            tape, journal, 0, in agreementDecision,
            [new MetricSample(new MetricID(0), NumericValue.FromI64(0))], 2, out byte[] agreementPayload);
        LoopClosureLinkAttempt preference = LoopClosureLinkAttempt.Create(
            "fixture-preference", "fixture-run", LoopClosureLinkSpecies.PreferenceDivergence,
            LoopClosureLinkPaths.Organic, LoopClosureLinkStates.Denied, 0, agreementID,
            new(DigestPayload(agreementPayload)), -1, default,
            DigestJournalReceipt(0, "policy-decision", agreementID.Value),
            LoopClosureGateDenialReasons.ReflexAgreement, true, default, default, default, revision);

        TapeEventID censusID = TapePacketCreator.AppendLoopClosureOrganicOpportunityCensus(tape, journal, 1, policy, 0);
        tape.Resolve(censusID, out byte[] censusPayload);
        LoopClosureLinkAttempt censusPreference = LoopClosureLinkAttempt.Create(
            "fixture-preference-census", "fixture-run", LoopClosureLinkSpecies.PreferenceDivergence,
            LoopClosureLinkPaths.Organic, LoopClosureLinkStates.Denied, 1, censusID,
            new(DigestPayload(censusPayload)), -1, default,
            DigestJournalReceipt(1, "loop-closure-organic-opportunity", censusID.Value),
            LoopClosureGateDenialReasons.NoOrganicOpportunity, true, default, default, default, revision);
        LoopClosureLinkAttempt censusWithReflexReason = LoopClosureLinkAttempt.Create(
            "fixture-preference-census-reflex", "fixture-run", LoopClosureLinkSpecies.PreferenceDivergence,
            LoopClosureLinkPaths.Organic, LoopClosureLinkStates.Denied, 1, censusID,
            new(DigestPayload(censusPayload)), -1, default,
            DigestJournalReceipt(1, "policy-decision", censusID.Value),
            LoopClosureGateDenialReasons.ReflexAgreement, true, default, default, default, revision);
        LoopClosureLinkAttempt policyWithCensusReason = LoopClosureLinkAttempt.Create(
            "fixture-preference-policy-census", "fixture-run", LoopClosureLinkSpecies.PreferenceDivergence,
            LoopClosureLinkPaths.Organic, LoopClosureLinkStates.Denied, 0, agreementID,
            new(DigestPayload(agreementPayload)), -1, default,
            DigestJournalReceipt(0, "loop-closure-organic-opportunity", agreementID.Value),
            LoopClosureGateDenialReasons.NoOrganicOpportunity, true, default, default, default, revision);

        // Frozen tape source/payload tokens; identifier-side names are Paid/Divergence.
        (TapeEventID EventID, byte[] Payload) paid = Append(
            "policy-boundary:dissent", "mint", 2,
            $"POLICY-FUNDED-DISSENT\tfunding={fundingID}\tproof={divergenceDigest.Value}");
        LoopClosureLinkAttempt intervention = LoopClosureLinkAttempt.Create(
            "fixture-intervention", "fixture-run", LoopClosureLinkSpecies.InterventionDivergence,
            LoopClosureLinkPaths.Forced, LoopClosureLinkStates.Admitted, 2, paid.EventID,
            new(DigestPayload(paid.Payload)), preference.EventID.Value, preference.EvidenceSHA256,
            DigestJournalReceipt(2, "mint", paid.EventID.Value), default, false, fundingID, default,
            divergenceDigest, revision, preference.AttemptSHA256);

        (TapeEventID EventID, byte[] Payload) authority = Append(
            "policy:" + policy.Value, "policy-decision", 3,
            $"POLICY-DECISION\tdecision=fixture-authority\tfunding={fundingID}\tproof={divergenceDigest.Value}");
        LoopClosureLinkAttempt authorityLink = LoopClosureLinkAttempt.Create(
            "fixture-authority", "fixture-run", LoopClosureLinkSpecies.AuthorityEligible,
            LoopClosureLinkPaths.Forced, LoopClosureLinkStates.Admitted, 3, authority.EventID,
            new(DigestPayload(authority.Payload)), intervention.EventID.Value, intervention.EvidenceSHA256,
            DigestJournalReceipt(3, "policy-decision", authority.EventID.Value), default, false, fundingID,
            default, divergenceDigest, revision, intervention.AttemptSHA256);

        (TapeEventID EventID, byte[] Payload) boundary = Append(
            "policy:" + policy.Value, "policy-boundary", 4,
            $"POLICY-BOUNDARY\tfunding-id={fundingID}\tdigest={forkDigest.Value}\tproof={divergenceDigest.Value}");
        LoopClosureLinkAttempt boundaryLink = LoopClosureLinkAttempt.Create(
            "fixture-boundary", "fixture-run", LoopClosureLinkSpecies.BoundaryAdmitted,
            LoopClosureLinkPaths.Forced, LoopClosureLinkStates.Admitted, 4, boundary.EventID,
            new(DigestPayload(boundary.Payload)), authorityLink.EventID.Value, authorityLink.EvidenceSHA256,
            DigestJournalReceipt(4, "policy-boundary", boundary.EventID.Value), default, false, fundingID,
            forkDigest, divergenceDigest, revision, authorityLink.AttemptSHA256);

        (TapeEventID EventID, byte[] Payload) outcome = Append(
            "policy-boundary:outcome", "mint", 5,
            $"POLICY-BOUNDARY-OUTCOME\tfunding={fundingID}\tadjudication={divergenceDigest.Value}");
        LoopClosureLinkAttempt executed = LoopClosureLinkAttempt.Create(
            "fixture-executed", "fixture-run", LoopClosureLinkSpecies.ExecutedDivergence,
        LoopClosureLinkPaths.Forced, LoopClosureLinkStates.Admitted, 5, outcome.EventID,
            new(DigestPayload(outcome.Payload)), boundaryLink.EventID.Value, boundaryLink.EvidenceSHA256,
            DigestJournalReceipt(5, "mint", outcome.EventID.Value), default, false, fundingID,
            forkDigest, divergenceDigest, revision, boundaryLink.AttemptSHA256, "fixture-child", "children/fixture-child",
            Digest("fixture-authority"), Digest("fixture-rail"),
            new LoopClosureChildOutcomeReference("fixture-child", "children/fixture-child", Digest("fixture-authority"),
                Digest("fixture-rail"), new CortexPolicyDecisionID(10090), new TapeEventID(42), Digest("fixture-outcome"), true));

        CortexPolicyDecisionReadout absentCandidateReadout = new(
            1, -1, -1, 1, CortexPolicyAuthorities.Launchpad, revision,
            CortexPolicySelectionCauses.Launchpad);
        CortexPolicyDecision absentCandidateDecision = new(new CortexPolicyDecisionID(10088), policy, absentCandidateReadout);
        TapeEventID absentCandidateID = TapePacketCreator.AppendPolicyDecision(
            tape, journal, 6, in absentCandidateDecision,
            [new MetricSample(new MetricID(0), NumericValue.FromI64(0))], 2, out byte[] absentCandidatePayload);
        LoopClosureLinkAttempt absentCandidatePreference = LoopClosureLinkAttempt.Create(
            "fixture-preference-absent", "fixture-run", LoopClosureLinkSpecies.PreferenceDivergence,
            LoopClosureLinkPaths.Organic, LoopClosureLinkStates.Denied, 6, absentCandidateID,
            new(DigestPayload(absentCandidatePayload)), -1, default,
            DigestJournalReceipt(6, "policy-decision", absentCandidateID.Value),
            LoopClosureGateDenialReasons.CandidateUnavailable, true, default, default, default, revision);

        CortexPolicyDecisionReadout divergentReadout = new(
            1, 0, 1, 1, CortexPolicyAuthorities.Shadow, revision,
            CortexPolicySelectionCauses.ShadowCandidate,
            ReadoutCandidateOccurrenceDigest: 1, ReadoutCandidateFingerprint: 2);
        CortexPolicyDecision divergentDecision = new(new CortexPolicyDecisionID(10089), policy, divergentReadout);
        TapeEventID divergentID = TapePacketCreator.AppendPolicyDecision(
            tape, journal, 7, in divergentDecision,
            [new MetricSample(new MetricID(0), NumericValue.FromI64(0))], 2, out byte[] divergentPayload);
        LoopClosureLinkAttempt admittedPreference = LoopClosureLinkAttempt.Create(
            "fixture-preference-admitted", "fixture-run", LoopClosureLinkSpecies.PreferenceDivergence,
            LoopClosureLinkPaths.Organic, LoopClosureLinkStates.Admitted, 7, divergentID,
            new(DigestPayload(divergentPayload)), -1, default,
            DigestJournalReceipt(7, "policy-decision", divergentID.Value), default, false, default, default, default, revision);

        LoopClosureLinkAttempt[] attempts = [preference, intervention, authorityLink, boundaryLink, executed];
        LoopClosureLinkAttempt[] preferenceCases = [preference, censusPreference, absentCandidatePreference, admittedPreference];
        bool rowsAndPayloads = attempts.All(attempt =>
            tape.Resolve(attempt.EventID, out byte[] bytes)
            && DigestPayload(bytes) == attempt.EvidenceSHA256.Value
            && attempt.JournalSHA256 == DigestJournalReceipt(attempt.Step, ExpectedFixtureKind(attempt), attempt.EventID.Value)
            && journal.ResidentLines.Any(line =>
            {
                string[] fields = line.Split('\t');
                return fields.Length >= 3 && fields[1] == ExpectedFixtureKind(attempt)
                    && fields[2] == attempt.EventID.ToString();
            }));
        bool preferenceRowsAndPayloads = preferenceCases.All(attempt =>
            tape.Resolve(attempt.EventID, out byte[] bytes)
            && DigestPayload(bytes) == attempt.EvidenceSHA256.Value
            && attempt.JournalSHA256 == DigestJournalReceipt(attempt.Step, ExpectedFixtureKind(attempt), attempt.EventID.Value)
            && journal.ResidentLines.Any(line =>
            {
                string[] fields = line.Split('\t');
                return fields.Length >= 3 && fields[1] == ExpectedFixtureKind(attempt)
                    && fields[2] == attempt.EventID.ToString();
            }));
        bool denialSpeciesBound = preference.DenialReason == LoopClosureGateDenialReasons.ReflexAgreement
            && censusPreference.DenialReason == LoopClosureGateDenialReasons.NoOrganicOpportunity
            && absentCandidatePreference.DenialReason == LoopClosureGateDenialReasons.CandidateUnavailable
            && admittedPreference.State == LoopClosureLinkStates.Admitted
            && !admittedPreference.HasDenialReason
            && ExpectedFixtureKind(preference) == "policy-decision"
            && ExpectedFixtureKind(censusPreference) == "loop-closure-organic-opportunity"
            && TryDecodeOrganicAgreement(agreementPayload, agreementReadout)
            && TryDecodeOrganicAgreement(absentCandidatePayload, absentCandidateReadout)
            && TryDecodeOrganicAgreement(divergentPayload, divergentReadout)
            && IsOrganicOpportunityCensus(censusPayload, policy)
            && !TryDecodePolicyDecision(censusPayload)
            && !IsOrganicOpportunityCensus(agreementPayload, policy)
            && !IsOrganicOpportunityCensus(absentCandidatePayload, policy)
            && !IsOrganicOpportunityCensus(divergentPayload, policy)
            && !IsOrganicOpportunityCensus(Encoding.ASCII.GetBytes("LOOP-CLOSURE-ORGANIC-OPPORTUNITY\tpolicy=" + policy.Value + "\topportunities=1"), policy)
            && !LoopClosureAdjudicator.MatchesPreferenceEvidence(
                preference, tape.GetEventViews().FirstOrDefault(view => view.Id == preference.EventID), agreementPayload, in policyBinding)
            && !LoopClosureAdjudicator.MatchesPreferenceEvidence(
                absentCandidatePreference, tape.GetEventViews().FirstOrDefault(view => view.Id == absentCandidatePreference.EventID), absentCandidatePayload, in policyBinding)
            && LoopClosureAdjudicator.MatchesPreferenceEvidence(
                admittedPreference, tape.GetEventViews().FirstOrDefault(view => view.Id == admittedPreference.EventID), divergentPayload, in policyBinding)
            && LoopClosureAdjudicator.MatchesPreferenceEvidence(
                censusPreference, tape.GetEventViews().FirstOrDefault(view => view.Id == censusPreference.EventID), censusPayload, in policyBinding)
            && !LoopClosureAdjudicator.MatchesPreferenceEvidence(
                censusWithReflexReason, tape.GetEventViews().FirstOrDefault(view => view.Id == censusWithReflexReason.EventID), censusPayload, in policyBinding)
            && !LoopClosureAdjudicator.MatchesPreferenceEvidence(
                policyWithCensusReason, tape.GetEventViews().FirstOrDefault(view => view.Id == policyWithCensusReason.EventID), agreementPayload, in policyBinding)
            && !LoopClosureAdjudicator.MatchesPreferenceEvidence(
                censusPreference, tape.GetEventViews().FirstOrDefault(view => view.Id == censusPreference.EventID),
                Encoding.ASCII.GetBytes("LOOP-CLOSURE-ORGANIC-OPPORTUNITY\tpolicy=" + policy.Value + "\topportunities=1"), in policyBinding);
        CortexPolicyDecisionReadout forcedChildReadout = new(
            0, 0, 1, 1, CortexPolicyAuthorities.Grammar, new Grammar.GrammarRevisionID(2),
            CortexPolicySelectionCauses.TrialOverride, ReadoutCandidateOccurrenceDigest: 1, ReadoutCandidateFingerprint: 3);
        CortexPolicyDecision forcedChildDecision = new(new CortexPolicyDecisionID(10090), policy, forcedChildReadout);
        TapeEventID forcedChildID = TapePacketCreator.AppendPolicyDecision(
            tape, journal, 8, in forcedChildDecision,
            [new MetricSample(new MetricID(0), NumericValue.FromI64(0))], 3, out byte[] forcedChildPayload);
        PolicyBoundaryRailMetadataDocument forcedChildRail = new()
        {
            arm = PolicyBoundaryArms.ForcedDivergentNull,
            materializationAttemptID = fundingID.Value,
            executedDecisionID = forcedChildDecision.DecisionID.Value,
            executedStep = 8,
            executedLaunchpadAction = forcedChildReadout.LaunchpadAction,
            executedRawCandidateAction = forcedChildReadout.RawCandidateAction,
            executedSelectedCandidateAction = forcedChildReadout.SelectedCandidateAction,
            executedAction = forcedChildReadout.ExecutedAction,
            executedAuthority = forcedChildReadout.Authority,
            executedSelectionCause = forcedChildReadout.SelectionCause,
            executedReadoutRevision = forcedChildReadout.GrammarRevision.Value,
            executedReadoutOccurrenceDigest = forcedChildReadout.ReadoutCandidateOccurrenceDigest,
            executedCandidateFingerprint = forcedChildReadout.ReadoutCandidateFingerprint,
            executedDecisionEventID = forcedChildID.Value,
            forcedDivergenceSeed = 0xD1E3UL,
        };
        bool forcedChildAccepted = TryDecodeForcedChildReadout(
            forcedChildPayload, forcedChildRail, forcedChildID, 8, fundingID.Value, out _);
        CortexPolicySelectionCauses originalCause = forcedChildRail.executedSelectionCause;
        forcedChildRail.executedSelectionCause = CortexPolicySelectionCauses.GrammarCandidate;
        bool relabeledChildRejected = !TryDecodeForcedChildReadout(
            forcedChildPayload, forcedChildRail, forcedChildID, 8, fundingID.Value, out _);
        forcedChildRail.executedSelectionCause = originalCause;
        int originalSelected = forcedChildRail.executedSelectedCandidateAction;
        forcedChildRail.executedSelectedCandidateAction = forcedChildRail.executedRawCandidateAction;
        bool tamperedChildRejected = !TryDecodeForcedChildReadout(
            forcedChildPayload, forcedChildRail, forcedChildID, 8, fundingID.Value, out _);
        forcedChildRail.executedSelectedCandidateAction = originalSelected;
        bool childEvidenceNotTopLevelProof = !Encoding.ASCII.GetString(forcedChildPayload)
            .StartsWith("POLICY-FUNDED-DISSENT\t", StringComparison.Ordinal);
        LoopClosureDigest[] receiptsBeforeResume = attempts.Select(static attempt => attempt.JournalSHA256).ToArray();
        Append("resume", "mint", 9, "RESUME-CONTINUATION");
        bool journalReceiptsStable = attempts.Select(static attempt => attempt.JournalSHA256)
            .SequenceEqual(receiptsBeforeResume);
        bool distinctPredecessors = attempts.Skip(1).Select(static attempt => attempt.PredecessorEventID)
            .SequenceEqual(attempts.Take(4).Select(static attempt => attempt.EventID.Value));
        bool canonicalPredecessorChronology = attempts.All(attempt =>
            TryValidatePredecessorChronology(in attempt, tape, tape, childTape: null, out _));
        bool canonicalPredecessorBindings = attempts.Skip(1).Select((attempt, index) =>
            TryValidatePredecessorBinding(in attempt, in attempts[index], tape, tape, childTape: null, out _)).All(static valid => valid);
        LoopClosureLinkAttempt legacyR16Executed = executed with
        {
            EventID = new TapeEventID(boundary.EventID.Value - 1),
        };
        bool legacyR16ChronologyRejected = !TryValidatePredecessorChronology(
            in legacyR16Executed, tape, tape, childTape: null, out _);
        using Tape childAuthorityTape = new();
        byte[] childAuthorityPayload = Encoding.ASCII.GetBytes("POLICY-DECISION\tchild-authority");
        TapeEventID childAuthorityEventID = childAuthorityTape.Append(childAuthorityPayload, "policy:" + policy.Value, Provenances.Execution);
        LoopClosureLinkAttempt childAuthority = LoopClosureLinkAttempt.Create(
            "fixture-child-authority", "fixture-run", LoopClosureLinkSpecies.AuthorityEligible,
            LoopClosureLinkPaths.Forced, LoopClosureLinkStates.Admitted, 1, childAuthorityEventID,
            new(DigestPayload(childAuthorityPayload)), paid.EventID.Value, new(DigestPayload(paid.Payload)),
            Digest("child-authority-journal"), default, false, fundingID, default, divergenceDigest, revision,
            intervention.AttemptSHA256, "fixture-child", "children/fixture-child", Digest("fixture-authority"), Digest("fixture-rail"));
        bool crossTapeChildAuthorityAllowed = TryValidatePredecessorChronology(
            in childAuthority, tape, childAuthorityTape, childAuthorityTape, out _);
        bool typedProofPackets = Encoding.ASCII.GetString(paid.Payload).StartsWith("POLICY-FUNDED-DISSENT\t", StringComparison.Ordinal)
            && Encoding.ASCII.GetString(authority.Payload).StartsWith("POLICY-DECISION\t", StringComparison.Ordinal)
            && Encoding.ASCII.GetString(boundary.Payload).StartsWith("POLICY-BOUNDARY\t", StringComparison.Ordinal)
            && Encoding.ASCII.GetString(outcome.Payload).StartsWith("POLICY-BOUNDARY-OUTCOME\t", StringComparison.Ordinal)
            && Encoding.ASCII.GetString(paid.Payload).Contains(fundingID.Value, StringComparison.Ordinal)
            && Encoding.ASCII.GetString(boundary.Payload).Contains(forkDigest.Value, StringComparison.Ordinal)
            && Encoding.ASCII.GetString(outcome.Payload).Contains(divergenceDigest.Value, StringComparison.Ordinal);
        LoopClosureLinkReceipt[] receipts = attempts.Select(static attempt => attempt.ToReceipt()).ToArray();
        LoopClosureGateLiveness[] meters =
        [
            LoopClosureGateLiveness.Create(LoopClosureLinkSpecies.PreferenceDivergence, 2, 0, 2,
                [new(LoopClosureGateDenialReasons.NoOrganicOpportunity, 1), new(LoopClosureGateDenialReasons.ReflexAgreement, 1)]),
            LoopClosureGateLiveness.Create(LoopClosureLinkSpecies.InterventionDivergence, 1, 1, 0, []),
            LoopClosureGateLiveness.Create(LoopClosureLinkSpecies.AuthorityEligible, 1, 1, 0, []),
            LoopClosureGateLiveness.Create(LoopClosureLinkSpecies.BoundaryAdmitted, 1, 1, 0, []),
            LoopClosureGateLiveness.Create(LoopClosureLinkSpecies.ExecutedDivergence, 1, 1, 0, []),
        ];
        LoopClosureLinkAttempt[] shuffledAttempts = attempts.ToArray();
        shuffledAttempts[4] = shuffledAttempts[4] with
        {
            PredecessorEventID = authorityLink.EventID.Value,
            PredecessorEvidenceSHA256 = authorityLink.EvidenceSHA256,
        };
        LoopClosureLinkReceipt[] shuffledPredecessorReceipts = receipts.ToArray();
        shuffledPredecessorReceipts[4] = shuffledPredecessorReceipts[4] with
        {
            PredecessorEvidenceSHA256 = shuffledPredecessorReceipts[2].EvidenceSHA256,
        };
        bool shuffledPredecessorRejected = Rejects(() => new LoopClosureLinkContract(
            shuffledPredecessorReceipts, meters).Validate(requireComplete: true));
        bool shuffledEventPayloadMultisetStable = attempts
            .Select(static attempt => (attempt.EventID.Value, attempt.EvidenceSHA256))
            .SequenceEqual(shuffledAttempts.Select(static attempt => (attempt.EventID.Value, attempt.EvidenceSHA256)));
        bool shuffledAttemptBindingRejected = !TryValidatePredecessorBinding(
            in shuffledAttempts[4], in boundaryLink, tape, tape, childTape: null, out _);
        LoopClosureLinkContract contract = new(receipts, meters);
        contract.Validate(requireComplete: true);
        bool crossSpeciesReasonRejected;
        try
        {
            LoopClosureGateLiveness.Create(LoopClosureLinkSpecies.InterventionDivergence, 1, 0, 1,
                [new(LoopClosureGateDenialReasons.ReflexAgreement, 1)]);
            crossSpeciesReasonRejected = false;
        }
        catch (InvalidDataException) { crossSpeciesReasonRejected = true; }
        LoopLineageShuffledNullReceipt nullReceipt = new(
            new string('a', 64), new string('b', 64), new string('c', 64), 5, 4, 1, 7,
            new string('d', 64), 1, true, true, true, new string('e', 64),
            LoopLineageOccurrenceCheckStatuses.PASS, new string('f', 64),
            LoopLineageOccurrenceCheckStatuses.FAIL, new("fixture-edge"));
        LoopClosureLineageNullExecuted lineageNull = new(nullReceipt);
        lineageNull.Validate();
        bool lineageBound = executed.State == LoopClosureLinkStates.Admitted
            && lineageNull.Receipt.OriginalStatus == LoopLineageOccurrenceCheckStatuses.PASS
            && lineageNull.Receipt.ShuffledStatus == LoopLineageOccurrenceCheckStatuses.FAIL
            && lineageNull.Receipt.OriginalLineageSHA256 != lineageNull.Receipt.ShuffledLineageSHA256
            && lineageNull.Receipt.FirstDiscriminatingEdge.IsValid;
        return rowsAndPayloads && preferenceRowsAndPayloads && denialSpeciesBound && crossSpeciesReasonRejected
            && journalReceiptsStable && distinctPredecessors && typedProofPackets
            && contract.IsComplete && lineageBound && forcedChildAccepted
            && relabeledChildRejected && tamperedChildRejected && childEvidenceNotTopLevelProof
            && canonicalPredecessorChronology && canonicalPredecessorBindings && crossTapeChildAuthorityAllowed
            && legacyR16ChronologyRejected && shuffledPredecessorRejected
            && shuffledEventPayloadMultisetStable && shuffledAttemptBindingRejected;
    }

    /// Validate a typed predecessor against the tape that owns the link and the
    /// tape that owns its evidence. Event IDs are chronological only within one
    /// tape; a child Authority event may legitimately name a parent-tape event
    /// whose numeric ID is greater than the child's local ID.
    internal static bool TryValidatePredecessorChronology(
        in LoopClosureLinkAttempt attempt,
        Tape parentTape,
        Tape evidenceTape,
        Tape? childTape,
        out string failure)
    {
        failure = "";
        if (attempt.PredecessorEventID < 0) return true;
        bool childAuthorityPredecessor = childTape is not null
            && attempt.Species == LoopClosureLinkSpecies.AuthorityEligible
            && attempt.PredecessorEvidenceSHA256 == attempt.EvidenceSHA256;
        bool childBoundaryPredecessor = childTape is not null
            && attempt.Species == LoopClosureLinkSpecies.BoundaryAdmitted;
        Tape predecessorTape = childAuthorityPredecessor || childBoundaryPredecessor ? evidenceTape : parentTape;
        bool crossTapeBoundary = attempt.Species == LoopClosureLinkSpecies.BoundaryAdmitted
            && string.IsNullOrWhiteSpace(attempt.EvidenceRunID)
            && attempt.PredecessorAttemptSHA256.IsValid;
        bool predecessorResolved = predecessorTape.Resolve(new TapeEventID(attempt.PredecessorEventID), out byte[] predecessorPayload);
        bool sameTape = ReferenceEquals(predecessorTape, evidenceTape);
        // The payload digest binds unconditionally — fail-closed.  The cross-tape
        // boundary predecessor formerly escaped every check on shape alone (blank run
        // id + well-formed SHA), so a foreign or shuffled parent-tape payload passed;
        // its numeric ID still cannot be ordered against the child's, so only the
        // same-tape ordering gate is skipped for it.  Same-tape ordering is strict:
        // a predecessor at or after the child event is self-referential, never causal.
        if (!predecessorResolved
            || (!crossTapeBoundary && sameTape && attempt.PredecessorEventID >= attempt.EventID.Value)
            || DigestPayload(predecessorPayload) != attempt.PredecessorEvidenceSHA256.Value)
        {
            failure = $"loop-closure link {attempt.RecordID} has a shuffled or foreign predecessor";
            return false;
        }
        return true;
    }

    internal static bool TryValidatePredecessorBinding(
        in LoopClosureLinkAttempt attempt,
        in LoopClosureLinkAttempt predecessor,
        Tape parentTape,
        Tape evidenceTape,
        Tape? childTape,
        out string failure)
    {
        if (attempt.PredecessorEventID != predecessor.EventID.Value
            || attempt.PredecessorEvidenceSHA256 != predecessor.EvidenceSHA256
            || attempt.PredecessorAttemptSHA256 != predecessor.AttemptSHA256)
        {
            failure = $"loop-closure link {attempt.RecordID} is not bound to its typed predecessor";
            return false;
        }
        return TryValidatePredecessorChronology(in attempt, parentTape, evidenceTape, childTape, out failure);
    }

    private static bool TryDecodeOrganicAgreement(ReadOnlySpan<byte> payload, in CortexPolicyDecisionReadout expected)
    {
        try
        {
            CortexPolicyDecisionPacket packet = TapePacketCreator.DecodePolicyDecision(payload);
            return packet.Readout == expected;
        }
        catch (InvalidDataException) { return false; }
    }

    private static bool TryDecodePolicyDecision(ReadOnlySpan<byte> payload)
    {
        try { TapePacketCreator.DecodePolicyDecision(payload); return true; }
        catch (InvalidDataException) { return false; }
    }

    private static bool IsOrganicOpportunityCensus(ReadOnlySpan<byte> payload, CortexPolicyID policy)
    {
        string census = Encoding.ASCII.GetString(payload);
        return census.StartsWith("LOOP-CLOSURE-ORGANIC-OPPORTUNITY\t", StringComparison.Ordinal)
            && census.Contains("\tpolicy=" + policy.Value, StringComparison.Ordinal)
            && census.EndsWith("\topportunities=0", StringComparison.Ordinal);
    }

    private static string ExpectedFixtureKind(LoopClosureLinkAttempt attempt)
        => attempt.State == LoopClosureLinkStates.Denied
            && attempt.Species == LoopClosureLinkSpecies.PreferenceDivergence
            && attempt.DenialReason == LoopClosureGateDenialReasons.NoOrganicOpportunity
            ? "loop-closure-organic-opportunity"
            : attempt.Species is LoopClosureLinkSpecies.PreferenceDivergence or LoopClosureLinkSpecies.AuthorityEligible
                ? "policy-decision"
                : attempt.Species == LoopClosureLinkSpecies.BoundaryAdmitted
                    ? "policy-boundary" : "mint";

    private static LoopClosureDigest Digest(string value)
        => new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value))));

    private static bool Rejects(Action action)
    {
        try { action(); return false; }
        catch (InvalidDataException) { return true; }
    }
}

public sealed partial class Cortex
{
    internal bool TryRecordRepositoryPreferenceLink(
        int step,
        TapeEventID eventID,
        byte[] payload,
        out LoopClosureLinkAttempt recorded)
    {
        recorded = default;
        if (!_loopLineageEnabled) return true;
        if (_runtimeRun is null || _runtimeTape is null || _runtimeJournal is null)
            throw new InvalidDataException("repository preference link has no runtime lineage tape");
        string runID = Path.GetFileName(Path.GetFullPath(_runtimeRun.Dir));
        LoopClosureLinkAttempt attempt = CreateLoopClosureLinkAttempt(
            runID, "preference-repository", LoopClosureLinkSpecies.PreferenceDivergence,
            LoopClosureLinkPaths.Organic, LoopClosureLinkStates.Admitted, step, eventID, payload,
            -1, default,
            LoopClosureLinkAttemptStore.DigestJournalReceipt(step, "repository-preference", eventID.Value),
            default, false, default, default, default, default);
        return TryRecordLoopClosureLinkAttempt(in attempt, out recorded);
    }

    internal bool TryRecordRepositoryInterventionLink(
        int step,
        in CortexPolicyDecision decision,
        TapeEventID decisionEventID,
        byte[] decisionPayload,
        TapeEventID fundedDivergenceEventID,
        byte[] fundedDivergencePayload,
        in CortexPolicyTrialQuotaDecision funding,
        LoopClosureLinkAttempt? preference,
        out LoopClosureLinkAttempt recorded)
    {
        recorded = default;
        if (!_loopLineageEnabled) return true;
        if (_runtimeRun is null || _runtimeTape is null || _runtimeJournal is null)
            throw new InvalidDataException("repository intervention link has no runtime lineage tape");
        string runID = Path.GetFileName(Path.GetFullPath(_runtimeRun.Dir));
        long predecessorEventID = preference?.EventID.Value ?? decisionEventID.Value;
        LoopClosureDigest predecessorEvidence = preference?.EvidenceSHA256 ?? DigestPayload(decisionPayload);
        LoopClosureDigest predecessorAttempt = preference?.AttemptSHA256 ?? default;
        LoopClosureLinkAttempt attempt = CreateLoopClosureLinkAttempt(
            runID, "intervention-repository", LoopClosureLinkSpecies.InterventionDivergence,
            LoopClosureLinkPaths.Forced, LoopClosureLinkStates.Admitted, step, fundedDivergenceEventID, fundedDivergencePayload,
            predecessorEventID, predecessorEvidence,
            LoopClosureLinkAttemptStore.DigestJournalReceipt(step, "mint", fundedDivergenceEventID.Value),
            default, false, new(funding.QuotaDecisionID.ToString()), default, default,
            funding.CandidateRevision, predecessorAttempt);
        return TryRecordLoopClosureLinkAttempt(in attempt, out recorded);
    }

    internal bool TryRecordRepositoryDivergenceContinuationLinks(
        int step,
        in CortexPolicyDecision decision,
        in PolicyBoundaryForkReceipt forkReceipt,
        in PolicyBoundaryDivergenceArmOutcome forcedNull,
        in CortexPolicyTrialQuotaDecision funding,
        in PolicyBoundaryDivergenceAdjudication adjudication,
        in LoopClosureLinkAttempt intervention,
        TapeEventID boundaryEventID,
        byte[] boundaryPayload,
        TapeEventID outcomeEventID,
        byte[] outcomePayload)
    {
        if (!_loopLineageEnabled) return true;
        if (_runtimeRun is null || _runtimeTape is null || _runtimeJournal is null)
            throw new InvalidDataException("repository divergence links have no runtime lineage tape");
        if (!TryGetPolicyBoundaryDomain(funding.Policy, out IPolicyBoundaryDomain domain))
            throw new InvalidDataException("repository divergence links have no registered domain");
        if (!TryReadForcedChildEvidence(in funding, domain.PolicyBinding, _runtimeRun.Dir, out ChildLinkEvidence child)
            || !MatchesForcedChildProof(in child, in forcedNull, adjudication.Proof.ReadoutFingerprint,
                adjudication.Proof.ReadoutRevision.Value))
            throw new InvalidDataException("repository divergence links lack the authenticated forced child");
        bool grammar = domain.ValidateExecutionAuthority(child.Readout.Authority, child.Readout.SelectionCause, requireGrammar: true);
        LoopClosureQuotaID fundingID = new(funding.QuotaDecisionID.ToString());
        LoopClosureLinkAttempt authority = CreateLoopClosureLinkAttempt(
            Path.GetFileName(Path.GetFullPath(_runtimeRun.Dir)), "authority-repository", LoopClosureLinkSpecies.AuthorityEligible,
            LoopClosureLinkPaths.Forced, grammar ? LoopClosureLinkStates.Admitted : LoopClosureLinkStates.Denied,
            child.Step, child.EventID, child.Payload, intervention.EventID.Value, intervention.EvidenceSHA256,
            child.JournalDigest, grammar ? default : LoopClosureGateDenialReasons.AuthorityNotEligible, !grammar,
            fundingID, default, adjudication.EvidenceSHA256, funding.CandidateRevision, intervention.AttemptSHA256,
            child.RunID, child.RelativePath, child.AuthorityDigest, child.RailDigest, child.OutcomeReference);
        if (!TryRecordLoopClosureLinkAttempt(in authority, out LoopClosureLinkAttempt recordedAuthority)) return false;
        if (!grammar) return true;

        LoopClosureDigest forkDigest = new(PolicyBoundaryObligation.ComputeReceiptDigest(in forkReceipt));
        if (boundaryEventID.Value <= 0 || boundaryPayload is null || boundaryPayload.Length == 0)
            throw new InvalidDataException("repository divergence links lack the authenticated boundary receipt");
        string boundaryText = Encoding.ASCII.GetString(boundaryPayload);
        if (!boundaryText.StartsWith("POLICY-BOUNDARY\t", StringComparison.Ordinal)
            || !boundaryText.Contains("digest=" + forkDigest.Value, StringComparison.Ordinal))
            throw new InvalidDataException("repository divergence links received a boundary packet for another fork");
        LoopClosureLinkAttempt boundary = CreateLoopClosureLinkAttempt(
            Path.GetFileName(Path.GetFullPath(_runtimeRun.Dir)), "boundary-repository", LoopClosureLinkSpecies.BoundaryAdmitted,
            LoopClosureLinkPaths.Forced, LoopClosureLinkStates.Admitted, step, boundaryEventID, boundaryPayload,
            child.EventID.Value, DigestPayload(child.Payload),
            LoopClosureLinkAttemptStore.DigestJournalReceipt(step, "policy-boundary", boundaryEventID.Value),
            default, false, fundingID, forkDigest, adjudication.EvidenceSHA256, funding.CandidateRevision,
            recordedAuthority.AttemptSHA256);
        if (!TryRecordLoopClosureLinkAttempt(in boundary, out LoopClosureLinkAttempt recordedBoundary)) return false;

        if (outcomeEventID.Value <= 0 || outcomePayload is null || outcomePayload.Length == 0)
            throw new InvalidDataException("repository divergence links lack the authenticated terminal outcome");
        string outcomeText = Encoding.ASCII.GetString(outcomePayload);
        if (!outcomeText.StartsWith("POLICY-BOUNDARY-OUTCOME\t", StringComparison.Ordinal)
            || !outcomeText.Contains("adjudication=" + adjudication.EvidenceSHA256.Value, StringComparison.Ordinal))
            throw new InvalidDataException("repository divergence links received an outcome packet for another adjudication");
        LoopClosureLinkAttempt executed = CreateLoopClosureLinkAttempt(
            Path.GetFileName(Path.GetFullPath(_runtimeRun.Dir)), "executed-repository", LoopClosureLinkSpecies.ExecutedDivergence,
            LoopClosureLinkPaths.Forced, LoopClosureLinkStates.Admitted, step, outcomeEventID, outcomePayload,
            boundaryEventID.Value, recordedBoundary.EvidenceSHA256,
            LoopClosureLinkAttemptStore.DigestJournalReceipt(step, "mint", outcomeEventID.Value),
            default, false, fundingID, forkDigest, adjudication.EvidenceSHA256, funding.CandidateRevision,
            recordedBoundary.AttemptSHA256, child.RunID, child.RelativePath, child.AuthorityDigest, child.RailDigest,
            child.OutcomeReference);
        return TryRecordLoopClosureLinkAttempt(in executed, out _);
    }

    internal void EmitPolicyBoundaryLoopClosureLinkAttemptsBeforeAuthoritySeal(Run run, Tape tape, Journal journal)
    {
        foreach (IPolicyBoundaryDomain domain in _policyBoundaryDomains.Values.OrderBy(static domain => domain.PolicyID.Value, StringComparer.Ordinal))
            if (domain is HomeostatPolicyBoundaryDomain)
                EmitLoopClosureLinkAttemptsBeforeAuthoritySeal(run, tape, journal, domain.PolicyBinding);
    }

    internal void EmitLoopClosureLinkAttemptsBeforeAuthoritySeal(
        Run run,
        Tape tape,
        Journal journal,
        in LoopClosurePolicyBinding policy)
    {
        policy.Validate();
        if (policy.PolicyID != HomeostatPolicyBoundaryDomain.Instance.PolicyID)
            return;
        if (!_loopLineageEnabled) return;
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(tape);
        if (!_policyBoundaryDomains.TryGetValue(policy.PolicyID, out IPolicyBoundaryDomain domain)
            || !domain.PolicyBinding.Equals(policy))
            throw new InvalidDataException($"loop-closure link emission has no registered domain binding for {policy.PolicyID}");
        // The row filters below are lambdas, which cannot capture the `in` binding directly.
        LoopClosurePolicyBinding policyBinding = policy;
        string runID = Path.GetFileName(Path.GetFullPath(run.Dir));
        IReadOnlyList<PolicyBoundaryDivergenceAdjudication> proofs = LoopClosureEvidenceStore.ReadDivergenceProof(
            run.Dir, runID, LoopClosureEvidenceStore.ResolveRegisteredDomain(domain));
        string journalPath = run.PathOf("journal.log");
        if (!File.Exists(journalPath)) throw new InvalidDataException("loop-closure link emission requires journal.log");
        Dictionary<long, (int Step, string Kind)> journalRows = ReadLinkJournalRows(journalPath);
        List<(TapeEventView View, int Step, byte[] Payload)> events = [];
        foreach (TapeEventView view in tape.GetEventViews())
        {
            if (!tape.Resolve(view.Id, out byte[] payload)) continue;
            if (journalRows.TryGetValue(view.Id.Value, out var row)) events.Add((view, row.Step, payload));
        }
        List<(TapeEventView View, int Step, byte[] Payload)> preferences = events
            .Where(item => string.Equals(item.View.Source, "repository-preference", StringComparison.Ordinal)
                && journalRows[item.View.Id.Value].Kind == "repository-preference"
                // The native producer emits a typed comparison for every
                // reached decision.  Only its explicit Diverged outcome is a
                // preference link; agreement/unavailable/no-opportunity rows
                // remain liveness evidence and are never upgraded here.
                && RepositoryPreferenceComparisonReceipt.TryDecode(item.Payload, out RepositoryPreferenceComparisonReceipt comparison)
                && comparison.PolicyID.Equals(policyBinding.PolicyID)
                && comparison.IsPreferenceDivergence)
            .Select(item => (item.View, item.Step, item.Payload))
            .OrderBy(item => item.View.Id.Value).ToList();
        List<LoopClosureLinkAttempt> preferenceAttempts = [];
        foreach (var preference in preferences)
        {
            preferenceAttempts.Add(CreateLoopClosureLinkAttempt(runID, $"preference-{preference.View.Id.Value}", LoopClosureLinkSpecies.PreferenceDivergence,
                LoopClosureLinkPaths.Organic, LoopClosureLinkStates.Admitted,
                preference.Step, preference.View.Id, preference.Payload, -1, default,
                LoopClosureLinkAttemptStore.DigestJournalReceipt(preference.Step, "repository-preference", preference.View.Id.Value),
                default, false, default, default, default, default));
        }
        if (preferenceAttempts.Count == 0)
        {
            int censusStep = Step;
            TapeEventID censusEvent = TapePacketCreator.AppendLoopClosureOrganicOpportunityCensus(
                tape, journal, censusStep, policy.PolicyID, opportunities: 0);
            if (!tape.Resolve(censusEvent, out byte[] censusPayload))
                throw new InvalidDataException("organic opportunity census packet was not retained");
            preferenceAttempts.Add(CreateLoopClosureLinkAttempt(runID, $"preference-none-{censusEvent.Value}",
                LoopClosureLinkSpecies.PreferenceDivergence, LoopClosureLinkPaths.Organic,
                LoopClosureLinkStates.Denied, censusStep, censusEvent,
                censusPayload,
                -1, default,
                LoopClosureLinkAttemptStore.DigestJournalReceipt(censusStep,
                    "loop-closure-organic-opportunity", censusEvent.Value),
                LoopClosureGateDenialReasons.NoOrganicOpportunity, true, default, default, default, default));
        }
        foreach (LoopClosureLinkAttempt preferenceRecord in preferenceAttempts)
            LoopClosureLinkAttemptStore.TryWrite(run, in preferenceRecord);
        foreach ((TapeEventView View, int Step, byte[] Payload) fundingEvent in events)
        {
            if (!TapePacketCreator.TryDecodePolicyTrialQuota(fundingEvent.Payload, out CortexPolicyTrialQuotaDecision funding, out _)
                || !policy.MatchesPolicy(funding.Policy)
                || funding.Decision is not (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused)) continue;
            if (proofs.Any(proof => proof.Proof.Funding.QuotaDecisionID.Equals(funding.QuotaDecisionID))) continue;
            if (!_policyTrialCompletionByID.TryGetValue(funding.QuotaDecisionID, out CortexPolicyTrialCompletion settlement))
            {
                // A paid lease with authenticated seed custody but no settlement
                // is still active/incomplete; it has not reached a lifetime gate.
                if (!TryReadPolicyBoundarySeedCustodyDocument(run.Dir, runID, funding.QuotaDecisionID.ToString(), out PolicyBoundarySeedCustody activeSeed)
                    || !string.Equals(activeSeed.custodyDigest, funding.SeedAuditOnlyDigest, StringComparison.Ordinal))
                    throw new InvalidDataException($"loop-closure paid trial {funding.QuotaDecisionID} has no authenticated active custody");
                continue;
            }
            if (settlement.VerifierOutcome != CortexPolicyVerifierOutcomes.Passed) continue;
            if (!HasAuthenticatedUnprovenFunding(in policy, domain, in funding))
                throw new InvalidDataException($"loop-closure paid trial {funding.QuotaDecisionID} has no settled authenticated child custody");
            EmitUnprovenFundingAttempts(run, runID, fundingEvent, preferenceAttempts, in policy, domain, funding);
        }
        foreach (PolicyBoundaryDivergenceAdjudication proof in proofs)
        {
            if (!policy.MatchesPolicy(proof.Proof.Policy))
                throw new InvalidDataException($"loop-closure proof {proof.Proof.Funding.QuotaDecisionID} belongs to another policy");
            string fundingID = proof.Proof.Funding.QuotaDecisionID.ToString();
            CortexPolicyTrialQuotaDecision proofFunding = proof.Proof.Funding;
            PolicyBoundaryDivergenceArmOutcome forcedNull = proof.Proof.ForcedNull;
            (TapeEventView View, int Step, byte[] Payload) paid = events
                .Where(item => MatchesPaidDivergencePayload(item.Payload, proof.Proof))
                .OrderBy(item => item.View.Id.Value).LastOrDefault();
            (TapeEventView View, int Step, byte[] Payload) executed = events
                .Where(item => journalRows[item.View.Id.Value].Kind == "mint"
                    && Encoding.ASCII.GetString(item.Payload).StartsWith("POLICY-BOUNDARY-OUTCOME\t", StringComparison.Ordinal)
                    && Encoding.ASCII.GetString(item.Payload).Contains("adjudication=" + proof.EvidenceSHA256.Value, StringComparison.Ordinal))
                .OrderBy(item => item.View.Id.Value).LastOrDefault();
            if (!TryReadForcedChildEvidence(in proofFunding, in policy, run.Dir, out ChildLinkEvidence forcedChild)
                || !MatchesForcedChildProof(in forcedChild, in forcedNull, proof.Proof.ReadoutFingerprint,
                    proof.Proof.ReadoutRevision.Value))
                throw new InvalidDataException($"loop-closure proof {fundingID} lacks the authenticated forced TrialOverride child decision");
            LoopClosureLinkAttempt preferenceAttempt = preferenceAttempts.LastOrDefault(item => item.EventID.Value < paid.View.Id.Value);
            PolicyBoundaryForkReceipt proofReceipt = proof.Proof.ForkReceipt;
            LoopClosureDigest forkDigest = new(PolicyBoundaryObligation.ComputeReceiptDigest(in proofReceipt));
            string forkDigestText = forkDigest.Value;
            (TapeEventView View, int Step, byte[] Payload) boundaryEvidence = events
                .Where(item => policyBinding.MatchesSource(item.View.Source)
                    && Encoding.ASCII.GetString(item.Payload).StartsWith("POLICY-BOUNDARY\t", StringComparison.Ordinal)
                    && Encoding.ASCII.GetString(item.Payload).Contains("digest=" + forkDigestText, StringComparison.Ordinal))
                .OrderBy(item => item.View.Id.Value).LastOrDefault();
            if (paid.Payload is null || boundaryEvidence.Payload is null
                || executed.Payload is null || string.IsNullOrEmpty(preferenceAttempt.RecordID))
                throw new InvalidDataException($"loop-closure proof {fundingID} lacks a complete persisted five-link event chain");
            LoopClosureLinkAttempt interventionAttempt = CreateLoopClosureLinkAttempt(runID, $"intervention-{fundingID}", LoopClosureLinkSpecies.InterventionDivergence,
                LoopClosureLinkPaths.Forced, LoopClosureLinkStates.Admitted, paid.Step, paid.View.Id, paid.Payload,
                preferenceAttempt.EventID.Value, preferenceAttempt.EvidenceSHA256,
                LoopClosureLinkAttemptStore.DigestJournalReceipt(paid.Step, "mint", paid.View.Id.Value), default, false,
                new(fundingID), default, proof.EvidenceSHA256, proof.Proof.ReadoutRevision, preferenceAttempt.AttemptSHA256);
            LoopClosureLinkAttempt authorityAttempt = CreateLoopClosureLinkAttempt(runID, $"authority-{fundingID}", LoopClosureLinkSpecies.AuthorityEligible,
                LoopClosureLinkPaths.Forced, LoopClosureLinkStates.Admitted, forcedChild.Step, forcedChild.EventID, forcedChild.Payload,
                paid.View.Id.Value, interventionAttempt.EvidenceSHA256,
                forcedChild.JournalDigest, default, false,
                new(fundingID), default, proof.EvidenceSHA256, proof.Proof.ReadoutRevision, interventionAttempt.AttemptSHA256,
                forcedChild.RunID, forcedChild.RelativePath, forcedChild.AuthorityDigest, forcedChild.RailDigest,
                forcedChild.OutcomeReference);
            LoopClosureLinkAttempt boundaryAttempt = CreateLoopClosureLinkAttempt(runID, $"boundary-{fundingID}", LoopClosureLinkSpecies.BoundaryAdmitted,
                LoopClosureLinkPaths.Forced, LoopClosureLinkStates.Admitted, boundaryEvidence.Step, boundaryEvidence.View.Id, boundaryEvidence.Payload,
                forcedChild.EventID.Value, authorityAttempt.EvidenceSHA256,
                LoopClosureLinkAttemptStore.DigestJournalReceipt(boundaryEvidence.Step, "policy-boundary", boundaryEvidence.View.Id.Value), default, false,
                new(fundingID), forkDigest, proof.EvidenceSHA256, proof.Proof.ReadoutRevision, authorityAttempt.AttemptSHA256);
            LoopClosureLinkAttempt executedAttempt = CreateLoopClosureLinkAttempt(runID, $"executed-{fundingID}", LoopClosureLinkSpecies.ExecutedDivergence,
                LoopClosureLinkPaths.Forced, LoopClosureLinkStates.Admitted, executed.Step, executed.View.Id, executed.Payload,
                boundaryEvidence.View.Id.Value, boundaryAttempt.EvidenceSHA256,
                LoopClosureLinkAttemptStore.DigestJournalReceipt(executed.Step, "mint", executed.View.Id.Value), default, false,
                new(fundingID), forkDigest, proof.EvidenceSHA256, proof.Proof.ReadoutRevision, boundaryAttempt.AttemptSHA256,
                forcedChild.RunID, forcedChild.RelativePath, forcedChild.AuthorityDigest, forcedChild.RailDigest,
                forcedChild.OutcomeReference);
            LoopClosureLinkAttemptStore.TryWrite(run, in interventionAttempt);
            LoopClosureLinkAttemptStore.TryWrite(run, in authorityAttempt);
            LoopClosureLinkAttemptStore.TryWrite(run, in boundaryAttempt);
            LoopClosureLinkAttemptStore.TryWrite(run, in executedAttempt);
        }
    }

    private bool HasAuthenticatedUnprovenFunding(
        in LoopClosurePolicyBinding policy,
        IPolicyBoundaryDomain domain,
        in CortexPolicyTrialQuotaDecision funding)
    {
        try
        {
            policy.Validate();
            if (!policy.MatchesPolicy(funding.Policy)) return false;
            ValidatePolicyBoundarySeedCustody(in funding, domain);
            if (!_policyTrialCompletionByID.TryGetValue(funding.QuotaDecisionID, out CortexPolicyTrialCompletion settlement)
                || settlement.VerifierOutcome != CortexPolicyVerifierOutcomes.Passed)
                return false;
            return TryReadTerminalPolicyBoundaryGeneration(in policy, in funding, funding.ReadoutFingerprint, [16, 64, 256],
                out _, out _, out _, requireReceipt: false);
        }
        catch (InvalidDataException) { return false; }
    }

    private void EmitUnprovenFundingAttempts(
        Run run,
        string runID,
        (TapeEventView View, int Step, byte[] Payload) fundingEvent,
        IReadOnlyList<LoopClosureLinkAttempt> preferenceAttempts,
        in LoopClosurePolicyBinding policy,
        IPolicyBoundaryDomain domain,
        in CortexPolicyTrialQuotaDecision funding)
    {
        LoopClosureLinkAttempt preference = preferenceAttempts.LastOrDefault(item => item.EventID.Value < fundingEvent.View.Id.Value);
        if (string.IsNullOrEmpty(preference.RecordID))
            throw new InvalidDataException($"paid trial {funding.QuotaDecisionID} has no organic predecessor");
        if (!TryReadForcedChildEvidence(in funding, in policy, run.Dir, out ChildLinkEvidence child))
            throw new InvalidDataException($"paid trial {funding.QuotaDecisionID} lacks an authenticated child decision event");
        CortexPolicyDecisionReadout readout = child.Readout;
        bool diverges = readout.RawCandidateAction >= 0 && readout.SelectedCandidateAction >= 0
            && readout.SelectedCandidateAction != readout.RawCandidateAction
            && readout.SelectedCandidateAction != readout.LaunchpadAction;
        bool grammar = domain.ValidateExecutionAuthority(readout.Authority, readout.SelectionCause, requireGrammar: true);
        LoopClosureQuotaID fundingIdentity = new(funding.QuotaDecisionID.ToString());
        LoopClosureLinkAttempt intervention = CreateLoopClosureLinkAttempt(runID, $"intervention-child-{funding.QuotaDecisionID}", LoopClosureLinkSpecies.InterventionDivergence,
            LoopClosureLinkPaths.Forced, diverges ? LoopClosureLinkStates.Admitted : LoopClosureLinkStates.Denied, child.Step, child.EventID, child.Payload,
            preference.EventID.Value, preference.EvidenceSHA256, child.JournalDigest,
            diverges ? default : LoopClosureGateDenialReasons.CandidateUnavailable, !diverges,
            fundingIdentity, default, default, funding.CandidateRevision, preference.AttemptSHA256,
            child.RunID, child.RelativePath, child.AuthorityDigest, child.RailDigest, child.OutcomeReference);
        LoopClosureLinkAttemptStore.TryWrite(run, in intervention);
        if (!diverges) return;
        LoopClosureLinkAttempt authority = CreateLoopClosureLinkAttempt(runID, $"authority-child-{funding.QuotaDecisionID}", LoopClosureLinkSpecies.AuthorityEligible,
            LoopClosureLinkPaths.Forced, grammar ? LoopClosureLinkStates.Admitted : LoopClosureLinkStates.Denied, child.Step, child.EventID, child.Payload,
            child.EventID.Value, DigestPayload(child.Payload), child.JournalDigest,
            grammar ? default : LoopClosureGateDenialReasons.AuthorityNotEligible, !grammar,
            fundingIdentity, default, default, funding.CandidateRevision, intervention.AttemptSHA256,
            child.RunID, child.RelativePath, child.AuthorityDigest, child.RailDigest, child.OutcomeReference);
        LoopClosureLinkAttemptStore.TryWrite(run, in authority);
        if (!grammar) return;
        LoopClosureLinkAttempt boundary = CreateLoopClosureLinkAttempt(runID, $"boundary-child-{funding.QuotaDecisionID}", LoopClosureLinkSpecies.BoundaryAdmitted,
            LoopClosureLinkPaths.Forced, LoopClosureLinkStates.Denied, child.Step, child.EventID, child.Payload,
            child.EventID.Value, DigestPayload(child.Payload), child.JournalDigest, LoopClosureGateDenialReasons.BoundaryNotAdmitted, true,
            fundingIdentity, default, default, funding.CandidateRevision, authority.AttemptSHA256,
            child.RunID, child.RelativePath, child.AuthorityDigest, child.RailDigest, child.OutcomeReference);
        LoopClosureLinkAttemptStore.TryWrite(run, in boundary);
    }

    internal readonly record struct ChildLinkEvidence(
        string RunID, string RelativePath, TapeEventID EventID, CortexPolicyDecisionID DecisionID, int Step, byte[] Payload,
        CortexPolicyDecisionReadout Readout, LoopClosureDigest JournalDigest,
        LoopClosureDigest AuthorityDigest, LoopClosureDigest RailDigest,
        LoopClosureChildOutcomeReference OutcomeReference);

    internal static bool MatchesForcedChildProof(
        in ChildLinkEvidence child,
        in PolicyBoundaryDivergenceArmOutcome forced,
        ulong readoutFingerprint,
        ulong readoutRevision)
        => child.DecisionID.Equals(forced.DecisionID)
            && child.Readout.ReadoutFingerprint == forced.ReadoutFingerprint
            && child.Readout.ReadoutFingerprint == readoutFingerprint
            && child.Readout.GrammarRevision.Value == forced.ReadoutRevision
            && child.Readout.GrammarRevision.Value == readoutRevision
            && child.Readout.ReadoutCandidateOccurrenceDigest == forced.ReadoutOccurrenceDigest
            && child.Readout.ReadoutCandidateFingerprint == forced.CandidateFingerprint
            && child.Readout.LaunchpadAction == forced.LaunchpadAction
            && child.Readout.RawCandidateAction == forced.RawCandidateAction
            && child.Readout.SelectedCandidateAction == forced.SelectedCandidateAction
            && child.Readout.ExecutedAction == forced.Action
            && child.Readout.Authority == forced.Authority
            && child.Readout.SelectionCause == CortexPolicySelectionCauses.TrialOverride
            && forced.SelectionCause == CortexPolicySelectionCauses.TrialOverride
            && forced.BehaviorallyExecuted
            && forced.Diverged;

    internal static bool TryReadForcedChildEvidence(
        in CortexPolicyTrialQuotaDecision funding,
        in LoopClosurePolicyBinding policy,
        string parentDirectory,
        out ChildLinkEvidence evidence)
    {
        evidence = default;
        string children = Path.Combine(parentDirectory, "children");
        if (!Directory.Exists(children)) return false;
        foreach (string child in Directory.GetDirectories(children)
            .OrderByDescending(path => ParsePolicyBoundaryChildIndex(Path.GetFileName(path))))
        {
            string marker = Path.Combine(child, CortexForkMaterializationContract.MarkerFileName);
            if (!File.Exists(marker)) continue;
            try
            {
                if (!TryReadPolicyBoundaryMaterializationContract(marker, child, out CortexForkMaterializationContract contract)
                    || contract.AttemptID != funding.QuotaDecisionID.ToString()) continue;
                string railPath = Path.Combine(child, "policy-boundary.rail.ron");
                if (!File.Exists(railPath)) continue;
                PolicyBoundaryRailMetadataDocument rail = RonSerializer.Deserialize<PolicyBoundaryRailMetadataDocument>(File.ReadAllBytes(railPath));
                if (rail.arm != PolicyBoundaryArms.ForcedDivergentNull || rail.horizon != funding.RequestedHorizonSteps
                    || rail.executedDecisionID == 0 && rail.lastRequestDecisionID == 0) continue;
                using Tape tape = Checkpoint.LoadTape(child);
                ulong decisionID = rail.executedDecisionID != 0 ? rail.executedDecisionID : rail.lastRequestDecisionID;
                foreach (TapeEventView view in tape.GetEventViews())
                {
                    if (!tape.Resolve(view.Id, out byte[] payload)) continue;
                    CortexPolicyDecisionReadout readout = ReadPolicyDecision(payload);
                    int evidenceStep = rail.executedStep >= 0 ? rail.executedStep : rail.lastRequestStep;
                    if (!TryDecodePolicyDecisionID(payload, decisionID)
                        || !LoopClosureLinkAttemptStore.TryDecodeForcedChildReadout(
                            payload, rail, view.Id, evidenceStep, funding.QuotaDecisionID.ToString(), out readout)) continue;
                    string journalPath = Path.Combine(child, "journal.log");
                    if (!File.Exists(journalPath)) continue;
                    if (rail.ordinaryOutcomeRequired
                        && (rail.executedOutcomeEventID <= 0 || rail.executedOutcomePayloadSHA256.Length != 64
                            || !LoopClosureLinkAttemptStore.TryReadTerminalPolicyOutcome(
                                tape, journalPath, new CortexPolicyDecisionID(decisionID),
                                new TapeEventID(rail.executedOutcomeEventID), rail.executedOutcomePayloadSHA256,
                                in policy))) continue;
                    string relative = Path.GetRelativePath(parentDirectory, child);
                    string railDigest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(railPath)));
                    string authorityPath = Path.Combine(child, RunAuthority.FileName);
                    if (!File.Exists(authorityPath)) continue;
                    string authorityDigest = RunAuthority.LoadIdentity(child).Digest;
                    CortexPolicyDecisionPacket packet = TapePacketCreator.DecodePolicyDecision(payload);
                    LoopClosureChildOutcomeReference childOutcome = rail.ordinaryOutcomeRequired
                        ? new(Path.GetFileName(child), relative, new(authorityDigest), new(railDigest), packet.DecisionID,
                            new TapeEventID(rail.executedOutcomeEventID), new(rail.executedOutcomePayloadSHA256), BeforeSeal: true)
                        : default;
                    evidence = new(Path.GetFileName(child), relative, view.Id, packet.DecisionID, evidenceStep,
                        payload, readout,
                        LoopClosureLinkAttemptStore.DigestJournalReceipt(evidenceStep, "policy-decision", view.Id.Value),
                        new(authorityDigest), new(railDigest), childOutcome);
                    return true;
                }
            }
            catch (Exception error) when (error is InvalidDataException or IOException or FormatException) { }
        }
        return false;
    }

    private static bool TryDecodePolicyDecisionID(ReadOnlySpan<byte> payload, ulong decisionID)
    {
        try { return TapePacketCreator.DecodePolicyDecision(payload).DecisionID.Value == decisionID; }
        catch (InvalidDataException) { return false; }
    }

    private static CortexPolicyDecisionReadout ReadPolicyDecision(ReadOnlySpan<byte> payload)
    {
        try { return TapePacketCreator.DecodePolicyDecision(payload).Readout; }
        catch (InvalidDataException) { return default; }
    }

    internal static LoopClosureLinkAttempt CreateLoopClosureLinkAttempt(
        string runID, string name, LoopClosureLinkSpecies species, LoopClosureLinkPaths path, LoopClosureLinkStates state,
        int step, TapeEventID eventID, byte[] payload, long predecessorEventID, LoopClosureDigest predecessorEvidence,
        LoopClosureDigest journalDigest, LoopClosureGateDenialReasons denialReason, bool hasDenialReason,
        LoopClosureQuotaID fundingID, LoopClosureDigest forkDigest, LoopClosureDigest divergenceDigest,
        Grammar.GrammarRevisionID revision, LoopClosureDigest predecessorAttempt = default,
        string evidenceRunID = "", string evidenceRelativePath = "", LoopClosureDigest evidenceAuthorityDigest = default,
        LoopClosureDigest evidenceRailDigest = default,
        LoopClosureChildOutcomeReference childOutcome = default)
        => LoopClosureLinkAttempt.Create($"{name}-{eventID.Value}", runID, species, path, state, step, eventID,
            DigestPayload(payload), predecessorEventID, predecessorEvidence, journalDigest, denialReason, hasDenialReason,
            fundingID, forkDigest, divergenceDigest, revision, predecessorAttempt, evidenceRunID, evidenceRelativePath,
            evidenceAuthorityDigest, evidenceRailDigest, childOutcome);

    private static LoopClosureDigest DigestPayload(byte[] payload)
        => new(LoopClosureLinkAttemptStore.DigestPayload(payload));

    private static bool TryDecodeOrganicPreference(ReadOnlySpan<byte> payload)
        => TryDecodeOrganicPolicyDecision(payload, out CortexPolicyDecisionReadout readout)
            && (readout.RawCandidateAction < 0 || readout.RawCandidateAction == readout.LaunchpadAction);

    private static bool TryDecodeOrganicPolicyDecision(ReadOnlySpan<byte> payload, out CortexPolicyDecisionReadout readout)
    {
        readout = default;
        try
        {
            CortexPolicyDecisionPacket packet = TapePacketCreator.DecodePolicyDecision(payload);
            readout = packet.Readout;
            return readout.SelectionCause is CortexPolicySelectionCauses.Launchpad or CortexPolicySelectionCauses.ShadowCandidate;
        }
        catch (InvalidDataException) { return false; }
    }

    private static CortexPolicyDecisionReadout ReadOrganicPolicyDecision(ReadOnlySpan<byte> payload)
        => TapePacketCreator.DecodePolicyDecision(payload).Readout;

    private static bool MatchesPaidDivergencePayload(ReadOnlySpan<byte> payload, in PolicyBoundaryDivergenceProof proof)
    {
        string text = Encoding.ASCII.GetString(payload);
        return text.StartsWith("POLICY-FUNDED-DISSENT\t", StringComparison.Ordinal)
            && text.Contains("\tfunding=" + proof.Funding.QuotaDecisionID, StringComparison.Ordinal)
            && text.Contains("\treadout=u:" + proof.ReadoutFingerprint.ToString("X16"), StringComparison.Ordinal)
            && text.Contains("\trevision=" + proof.ReadoutRevision.Value, StringComparison.Ordinal)
            && text.Contains("\texecution=" + (proof.ForkReceipt.ExecutionCorroboration?.PaidDivergenceExecutionCorroborationSHA256.Value ?? "none"), StringComparison.Ordinal);
    }

    private static Dictionary<long, (int Step, string Kind)> ReadLinkJournalRows(string path)
    {
        Dictionary<long, (int Step, string Kind)> rows = new();
        foreach (string line in File.ReadLines(path))
        {
            string[] fields = line.Split('\t');
            if (fields.Length < 3 || !int.TryParse(fields[0], out int step) || fields[2].Length < 2 || fields[2][0] != 's'
                || !long.TryParse(fields[2].AsSpan(1), out long eventID)) continue;
            rows[eventID] = (step, fields[1]);
        }
        return rows;
    }
}

[RonObject]
internal partial class LoopClosureLinkAttemptRON
{
    public int schemaVersion;
    public string recordID = ""; public string runID = ""; public string species = ""; public string path = ""; public string state = "";
    public int step; public long eventID; public string evidenceSHA256 = ""; public long predecessorEventID = -1; public string predecessorEvidenceSHA256 = "";
    public string journalSHA256 = ""; public string denialReason = ""; public bool hasDenialReason; public string fundingID = ""; public string forkReceiptSHA256 = "";
    public string dissentEvidenceSHA256 = ""; public ulong grammarRevision; public string attemptSHA256 = ""; public string predecessorAttemptSHA256 = "";
    public string evidenceRunID = ""; public string evidenceRelativePath = ""; public string evidenceAuthoritySHA256 = ""; public string evidenceRailSHA256 = "";
    public string childOutcomeRunID = ""; public string childOutcomeRelativePath = "";
    public string childOutcomeAuthoritySHA256 = ""; public string childOutcomeRailSHA256 = "";
    public ulong childOutcomeForcedDecisionID; public long childOutcomeEventID;
    public string childOutcomePayloadSHA256 = ""; public bool childOutcomeBeforeSeal;
    public long linkEventID; public string linkPacketSHA256 = ""; public string linkJournalSHA256 = "";
}
