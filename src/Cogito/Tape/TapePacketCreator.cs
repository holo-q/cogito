namespace Cogito;

using System.Text;
using System.Globalization;
using System.Security.Cryptography;
using Cogito.Exec;
using Cogito.Grammar;

/// Writes domain objects onto the tape. Callers pass the object they have; this type owns the
/// byte encoding, source label, provenance, and journal side effect for that packet kind.
internal static class TapePacketCreator
{
    /// A policy teacher is two tape events: the grammar learner's context/continuation
    /// and an audit-only companion carrying raw measurements and fold provenance. Keeping
    /// both IDs typed prevents an audit tail from being mistaken for learner input.
    internal readonly record struct PolicyTeacherPacketIDs(TapeEventID GrammarEventID, TapeEventID AuditOnlyEventID)
    {
        internal void Validate()
        {
            if (GrammarEventID.Value < 0 || AuditOnlyEventID.Value < 0 || GrammarEventID == AuditOnlyEventID)
                throw new InvalidDataException("policy teacher packet identities are malformed");
        }
    }

    internal static ReadOnlySpan<byte> LoopLineagePrefix => "LOOP-LINEAGE-R1\n"u8;
    private static ReadOnlySpan<byte> RepositoryOccurrenceCheckPrefix => "REPOSITORY-VERIFICATION\t"u8;
    private const string WorldManifestSource = "world:manifest";
    private const string AdmissionPlanSource = "world:encounter";
    // Frozen wire token; identifier-side name is PatternEconomicsSource.
    private const string RepositoryPatternEconomicsSource = "repository:theory-economics";
    // Frozen wire token; identifier-side name is PatternSource.
    private const string RepositoryPatternSource = "repository:theory";
    private const string MetricSource = "self:metric";
    private static ReadOnlySpan<byte> MetricPrefix => "METRIC\t"u8;
    private static ReadOnlySpan<byte> PolicyDecisionPrefix => "POLICY-DECISION"u8;
    private const string PolicyBoundarySourceName = "POLICY-BOUNDARY-SOURCE";
    private static ReadOnlySpan<byte> PolicyOutcomePrefix => "POLICY-OUTCOME"u8;
    private static ReadOnlySpan<byte> OrganicComparisonPrefix => "ORGANIC-COMPARISON"u8;
    // Frozen tape prefix: POLICY-VERIFICATION remains the wire token; the identifier uses OccurrenceCheck.
    private static ReadOnlySpan<byte> PolicyOccurrenceCheckPrefix => "POLICY-VERIFICATION"u8;
    private const string PolicyTrialQuotaPrefix = "POLICY-TRIAL-FUNDING";
    private static ReadOnlySpan<byte> PolicyTrialCompletionPrefix => "POLICY-TRIAL-SETTLEMENT"u8;
    // Frozen tape source token; only the identifier uses InstallRevision.
    private const string GrammarFoldInstallRevisionPrefix = "GRAMMAR-FOLD-PUBLICATION";
    internal const string PolicyBoundaryPrefix = "POLICY-BOUNDARY";
    private static ReadOnlySpan<byte> PolicyGrammarContextPrefix => "POLICY-CONTEXT"u8;
    // Frozen tape source token; only the identifier uses AuditOnly.
    private static ReadOnlySpan<byte> PolicyTeacherAuditOnlyPrefix => "POLICY-TEACHER-CUSTODY"u8;
    private static ReadOnlySpan<byte> PolicyField => "\tpolicy="u8;
    private static ReadOnlySpan<byte> DecisionField => "\tdecision="u8;
    private static ReadOnlySpan<byte> AuthorityField => "\tauthority="u8;
    private static ReadOnlySpan<byte> RevisionField => "\trevision="u8;
    private static ReadOnlySpan<byte> ReadoutFingerprintField => "\treadout-fingerprint="u8;
    private static ReadOnlySpan<byte> CandidateFingerprintField => "\tcandidate-fingerprint="u8;
    private static ReadOnlySpan<byte> SupportDigestField => "\tsupport="u8;
    private static ReadOnlySpan<byte> ActionField => "\taction="u8;
    private static ReadOnlySpan<byte> LaunchpadActionField => "\tlaunchpad-action="u8;
    private static ReadOnlySpan<byte> RawCandidateActionField => "\traw-candidate-action="u8;
    private static ReadOnlySpan<byte> SelectedCandidateActionField => "\tselected-candidate-action="u8;
    private static ReadOnlySpan<byte> SelectionCauseField => "\tselection-cause="u8;
    private static ReadOnlySpan<byte> ActionCountField => "\taction-count="u8;
    private static ReadOnlySpan<byte> DrillField => "\tdrill="u8;
    private static ReadOnlySpan<byte> FeaturesField => "\tfeatures="u8;
    private static ReadOnlySpan<byte> InvariantField => "\tinvariant="u8;
    private static ReadOnlySpan<byte> ConservedCostField => "\tconserved-cost="u8;
    private static ReadOnlySpan<byte> OutcomesField => "\toutcomes="u8;
    private static ReadOnlySpan<byte> FingerprintField => "\tfingerprint="u8;
    private static ReadOnlySpan<byte> ComparisonsField => "\tcomparisons="u8;
    private static ReadOnlySpan<byte> AgreementsField => "\tagreements="u8;
    private static ReadOnlySpan<byte> FailuresField => "\tfailures="u8;
    private static ReadOnlySpan<byte> PassedField => "\tpassed="u8;

    private const int TypedNumberLength = 18;
    private const int SampleLength = 24;

    /// Emit a typed lineage edge through the same tape/journal turnstile as every other
    /// ordinary packet. The receipt's causal node event ID is intentionally distinct from
    /// this packet event ID; the latter is only the durable mirror address.
    internal static TapeEventID AppendLoopLineageEdge(
        Tape tape,
        Journal journal,
        int step,
        in LoopLineageEdgeReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(tape);
        ArgumentNullException.ThrowIfNull(journal);
        receipt.Validate();
        byte[] receiptBytes = receipt.Encode();
        byte[] encoded = new byte[LoopLineagePrefix.Length + receiptBytes.Length];
        LoopLineagePrefix.CopyTo(encoded);
        receiptBytes.CopyTo(encoded, LoopLineagePrefix.Length);
        TapeEventID eventID = tape.Append(encoded, "lineage", Provenances.Execution, TapeEventRoles.AuditOnly);
        journal.RecordLoopLineageEdge(step, eventID, receipt, encoded);
        return eventID;
    }

    internal static bool TryDecodeLoopLineageEdge(ReadOnlySpan<byte> packet, out LoopLineageEdgeReceipt receipt)
    {
        if (!packet.StartsWith(LoopLineagePrefix)) { receipt = null!; return false; }
        receipt = LoopLineageEdgeReceipt.Decode(packet[LoopLineagePrefix.Length..]);
        return true;
    }

    internal static TapeEventID AppendRepositoryOccurrenceCheckReceipt(
        Tape tape, Journal journal, int step, in RepositoryOccurrenceCheckReceipt receipt)
    {
        receipt.Validate();
        // Frozen tape prefix and field names; identifier-side names are OccurrenceCheck and Prediction.
        string packet = $"REPOSITORY-VERIFICATION\tstep={receipt.Step}\tspecies={receipt.Prediction.Species}\tclaim={receipt.Prediction.Canonical}\toutcome={receipt.Outcome}\tworld={receipt.WorldSHA256}\taccess={receipt.AccessSHA256}\taccess-sequence={receipt.AccessSequence}\taccess-entry-sha256={receipt.AccessEntrySHA256}\taccess-entry-count={receipt.AccessEntryCount}\tclaim-sha256={receipt.PredictionSHA256}\tevidence={receipt.EvidenceSHA256}\tevaluator-cost={receipt.EvaluatorCost}\taccess-cost={receipt.AccessCost}\tpredecessor={receipt.PredecessorEventID.Value}\tcall={receipt.CallSHA256}\treceipt={receipt.ReceiptSHA256}";
        byte[] encoded = Encoding.UTF8.GetBytes(packet);
        // Frozen tape source token; identifier-side name is OccurrenceCheck.
        TapeEventID eventID = tape.Append(encoded, "repository:verification", Provenances.Execution,
            TapeEventRoles.Measurement | TapeEventRoles.AuditOnly);
        journal.RecordRepositoryOccurrenceCheck(step, eventID, receipt, encoded);
        return eventID;
    }

    /// Append the ordinary native repository task action. The selection receipt
    /// must already be on the same tape; this turnstile never repairs a missing
    /// predecessor or appends a post-hoc substitute.
    internal static TapeEventID AppendRepositoryLoopTaskAction(
        Tape tape,
        Journal journal,
        int step,
        in RepositoryLoopTaskActionReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(tape);
        ArgumentNullException.ThrowIfNull(journal);
        receipt.Validate();
        RequireRepositorySelectionPredecessor(tape, in receipt);
        byte[] encoded = receipt.Encode();
        const string source = "repository-action";
        ObservationEnvelope observation = BeginObservation(source, Provenances.Execution, TapeEventRoles.AuditOnly);
        journal.EnsureRepositoryTaskIndex(tape);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.RecordRepositoryLoopTaskReceipt(step, eventID, source, RepositoryLoopTaskReceiptCodec.ActionPrefix,
            receipt.SelectionEventID, receipt.ReceiptSHA256, encoded);
        return eventID;
    }

    internal static TapeEventID AppendRepositoryLoopTaskOccurrenceCheck(
        Tape tape,
        Journal journal,
        int step,
        in RepositoryLoopTaskOccurrenceCheckReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(tape);
        ArgumentNullException.ThrowIfNull(journal);
        receipt.Validate();
        RequireRepositoryTaskPredecessor(tape, receipt.ActionEventID, "repository-action", receipt.ActionPayloadSHA256);
        if (!tape.Resolve(receipt.ActionEventID, out byte[] actionPayload)
            || !RepositoryLoopTaskActionReceipt.TryDecode(actionPayload, out RepositoryLoopTaskActionReceipt action)
            || action.TaskID != receipt.TaskID
            || action.TaskSpecies != receipt.TaskSpecies
            || action.TaskAuthoritySHA256 != receipt.TaskAuthoritySHA256
            || action.CallSHA256 != receipt.CallSHA256)
            throw new InvalidDataException("repository task occurrence check does not bind its action task/call");
        byte[] encoded = receipt.Encode();
        // Frozen journal source; identifier-side name is OccurrenceCheck.
        const string source = "repository-verification";
        ObservationEnvelope observation = BeginObservation(source, Provenances.Execution, TapeEventRoles.AuditOnly);
        journal.EnsureRepositoryTaskIndex(tape);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.RecordRepositoryLoopTaskReceipt(step, eventID, source, RepositoryLoopTaskReceiptCodec.OccurrenceCheckPrefix,
            receipt.ActionEventID, receipt.ReceiptSHA256, encoded);
        return eventID;
    }

    internal static TapeEventID AppendRepositoryLoopTaskOutcome(
        Tape tape,
        Journal journal,
        int step,
        in RepositoryLoopTaskOutcomeReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(tape);
        ArgumentNullException.ThrowIfNull(journal);
        receipt.Validate();
        RequireRepositoryTaskPredecessor(tape, receipt.OccurrenceCheckEventID, "repository-verification", receipt.OccurrenceCheckPayloadSHA256);
        if (!tape.Resolve(receipt.OccurrenceCheckEventID, out byte[] occurrenceCheckPayload)
            || !RepositoryLoopTaskOccurrenceCheckReceipt.TryDecode(occurrenceCheckPayload, out RepositoryLoopTaskOccurrenceCheckReceipt occurrenceCheck)
            || occurrenceCheck.TaskID != receipt.TaskID
            || occurrenceCheck.TaskSpecies != receipt.TaskSpecies
            || occurrenceCheck.TaskAuthoritySHA256 != receipt.TaskAuthoritySHA256
            || occurrenceCheck.Outcome != receipt.VerifierOutcome
            || !tape.Resolve(occurrenceCheck.ActionEventID, out byte[] actionPayload)
            || !RepositoryLoopTaskActionReceipt.TryDecode(actionPayload, out RepositoryLoopTaskActionReceipt action)
            || action.CandidateSpecies != receipt.CandidateSpecies
            || action.CandidateCanonical != receipt.CandidateCanonical
            || action.CandidateDigest != receipt.CandidateDigest)
            throw new InvalidDataException("repository task outcome does not bind its occurrence-check/action candidate");
        byte[] encoded = receipt.Encode();
        const string source = "repository-outcome";
        ObservationEnvelope observation = BeginObservation(source, Provenances.Execution, TapeEventRoles.AuditOnly);
        journal.EnsureRepositoryTaskIndex(tape);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.RecordRepositoryLoopTaskReceipt(step, eventID, source, RepositoryLoopTaskReceiptCodec.OutcomePrefix,
            receipt.OccurrenceCheckEventID, receipt.ReceiptSHA256, encoded);
        return eventID;
    }

    internal readonly record struct RepositoryLoopTaskReceiptEventIDs(
        TapeEventID ActionEventID,
        TapeEventID OccurrenceCheckEventID,
        TapeEventID OutcomeEventID);

    /// Validate and append the complete task audit-only chain as one contiguous
    /// tape transaction. Receipt payloads carry predicted predecessor ids, so
    /// all joins are checked before the tape mutates and no action-only prefix
    /// can become a checkpoint boundary.
    internal static RepositoryLoopTaskReceiptEventIDs AppendRepositoryLoopTaskTransaction(
        Tape tape,
        Journal journal,
        int step,
        in RepositoryLoopTaskActionReceipt action,
        in RepositoryLoopTaskOccurrenceCheckReceipt occurrenceCheck,
        in RepositoryLoopTaskOutcomeReceipt outcome)
    {
        ArgumentNullException.ThrowIfNull(tape);
        ArgumentNullException.ThrowIfNull(journal);
        action.Validate(); occurrenceCheck.Validate(); outcome.Validate();
        RequireRepositorySelectionPredecessor(tape, in action);
        TapeEventID actionEventID = new(tape.NextId);
        TapeEventID occurrenceCheckEventID = new(actionEventID.Value + 1);
        TapeEventID outcomeEventID = new(actionEventID.Value + 2);
        byte[] actionPayload = action.Encode();
        byte[] occurrenceCheckPayload = occurrenceCheck.Encode();
        byte[] outcomePayload = outcome.Encode();
        string actionPayloadSHA = Convert.ToHexStringLower(SHA256.HashData(actionPayload));
        string occurrenceCheckPayloadSHA = Convert.ToHexStringLower(SHA256.HashData(occurrenceCheckPayload));
        if (occurrenceCheck.ActionEventID != actionEventID
            || occurrenceCheck.ActionPayloadSHA256 != actionPayloadSHA
            || outcome.OccurrenceCheckEventID != occurrenceCheckEventID
            || outcome.OccurrenceCheckPayloadSHA256 != occurrenceCheckPayloadSHA
            || occurrenceCheck.TaskID != action.TaskID || occurrenceCheck.TaskSpecies != action.TaskSpecies
            || occurrenceCheck.TaskAuthoritySHA256 != action.TaskAuthoritySHA256 || occurrenceCheck.CallSHA256 != action.CallSHA256
            || outcome.TaskID != action.TaskID || outcome.TaskSpecies != action.TaskSpecies
            || outcome.TaskAuthoritySHA256 != action.TaskAuthoritySHA256
            || outcome.CandidateDigest != action.CandidateDigest || outcome.CandidateCanonical != action.CandidateCanonical
            || outcome.VerifierOutcome != occurrenceCheck.Outcome)
            throw new InvalidDataException("repository task transaction payload joins diverge");

        journal.EnsureRepositoryTaskIndex(tape);
        journal.ValidateRepositoryLoopTaskTransaction(
            step, actionEventID, action, actionPayload, occurrenceCheckEventID, occurrenceCheck, occurrenceCheckPayload,
            outcomeEventID, outcome, outcomePayload);
        journal.PrepareRepositoryLoopTaskTransaction(
            step, actionEventID, actionPayload, occurrenceCheckEventID, occurrenceCheckPayload, outcomeEventID, outcomePayload);
        // The journal write rides inside the tape transaction so a torn append can never leave a
        // committed action row without its occurrence-check/outcome siblings. The commit callback is a
        // lambda, which cannot capture the `in` receipts — they are copied for the closure.
        RepositoryLoopTaskActionReceipt committedAction = action;
        RepositoryLoopTaskOccurrenceCheckReceipt committedOccurrenceCheck = occurrenceCheck;
        RepositoryLoopTaskOutcomeReceipt committedOutcome = outcome;
        TapeEventID[] eventIDs = tape.AppendTransaction([
            new(actionPayload, "repository-action", Provenances.Execution, TapeEventRoles.AuditOnly),
            // Frozen journal source; identifier-side name is OccurrenceCheck.
            new(occurrenceCheckPayload, "repository-verification", Provenances.Execution, TapeEventRoles.AuditOnly),
            new(outcomePayload, "repository-outcome", Provenances.Execution, TapeEventRoles.AuditOnly)],
            _ => journal.RecordRepositoryLoopTaskTransaction(
                step, actionEventID, committedAction, actionPayload, occurrenceCheckEventID, committedOccurrenceCheck,
                occurrenceCheckPayload, outcomeEventID, committedOutcome, outcomePayload));
        if (eventIDs[0] != actionEventID || eventIDs[1] != occurrenceCheckEventID || eventIDs[2] != outcomeEventID)
            throw new InvalidDataException("repository task transaction event reservation diverged");
        return new(actionEventID, occurrenceCheckEventID, outcomeEventID);
    }

    /// Append the one terminal repository seal after every ordinary runtime
    /// transition has landed. The packet binds the pre-seal tape digest and the
    /// immutable authority digest; the final tape digest is computed only after
    /// this event exists.
    internal static TapeEventID AppendRepositoryLoopSeal(
        Tape tape,
        Journal journal,
        int step,
        string preSealTapeSHA256,
        string immutableAuthoritySHA256,
        out RepositoryLoopClosureTapeSeal seal)
    {
        ArgumentNullException.ThrowIfNull(tape);
        ArgumentNullException.ThrowIfNull(journal);
        RequireSHA(preSealTapeSHA256, "repository seal pre-seal tape");
        RequireSHA(immutableAuthoritySHA256, "repository seal immutable authority");
        TapeEventID eventID = new(tape.NextId);
        byte[] encoded = Encoding.UTF8.GetBytes(string.Join('\t',
            "REPOSITORY-LOOP-SEAL", "version=3", $"event={eventID.Value}",
            $"pre-seal={preSealTapeSHA256}", $"authority={immutableAuthoritySHA256}"));
        string payloadSHA256 = Convert.ToHexStringLower(SHA256.HashData(encoded));
        string receiptSHA256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            "repository-loop-seal-v3", eventID.Value, preSealTapeSHA256,
            immutableAuthoritySHA256, payloadSHA256, "repository-seal",
            Provenances.Execution, TapeEventRoles.AuditOnly))));
        seal = new(eventID, payloadSHA256, receiptSHA256, preSealTapeSHA256,
            "repository-seal", Provenances.Execution, TapeEventRoles.AuditOnly)
        { ImmutableAuthoritySHA256 = immutableAuthoritySHA256 };
        TapeEventID appended = tape.Append(encoded, "repository-seal", Provenances.Execution, TapeEventRoles.AuditOnly);
        if (appended != eventID) throw new InvalidDataException("repository seal event reservation was consumed");
        journal.RecordRepositoryLoopSeal(step, appended, encoded);
        return appended;
    }

    internal static bool TryDecodeRepositoryLoopSeal(ReadOnlySpan<byte> payload, out TapeEventID eventID,
        out string preSealTapeSHA256, out string immutableAuthoritySHA256)
    {
        eventID = default; preSealTapeSHA256 = ""; immutableAuthoritySHA256 = "";
        string[] fields = Encoding.UTF8.GetString(payload).Split('\t');
        if (fields.Length != 5 || fields[0] != "REPOSITORY-LOOP-SEAL" || fields[1] != "version=3"
            || !TryParseField(fields[2], "event", out string eventText)
            || !long.TryParse(eventText, out long eventValue)
            || !TryParseField(fields[3], "pre-seal", out preSealTapeSHA256)
            || !TryParseField(fields[4], "authority", out immutableAuthoritySHA256))
            return false;
        eventID = new TapeEventID(eventValue);
        return eventID.Value >= 0 && IsSHA(preSealTapeSHA256) && IsSHA(immutableAuthoritySHA256);
    }

    internal static byte[] EncodeRepositoryLoopTaskAction(in RepositoryLoopTaskActionReceipt receipt) => receipt.Encode();
    internal static byte[] EncodeRepositoryLoopTaskOccurrenceCheck(in RepositoryLoopTaskOccurrenceCheckReceipt receipt) => receipt.Encode();
    internal static byte[] EncodeRepositoryLoopTaskOutcome(in RepositoryLoopTaskOutcomeReceipt receipt) => receipt.Encode();
    internal static RepositoryLoopTaskActionReceipt DecodeRepositoryLoopTaskAction(ReadOnlySpan<byte> payload)
        => RepositoryLoopTaskActionReceipt.Decode(payload);
    internal static RepositoryLoopTaskOccurrenceCheckReceipt DecodeRepositoryLoopTaskOccurrenceCheck(ReadOnlySpan<byte> payload)
        => RepositoryLoopTaskOccurrenceCheckReceipt.Decode(payload);
    internal static RepositoryLoopTaskOutcomeReceipt DecodeRepositoryLoopTaskOutcome(ReadOnlySpan<byte> payload)
        => RepositoryLoopTaskOutcomeReceipt.Decode(payload);

    internal static bool TryDecodeRepositoryLoopTaskAction(ReadOnlySpan<byte> payload, out RepositoryLoopTaskActionReceipt receipt)
        => RepositoryLoopTaskActionReceipt.TryDecode(payload, out receipt);
    internal static bool TryDecodeRepositoryLoopTaskOccurrenceCheck(ReadOnlySpan<byte> payload, out RepositoryLoopTaskOccurrenceCheckReceipt receipt)
        => RepositoryLoopTaskOccurrenceCheckReceipt.TryDecode(payload, out receipt);
    internal static bool TryDecodeRepositoryLoopTaskOutcome(ReadOnlySpan<byte> payload, out RepositoryLoopTaskOutcomeReceipt receipt)
        => RepositoryLoopTaskOutcomeReceipt.TryDecode(payload, out receipt);
    private static void RequireRepositoryTaskPredecessor(
        Tape tape,
        TapeEventID predecessorEventID,
        string source,
        string expectedPayloadSHA256)
    {
        if (!tape.TryGetEventView(predecessorEventID, out TapeEventView view)
            || view.Source != source
            || view.Provenance != Provenances.Execution
            || view.Roles != TapeEventRoles.AuditOnly
            || !tape.Resolve(predecessorEventID, out byte[] payload)
            || Convert.ToHexStringLower(SHA256.HashData(payload)) != expectedPayloadSHA256
            || source == "repository-action" && !RepositoryLoopTaskActionReceipt.TryDecode(payload, out _)
            || source == "repository-verification" && !RepositoryLoopTaskOccurrenceCheckReceipt.TryDecode(payload, out _))
            throw new InvalidDataException($"repository task predecessor is not an ordinary {source} event");
    }

    private static void RequireRepositorySelectionPredecessor(
        Tape tape,
        in RepositoryLoopTaskActionReceipt action)
    {
        if (!tape.TryGetEventView(action.SelectionEventID, out TapeEventView view)
            || view.Source != "repository-selection"
            || view.Provenance != Provenances.Execution
            || view.Roles != (TapeEventRoles.Measurement | TapeEventRoles.AuditOnly)
            || !tape.Resolve(action.SelectionEventID, out byte[] payload)
            || !RepositorySelectionReceipt.TryDecode(payload, out RepositorySelectionReceipt selection)
            || selection.ReceiptSHA256 != action.SelectionReceiptSHA256
            || selection.SelectionOrdinal != action.SelectionOrdinal
            || selection.FrontierRevision != action.FrontierRevision
            || selection.FrontierAuthoritySHA256 != action.FrontierAuthoritySHA256
            || selection.CandidateSpecies != action.CandidateSpecies
            || selection.CandidateCanonical != action.CandidateCanonical
            || selection.CandidateDigest != action.CandidateDigest
            || action.CallSHA256 != Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                Tool.ToolCall.Create(action.Candidate.Verb, action.Candidate.Argument).Raw))))
            throw new InvalidDataException("repository task predecessor is not an ordinary repository-selection event");
    }

    internal static TapeEventID AppendRepositoryPatternAdmissionEconomics(
        Tape tape, Journal journal, int step, ReadOnlySpan<byte> payload, out JournalRowBinding journalBinding)
    {
        byte[] encoded = payload.ToArray();
        ObservationEnvelope observation = BeginObservation(RepositoryPatternEconomicsSource,
            Provenances.Execution, TapeEventRoles.Measurement | TapeEventRoles.AuditOnly);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journalBinding = journal.MintWithBinding(step, eventID, RepositoryPatternEconomicsSource, encoded);
        return eventID;
    }

    internal static TapeEventID AppendRepositoryPatternGrammarInput(
        Tape tape, Journal journal, int step, ReadOnlySpan<byte> payload, out JournalRowBinding journalBinding)
    {
        byte[] encoded = payload.ToArray();
        ObservationEnvelope observation = BeginObservation(RepositoryPatternSource,
            Provenances.Reflected, TapeEventRoles.GrammarInput);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journalBinding = journal.MintWithBinding(step, eventID, RepositoryPatternSource, encoded);
        return eventID;
    }

    internal static bool TryReadWorldEncounterObservation(ReadOnlySpan<byte> packet, out TapeEventID observationID)
    {
        observationID = default;
        string[] fields = Encoding.ASCII.GetString(packet).Split('\t');
        if (fields.Length < 2 || fields[0] != "WORLD-ENCOUNTER") return false;
        foreach (string field in fields[1..])
            if (field.StartsWith("observation=", StringComparison.Ordinal)
                && long.TryParse(field["observation=".Length..], out long value)
                && value >= 0)
            {
                observationID = new TapeEventID(value);
                return true;
            }
        return false;
    }

    internal static bool TryReadAdmissionPlanDomain(ReadOnlySpan<byte> packet, out int domain)
    {
        domain = -1;
        string[] fields = Encoding.ASCII.GetString(packet).Split('\t');
        if (fields.Length < 2 || fields[0] != "WORLD-ENCOUNTER") return false;
        foreach (string field in fields[1..])
            if (field.StartsWith("domain=", StringComparison.Ordinal)
                && int.TryParse(field["domain=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                && value >= 0)
            {
                domain = value;
                return true;
            }
        return false;
    }

    /// Read the corpus event IDs named by WORLD-ENCOUNTER receipts already on a
    /// restored tape. The receipt's own event ID is not the opportunity root;
    /// its observation field is the adjacent corpus event returned by
    /// CommitWorldEncounter.
    internal static IReadOnlyList<TapeEventID> ReadWorldEncounterEventIDs(Tape tape)
    {
        List<TapeEventID> eventIDs = new();
        foreach (TapeEventView view in tape.GetEventViews())
        {
            if (!string.Equals(view.Source, AdmissionPlanSource, StringComparison.Ordinal)
                || !tape.Resolve(view.Id, out byte[] payload)
                || !TryReadWorldEncounterObservation(payload, out TapeEventID observationID)) continue;
            eventIDs.Add(observationID);
        }
        return eventIDs.Distinct().OrderBy(static id => id.Value).ToArray();
    }

    internal static TapeEventID AppendLoopLineageEvidence(Tape tape, Journal journal, int step, TapeEventID outcomeEventID)
    {
        byte[] encoded = Encoding.ASCII.GetBytes($"LOOP-LINEAGE-EVIDENCE\toutcome={outcomeEventID.Value}");
        ObservationEnvelope observation = BeginObservation("lineage:evidence", Provenances.Execution, TapeEventRoles.AuditOnly);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.Mint(step, eventID, "lineage:evidence", encoded);
        return eventID;
    }

    public static TapeEventID AppendCorpusItem(Tape tape, Journal journal, int step, List<byte[]> lines, int index)
    {
        var observation = BeginObservation("corpus", Provenances.Real, TapeEventRoles.GrammarInput);
        var bytes = lines[index];
        var sid = EndObservation(tape, in observation, bytes);
        journal.Ingest(step, sid, "corpus", bytes);
        return sid;
    }

    public static TapeEventID AppendWorldManifest(Tape tape, Journal journal, int workloadCount)
    {
        byte[] encoded = Encoding.ASCII.GetBytes($"WORLD-MANIFEST\titems={workloadCount}");
        ObservationEnvelope observation = BeginObservation(WorldManifestSource, Provenances.Execution, TapeEventRoles.AuditOnly);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.Mint(0, eventID, WorldManifestSource, encoded);
        return eventID;
    }

    /// The receipt is emitted before the observation it authorizes. Internal world contact is deterministic from
    /// this packet; only records cross frames, checkpoints, and forks.
    public static TapeEventID CommitWorldEncounter(
        Tape tape,
        Journal journal,
        int step,
        List<byte[]> lines,
        int index,
        int domain,
        bool fresh,
        double coverage)
        => CommitWorldEncounter(tape, journal, step, lines[index], index, domain, fresh, coverage);

    /// Commit one bounded world source item. The byte payload stays owned by the
    /// admission cursor; no corpus-sized list is required at the turnstile.
    public static TapeEventID CommitWorldEncounter(
        Tape tape,
        Journal journal,
        int step,
        ReadOnlyMemory<byte> bytes,
        int index,
        int domain,
        bool fresh,
        double coverage)
        => CommitWorldEncounter(tape, journal, step, bytes.ToArray(), index, domain, fresh, coverage);

    internal static TapeEventID CommitWorldEncounter(
        Tape tape,
        Journal journal,
        int step,
        byte[] bytes,
        int index,
        int domain,
        bool fresh,
        double coverage)
    {
        long observationID = tape.NextId + 1;
        string coverageText = double.IsNaN(coverage) ? "nan" : coverage.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        byte[] encoded = Encoding.ASCII.GetBytes(
            $"WORLD-ENCOUNTER\tobservation={observationID}\titem={index}\tdomain={domain}\tfresh={(fresh ? 1 : 0)}\tcoverage={coverageText}");
        ObservationEnvelope receipt = BeginObservation(AdmissionPlanSource, Provenances.Execution, TapeEventRoles.Measurement | TapeEventRoles.AuditOnly);
        TapeEventID receiptID = EndObservation(tape, in receipt, encoded);
        journal.Mint(step, receiptID, AdmissionPlanSource, encoded);
        var observation = BeginObservation("corpus", Provenances.Real, TapeEventRoles.GrammarInput);
        TapeEventID sourceID = EndObservation(tape, in observation, bytes);
        journal.Ingest(step, sourceID, "corpus", bytes);
        return sourceID;
    }

    /// Append a source-backed repository admissionPlan as an audit-only/measurement pair, with the tool
    /// result's diet role decided by the caller's intake organ. Audit-only is unconditional — the
    /// access happened and the loop-closure chain validates its event, digest and journal row
    /// whatever the bytes were worth — so admission never suppresses the append; it only decides
    /// whether the result also carries GrammarInput and so counts as grammar diet. Payload and
    /// digests are byte-identical either way, which is what lets the re-grep null hold: a second
    /// look over known territory still records audit-only data and still earns nothing.
    /// The receipt is placed immediately before its source event so the generic lineage root
    /// predicate can recognize the pair after resume.
    internal static TapeEventID AppendRepositoryWorldEncounter(
        Tape tape,
        Journal journal,
        int step,
        in RepositoryAdmissionReceipt receipt,
        ReadOnlySpan<byte> sourceBytes,
        bool admitToGrammar)
    {
        ArgumentNullException.ThrowIfNull(tape);
        ArgumentNullException.ThrowIfNull(journal);
        receipt.Validate();
        if (receipt.Step != step)
            throw new InvalidDataException("repository world encounter packet step disagrees with journal step");
        byte[] ownedSourceBytes = sourceBytes.ToArray();
        string sourceEvidenceSHA256 = Convert.ToHexStringLower(SHA256.HashData(ownedSourceBytes));
        if (!string.Equals(sourceEvidenceSHA256, receipt.EvidenceSHA256, StringComparison.Ordinal))
            throw new InvalidDataException("repository world encounter evidence digest disagrees with source bytes");
        TapeEventID expectedObservation = new(tape.NextId + 1);
        if (receipt.ObservationEventID != expectedObservation)
            throw new InvalidDataException("repository world encounter observation identity does not match tape cursor");
        byte[] encodedReceipt = Encoding.UTF8.GetBytes(
            $"REPOSITORY-WORLD-ENCOUNTER\tstep={receipt.Step}\tobservation={receipt.ObservationEventID.Value}\tworld={receipt.WorldSHA256}\taccess={receipt.AccessSHA256}\taccess-sequence={receipt.AccessSequence}\taccess-entry-sha256={receipt.AccessEntrySHA256}\tcall={receipt.CallSHA256}\tpath64={Convert.ToBase64String(Encoding.UTF8.GetBytes(receipt.SourcePath))}\tline={receipt.SourceLine}\tevidence={receipt.EvidenceSHA256}\treceipt={receipt.ReceiptSHA256}");
        ObservationEnvelope envelope = BeginObservation("repository:encounter", Provenances.Execution,
            TapeEventRoles.Measurement | TapeEventRoles.AuditOnly);
        TapeEventID receiptEventID = EndObservation(tape, in envelope, encodedReceipt);
        journal.Mint(step, receiptEventID, "repository:encounter", encodedReceipt);
        TapeEventRoles sourceRoles = TapeEventRoles.Measurement | TapeEventRoles.AuditOnly
            | (admitToGrammar ? TapeEventRoles.GrammarInput : 0);
        ObservationEnvelope source = BeginObservation("repository:world", Provenances.Real, sourceRoles);
        TapeEventID observationEventID = EndObservation(tape, in source, ownedSourceBytes);
        journal.Mint(step, observationEventID, "repository:world", ownedSourceBytes);
        return observationEventID;
    }

    internal static bool TryReadRepositoryWorldEncounter(ReadOnlySpan<byte> packet, out RepositoryAdmissionReceipt receipt)
    {
        receipt = default;
        string[] fields = Encoding.UTF8.GetString(packet).Split('\t');
        if (fields.Length != 12 || fields[0] != "REPOSITORY-WORLD-ENCOUNTER") return false;
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        foreach (string field in fields[1..])
        {
            int equals = field.IndexOf('=');
            if (equals <= 0 || !values.TryAdd(field[..equals], field[(equals + 1)..])) return false;
        }
        string[] required = ["step", "observation", "world", "access", "access-sequence", "access-entry-sha256", "call", "path64", "line", "evidence", "receipt"];
        if (required.Any(key => !values.ContainsKey(key))
            || !int.TryParse(values["step"], NumberStyles.Integer, CultureInfo.InvariantCulture, out int step)
            || !long.TryParse(values["observation"], NumberStyles.Integer, CultureInfo.InvariantCulture, out long observation)
            || !long.TryParse(values["access-sequence"], NumberStyles.Integer, CultureInfo.InvariantCulture, out long accessSequence)
            || !int.TryParse(values["line"], NumberStyles.Integer, CultureInfo.InvariantCulture, out int line)) return false;
        string path;
        try { path = Encoding.UTF8.GetString(Convert.FromBase64String(values["path64"])); }
        catch (FormatException) { return false; }
        receipt = new RepositoryAdmissionReceipt(step, new TapeEventID(observation), values["world"], values["access"], values["call"],
            path, line, values["evidence"], values["receipt"])
        {
            AccessSequence = accessSequence,
            AccessEntrySHA256 = values["access-entry-sha256"],
        };
        try { receipt.Validate(); return true; }
        catch (InvalidDataException) { receipt = default; return false; }
    }

    /// Emit any typed repository lineage receipt as an ordinary audit-only/measurement
    /// event. The packet carries the canonical receipt text verbatim; the typed record
    /// remains the validator and the generic loop edge remains the ancestry authority.
    internal static TapeEventID AppendRepositoryLineageReceipt(
        Tape tape,
        Journal journal,
        int step,
        IRepositoryLineageReceipt receipt)
        => AppendRepositoryLineageReceipt(tape, journal, step, receipt, "repository:lineage",
            TapeEventRoles.Measurement | TapeEventRoles.AuditOnly);

    internal static TapeEventID AppendRepositoryOutcomeReceipt(
        Tape tape,
        Journal journal,
        int step,
        in RepositoryPaidOutcomeReceipt receipt)
        => AppendRepositoryLineageReceipt(tape, journal, step, receipt, "repository-outcome", TapeEventRoles.AuditOnly);

    internal static TapeEventID AppendRepositoryEvidenceReceipt(
        Tape tape,
        Journal journal,
        int step,
        in RepositoryNewEvidenceReceipt receipt)
        => AppendRepositoryLineageReceipt(tape, journal, step, receipt, "repository-evidence", TapeEventRoles.AuditOnly);

    internal static TapeEventID AppendRepositoryPreferenceComparison(
        Tape tape,
        Journal journal,
        int step,
        in RepositoryPreferenceComparisonReceipt receipt)
        => AppendRepositoryLineageReceipt(tape, journal, step, receipt, "repository-preference", TapeEventRoles.Measurement | TapeEventRoles.AuditOnly);

    internal static TapeEventID AppendRepositorySelection(
        Tape tape,
        Journal journal,
        int step,
        in RepositorySelectionReceipt receipt)
        => AppendRepositoryLineageReceipt(tape, journal, step, receipt, "repository-selection", TapeEventRoles.Measurement | TapeEventRoles.AuditOnly);

    /// Append a causal loop link at the transition that reached it.  The link
    /// packet is an ordinary in-run audit-only event; adjudication may only read it.
    internal static TapeEventID AppendRepositoryLoopClosureLink(
        Tape tape,
        Journal journal,
        int step,
        in LoopClosureLinkAttempt receipt)
        => AppendRepositoryLineageReceipt(tape, journal, step, receipt,
            "repository:loop-link", TapeEventRoles.AuditOnly);

    private static TapeEventID AppendRepositoryLineageReceipt(
        Tape tape,
        Journal journal,
        int step,
        IRepositoryLineageReceipt receipt,
        string source)
        => AppendRepositoryLineageReceipt(tape, journal, step, receipt, source,
            TapeEventRoles.Measurement | TapeEventRoles.AuditOnly);

    private static TapeEventID AppendRepositoryLineageReceipt(
        Tape tape,
        Journal journal,
        int step,
        IRepositoryLineageReceipt receipt,
        string source,
        TapeEventRoles roles)
    {
        ArgumentNullException.ThrowIfNull(tape);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(receipt);
        receipt.Validate();
        string encodedCanonical = Convert.ToBase64String(Encoding.UTF8.GetBytes(receipt.Canonical));
        byte[] encoded = Encoding.ASCII.GetBytes(
            $"REPOSITORY-LINEAGE\tkind={receipt.Kind}\tdigest={Encoding.UTF8.GetByteCount(receipt.Canonical)}:{RepositoryLineageReceiptCodec.Digest(receipt.Kind, receipt.Canonical)}\tcanonical={encodedCanonical}");
        TapeEventID eventID = tape.Append(encoded, source, Provenances.Execution, roles);
        journal.Mint(step, eventID, source, encoded);
        return eventID;
    }

    internal static bool TryReadRepositoryLineageReceipt(
        ReadOnlySpan<byte> packet,
        out string kind,
        out string canonical,
        out string digest)
    {
        kind = canonical = digest = "";
        string[] fields = Encoding.ASCII.GetString(packet).Split('\t');
        if (fields.Length != 4 || fields[0] != "REPOSITORY-LINEAGE") return false;
        if (!fields[1].StartsWith("kind=", StringComparison.Ordinal)
            || !fields[2].StartsWith("digest=", StringComparison.Ordinal)
            || !fields[3].StartsWith("canonical=", StringComparison.Ordinal)) return false;
        kind = fields[1]["kind=".Length..];
        string digestValue = fields[2]["digest=".Length..];
        int colon = digestValue.IndexOf(':');
        if (colon <= 0 || !int.TryParse(digestValue.AsSpan(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out int canonicalLength)
            || canonicalLength < 0 || !RepositoryLineageReceiptCodec.IsSHA(digestValue[(colon + 1)..])) return false;
        try { canonical = Encoding.UTF8.GetString(Convert.FromBase64String(fields[3]["canonical=".Length..])); }
        catch (FormatException) { return false; }
        digest = digestValue[(colon + 1)..];
        return Encoding.UTF8.GetByteCount(canonical) == canonicalLength
            && string.Equals(digest, RepositoryLineageReceiptCodec.Digest(kind, canonical), StringComparison.Ordinal);
    }

    public static TapeEventID AppendDomainCorpusItem(Tape tape, Journal journal, int step, List<byte[]> lines, int index)
        => AppendCorpusItem(tape, journal, step, lines, index);

    public static TapeEventID AppendGeneratedUtterance(Tape tape, Journal journal, int step, string source, ReadOnlyMemory<byte> line)
    {
        var observation = BeginObservation(source, Provenances.Replay, TapeEventRoles.GrammarInput);
        // Generated lines are already UTF-8. Keep valid bytes in their original encoding; retain the old replacement
        // behavior only for malformed input so the packet image remains byte-identical at the world boundary.
        byte[] safe = System.Text.Unicode.Utf8.IsValid(line.Span)
            ? line.ToArray()
            : Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(line.Span));
        var sid = EndObservation(tape, in observation, safe);
        journal.Mint(step, sid, source, safe);
        return sid;
    }

    public static TapeEventID AppendEmlMint(Tape tape, Journal journal, int step, in EmlMint mint, string source = "eml")
    {
        var provenance = mint.Grade == 'E' ? Provenances.Reflected : Provenances.Replay;
        var observation = BeginObservation(source, provenance, TapeEventRoles.GrammarInput);
        var encoded = Encoding.ASCII.GetBytes(mint.Line);
        var sid = EndObservation(tape, in observation, encoded);
        journal.Mint(step, sid, source, encoded);
        return sid;
    }

    public static TapeEventID AppendEmlCounterexample(Tape tape, Journal journal, int step, string text)
    {
        const string Source = "eml:counterexample";
        ObservationEnvelope observation = BeginObservation(Source, Provenances.Breach, TapeEventRoles.Measurement | TapeEventRoles.AuditOnly);
        byte[] encoded = Encoding.ASCII.GetBytes(text);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.Ingest(step, eventID, Source, encoded);
        return eventID;
    }

    public static TapeEventID AppendEmlRung0Closure(
        Tape tape, Journal journal, int step, EmlRung0ComposedFormObligationEvidence evidence, string species,
        EmlObligationTargetSpecies targetSpecies,
        IReadOnlyList<TapeEventID>? supportEventIDs = null,
        IReadOnlyList<string>? lawAdmissionIDs = null)
    {
        if (species is not ("derivation" or "displaced")) throw new ArgumentOutOfRangeException(nameof(species));
        if (string.IsNullOrEmpty(evidence.AdmissionPathCanonical)
            || string.IsNullOrEmpty(evidence.AdmissionPathFingerprint))
            throw new InvalidDataException("rung-0 closure packet omits its structural admission path");
        if (!Enum.IsDefined(targetSpecies)) throw new ArgumentOutOfRangeException(nameof(targetSpecies));
        supportEventIDs ??= Array.Empty<TapeEventID>();
        lawAdmissionIDs ??= Array.Empty<string>();
        if (targetSpecies == EmlObligationTargetSpecies.ExactComposition && supportEventIDs.Count == 0)
            throw new InvalidDataException("exact rung-0 closure packet omits source support events");
        string supports = string.Join(',', supportEventIDs.Select(static id => id.Value.ToString(CultureInfo.InvariantCulture)));
        string supportDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(supports)));
        string admissions = string.Join(',', lawAdmissionIDs);
        string source = "eml:rung0-" + species;
        // Frozen tape species token; identifier-side name is Rung0ComposedForm.
        string packet = $"RUNG0-{species.ToUpperInvariant()}\tspecies=Rung0DerivedForm\ttarget_species={targetSpecies}\tstatus=Accepted\tclaim={evidence.ObligationPredictionID.Value}\tobligation={evidence.ObligationID}\tderived={evidence.ComposedPredictionID.Value}\tproof={evidence.ProofID}\taudit={evidence.AuditID}\tproof_sha256={evidence.ProofSHA256}\taudit_sha256={evidence.AuditSHA256}\tadmission={evidence.AdmissionID}\tclosure={evidence.ClosureID}\tlhs={evidence.LhsRPN}\trhs={evidence.RhsRPN}\tpath_claim={evidence.ObligationPredictionID.Value}\tpath_lhs={evidence.LhsRPN}\tpath_rhs={evidence.RhsRPN}\tpath_guard={evidence.GuardPackageDigest}\tpath_canonical={evidence.AdmissionPathCanonical}\tpath_fingerprint={evidence.AdmissionPathFingerprint}\tmain={evidence.Evaluator.Start}..{evidence.Evaluator.End}\tnumeric={evidence.ComparatorEvaluation.Start}..{evidence.ComparatorEvaluation.End}\tworld=0\ttarget_supports={supports}\ttarget_support_digest={supportDigest}\tlaw_admissions={admissions}\tcandidate={evidence.CandidateDigest}";
        ObservationEnvelope observation = BeginObservation(source, Provenances.Reflected, TapeEventRoles.AuditOnly);
        byte[] encoded = Encoding.ASCII.GetBytes(packet);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.Mint(step, eventID, source, encoded);
        return eventID;
    }

    internal readonly record struct EmlRung0ClosurePacket(
        string Kind,
        string Species,
        string Status,
        EmlPredictionID SourcePredictionID,
        string ObligationID,
        EmlPredictionID ComposedPredictionID,
        string ProofID,
        string AuditID,
        string ProofSHA256,
        string AuditSHA256,
        string AdmissionID,
        string ClosureID,
        string LhsRPN,
        string RhsRPN,
        EmlRung0AdmissionPath AdmissionPath,
        EmlEvaluatorInterval MainEvaluation,
        EmlEvaluatorInterval ComparatorEvaluation,
        long WorldContacts,
        string CandidateDigest,
        EmlObligationTargetSpecies TargetSpecies,
        IReadOnlyList<TapeEventID> SupportEventIDs,
        string OccurrenceDigest,
        IReadOnlyList<string> LawAdmissionIDs);

    internal static bool TryReadEmlRung0Closure(ReadOnlySpan<byte> encoded, out EmlRung0ClosurePacket packet)
    {
        packet = default;
        string text;
        try { text = new UTF8Encoding(false, true).GetString(encoded); }
        catch (DecoderFallbackException) { return false; }
        string[] fields = text.Split('\t');
        if (fields.Length == 0 || fields[0] is not ("RUNG0-DERIVATION" or "RUNG0-DISPLACED")) return false;
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int i = 1; i < fields.Length; i++)
        {
            int equals = fields[i].IndexOf('=');
            if (equals <= 0) return false;
            if (!values.TryAdd(fields[i][..equals], fields[i][(equals + 1)..])) return false;
        }
        string[] required = ["species", "target_species", "status", "claim", "obligation", "derived", "proof", "audit", "proof_sha256", "audit_sha256", "admission", "closure", "lhs", "rhs", "path_claim", "path_lhs", "path_rhs", "path_guard", "path_canonical", "path_fingerprint", "main", "numeric", "world", "target_supports", "target_support_digest", "law_admissions", "candidate"];
        if (values.Keys.Any(key => !required.Contains(key, StringComparer.Ordinal) && key != "target_species")) return false;
        for (int i = 0; i < required.Length; i++)
            if (!values.TryGetValue(required[i], out string? value)
                || value.Length == 0 && required[i] is not ("target_supports" or "law_admissions")) return false;
        if (values["species"] != EmlRung0AdmissionPath.Species || values["status"] != "Accepted"
            || !Enum.TryParse(values["target_species"], out EmlObligationTargetSpecies parsedSpecies)
            || !Enum.IsDefined(parsedSpecies)) return false;
        if (!long.TryParse(values["claim"], NumberStyles.Integer, CultureInfo.InvariantCulture, out long sourcePrediction)
            || !long.TryParse(values["derived"], NumberStyles.Integer, CultureInfo.InvariantCulture, out long derivedPrediction)
            || sourcePrediction < 0 || derivedPrediction < 0
            || !long.TryParse(values["path_claim"], NumberStyles.Integer, CultureInfo.InvariantCulture, out long pathPrediction)
            || pathPrediction != sourcePrediction
            || !long.TryParse(values["world"], NumberStyles.Integer, CultureInfo.InvariantCulture, out long worldContacts)
            || worldContacts < 0
            || !TryReadInterval(values["main"], out EmlEvaluatorInterval main)
            || !TryReadInterval(values["numeric"], out EmlEvaluatorInterval numeric)) return false;
        if (!string.Equals(values["lhs"], values["path_lhs"], StringComparison.Ordinal)
            || !string.Equals(values["rhs"], values["path_rhs"], StringComparison.Ordinal)) return false;
        EmlRung0AdmissionPath path = new(
            new EmlPredictionID(checked((int)pathPrediction)), values["path_lhs"], values["path_rhs"], values["path_canonical"], values["path_guard"], values["path_fingerprint"]);
        if (!path.IsBound) return false;
        List<TapeEventID> supportEvents = [];
        if (values["target_supports"].Length > 0)
            foreach (string value in values["target_supports"].Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long eventID) || eventID < 0)
                    return false;
                else supportEvents.Add(new TapeEventID(eventID));
        string supportDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(values["target_supports"])));
        string canonicalSupports = string.Join(',', supportEvents.Select(static id => id.Value.ToString(CultureInfo.InvariantCulture)));
        if (!string.Equals(values["target_supports"], canonicalSupports, StringComparison.Ordinal)
            || !string.Equals(values["target_support_digest"], supportDigest, StringComparison.Ordinal)
            || parsedSpecies == EmlObligationTargetSpecies.ExactComposition && supportEvents.Count == 0) return false;
        string[] lawAdmissions = values["law_admissions"].Length == 0 ? [] : values["law_admissions"].Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (lawAdmissions.Length > 0
            && (lawAdmissions.Any(string.IsNullOrEmpty) || lawAdmissions.Distinct(StringComparer.Ordinal).Count() != lawAdmissions.Length
                || !string.Equals(string.Join(',', lawAdmissions), values["law_admissions"], StringComparison.Ordinal))) return false;
        packet = new(
            fields[0], values["species"], values["status"], new EmlPredictionID(checked((int)sourcePrediction)), values["obligation"], new EmlPredictionID(checked((int)derivedPrediction)),
            values["proof"], values["audit"], values["proof_sha256"], values["audit_sha256"], values["admission"], values["closure"],
            values["lhs"], values["rhs"], path, main, numeric, worldContacts, values["candidate"], parsedSpecies,
            supportEvents, values["target_support_digest"], lawAdmissions);
        return true;
    }

    private static bool TryReadInterval(string text, out EmlEvaluatorInterval interval)
    {
        interval = default;
        int separator = text.IndexOf("..", StringComparison.Ordinal);
        if (separator <= 0 || separator == text.Length - 2 || text.IndexOf("..", separator + 2, StringComparison.Ordinal) >= 0) return false;
        if (!long.TryParse(text[..separator], NumberStyles.Integer, CultureInfo.InvariantCulture, out long start)
            || !long.TryParse(text[(separator + 2)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out long end)
            || start < 0 || end < start) return false;
        interval = new EmlEvaluatorInterval(start, end);
        return true;
    }

    public static TapeEventID AppendEmlOrdinaryRunRung0Receipt(
        Tape tape,
        Journal journal,
        int step,
        in EmlOrdinaryRunRung0Receipt receipt)
    {
        if (!receipt.IsValid) throw new InvalidDataException("cannot append an invalid ordinary EML rung-0 receipt");
        StringBuilder packet = new();
        packet.Append("EML-RUNG0-RECEIPT")
            .Append("\trung0=").Append(receipt.Rung0)
            .Append("\tassay=").Append(receipt.Assay)
            .Append("\tpower=").Append(receipt.Power)
            .Append("\topportunities=").Append(receipt.Opportunities)
            .Append("\tcarrier_bound=").Append(receipt.CarrierBoundCandidates)
            .Append("\tguard_eligible=").Append(receipt.GuardEligibleCandidates)
            .Append("\tfunded_attempts=").Append(receipt.PaidAttempts)
            .Append("\tattempted_candidates=").Append(receipt.AttemptedCandidates)
            .Append("\tderivations=").Append(receipt.Compositions)
            .Append("\tzero_evaluator=").Append(receipt.ZeroEvaluatorCompositions)
            .Append("\taudits=").Append(receipt.Audits)
            .Append("\tagreed_audits=").Append(receipt.AgreedAudits)
            .Append("\tdisagreed_audits=").Append(receipt.DisagreedAudits)
            .Append("\tnot_selected_audits=").Append(receipt.NotSelectedAudits)
            .Append("\tschema=").Append(receipt.SchemaVersion)
            .Append("\tnull_executions=").Append(receipt.RelationNullExecutions)
            .Append("\tnull_divergences=").Append(receipt.RelationNullDivergences)
            .Append("\tnull_authority=").Append(receipt.RelationNullAuthorityPredictions)
            .Append("\tnull_pairs_considered=").Append(receipt.RelationNullPairsConsidered)
            .Append("\tnull_pairs_created=").Append(receipt.RelationNullPairsCreated)
            .Append("\tnull_reject_no_carrier=").Append(receipt.RelationNullRejectNoCarrier)
            .Append("\tnull_reject_shape=").Append(receipt.RelationNullRejectShape)
            .Append("\tnull_reject_grade=").Append(receipt.RelationNullRejectGrade)
            .Append("\tderivation=").Append(receipt.CompositionDigest)
            .Append("\tsource=").Append(receipt.SourceDigest)
            .Append("\tconfig=").Append(receipt.ConfigDigest)
            .Append("\tdigest=").Append(receipt.Digest);
        byte[] encoded = Encoding.ASCII.GetBytes(packet.ToString());
        ObservationEnvelope observation = BeginObservation("eml:rung0", Provenances.Reflected, TapeEventRoles.Measurement | TapeEventRoles.AuditOnly);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.RecordEmlOrdinaryRunRung0Receipt(step, eventID, "eml:rung0", in receipt, encoded);
        return eventID;
    }

    public static TapeEventID AppendEmlLawAdmission(Tape tape, Journal journal, int step,
        EmlVerifiedLaw law, bool representativeChanged)
    {
        const string Source = "eml:law";
        StringBuilder packet = new();
        packet.Append("LAW\t")
            .Append(representativeChanged ? "representative" : "class")
            .Append('\t').Append(law.Law.Template)
            .Append('\t').Append(law.Proof.OccurrenceCheckPrediction)
            .Append('\t').Append(law.Proof.OccurrenceDigest.ToString("X16"))
            .Append('\t').Append(EmlLawStore.CreateAdmissionID(law));
        ObservationEnvelope observation = BeginObservation(Source, Provenances.Reflected, TapeEventRoles.AuditOnly);
        byte[] encoded = Encoding.ASCII.GetBytes(packet.ToString());
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.Mint(step, eventID, Source, encoded);
        return eventID;
    }

    /// World support for a verified member is a separate event from class/representative
    /// admission.  SemanticCAS membership does not carry the world ancestry, so this
    /// packet is the durable post-hoc bridge used by the lineage turnstile.
    public static TapeEventID AppendEmlLawSupport(
        Tape tape,
        Journal journal,
        int step,
        EmlVerifiedLawSupportReceipt support)
    {
        support.Validate();
        StringBuilder packet = new();
        packet.Append("LAW-SUPPORT\tcandidate=").Append(support.CandidateAdmissionID)
            .Append("\tauthority=").Append(support.CanonicalAuthorityID)
            .Append("\tcertificate=").Append(support.Certificate)
            .Append("\tpackage=").Append(support.CandidatePackageDigest)
            .Append("\tclaims=").Append(string.Join(',', support.SourcePredictionIDs))
            .Append("\tclaim-digests=").Append(string.Join(',', support.SourcePredictionDigests))
            .Append("\tmint-line-digests=").Append(string.Join(',', support.SourcePredictionMintLineDigests))
            .Append("\tclaim-map=").Append(string.Join(';', support.SourcePredictionIDs.Select((id, index) =>
                id + ":" + support.SourcePredictionDigests[index] + ":" + string.Join(',', support.SourcePredictionOpportunityEvents[index].Select(static eventID => eventID.Value)))))
            .Append("\tadmissions=").Append(string.Join(',', support.SourcePredictionAdmissions.Select(static admission => admission is EmlSourcePredictionAdmission path
                ? ((byte)path.Species).ToString(CultureInfo.InvariantCulture) + ":" + path.EventID.Value.ToString(CultureInfo.InvariantCulture) : "-")))
            .Append("\tset=").Append(support.SupportSetDigest)
            .Append("\tworld=").Append(string.Join(',', support.WorldOpportunityEventIDs.Select(static id => id.Value)))
            .Append("\tstep=").Append(support.CaptureStep)
            .Append("\tindex=").Append(support.CaptureIndex)
            .Append("\tfirst=").Append(support.FirstCapture ? 1 : 0)
            .Append("\trepresentative=").Append(support.RepresentativeChanged ? 1 : 0)
            .Append("\tdigest=").Append(support.Digest);
        ObservationEnvelope observation = BeginObservation("eml:law-support", Provenances.Reflected, TapeEventRoles.AuditOnly);
        byte[] encoded = Encoding.ASCII.GetBytes(packet.ToString());
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.Mint(step, eventID, "eml:law-support", encoded);
        return eventID;
    }

    /// Append the one ordinary EML prediction admitted by pattern-to-grammar
    /// admission.  The learning bytes are deliberately just the canonical
    /// prediction line; receipt metadata remains in the law-store side journal.
    public static TapeEventID AppendEmlPatternGrammarAdmission(
        Tape tape,
        Journal journal,
        int step,
        EmlPatternGrammarAdmissionReceipt admission)
    {
        admission.GeneratedPrediction.Validate();
        byte[] encoded = admission.GeneratedPrediction.CreateLinePayload();
        // Frozen tape source token; identifier-side name is PatternGrammarAdmission.
        const string source = "eml:theory-grammar";
        ObservationEnvelope observation = BeginObservation(source, Provenances.Reflected, TapeEventRoles.GrammarInput);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.Mint(step, eventID, source, encoded);
        return eventID;
    }

    internal static TapeEventID AppendEmlPatternGrammarAdmissionEconomics(
        Tape tape,
        Journal journal,
        int step,
        EmlPatternGrammarAdmissionEconomicsReceipt receipt,
        out JournalRowBinding journalBinding)
    {
        receipt.Validate();
        byte[] encoded = receipt.Encode();
        // Frozen tape source token; identifier-side name is PatternGrammarAdmissionEconomics.
        const string source = "eml:theory-grammar-economics";
        ObservationEnvelope observation = BeginObservation(source, Provenances.Reflected, TapeEventRoles.Measurement | TapeEventRoles.AuditOnly);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journalBinding = journal.MintWithBinding(step, eventID, source, encoded);
        return eventID;
    }

    internal static bool TryDecodeEmlPatternGrammarAdmissionEconomics(
        ReadOnlySpan<byte> payload,
        out EmlPatternGrammarAdmissionEconomicsReceipt receipt)
    {
        try
        {
            receipt = EmlPatternGrammarAdmissionEconomicsReceipt.Decode(payload);
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            receipt = null!;
            return false;
        }
    }

    internal readonly record struct EmlLawSupportPacket(
        string CandidateAdmissionID,
        string CanonicalAuthorityID,
        string Certificate,
        string CandidatePackageDigest,
        IReadOnlyList<int> SourcePredictionIDs,
        IReadOnlyList<string> SourcePredictionDigests,
        IReadOnlyList<string> SourcePredictionMintLineDigests,
        IReadOnlyList<IReadOnlyList<TapeEventID>> SourcePredictionOpportunityEvents,
        IReadOnlyList<EmlSourcePredictionAdmission?> SourcePredictionAdmissions,
        IReadOnlyList<TapeEventID> WorldOpportunityEventIDs,
        string SupportSetDigest,
        int CaptureStep,
        int CaptureIndex,
        bool FirstCapture,
        bool RepresentativeChanged,
        string Digest)
    {
        internal IReadOnlyList<TapeEventID?> SourcePredictionMintEvents
            => SourcePredictionAdmissions.Select(static admission => admission?.EventID).ToArray();
    }

    internal static bool TryReadEmlLawSupport(ReadOnlySpan<byte> encoded, out EmlLawSupportPacket packet)
    {
        packet = default;
        string[] fields = Encoding.ASCII.GetString(encoded).Split('\t');
        if (fields.Length != 17 || fields[0] != "LAW-SUPPORT") return false;
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int i = 1; i < fields.Length; i++)
        {
            int separator = fields[i].IndexOf('=');
            if (separator <= 0 || !values.TryAdd(fields[i][..separator], fields[i][(separator + 1)..])) return false;
        }
        if (!values.TryGetValue("candidate", out string? candidate)
            || !values.TryGetValue("authority", out string? authority)
            || !values.TryGetValue("certificate", out string? certificate)
            || !values.TryGetValue("package", out string? candidatePackageDigest)
            || !values.TryGetValue("claims", out string? claims)
            || !values.TryGetValue("claim-digests", out string? claimDigests)
            || !values.TryGetValue("mint-line-digests", out string? mintLineDigests)
            || !values.TryGetValue("claim-map", out string? claimMap)
            || !values.TryGetValue("admissions", out string? admissions)
            || !values.TryGetValue("set", out string? supportSetDigest)
            || !values.TryGetValue("world", out string? world)
            || !values.TryGetValue("step", out string? step)
            || !values.TryGetValue("index", out string? index)
            || !values.TryGetValue("first", out string? first)
            || !values.TryGetValue("representative", out string? representative)
            || !values.TryGetValue("digest", out string? digest)
            || candidate.Length == 0 || authority.Length == 0 || certificate.Length == 0 || digest.Length != 64
            || !IsCanonicalHex(candidatePackageDigest)
            || !IsCanonicalHex(supportSetDigest)
            || !IsCanonicalHex(digest)
            || !int.TryParse(step, out int captureStep) || captureStep < 0
            || !int.TryParse(index, out int captureIndex) || captureIndex < 0
            || first is not ("0" or "1") || representative is not ("0" or "1")) return false;
        List<int> claimIDs = new();
        List<string> parsedPredictionDigests = new();
        List<IReadOnlyList<TapeEventID>> claimEvents = new();
        List<EmlSourcePredictionAdmission?> parsedAdmissions = new();
        string[] listedPredictions = claims.Length == 0 ? Array.Empty<string>() : claims.Split(',');
        string[] listedDigests = claimDigests.Length == 0 ? Array.Empty<string>() : claimDigests.Split(',');
        string[] listedMintLineDigests = mintLineDigests.Length == 0 ? Array.Empty<string>() : mintLineDigests.Split(',');
        if (listedPredictions.Length != listedDigests.Length || listedPredictions.Length != listedMintLineDigests.Length) return false;
        if (claimMap.Length > 0)
            foreach (string claim in claimMap.Split(';'))
            {
                string[] parts = claim.Split(':');
                if (parts.Length != 3 || !int.TryParse(parts[0], out int claimID) || claimID < 0
                    || parts[1].Length != 64 || !IsCanonicalHex(parts[1])) return false;
                List<TapeEventID> events = new();
                if (parts[2].Length > 0)
                    foreach (string value in parts[2].Split(','))
                        if (!long.TryParse(value, out long eventID) || eventID < 0
                            || (events.Count > 0 && eventID <= events[^1].Value)) return false;
                        else events.Add(new TapeEventID(eventID));
                if (claimIDs.Count > 0 && claimID <= claimIDs[^1]) return false;
                claimIDs.Add(claimID); parsedPredictionDigests.Add(parts[1]); claimEvents.Add(events);
            }
        if (claimIDs.Count != listedPredictions.Length) return false;
        string[] listedAdmissions = admissions.Length == 0 ? Array.Empty<string>() : admissions.Split(',');
        if (listedAdmissions.Length != claimIDs.Count) return false;
        for (int i = 0; i < listedAdmissions.Length; i++)
        {
            if (listedAdmissions[i] == "-") parsedAdmissions.Add(null);
            else
            {
                string[] parts = listedAdmissions[i].Split(':');
                if (parts.Length != 2 || !byte.TryParse(parts[0], out byte species)
                    || !Enum.IsDefined((EmlSourcePredictionAdmissionSpecies)species)
                    || !long.TryParse(parts[1], out long eventID) || eventID < 0) return false;
                parsedAdmissions.Add(new EmlSourcePredictionAdmission((EmlSourcePredictionAdmissionSpecies)species, new TapeEventID(eventID)));
            }
        }
        for (int i = 0; i < claimIDs.Count; i++)
            if (!int.TryParse(listedPredictions[i], out int listedID) || listedID != claimIDs[i]
                || !string.Equals(listedDigests[i], parsedPredictionDigests[i], StringComparison.Ordinal)
                || !IsCanonicalHex(listedDigests[i])
                || !IsCanonicalHex(listedMintLineDigests[i])
                || (i > 0 && listedID <= claimIDs[i - 1])) return false;
        List<TapeEventID> worldEvents = new();
        if (world.Length > 0)
            foreach (string value in world.Split(','))
                if (!long.TryParse(value, out long eventID) || eventID < 0) return false;
                else if (worldEvents.Count > 0 && eventID <= worldEvents[^1].Value) return false;
                else worldEvents.Add(new TapeEventID(eventID));
        if (worldEvents.Distinct().Count() != worldEvents.Count || claimEvents.SelectMany(static events => events).Distinct().OrderBy(static id => id.Value).SequenceEqual(worldEvents.OrderBy(static id => id.Value)) is false)
            return false;
        packet = new EmlLawSupportPacket(candidate, authority, certificate, candidatePackageDigest, claimIDs, parsedPredictionDigests,
            listedMintLineDigests, claimEvents, parsedAdmissions,
            worldEvents.OrderBy(static id => id.Value).ToArray(), supportSetDigest, captureStep, captureIndex,
            first == "1", representative == "1", digest);
        return true;
    }

    private static bool IsCanonicalHex(string value)
        => value.Length == 64 && value.All(Uri.IsHexDigit)
            && string.Equals(value, value.ToLowerInvariant(), StringComparison.Ordinal);

    internal static bool TryReadEmlLawSupportDigest(ReadOnlySpan<byte> encoded, out ulong supportDigest)
    {
        supportDigest = 0;
        string[] fields = Encoding.ASCII.GetString(encoded).Split('\t');
        return fields.Length is 5 or 6
            && fields[0] == "LAW"
            && fields[1] is "class" or "representative"
            && fields[2].Length > 0
            && fields[3].Length > 0
            && fields[4].Length == 16
            && ulong.TryParse(fields[4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out supportDigest)
            && supportDigest != 0;
    }

    internal static bool TryReadEmlLawAdmissionID(ReadOnlySpan<byte> encoded, out string admissionID)
    {
        admissionID = string.Empty;
        string[] fields = Encoding.ASCII.GetString(encoded).Split('\t');
        if (fields.Length is not (5 or 6) || fields[0] != "LAW" || fields[1] is not ("class" or "representative")
            || fields[2].Length == 0 || fields[3].Length == 0) return false;
        if (!TryReadEmlLawSupportDigest(encoded, out _)) return false;
        string canonical = fields[2] + "\u0001" + fields[4] + "\u0001" + fields[3];
        if (fields.Length == 6 && !string.Equals(fields[5], canonical, StringComparison.Ordinal)) return false;
        admissionID = canonical;
        return true;
    }

    internal readonly record struct EmlLawExecutionSupportPacket(
        IReadOnlyList<string> Digests,
        IReadOnlyList<string> Authorities,
        IReadOnlyList<(string Digest, int Start, int Count)> Ranges,
        IReadOnlyList<int> PredictionIDs,
        int Offers,
        int Mints);

    internal static bool TryReadEmlLawExecutionSupports(ReadOnlySpan<byte> encoded, out EmlLawExecutionSupportPacket packet)
    {
        packet = default;
        string[] fields = Encoding.ASCII.GetString(encoded).Split('\t');
        if (fields.Length == 0 || fields[0] != "LAW-EXECUTION") return false;
        string? supports = null;
        string? authorities = null;
        string? supportRanges = null;
        string? claims = null;
        int offers = -1, mints = -1;
        bool seenOffers = false, seenMints = false, seenSupports = false, seenAuthorities = false,
            seenPredictions = false, seenSupportRanges = false;
        for (int i = 1; i < fields.Length; i++)
        {
            int separator = fields[i].IndexOf('=');
            if (separator <= 0) return false;
            if (fields[i][..separator] == "supports")
            {
                if (seenSupports) return false;
                seenSupports = true;
                supports = fields[i][(separator + 1)..];
            }
            else if (fields[i][..separator] == "authorities")
            {
                if (seenAuthorities) return false;
                seenAuthorities = true;
                authorities = fields[i][(separator + 1)..];
            }
            else if (fields[i][..separator] == "support-ranges")
            {
                if (seenSupportRanges) return false;
                seenSupportRanges = true;
                supportRanges = fields[i][(separator + 1)..];
            }
            else if (fields[i][..separator] == "claims")
            {
                if (seenPredictions) return false;
                seenPredictions = true;
                claims = fields[i][(separator + 1)..];
            }
            else if (fields[i][..separator] == "offers")
            {
                if (seenOffers || !int.TryParse(fields[i][(separator + 1)..], out offers)) return false;
                seenOffers = true;
            }
            else if (fields[i][..separator] == "mints")
            {
                if (seenMints || !int.TryParse(fields[i][(separator + 1)..], out mints)) return false;
                seenMints = true;
            }
        }
        if (!seenOffers || !seenMints || !seenSupports || !seenAuthorities || !seenPredictions || !seenSupportRanges
            || offers <= 0 || mints <= 0 || supports is null || claims is null) return false;
        if (supports.Length == 0 || authorities is null || authorities.Length == 0) return false;
        string[] values = supports.Split(',');
        string[] authorityValues = authorities.Split(',');
        if (values.Length != authorityValues.Length
            || values.Any(static value => !IsCanonicalHex(value))
            || values.Distinct(StringComparer.Ordinal).Count() != values.Length
            || authorityValues.Any(static value => value.Length == 0))
            return false;
        List<(string Digest, int Start, int Count)> ranges = new();
        List<int> claimIDs = new();
        if (claims.Length > 0)
            foreach (string claim in claims.Split(','))
                if (!int.TryParse(claim, out int claimID) || claimID < 0 || (claimIDs.Count > 0 && claimID <= claimIDs[^1])) return false;
                else claimIDs.Add(claimID);
        if (supportRanges is not null)
        {
            string[] rangeValues = supportRanges.Split(',');
            if (rangeValues.Length < values.Length) return false;
            int previousStart = -1;
            HashSet<string> rangedSupports = new(StringComparer.Ordinal);
            foreach (string range in rangeValues)
            {
                string[] parts = range.Split(':');
                if (parts.Length != 3 || !IsCanonicalHex(parts[0])
                    || !int.TryParse(parts[1], out int start) || start < 0
                    || !int.TryParse(parts[2], out int count) || count <= 0
                    || start > int.MaxValue - count
                    || (ranges.Count > 0 && start <= previousStart)
                    || !values.Contains(parts[0], StringComparer.Ordinal)) return false;
                previousStart = start;
                rangedSupports.Add(parts[0]);
                ranges.Add((parts[0], start, count));
            }
            long rangedPredictionCount = 0;
            HashSet<int> rangedPredictionIDs = new();
            for (int rangeIndex = 0; rangeIndex < ranges.Count; rangeIndex++)
            {
                rangedPredictionCount += ranges[rangeIndex].Count;
                for (int claimOffset = 0; claimOffset < ranges[rangeIndex].Count; claimOffset++)
                    if (!claimIDs.Contains(ranges[rangeIndex].Start + claimOffset)
                        || !rangedPredictionIDs.Add(ranges[rangeIndex].Start + claimOffset)) return false;
            }
            if (rangedSupports.Count != values.Length || rangedPredictionCount != mints || rangedPredictionIDs.Count != claimIDs.Count
                || !rangedPredictionIDs.SetEquals(claimIDs)) return false;
        }
        else return false;
        packet = new EmlLawExecutionSupportPacket(values, authorityValues, ranges, claimIDs, offers, mints);
        return true;
    }

    public static TapeEventID AppendEmlLawExecution(Tape tape, Journal journal, int step,
        int offers, int mints, string firstPrediction, ulong firstProof, in EmlFormFarmResult formFarm)
        => AppendEmlLawExecution(tape, journal, step, offers, mints, firstPrediction, firstProof, in formFarm, Array.Empty<EmlPredictionID>());

    public static TapeEventID AppendEmlLawExecution(Tape tape, Journal journal, int step,
        int offers, int mints, string firstPrediction, ulong firstProof, in EmlFormFarmResult formFarm,
        IReadOnlyList<EmlPredictionID> claimIDs,
        IReadOnlyList<EmlVerifiedLawSupportReceipt>? supports = null,
        IReadOnlyList<(string Digest, int Start, int Count)>? supportRanges = null)
    {
        const string Source = "eml:law-execution";
        StringBuilder packet = new();
        packet.Append("LAW-EXECUTION\toffers=").Append(offers).Append("\tmints=").Append(mints)
            .Append("\tfarm_attempted=").Append(formFarm.Attempted)
            .Append("\tfarm_accepted=").Append(formFarm.Accepted)
            .Append("\tfarm_rejected=").Append(formFarm.Rejected)
            .Append("\tfarm_evaluator=").Append(formFarm.Evaluation.Start)
            .Append("..").Append(formFarm.Evaluation.End);
        if (firstPrediction.Length > 0)
            packet.Append("\tclaim=").Append(firstPrediction)
                .Append("\tproof=").Append(firstProof.ToString("X16"));
        if (claimIDs.Count > 0)
            packet.Append("\tclaims=").Append(string.Join(',', claimIDs.Select(static claim => claim.Value)));
        if (supports is { Count: > 0 })
        {
            packet.Append("\tsupports=").Append(string.Join(',', supports.Select(static support => support.Digest)));
            packet.Append("\tauthorities=").Append(string.Join(',', supports.Select(static support => support.CanonicalAuthorityID)));
        }
        if (supportRanges is { Count: > 0 })
            packet.Append("\tsupport-ranges=").Append(string.Join(',', supportRanges.Select(static range => range.Digest + ":" + range.Start + ":" + range.Count)));
        ObservationEnvelope observation = BeginObservation(Source, Provenances.Reflected, TapeEventRoles.AuditOnly);
        byte[] encoded = Encoding.ASCII.GetBytes(packet.ToString());
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.Mint(step, eventID, Source, encoded);
        return eventID;
    }

    public static TapeEventID AppendEmlProcessConstant(
        Tape tape,
        Journal journal,
        int step,
        in EmlProcessConstantCertificate certificate)
    {
        EmlProcessConstantCheck check = EmlProcessConstantChecker.Check(in certificate);
        if (!check.Accepted)
            throw new InvalidDataException($"cannot append rejected process-constant certificate: {check.Detail}");
        const string Source = "eml:process";
        StringBuilder packet = new();
        packet.Append("PROCESS\t")
            .Append(EmlProcessConstants.GetAlgorithmToken(certificate.Algorithm))
            .Append("\tversion=").Append(certificate.Version)
            .Append("\tterms=").Append(certificate.Terms)
            .Append("\tfuel=").Append(certificate.Fuel)
            .Append("\tlower=").Append(certificate.Bounds.Lower)
            .Append("\tupper=").Append(certificate.Bounds.Upper)
            .Append("\tremainder=").Append(certificate.RemainderCorroboration.Rule)
            .Append(':').Append(certificate.RemainderCorroboration.LowerOffset)
            .Append("..").Append(certificate.RemainderCorroboration.UpperOffset)
            .Append("\tdigest=").Append(certificate.StateDigest);
        ObservationEnvelope observation = BeginObservation(Source, Provenances.Reflected, TapeEventRoles.AuditOnly);
        byte[] encoded = Encoding.ASCII.GetBytes(packet.ToString());
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.Mint(step, eventID, Source, encoded);
        return eventID;
    }

    /// The pre-contact identity packet is built from the parsed action, not the
    /// generated raw line.  Length-prefixed tokens keep arguments containing tabs or
    /// newlines canonical without putting their bytes into the admission receipt.
    internal static byte[] EncodeActionRequest(CortexAction action, List<CortexActionArgument> arguments)
    {
        StringBuilder canonical = new();
        AppendCanonicalToken(canonical, action.Tool.Name);
        AppendCanonicalToken(canonical, arguments.Count.ToString(CultureInfo.InvariantCulture));
        for (int i = 0; i < arguments.Count; i++)
        {
            CortexActionArgument argument = arguments[i];
            AppendCanonicalToken(canonical, i.ToString(CultureInfo.InvariantCulture));
            AppendCanonicalToken(canonical, argument.Slot);
            AppendCanonicalToken(canonical, Blur.SourceToken(argument.Source));
            AppendCanonicalToken(canonical, argument.Value);
        }
        return Encoding.UTF8.GetBytes(canonical.ToString());
    }

    internal static byte[] EncodeActionExecution(Cortex cortex, CortexActionPolicy policy, CortexAction action,
        List<CortexActionArgument> arguments, List<CortexObservationField> fields)
    {
        StringBuilder packet = new();
        packet.Append("ACTION ").Append(action.Tool.Name);
        HashSet<string> routedSlots = new(StringComparer.Ordinal);
        bool wroteActionSlot = false;
        for (int i = 0; i < arguments.Count; i++)
        {
            CortexActionArgument argument = arguments[i];
            string slot = argument.Slot.Length == 0 ? "action" : argument.Slot;
            if (!policy.ShouldRouteActionArgument(cortex, action, argument) || !routedSlots.Add(slot)) continue;
            packet.Append(wroteActionSlot ? " ARGUMENT slot " : " slot ").Append(slot)
              .Append(' ').Append(Blur.SourceToken(argument.Source))
              .Append(" filler ").Append(policy.FormatTapeValue(cortex, argument.Value));
            wroteActionSlot = true;
        }
        foreach (CortexObservationField field in fields)
        {
            if (!policy.ShouldRouteObservationField(cortex, action, field)) continue;
            packet.Append(" OBSERVATION field slot ").Append(field.Slot)
              .Append(' ').Append(Blur.SourceToken(field.Source))
              .Append(" filler ").Append(policy.FormatTapeValue(cortex, field.Value));
        }
        return Encoding.UTF8.GetBytes(packet.ToString());
    }

    internal static string ComputeSHA256(ReadOnlySpan<byte> bytes)
        => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// Admission is audit-only/measurement only: it must survive checkpoints but never
    /// become grammar input or trigger reward observers while a request is denied.
    internal static TapeEventID AppendActionAdmission(
        Tape tape,
        Journal journal,
        int step,
        CortexActionAdmissionPhases phase,
        string tool,
        string source,
        string actionRequestSHA256,
        string executionSHA256,
        CortexActionAdmissionDecisionSpecies decision,
        string reason)
    {
        string canonical =
            $"ACTION-ADMISSION\tphase={phase}\ttool={tool}\tsource={source}\taction-request={actionRequestSHA256}\texecution={executionSHA256}\tdecision={decision}\treason={reason}";
        string receiptSHA256 = ComputeSHA256(Encoding.UTF8.GetBytes(canonical));
        CortexActionAdmissionReceipt receipt = new(
            step,
            phase,
            tool,
            source,
            actionRequestSHA256,
            executionSHA256,
            decision,
            reason,
            receiptSHA256);
        receipt.Validate();
        byte[] encoded = Encoding.UTF8.GetBytes($"{canonical}\treceipt-sha256={receiptSHA256}");
        ObservationEnvelope observation = BeginObservation("action:admission", Provenances.Execution,
            TapeEventRoles.Measurement | TapeEventRoles.AuditOnly);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.RecordActionAdmission(receipt.Step, eventID, receipt, encoded.Length);
        return eventID;
    }

    private static void AppendCanonicalToken(StringBuilder target, string value)
    {
        target.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
    }

    public static bool TryAppendActionExecution(Cortex cortex, CortexActionPolicy policy, CortexAction action,
        List<CortexActionArgument> arguments, List<CortexObservationField> fields,
        Engine.GrammarCover? affirmCover, double affirmCut, out byte[] executionBytes, out TapeEventID eventID)
    {
        executionBytes = EncodeActionExecution(cortex, policy, action, arguments, fields);
        return TryAppendActionExecution(cortex, policy, action, executionBytes, affirmCover, affirmCut, out eventID);
    }

    internal static bool TryAppendActionExecution(Cortex cortex, CortexActionPolicy policy, CortexAction action,
        byte[] executionBytes, Engine.GrammarCover? affirmCover, double affirmCut, out TapeEventID eventID)
    {

        // The journal already carries the exhaustive telemetry. The tape receives only the reusable routing shape,
        // and even that leaves the resident diet once the standing grammar generates it whole.
        CortexTapeAdmissionChoice admission = cortex.ChooseTapeAdmission(
            affirmCover, executionBytes, executionBytes.Length, Provenances.Execution, affirmCut);
        if (admission.Action == CortexTapeAdmissionActions.Reject)
        {
            cortex.CompleteTapeAdmission(in admission, appended: false);
            eventID = default;
            return false;
        }
        eventID = cortex.AppendExecution(executionBytes, policy.GetSource(cortex, action),
            policy.ActionExecutionRoles(cortex, action));
        cortex.CompleteTapeAdmission(in admission, appended: true);
        return true;
    }

    public static TapeEventID AppendWeftExecution(Tape tape, Journal journal, int step, string source, in WeftProgram program, in ExecResult result)
    {
        var observation = BeginObservation(source, Provenances.Replay, TapeEventRoles.GrammarInput);
        var sid = EndObservation(tape, in observation, result.Trace);
        journal.Weft(step, sid, source, program.Name, result.Trace, result);
        return sid;
    }

    public static TapeEventID AppendSelfSignal(Tape tape, Journal journal, int step, string channel, string token)
    {
        var source = "self:" + channel;
        var observation = BeginObservation(source, Provenances.Reflected, TapeEventRoles.Measurement | TapeEventRoles.AuditOnly);
        var encoded = Encoding.UTF8.GetBytes($"self\t{channel}\t{token}");
        var sid = EndObservation(tape, in observation, encoded);
        journal.Self(step, sid, source, channel, token, encoded);
        return sid;
    }

    /// Append one exact numeric observation frame. The caller supplies samples in ascending catalog order;
    /// packet ownership stays here, including canonical validation and the diagnostic journal mirror.
    public static TapeEventID AppendMetricFrame(
        Tape tape,
        Journal journal,
        int step,
        ReadOnlySpan<MetricSample> samples)
    {
        byte[] encoded = EncodeMetricFrame(samples);
        ObservationEnvelope observation = BeginObservation(MetricSource, Provenances.Execution, TapeEventRoles.Measurement | TapeEventRoles.AuditOnly);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.RecordMetricFrame(step, eventID, MetricSource, samples.Length, encoded);
        return eventID;
    }

    public static TapeEventID AppendPolicyDecision(
        Tape tape,
        Journal journal,
        int step,
        in CortexPolicyDecision decision,
        ReadOnlySpan<MetricSample> features,
        int actionCount,
        out byte[] encoded)
    {
        if ((uint)decision.Action >= (uint)actionCount)
            throw new ArgumentOutOfRangeException(nameof(decision), "policy action must be inside the action count");
        decision.Readout.Validate(actionCount);
        encoded = EncodePolicyDecision(in decision, features, actionCount);
        string source = "policy:" + decision.Policy.Value;
        ObservationEnvelope observation = BeginObservation(source, Provenances.Execution, TapeEventRoles.Measurement | TapeEventRoles.AuditOnly);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.RecordPolicyDecision(step, eventID, source, in decision, actionCount, features.Length, encoded);
        return eventID;
    }

    internal static TapeEventID AppendOrganicComparison(
        Tape tape,
        Journal journal,
        int step,
        in OrganicComparisonReceipt receipt)
    {
        receipt.Validate();
        byte[] encoded = EncodeOrganicComparison(in receipt);
        LoopClosurePolicyBinding policy = new(receipt.Policy, "policy:" + receipt.Policy.Value);
        string source = policy.OrganicComparisonPacketSource;
        ObservationEnvelope observation = BeginObservation(source, Provenances.Execution,
            TapeEventRoles.Measurement | TapeEventRoles.AuditOnly);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.RecordOrganicComparison(step, eventID, source, in receipt, encoded);
        return eventID;
    }

    internal static byte[] EncodeOrganicComparison(in OrganicComparisonReceipt receipt)
    {
        receipt.Validate();
        string fundingID = receipt.QuotaDecisionID?.Value.ToString("X16", CultureInfo.InvariantCulture) ?? "0000000000000000";
        static string SignedHex(int value) => unchecked((ulong)(long)value).ToString("X16", CultureInfo.InvariantCulture);
        return Encoding.ASCII.GetBytes(string.Join('\t',
            Encoding.ASCII.GetString(OrganicComparisonPrefix),
            $"step={receipt.Step.ToString(CultureInfo.InvariantCulture)}",
            $"policy={receipt.Policy.Value}",
            $"decision=u:{receipt.DecisionID.Value:X16}",
            $"source-event=i:{receipt.SourceDecisionEventID.Value:X16}",
            $"source-payload-sha256={receipt.SourceDecisionPayloadSHA256}",
            $"source-journal-sha256={receipt.SourceDecisionJournalSHA256}",
            $"readout-revision=u:{receipt.ReadoutRevision.Value:X16}",
            $"readout-fingerprint=u:{receipt.ReadoutFingerprint:X16}",
            $"candidate-fingerprint=u:{receipt.CandidateFingerprint:X16}",
            $"candidate-support=u:{receipt.CandidateOccurrenceDigest:X16}",
            $"launchpad=i:{SignedHex(receipt.LaunchpadAction)}",
            $"raw=i:{SignedHex(receipt.RawCandidateAction)}",
            $"selected=i:{SignedHex(receipt.SelectedCandidateAction)}",
            $"outcome={receipt.Outcome}",
            $"funding-id=u:{fundingID}",
            $"funding-decision={(receipt.FundingDecision?.ToString() ?? "")}",
            $"funding-journal-sha256={receipt.FundingJournalRowSHA256}",
            $"settlement-journal-sha256={receipt.SettlementJournalRowSHA256}",
            $"canonical-receipt-sha256={receipt.CanonicalReceiptSHA256}"));
    }

    internal static OrganicComparisonReceipt DecodeOrganicComparison(ReadOnlySpan<byte> encoded)
    {
        string[] fields = Encoding.ASCII.GetString(encoded).Split('\t');
        if (fields.Length != 20 || fields[0] != "ORGANIC-COMPARISON")
            throw new InvalidDataException("organic comparison packet prefix or field count is invalid");
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        foreach (string field in fields[1..])
        {
            int equals = field.IndexOf('=');
            if (equals <= 0 || !values.TryAdd(field[..equals], field[(equals + 1)..]))
                throw new InvalidDataException("organic comparison packet repeats or lacks a field");
        }
        string[] expected = ["step", "policy", "decision", "source-event", "source-payload-sha256", "source-journal-sha256", "readout-revision", "readout-fingerprint", "candidate-fingerprint", "candidate-support", "launchpad", "raw", "selected", "outcome", "funding-id", "funding-decision", "funding-journal-sha256", "settlement-journal-sha256", "canonical-receipt-sha256"];
        if (values.Count != expected.Length || expected.Any(key => !values.ContainsKey(key)))
            throw new InvalidDataException("organic comparison packet fields are incomplete");
        static long Signed(Dictionary<string, string> map, string key)
        {
            string token = map[key];
            if (token.Length != 18 || token[0] != 'i' || token[1] != ':') throw new InvalidDataException($"organic comparison field '{key}' is not signed");
            return unchecked((long)ulong.Parse(token.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }
        static ulong Unsigned(Dictionary<string, string> map, string key)
        {
            string token = map[key];
            if (token.Length != 18 || token[0] != 'u' || token[1] != ':') throw new InvalidDataException($"organic comparison field '{key}' is not unsigned");
            return ulong.Parse(token.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        ulong funding = Unsigned(values, "funding-id");
        OrganicComparisonReceipt receipt = new(
            checked((int)long.Parse(values["step"], CultureInfo.InvariantCulture)), new CortexPolicyID(values["policy"]),
            new CortexPolicyDecisionID(Unsigned(values, "decision")), new TapeEventID(Signed(values, "source-event")),
            values["source-payload-sha256"], values["source-journal-sha256"], new GrammarRevisionID(Unsigned(values, "readout-revision")),
            Unsigned(values, "readout-fingerprint"), Unsigned(values, "candidate-fingerprint"), Unsigned(values, "candidate-support"),
            checked((int)Signed(values, "launchpad")), checked((int)Signed(values, "raw")), checked((int)Signed(values, "selected")),
            Enum.Parse<OrganicComparisonOutcomeKinds>(values["outcome"], ignoreCase: false), funding == 0 ? null : new CortexPolicyQuotaDecisionID(funding),
            string.IsNullOrEmpty(values["funding-decision"]) ? null : Enum.Parse<CortexPolicyQuotaDecisions>(values["funding-decision"], ignoreCase: false), values["funding-journal-sha256"],
            values["settlement-journal-sha256"], values["canonical-receipt-sha256"]);
        receipt.Validate();
        return receipt;
    }

    internal static bool TryDecodeOrganicComparison(ReadOnlySpan<byte> encoded, out OrganicComparisonReceipt receipt)
    {
        try { receipt = DecodeOrganicComparison(encoded); return true; }
        catch (Exception error) when (error is InvalidDataException or FormatException or OverflowException or ArgumentException)
        { receipt = default; return false; }
    }

    /// Emit the organism's authenticated boundary source.  This is deliberately a
    /// separate packet from POLICY-DECISION: launchpad decisions retain their
    /// candidate-free 0/0 contract while the active readout tuple is corroborated here.
    internal static TapeEventID AppendPolicyBoundarySourceCorroboration(
        Tape tape,
        Journal journal,
        int step,
        in CortexPolicyBoundarySourceCorroboration corroboration)
    {
        corroboration.Validate();
        byte[] encoded = EncodePolicyBoundarySourceCorroboration(in corroboration);
        const string source = "policy-boundary:source";
        ObservationEnvelope observation = BeginObservation(source, Provenances.Execution, TapeEventRoles.AuditOnly);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.RecordPolicyBoundarySourceCorroboration(step, eventID, source, in corroboration, encoded);
        return eventID;
    }

    internal static byte[] EncodePolicyBoundarySourceCorroboration(in CortexPolicyBoundarySourceCorroboration corroboration)
    {
        corroboration.Validate();
        return Encoding.ASCII.GetBytes(string.Join('\t',
            PolicyBoundarySourceName,
            $"policy={corroboration.Policy.Value}",
            $"decision=u:{corroboration.SourceDecisionID.Value:X16}",
            $"event=i:{unchecked((ulong)corroboration.SourceDecisionEventID.Value):X16}",
            $"authority=u:{(byte)corroboration.SourceAuthority:X16}",
            $"cause=u:{(byte)corroboration.SourceSelectionCause:X16}",
            $"revision=u:{corroboration.ReadoutRevision.Value:X16}",
            $"readout=u:{corroboration.ReadoutFingerprint:X16}",
            $"candidate=u:{corroboration.CandidateFingerprint:X16}",
            $"support=u:{corroboration.OccurrenceDigest:X16}",
            $"cached=u:{corroboration.CachedContexts:X16}",
            $"comparisons=u:{corroboration.Comparisons:X16}",
            $"agreements=u:{corroboration.Agreements:X16}",
            $"misses=u:{corroboration.Misses:X16}",
            $"state={corroboration.CanonicalState.Policy.Value}:{(byte)corroboration.CanonicalState.Kind}:{corroboration.CanonicalState.Version}:{corroboration.CanonicalState.Value:X16}",
            $"digest={corroboration.CorroborationDigest}"));
    }

    internal static bool TryReadPolicyBoundarySourceCorroboration(
        ReadOnlySpan<byte> encoded,
        out CortexPolicyBoundarySourceCorroboration corroboration)
    {
        corroboration = default;
        try
        {
            string[] fields = Encoding.ASCII.GetString(encoded).Split('\t');
            if (fields.Length is not (12 or 16) || fields[0] != PolicyBoundarySourceName) return false;
            Dictionary<string, string> values = new(StringComparer.Ordinal);
            for (int index = 1; index < fields.Length; index++)
            {
                int equals = fields[index].IndexOf('=');
                if (equals <= 0 || !values.TryAdd(fields[index][..equals], fields[index][(equals + 1)..])) return false;
            }
            if (values.Keys.Any(static key => key is not ("policy" or "decision" or "event" or "authority" or "cause" or "revision" or "readout" or "candidate" or "support" or "cached" or "comparisons" or "agreements" or "misses" or "state" or "digest"))
                || values.Count is not (11 or 15)) return false;
            string policy = values["policy"];
            if (policy.Length == 0 || values["digest"].Length != 64) return false;
            string[] stateParts = values["state"].Split(':');
            if (stateParts.Length != 4 || !ushort.TryParse(stateParts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort stateVersion)
                || !ulong.TryParse(stateParts[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong stateValue)) return false;
            CortexPolicyID statePolicy = new(stateParts[0]);
            PolicyCanonicalStateID canonicalState = new(statePolicy, (PolicyCanonicalStateKinds)byte.Parse(stateParts[1], CultureInfo.InvariantCulture), stateVersion, stateValue);
            int cachedContexts = values.ContainsKey("cached") ? checked((int)ParsePacketU64(values, "cached")) : 0;
            int comparisons = values.ContainsKey("comparisons") ? checked((int)ParsePacketU64(values, "comparisons")) : 0;
            int agreements = values.ContainsKey("agreements") ? checked((int)ParsePacketU64(values, "agreements")) : 0;
            int misses = values.ContainsKey("misses") ? checked((int)ParsePacketU64(values, "misses")) : 0;
            corroboration = new CortexPolicyBoundarySourceCorroboration(
                new CortexPolicyID(policy),
                new CortexPolicyDecisionID(ParsePacketU64(values, "decision")),
                new TapeEventID(ParsePacketI64(values, "event")),
                (CortexPolicyAuthorities)ParsePacketU64(values, "authority"),
                (CortexPolicySelectionCauses)ParsePacketU64(values, "cause"),
                new GrammarRevisionID(ParsePacketU64(values, "revision")),
                ParsePacketU64(values, "readout"), ParsePacketU64(values, "candidate"),
                ParsePacketU64(values, "support"), values["digest"], cachedContexts, comparisons, agreements, misses)
            { CanonicalState = canonicalState };
            corroboration.Validate();
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or FormatException or OverflowException or ArgumentException)
        {
            corroboration = default;
            return false;
        }
    }

    internal static TapeEventID AppendLoopClosureOrganicOpportunityCensus(
        Tape tape,
        Journal journal,
        int step,
        CortexPolicyID policy,
        int opportunities)
    {
        if (opportunities < 0) throw new ArgumentOutOfRangeException(nameof(opportunities));
        byte[] encoded = Encoding.ASCII.GetBytes(
            $"LOOP-CLOSURE-ORGANIC-OPPORTUNITY\tpolicy={policy.Value}\topportunities={opportunities}");
        const string source = "loop-closure:organic-opportunity";
        ObservationEnvelope observation = BeginObservation(source, Provenances.Execution, TapeEventRoles.Measurement | TapeEventRoles.AuditOnly);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.RecordLoopClosureOrganicOpportunity(step, eventID, source, policy, opportunities, encoded);
        return eventID;
    }

    public static TapeEventID AppendPolicyOutcome(
        Tape tape,
        Journal journal,
        int step,
        in CortexPolicyDecision decision,
        ReadOnlySpan<MetricSample> outcomes,
        bool invariantClean,
        long conservedCost)
    {
        if (conservedCost < 0) throw new ArgumentOutOfRangeException(nameof(conservedCost));
        byte[] encoded = EncodePolicyOutcome(decision.DecisionID, outcomes, invariantClean, conservedCost);
        string source = "policy:" + decision.Policy.Value;
        ObservationEnvelope observation = BeginObservation(source, Provenances.Execution, TapeEventRoles.AuditOnly);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.RecordPolicyOutcome(step, eventID, source, decision.DecisionID, outcomes.Length,
            invariantClean, conservedCost, encoded);
        return eventID;
    }

    internal static CortexPolicyOutcomePacket DecodePolicyOutcome(ReadOnlySpan<byte> encoded)
    {
        string[] fields = Encoding.ASCII.GetString(encoded).Split('\t');
        if (fields.Length < 6 || fields[0] != "POLICY-OUTCOME")
            throw new InvalidDataException("policy outcome packet prefix is invalid");
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        int outcomeIndex = -1;
        for (int index = 1; index < fields.Length; index++)
        {
            int equals = fields[index].IndexOf('=');
            if (equals <= 0)
            {
                if (outcomeIndex < 0) throw new InvalidDataException("policy outcome packet has a trailing token before outcomes");
                ValidatePacketSample(fields[index]);
                continue;
            }
            if (outcomeIndex >= 0) throw new InvalidDataException("policy outcome packet has an unknown field after outcomes");
            string field = fields[index][..equals];
            if (field == "outcomes") outcomeIndex = index;
            else if (field is not ("decision" or "invariant" or "conserved-cost"))
                throw new InvalidDataException($"policy outcome packet has unknown field '{field}'");
            if (!values.TryAdd(field, fields[index][(equals + 1)..]))
                throw new InvalidDataException($"policy outcome packet repeats field '{field}'");
        }
        if (outcomeIndex < 0) throw new InvalidDataException("policy outcome packet omits outcomes");
        ulong decisionID = ParsePacketU64(values, "decision");
        ulong invariant = ParsePacketU64(values, "invariant");
        if (invariant > 1) throw new InvalidDataException("policy outcome invariant marker is not boolean");
        long conservedCost = ParsePacketI64(values, "conserved-cost");
        if (conservedCost < 0) throw new InvalidDataException("policy outcome conserved cost is negative");
        ulong outcomeCount = ParsePacketU64(values, "outcomes");
        if (outcomeCount == 0 || outcomeCount > int.MaxValue
            || outcomeCount != (ulong)(fields.Length - outcomeIndex - 1))
            throw new InvalidDataException("policy outcome packet sample count does not match its payload");
        MetricSample[] outcomes = new MetricSample[checked((int)outcomeCount)];
        int previousMetric = -1;
        for (int index = 0; index < outcomes.Length; index++)
        {
            string[] parts = fields[outcomeIndex + index + 1].Split(':');
            if (parts.Length != 3 || parts[0].Length != 4 || parts[2].Length != 16
                || parts[1] is not ("i" or "u" or "f")
                || !ushort.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort metricID)
                || metricID <= previousMetric
                || !ulong.TryParse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong bits))
                throw new InvalidDataException("policy outcome packet sample payload is malformed");
            previousMetric = metricID;
            NumericValue value = parts[1][0] switch
            {
                'i' => NumericValue.FromI64(unchecked((long)bits)),
                'u' => NumericValue.FromU64(bits),
                'f' => NumericValue.FromF64(BitConverter.Int64BitsToDouble(unchecked((long)bits))),
                _ => throw new InvalidDataException("policy outcome packet sample kind is unsupported"),
            };
            outcomes[index] = new MetricSample(new MetricID(metricID), value);
        }
        return new CortexPolicyOutcomePacket(new CortexPolicyDecisionID(decisionID), outcomes, invariant != 0, conservedCost);
    }

    internal static bool TryDecodePolicyOutcome(ReadOnlySpan<byte> encoded, out CortexPolicyOutcomePacket packet)
    {
        try
        {
            packet = DecodePolicyOutcome(encoded);
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or FormatException or OverflowException or ArgumentException)
        {
            packet = default;
            return false;
        }
    }

    internal static bool VerifyPolicyOutcomeCodecFixture()
    {
        MetricSample[] outcomes =
        [
            new(new MetricID(500), NumericValue.FromI64(1)),
            new(new MetricID(501), NumericValue.FromI64(2)),
        ];
        byte[] encoded = EncodePolicyOutcome(new CortexPolicyDecisionID(89), outcomes, true, 3);
        if (!TryDecodePolicyOutcome(encoded, out CortexPolicyOutcomePacket decoded)
            || decoded.DecisionID.Value != 89
            || decoded.Outcomes.Length != 2
            || decoded.ConservedCost != 3)
            return false;
        string malformedText = Encoding.ASCII.GetString(encoded).Replace(
            "decision=u:0000000000000059", "decision=u:GGGGGGGGGGGGGGGG", StringComparison.Ordinal);
        if (TryDecodePolicyOutcome(Encoding.ASCII.GetBytes(malformedText), out _))
            return false;
        byte[] tampered = (byte[])encoded.Clone();
        tampered[^1] = tampered[^1] == (byte)'2' ? (byte)'3' : (byte)'2';
        return TryDecodePolicyOutcome(tampered, out _)
            && !string.Equals(DigestPolicyOutcomePayload(encoded), DigestPolicyOutcomePayload(tampered), StringComparison.Ordinal);
    }

    internal static bool TryReadPolicyOutcomeJournalRow(
        string line,
        TapeEventID expectedEventID,
        string expectedSource,
        in CortexPolicyOutcomePacket packet,
        string expectedPayloadSHA256,
        int expectedPayloadLength,
        out int step)
    {
        step = -1;
        string[] columns = line.Split('\t');
        if (columns.Length != 10 || columns[1] != "policy-outcome"
            || columns[2] != expectedEventID.ToString() || columns[3] != expectedSource
            || !int.TryParse(columns[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out step)
            || step < 0
            || columns[4] != "decision=" + packet.DecisionID.Value.ToString(CultureInfo.InvariantCulture)
            || columns[5] != "outcomes=" + packet.Outcomes.Length.ToString(CultureInfo.InvariantCulture)
            || columns[6] != "invariant=" + (packet.InvariantClean ? "1" : "0")
            || columns[7] != "conserved-cost=" + packet.ConservedCost.ToString(CultureInfo.InvariantCulture)
            || columns[8] != "payload-sha256=" + expectedPayloadSHA256
            || columns[9] != expectedPayloadLength.ToString(CultureInfo.InvariantCulture) + "B")
        {
            step = -1;
            return false;
        }
        return true;
    }

    internal static string DigestPolicyOutcomePayload(ReadOnlySpan<byte> payload)
        => Convert.ToHexStringLower(SHA256.HashData(payload));

    private static PolicyTeacherPacketIDs AppendSplitPolicyTeacher(
        Tape tape,
        string policy,
        byte[] grammarBytes,
        byte[] evidenceBytes,
        string auditOnlyFields)
    {
        const string grammarSourcePrefix = "policy:";
        string grammarSource = grammarSourcePrefix + policy;
        ObservationEnvelope grammarObservation = BeginObservation(grammarSource, Provenances.Execution, TapeEventRoles.GrammarInput);
        TapeEventID grammarEventID = EndObservation(tape, in grammarObservation, grammarBytes);

        string auditOnlySource = "policy-teacher:" + policy;
        string auditOnlyText = string.Concat(
            Encoding.ASCII.GetString(PolicyTeacherAuditOnlyPrefix),
            "\tgrammar-event=i:", grammarEventID.Value.ToString("X16", CultureInfo.InvariantCulture),
            "\t", auditOnlyFields,
            "\tRAW-EVIDENCE=", Convert.ToHexString(evidenceBytes));
        ObservationEnvelope auditOnlyObservation = BeginObservation(auditOnlySource, Provenances.Execution,
            TapeEventRoles.Measurement | TapeEventRoles.AuditOnly);
        TapeEventID auditOnlyEventID = EndObservation(tape, in auditOnlyObservation, Encoding.ASCII.GetBytes(auditOnlyText));
        PolicyTeacherPacketIDs result = new(grammarEventID, auditOnlyEventID);
        result.Validate();
        return result;
    }

    /// Post-funded terminal outcome. This is deliberately separate from the ordinary
    /// POLICY-OUTCOME packet: the latter records the source decision before a boundary
    /// lease exists, while this packet records the candidate rail actually adjudicated.
    internal static TapeEventID AppendPolicyBoundaryAdjudicatedOutcome(
        Tape tape,
        Journal journal,
        int step,
        in PolicyBoundaryDivergenceAdjudication adjudication,
        IPolicyBoundaryDomain domain)
    {
        adjudication.Validate(domain);
        PolicyBoundaryDivergenceProof proof = adjudication.Proof;
        string encodedText = string.Join('\t',
            "POLICY-BOUNDARY-OUTCOME",
            $"decision=u:{proof.DecisionID.Value:X16}",
            $"funding={proof.Funding.QuotaDecisionID}",
            $"policy={proof.Policy.Value}",
            $"candidate={proof.Candidate.ExecutedOutcome?.OutcomeID.Value ?? "none"}",
            $"candidate-action={proof.Candidate.ExecutedOutcome?.Action ?? -1}",
            $"candidate-launchpad={proof.Candidate.ExecutedOutcome?.LaunchpadAction ?? -1}",
            $"candidate-raw={proof.Candidate.ExecutedOutcome?.RawCandidateAction ?? -1}",
            $"candidate-executed={(proof.Candidate.ExecutedOutcome?.BehaviorallyExecuted == true ? 1 : 0)}",
            $"candidate-outcome={proof.Candidate.Outcome}",
            $"candidate-requested={proof.Candidate.RequestCount}",
            $"candidate-admitted={proof.Candidate.GuardAdmittedCount}",
            $"forced-null={proof.ForcedNull.OutcomeID.Value}",
            $"forced-decision=u:{proof.ForcedNull.DecisionID.Value:X16}",
            $"forced-action={proof.ForcedNull.Action}",
            $"forced-launchpad={proof.ForcedNull.LaunchpadAction}",
            $"forced-raw={proof.ForcedNull.RawCandidateAction}",
            $"forced-cause={proof.ForcedNull.SelectionCause}",
            $"forced-outcome-event={proof.ForcedNull.ExecutedOutcomeEventID.Value}",
            $"forced-outcome-payload={proof.ForcedNull.ExecutedOutcomePayloadSHA256}",
            $"horizon={proof.Candidate.Horizon}",
            $"spend={proof.Candidate.MatchedSpend}",
            $"settlement={proof.Completion.ActualExecutedArmSteps}",
            $"adjudication={adjudication.EvidenceSHA256.Value}");
        byte[] encoded = Encoding.ASCII.GetBytes(encodedText);
        ObservationEnvelope observation = BeginObservation("policy-boundary:outcome", Provenances.Execution, TapeEventRoles.AuditOnly);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.Mint(step, eventID, "policy-boundary:outcome", encoded);
        return eventID;
    }

    internal static TapeEventID AppendPolicyFundedDissent(
        Tape tape,
        Journal journal,
        int step,
        in PolicyBoundaryDivergenceAdjudication adjudication,
        IPolicyBoundaryDomain domain)
    {
        adjudication.Validate(domain);
        PolicyBoundaryDivergenceProof proof = adjudication.Proof;
        byte[] encoded = Encoding.ASCII.GetBytes(string.Join('\t',
            "POLICY-FUNDED-DISSENT",
            $"decision=u:{proof.DecisionID.Value:X16}",
            $"funding={proof.Funding.QuotaDecisionID}",
            $"policy={proof.Policy.Value}",
            $"readout=u:{proof.ReadoutFingerprint:X16}",
            $"support=u:{proof.ReadoutOccurrenceDigest:X16}",
            $"revision={proof.ReadoutRevision.Value}",
            $"candidate={proof.Candidate.ExecutedOutcome?.OutcomeID.Value ?? "none"}",
            $"forced-null={proof.ForcedNull.OutcomeID.Value}",
            $"execution={proof.ForkReceipt.ExecutionCorroboration?.PaidDivergenceExecutionCorroborationSHA256.Value ?? "none"}"));
        ObservationEnvelope observation = BeginObservation("policy-boundary:dissent", Provenances.Execution, TapeEventRoles.AuditOnly);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.Mint(step, eventID, "policy-boundary:dissent", encoded);
        return eventID;
    }

    internal static TapeEventID AppendPolicyBoundaryTrainingMount(
        Tape tape, Journal journal, int step, in PolicyBoundaryMountReceipt receipt)
    {
        byte[] encoded = EncodePolicyBoundaryTrainingMount(in receipt);
        if (!TryReadPolicyBoundaryTrainingMount(encoded, in receipt))
            throw new InvalidDataException("policy-boundary mount packet failed typed recovery");
        const string source = "policy-boundary:mount";
        ObservationEnvelope observation = BeginObservation(source, Provenances.Reflected, TapeEventRoles.AuditOnly);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.RecordPolicyBoundaryMount(step, eventID, source, in receipt, encoded);
        return eventID;
    }

    internal static byte[] EncodePolicyBoundaryTrainingMount(in PolicyBoundaryMountReceipt receipt)
    {
        receipt.ValidateForEmission();
        return Encoding.ASCII.GetBytes(
            $"POLICY-BOUNDARY-MOUNT\tparent={receipt.ParentRunID}\tsource={receipt.SourceChildID}\tdestination={receipt.DestinationChildID}\tcold={receipt.ColdSeedDigest}\ttraining={receipt.TrainingReceiptDigest}\tcontent={receipt.SourceContentDigest}\trelation={receipt.Relation}\tevaluation={receipt.EvaluationStartStep}..{receipt.EvaluationEndStep}\tmount={receipt.MountStep}\tdestination-fingerprint={receipt.DestinationDecisionReadoutFingerprint:X16}\tdestination-revision={receipt.DestinationDecisionReadoutRevision}\tdestination-handshake-digest={receipt.DestinationHandshakeReceiptDigest}\tdestination-handshake-decision-id={receipt.DestinationHandshakeDecisionID}\tverified={(receipt.VerifiedReceipt && receipt.VerifiedContent ? 1 : 0)}\treceipt={receipt.ReceiptDigest}");
    }

    internal static bool TryReadPolicyBoundaryTrainingMount(ReadOnlySpan<byte> packet, in PolicyBoundaryMountReceipt expected)
    {
        string text = Encoding.UTF8.GetString(packet);
        string[] fields = text.Split('\t');
        if (fields.Length < 13 || fields[0] != "POLICY-BOUNDARY-MOUNT") return false;
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int i = 1; i < fields.Length; i++)
        {
            int equals = fields[i].IndexOf('=');
            if (equals <= 0) return false;
            values[fields[i][..equals]] = fields[i][(equals + 1)..];
        }
        string packetHandshakeDigest = values.GetValueOrDefault("destination-handshake-digest") ?? "";
        string packetHandshakeDecisionID = values.GetValueOrDefault("destination-handshake-decision-id") ?? "0";
        if (!ulong.TryParse(packetHandshakeDecisionID, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsedHandshakeDecisionID))
            return false;
        // Historical schema-v1 packets predate the owner fields and remain readable. Current
        // schema-v2 packets must carry both owner tokens; a missing field is not a valid mount.
        if (expected.SchemaVersion >= 2
            && (!values.ContainsKey("destination-handshake-digest")
                || !values.ContainsKey("destination-handshake-decision-id")))
            return false;
        return values.GetValueOrDefault("parent") == expected.ParentRunID
            && values.GetValueOrDefault("source") == expected.SourceChildID
            && values.GetValueOrDefault("destination") == expected.DestinationChildID
            && values.GetValueOrDefault("cold") == expected.ColdSeedDigest
            && values.GetValueOrDefault("training") == expected.TrainingReceiptDigest
            && values.GetValueOrDefault("content") == expected.SourceContentDigest
            && values.GetValueOrDefault("relation") == expected.Relation.ToString()
            && values.GetValueOrDefault("evaluation") == $"{expected.EvaluationStartStep}..{expected.EvaluationEndStep}"
            && values.GetValueOrDefault("mount") == expected.MountStep.ToString(CultureInfo.InvariantCulture)
            && values.GetValueOrDefault("destination-fingerprint") == expected.DestinationDecisionReadoutFingerprint.ToString("X16", CultureInfo.InvariantCulture)
            && values.GetValueOrDefault("destination-revision") == expected.DestinationDecisionReadoutRevision.ToString(CultureInfo.InvariantCulture)
            && packetHandshakeDigest == expected.DestinationHandshakeReceiptDigest
            && parsedHandshakeDecisionID == expected.DestinationHandshakeDecisionID
            && values.GetValueOrDefault("verified") == "1"
            && values.GetValueOrDefault("receipt") == expected.ReceiptDigest;
    }

    public static TapeEventID AppendPolicyOccurrenceCheck(
        Tape tape,
        Journal journal,
        int step,
        CortexPolicyID policy,
        ulong fingerprint,
        int comparisons,
        int agreements,
        int failures,
        bool passed)
    {
        if (comparisons < 0 || agreements < 0 || agreements > comparisons || failures < 0)
            throw new ArgumentOutOfRangeException(nameof(comparisons));
        int length = PolicyOccurrenceCheckPrefix.Length
            + FingerprintField.Length + TypedNumberLength
            + ComparisonsField.Length + TypedNumberLength
            + AgreementsField.Length + TypedNumberLength
            + FailuresField.Length + TypedNumberLength
            + PassedField.Length + TypedNumberLength;
        byte[] encoded = new byte[length];
        int offset = 0;
        WriteBytes(encoded, ref offset, PolicyOccurrenceCheckPrefix);
        WriteTypedU64(encoded, ref offset, FingerprintField, fingerprint);
        WriteTypedI64(encoded, ref offset, ComparisonsField, comparisons);
        WriteTypedI64(encoded, ref offset, AgreementsField, agreements);
        WriteTypedI64(encoded, ref offset, FailuresField, failures);
        WriteTypedU64(encoded, ref offset, PassedField, passed ? 1UL : 0UL);
        string source = "policy:" + policy.Value;
        ObservationEnvelope observation = BeginObservation(source, Provenances.Execution, TapeEventRoles.AuditOnly);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.RecordPolicyOccurrenceCheck(step, eventID, source, fingerprint, comparisons, agreements, failures, passed, encoded);
        return eventID;
    }

    public static TapeEventID AppendPolicyOccurrenceCheckScope(
        Tape tape,
        Journal journal,
        int step,
        CortexPolicyID policy,
        ulong readoutFingerprint,
        ulong candidateFingerprint,
        ulong supportDigest,
        GrammarRevisionID revision,
        in PolicyCanonicalStateID canonicalState)
    {
        if (readoutFingerprint == 0 || candidateFingerprint == 0 || supportDigest == 0
            || revision == GrammarRevisionID.Zero || !canonicalState.IsValidFor(policy))
            throw new ArgumentException("policy verification scope identity is incomplete");
        byte[] encoded = Encoding.ASCII.GetBytes(
            $"POLICY-VERIFICATION-SCOPE\tpolicy={policy.Value}\treadout={readoutFingerprint:X16}\tcandidate={candidateFingerprint:X16}\tsupport={supportDigest:X16}\trevision={revision.Value}\tstate_policy={canonicalState.Policy.Value}\tstate_kind={(byte)canonicalState.Kind}\tstate_version={canonicalState.Version}\tstate_value={canonicalState.Value:X16}");
        string source = "policy:" + policy.Value;
        ObservationEnvelope observation = BeginObservation(source, Provenances.Execution, TapeEventRoles.AuditOnly);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.RecordPolicyOccurrenceCheck(step, eventID, source, readoutFingerprint, 0, 0, 0, true, encoded);
        return eventID;
    }

    public static TapeEventID AppendPolicyTrialQuota(
        Tape tape,
        Journal journal,
        int step,
        in CortexPolicyTrialQuotaDecision decision)
    {
        string state = decision.CanonicalState.IsValidFor(decision.Policy)
            ? $"\tstate={decision.CanonicalState.Policy.Value}:{(byte)decision.CanonicalState.Kind}:{decision.CanonicalState.Version}:{decision.CanonicalState.Value:X16}"
            : "";
        byte[] encoded = Encoding.ASCII.GetBytes(
            $"POLICY-TRIAL-FUNDING\tid={decision.QuotaDecisionID}\tpolicy={decision.Policy.Value}\tfingerprint={decision.CandidateFingerprint:X16}\treadout_fingerprint={decision.ReadoutFingerprint:X16}\tstep={decision.QuotaStep}\thorizon={decision.RequestedHorizonSteps}\tarms={decision.ArmCount}\tplanned={decision.PlannedArmSteps}\treserved={decision.HeldArmSteps}\tdecision={decision.Decision}\tcharged={decision.UsedSteps}\tremaining={decision.RemainingQuota}\tcandidate={decision.CandidateState}\tdenial={decision.DenialReason}\torigin={decision.CandidateOriginStep}\tcurrent={decision.CandidateCurrentStep}\trequired={decision.CandidateRequiredStep}\trevision={decision.CandidateRevision.Value}\tallocation={decision.AllocationIdentity}\tallocation_digest={decision.AllocationDigest}\tallocation_arm_steps={decision.AllocationArmSteps}\tseed_custody_digest={decision.SeedAuditOnlyDigest}{state}");
        string source = "policy:" + decision.Policy.Value;
        ObservationEnvelope observation = BeginObservation(source, Provenances.Execution, TapeEventRoles.AuditOnly);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.RecordPolicyTrialQuota(step, eventID, source, in decision, encoded);
        return eventID;
    }

    internal static TapeEventID AppendPolicyPendingForcedTrialRearm(
        Tape tape,
        Journal journal,
        int step,
        CortexPolicyID policy,
        in CortexPolicyPendingForcedTrialRearmEvaluation evaluation)
    {
        bool stateOnly = !evaluation.IntentBound
            && evaluation.Outcome == CortexPolicyPendingForcedTrialRearmOutcomes.Denied
            && (evaluation.DenialSpecies is CortexPolicyPendingForcedTrialRearmDenialSpecies.AlreadyArmed
                or CortexPolicyPendingForcedTrialRearmDenialSpecies.IntentNotBound);
        if (!evaluation.IntentBound && !stateOnly)
            throw new InvalidDataException("unbound policy rearm packet has an authenticated denial species");
        if (!evaluation.Policy.Equals(policy) && evaluation.Policy.Value.Length != 0)
            throw new InvalidDataException("policy rearm packet policy does not match its typed evaluation");
        CortexPolicyPendingForcedTrialRearmEvaluation receipt = CanonicalizePolicyPendingForcedTrialRearm(
            policy, in evaluation);
        string prefix = stateOnly ? "POLICY-TRIAL-REARM-STATE" : "POLICY-TRIAL-REARM";
        byte[] encoded = Encoding.ASCII.GetBytes(
            $"{prefix}\tpolicy={policy.Value}\tfunding={receipt.QuotaID:X16}\toutcome={receipt.Outcome}\tspecies={receipt.DenialSpecies}\tsource_funding={receipt.SourceQuotaDecision}\tsource_decision={receipt.SourceDecisionID:X16}\tsource_event={receipt.SourceDecisionEventID}\tsource_witness={receipt.SourceCorroborationEventID}\tsource_support={receipt.SourceOccurrenceDigest:X16}\tsource_candidate={receipt.SourceCandidateFingerprint:X16}\tsource_funded_candidate={receipt.SourceQuotaCandidateFingerprint:X16}\tsource_readout={receipt.SourceReadoutFingerprint:X16}\tsource_revision={receipt.SourceCandidateRevision.Value}\tsource_state={EncodePolicyCanonicalState(receipt.SourceCanonicalState)}\treadout={receipt.ReadoutFingerprint:X16}\tcandidate={receipt.CandidateFingerprint:X16}\trevision={receipt.CandidateRevision.Value}\tsupport={receipt.OccurrenceDigest:X16}\tstate={EncodePolicyCanonicalState(receipt.CanonicalState)}\tarm={receipt.Arm}\tfeature={receipt.FeatureID}\tobligation={receipt.ObligationID}\tbound={(receipt.IntentBound ? 1 : 0)}\tsource_run={receipt.SourceRunID}\tcustody={receipt.AuditOnlyDigest}");
        string source = "policy-rearm:" + policy.Value;
        ObservationEnvelope observation = BeginObservation(source, Provenances.Execution, TapeEventRoles.AuditOnly);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.RecordPolicyPendingForcedTrialRearm(step, eventID, source, in receipt, encoded);
        return eventID;
    }

    internal static CortexPolicyPendingForcedTrialRearmEvaluation CanonicalizePolicyPendingForcedTrialRearm(
        CortexPolicyID policy,
        in CortexPolicyPendingForcedTrialRearmEvaluation evaluation)
    {
        bool stateOnly = !evaluation.IntentBound
            && evaluation.Outcome == CortexPolicyPendingForcedTrialRearmOutcomes.Denied
            && (evaluation.DenialSpecies is CortexPolicyPendingForcedTrialRearmDenialSpecies.AlreadyArmed
                or CortexPolicyPendingForcedTrialRearmDenialSpecies.IntentNotBound);
        if (!stateOnly) return evaluation;
        bool hasCandidate = evaluation.CandidateFingerprint != 0
            && evaluation.CandidateRevision != GrammarRevisionID.Zero;
        return new(CortexPolicyPendingForcedTrialRearmOutcomes.Denied, evaluation.DenialSpecies,
            policy, 0, CortexPolicyQuotaDecisions.Denied, 0, 0, 0, 0, 0, 0, 0,
            GrammarRevisionID.Zero, default, 0, hasCandidate ? evaluation.CandidateFingerprint : 0,
            hasCandidate ? evaluation.CandidateRevision : GrammarRevisionID.Zero,
            0, default, null!, 0, 0, null!, null!, false);
    }

    private static string EncodePolicyCanonicalState(in PolicyCanonicalStateID state)
        => state.Version == 0 ? "" : string.Join(':', state.Policy.Value, (byte)state.Kind,
            state.Version, state.Value.ToString("X16", CultureInfo.InvariantCulture));

    internal static bool TryDecodePolicyPendingForcedTrialRearm(
        ReadOnlySpan<byte> encoded,
        out CortexPolicyID policy,
        out CortexPolicyPendingForcedTrialRearmEvaluation evaluation)
    {
        policy = default;
        evaluation = default;
        string[] fields = Encoding.ASCII.GetString(encoded).Split('\t');
        bool stateOnly = fields.Length == 26 && fields[0] == "POLICY-TRIAL-REARM-STATE";
        if (fields.Length != 26 || (!stateOnly && fields[0] != "POLICY-TRIAL-REARM")) return false;
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int i = 1; i < fields.Length; i++)
        {
            int equals = fields[i].IndexOf('=');
            if (equals <= 0 || !values.TryAdd(fields[i][..equals], fields[i][(equals + 1)..])) return false;
        }
        string[] required = ["policy", "funding", "outcome", "species", "source_funding", "source_decision", "source_event", "source_witness", "source_support", "source_candidate", "source_funded_candidate", "source_readout", "source_revision", "source_state", "readout", "candidate", "revision", "support", "state", "arm", "feature", "obligation", "bound", "source_run", "custody"];
        if (required.Any(key => !values.ContainsKey(key))) return false;
        if (!TryParseHex(values["funding"], out ulong funding) || !TryParseHex(values["source_decision"], out ulong sourceDecision)
            || !TryParseHex(values["source_support"], out ulong sourceSupport) || !TryParseHex(values["source_candidate"], out ulong sourceCandidate)
            || !TryParseHex(values["source_funded_candidate"], out ulong sourcePaidCandidate) || !TryParseHex(values["source_readout"], out ulong sourceReadout)
            || !TryParseHex(values["readout"], out ulong readout) || !TryParseHex(values["candidate"], out ulong candidate)
            || !TryParseHex(values["support"], out ulong support) || !long.TryParse(values["source_event"], NumberStyles.Integer, CultureInfo.InvariantCulture, out long sourceEvent)
            || !long.TryParse(values["source_witness"], NumberStyles.Integer, CultureInfo.InvariantCulture, out long sourceCorroboration)
            || !ulong.TryParse(values["source_revision"], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong sourceRevision)
            || !ulong.TryParse(values["revision"], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong revision)
            || !byte.TryParse(values["arm"], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte arm)
            || !ushort.TryParse(values["feature"], NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort feature)
            || values["bound"] is not ("0" or "1")) return false;
        if (!Enum.TryParse(values["outcome"], out CortexPolicyPendingForcedTrialRearmOutcomes outcome)
            || !Enum.IsDefined(outcome) || !Enum.TryParse(values["species"], out CortexPolicyPendingForcedTrialRearmDenialSpecies species)
            || !Enum.IsDefined(species) || !Enum.TryParse(values["source_funding"], out CortexPolicyQuotaDecisions sourceFunding)
            || !Enum.IsDefined(sourceFunding)) return false;
        if (!TryDecodePolicyCanonicalState(values["source_state"], out PolicyCanonicalStateID sourceState)
            || !TryDecodePolicyCanonicalState(values["state"], out PolicyCanonicalStateID state)) return false;
        try { policy = new CortexPolicyID(values["policy"]); }
        catch (ArgumentException) { return false; }
        if (policy.Value.Length == 0) return false;
        if (stateOnly)
        {
            if (outcome != CortexPolicyPendingForcedTrialRearmOutcomes.Denied
                || species is not (CortexPolicyPendingForcedTrialRearmDenialSpecies.AlreadyArmed
                    or CortexPolicyPendingForcedTrialRearmDenialSpecies.IntentNotBound)
                || values["bound"] != "0"
                || sourceFunding != CortexPolicyQuotaDecisions.Denied
                || funding != 0 || sourceDecision != 0 || sourceEvent != 0 || sourceCorroboration != 0
                || sourceSupport != 0 || sourceCandidate != 0 || sourcePaidCandidate != 0 || sourceReadout != 0
                || sourceRevision != 0 || sourceState.Version != 0
                || readout != 0 || support != 0 || state.Version != 0
                || (candidate == 0) != (revision == 0) || arm != 0 || feature != 0
                || values["obligation"].Length != 0 || values["source_run"].Length != 0
                || values["custody"].Length != 0) return false;
        }
        else if (funding == 0 || sourceDecision == 0 || sourceEvent <= 0 || sourceCorroboration <= 0
                || sourceSupport == 0 || sourceCandidate == 0 || sourcePaidCandidate == 0 || sourceReadout == 0
                || readout == 0 || candidate == 0 || support == 0 || sourceRevision == 0 || revision == 0
                || !sourceState.IsValidFor(policy) || !state.IsValidFor(policy)
                || values["obligation"].Length == 0 || values["source_run"].Length == 0 || !IsCanonicalSHA256(values["custody"])
                || sourceFunding is not (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused)
                || values["bound"] != "1"
                || (species is CortexPolicyPendingForcedTrialRearmDenialSpecies.AlreadyArmed
                    or CortexPolicyPendingForcedTrialRearmDenialSpecies.IntentNotBound)) return false;
        evaluation = new(outcome, species, policy, funding, sourceFunding, sourceDecision, sourceEvent, sourceCorroboration,
            sourceSupport, sourceCandidate, sourcePaidCandidate, sourceReadout, new GrammarRevisionID(sourceRevision), sourceState,
            readout, candidate, new GrammarRevisionID(revision), support, state,
            stateOnly && values["obligation"].Length == 0 ? null! : values["obligation"], arm, feature,
            stateOnly && values["source_run"].Length == 0 ? null! : values["source_run"],
            stateOnly && values["custody"].Length == 0 ? null! : values["custody"], values["bound"] == "1");
        return evaluation.Allowed == (outcome == CortexPolicyPendingForcedTrialRearmOutcomes.Allowed)
            && (outcome == CortexPolicyPendingForcedTrialRearmOutcomes.Allowed
                ? species == CortexPolicyPendingForcedTrialRearmDenialSpecies.None
                : species != CortexPolicyPendingForcedTrialRearmDenialSpecies.None);
    }

    private static bool TryDecodePolicyCanonicalState(string encoded, out PolicyCanonicalStateID state)
    {
        state = default;
        if (encoded.Length == 0) return true;
        string[] parts = encoded.Split(':');
        if (parts.Length != 4 || !byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte kind)
            || !ushort.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort version)
            || !TryParseHex(parts[3], out ulong value)) return false;
        try
        {
            state = new(new CortexPolicyID(parts[0]), (PolicyCanonicalStateKinds)kind, version, value);
            return state.IsValidFor(state.Policy);
        }
        catch (ArgumentException)
        {
            state = default;
            return false;
        }
    }

    private static bool IsCanonicalSHA256(string value)
        => value.Length == 64 && value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// Persist the exact fold provenance at the grammar install revision boundary.  The
    /// R4 sidecar may describe this same receipt, but it is not the authority: this
    /// packet is the ordinary tape/journal event that the audit-only verifier resolves.
    internal static TapeEventID AppendGrammarFoldInstallRevision(
        Tape tape,
        Journal journal,
        int step,
        in GrammarFoldProvenanceReceipt fold)
    {
        fold.Validate();
        string encodedText = string.Join('\t',
            GrammarFoldInstallRevisionPrefix,
            $"previous-revision={fold.PreviousRevision.Value}",
            $"revision={fold.Revision.Value}",
            $"consumed-events={string.Join(',', fold.ConsumedEventIDs.Select(static id => id.Value))}",
            $"consumed-digest={fold.ConsumedEventDigest.Value}",
            $"episodes={string.Join(',', fold.CompositionEpisodeDigests.Select(static digest => digest.Value))}",
            $"receipt-digest={fold.ReceiptDigest.Value}");
        byte[] encoded = Encoding.ASCII.GetBytes(encodedText);
        ObservationEnvelope observation = BeginObservation("grammar:fold", Provenances.Execution, TapeEventRoles.AuditOnly);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.Mint(step, eventID, "grammar:fold", encoded);
        return eventID;
    }

    internal static bool TryDecodeGrammarFoldInstallRevision(
        ReadOnlySpan<byte> encoded,
        out GrammarFoldProvenanceReceipt fold)
    {
        fold = default;
        string[] fields = Encoding.ASCII.GetString(encoded).Split('\t');
        if (fields.Length != 7 || fields[0] != GrammarFoldInstallRevisionPrefix) return false;
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int index = 1; index < fields.Length; index++)
        {
            int equals = fields[index].IndexOf('=');
            if (equals <= 0 || !values.TryAdd(fields[index][..equals], fields[index][(equals + 1)..])) return false;
        }
        if (!ulong.TryParse(values.GetValueOrDefault("previous-revision"), out ulong previous)
            || !ulong.TryParse(values.GetValueOrDefault("revision"), out ulong revision)
            || !values.TryGetValue("consumed-events", out string? consumedText)
            || !values.TryGetValue("consumed-digest", out string? consumedDigest)
            || !values.TryGetValue("episodes", out string? episodeText)
            || !values.TryGetValue("receipt-digest", out string? receiptDigest)) return false;
        string[] consumedTokens = consumedText.Split(',', StringSplitOptions.RemoveEmptyEntries);
        string[] episodeTokens = episodeText.Split(',', StringSplitOptions.RemoveEmptyEntries);
        TapeEventID[] consumed = new TapeEventID[consumedTokens.Length];
        LoopClosureDigest[] episodes = new LoopClosureDigest[episodeTokens.Length];
        for (int index = 0; index < consumed.Length; index++)
            if (!long.TryParse(consumedTokens[index], out long value)) return false;
            else consumed[index] = new TapeEventID(value);
        for (int index = 0; index < episodes.Length; index++) episodes[index] = new LoopClosureDigest(episodeTokens[index]);
        try
        {
            fold = new GrammarFoldProvenanceReceipt(
                new Cogito.Grammar.GrammarRevisionID(previous), new Cogito.Grammar.GrammarRevisionID(revision),
                consumed, episodes, new LoopClosureDigest(consumedDigest), new LoopClosureDigest(receiptDigest));
            return true;
        }
        catch (InvalidDataException) { return false; }
    }

    internal static bool TryReadPolicyTrialQuotaIdentity(
        ReadOnlySpan<byte> encoded,
        out CortexPolicyQuotaDecisionID fundingID)
    {
        fundingID = default;
        if (!TryDecodePolicyTrialQuota(encoded, out CortexPolicyTrialQuotaDecision decision)) return false;
        fundingID = decision.QuotaDecisionID;
        return fundingID.Value != 0;
    }

    /// Decode every typed field of a funding packet.  Custody must compare the
    /// producer's fingerprint/revision/policy, not merely its QuotaDecisionID.
    internal static bool TryDecodePolicyTrialQuota(
        ReadOnlySpan<byte> encoded,
        out CortexPolicyTrialQuotaDecision decision)
        => TryDecodePolicyTrialQuota(encoded, out decision, out _);

    /// Decode a funding packet and report whether it carries the current custody field.
    /// The 21-field packet is a readable legacy wire shape; it must never be treated as
    /// current Homeostat custody proof merely because its typed fields decode.
    internal static bool TryDecodePolicyTrialQuota(
        ReadOnlySpan<byte> encoded,
        out CortexPolicyTrialQuotaDecision decision,
        out bool hasSeedCustody)
        => TryDecodePolicyTrialQuota(encoded, out decision, out hasSeedCustody, out _);

    internal static bool TryDecodePolicyTrialQuota(
        ReadOnlySpan<byte> encoded,
        out CortexPolicyTrialQuotaDecision decision,
        out bool hasSeedCustody,
        out bool hasReadoutFingerprint)
    {
        decision = default;
        hasSeedCustody = false;
        hasReadoutFingerprint = false;
        string[] fields = Encoding.ASCII.GetString(encoded).Split('\t');
        string[] names =
        [
            "id", "policy", "fingerprint", "step", "horizon", "arms", "planned", "reserved",
            "decision", "charged", "remaining", "candidate", "denial", "origin", "current",
            "required", "revision", "allocation", "allocation_digest", "allocation_arm_steps",
        ];
        bool legacy = fields.Length == 12;
        int fieldCount = legacy ? 11 : 20;
        bool hasState = fields.Length == 24;
        if (fields.Length is not (12 or 22 or 23 or 24) || fields[0] != PolicyTrialQuotaPrefix) return false;
        if (fields.Length is 23 or 24)
        {
            // Current packets place the full readout identity beside the semantic candidate.
            const string readoutPrefix = "readout_fingerprint=";
            if (!fields[4].StartsWith(readoutPrefix, StringComparison.Ordinal)
                || !TryParseHex(fields[4][readoutPrefix.Length..], out ulong parsedReadout)
                || parsedReadout == 0) return false;
            hasReadoutFingerprint = true;
        }
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int index = 0; index < fieldCount; index++)
        {
            string prefix = names[index] + "=";
            int fieldIndex = index + 1 + (fields.Length is 23 or 24 && index >= 3 ? 1 : 0);
            if (!fields[fieldIndex].StartsWith(prefix, StringComparison.Ordinal)) return false;
            values.Add(names[index], fields[fieldIndex][prefix.Length..]);
        }
        string seedAuditOnlyDigest = "";
        if (fields.Length is 22 or 23 or 24)
        {
            const string auditOnlyPrefix = "seed_custody_digest=";
            int auditOnlyIndex = fields.Length is 23 or 24 ? 22 : 21;
            if (!fields[auditOnlyIndex].StartsWith(auditOnlyPrefix, StringComparison.Ordinal)) return false;
            seedAuditOnlyDigest = fields[auditOnlyIndex][auditOnlyPrefix.Length..];
            if (seedAuditOnlyDigest.Length != 0 && !IsCanonicalSHA256(seedAuditOnlyDigest)) return false;
            hasSeedCustody = true;
        }
        if (values["id"].Length != 16 || values["fingerprint"].Length != 16
            || !TryParseHex(values, "id", out ulong id)
            || !values.TryGetValue("policy", out string? policy)
            || !TryParseHex(values, "fingerprint", out ulong fingerprint)
            || !TryParseDecimal(values, "step", out int step)
            || !TryParseDecimal(values, "horizon", out int horizon)
            || !TryParseDecimal(values, "arms", out int arms)
            || !TryParseDecimal(values, "planned", out long planned)
            || !TryParseDecimal(values, "reserved", out long reserved)
            || !values.TryGetValue("decision", out string? decisionText)
            || !Enum.TryParse(decisionText, out CortexPolicyQuotaDecisions fundingDecision)
            || !Enum.IsDefined(fundingDecision)
            || !TryParseDecimal(values, "charged", out long charged)
            || !TryParseDecimal(values, "remaining", out long remaining)) return false;
        CortexPolicyTrialCandidateStates candidate = CortexPolicyTrialCandidateStates.Active;
        CortexPolicyTrialDenialReasons denial = CortexPolicyTrialDenialReasons.None;
        int origin = step, current = step, required = -1;
        ulong revision = 0;
        string allocation = "", allocationDigest = "";
        string? allocationValue = null, allocationDigestValue = null;
        long allocationArmSteps = 0;
        if (!legacy && (!values.TryGetValue("candidate", out string? candidateText)
            || !Enum.TryParse(candidateText, out candidate) || !Enum.IsDefined(candidate)
            || !values.TryGetValue("denial", out string? denialText)
            || !Enum.TryParse(denialText, out denial) || !Enum.IsDefined(denial)
            || !TryParseDecimal(values, "origin", out origin)
            || !TryParseDecimal(values, "current", out current)
            || !TryParseDecimal(values, "required", out required)
            || !TryParseDecimal(values, "revision", out revision)
            || !values.TryGetValue("allocation", out allocationValue)
            || !values.TryGetValue("allocation_digest", out allocationDigestValue)
            || !TryParseDecimal(values, "allocation_arm_steps", out allocationArmSteps))) return false;
        if (!legacy)
        {
            allocation = allocationValue!;
            allocationDigest = allocationDigestValue!;
        }
        if (fields.Length is 22 or 23 or 24 && string.Equals(policy, Homeostat.PolicyID.Value, StringComparison.Ordinal)
            && fundingDecision is (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused)
            && !IsCanonicalSHA256(seedAuditOnlyDigest)) return false;
        try
        {
            decision = new CortexPolicyTrialQuotaDecision(
                new CortexPolicyQuotaDecisionID(id), new CortexPolicyID(policy), fingerprint,
                step, horizon, arms, planned, reserved, fundingDecision, charged, remaining)
            {
                CandidateState = candidate,
                DenialReason = denial,
                CandidateOriginStep = origin,
                CandidateCurrentStep = current,
                CandidateRequiredStep = required,
                CandidateRevision = new Cogito.Grammar.GrammarRevisionID(revision),
                AllocationIdentity = allocation,
                AllocationDigest = allocationDigest,
                AllocationArmSteps = allocationArmSteps,
                SeedAuditOnlyDigest = seedAuditOnlyDigest,
                ReadoutFingerprint = fields.Length is 23 or 24
                    ? ulong.Parse(fields[4]["readout_fingerprint=".Length..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                    : 0,
            };
            if (hasState)
            {
                const string statePrefix = "state=";
                if (!fields[23].StartsWith(statePrefix, StringComparison.Ordinal)) return false;
                string[] parts = fields[23][statePrefix.Length..].Split(':');
                if (parts.Length != 4 || !byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte kind)
                    || !ushort.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort version)
                    || !TryParseHex(parts[3], out ulong value)) return false;
                decision = decision with { CanonicalState = new PolicyCanonicalStateID(
                    new CortexPolicyID(parts[0]), (PolicyCanonicalStateKinds)kind, version, value) };
                if (!decision.CanonicalState.IsValidFor(decision.Policy)) return false;
            }
            return decision.QuotaDecisionID.Value != 0;
        }
        catch (ArgumentException) { return false; }
    }

    private static bool TryParseHex(Dictionary<string, string> values, string field, out ulong value)
    {
        value = 0;
        return values.TryGetValue(field, out string? text)
            && ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseHex(string text, out ulong value)
        => ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

    private static bool TryParseDecimal(Dictionary<string, string> values, string field, out int value)
    {
        value = 0;
        return values.TryGetValue(field, out string? text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseDecimal(Dictionary<string, string> values, string field, out long value)
    {
        value = 0;
        return values.TryGetValue(field, out string? text)
            && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseDecimal(Dictionary<string, string> values, string field, out ulong value)
    {
        value = 0;
        return values.TryGetValue(field, out string? text)
            && ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public static TapeEventID AppendPolicyTrialCompletion(
        Tape tape,
        Journal journal,
        int step,
        CortexPolicyID policy,
        in CortexPolicyTrialCompletion settlement)
    {
        byte[] encoded = EncodePolicyTrialCompletion(in settlement);
        string source = "policy:" + policy.Value;
        ObservationEnvelope observation = BeginObservation(source, Provenances.Execution, TapeEventRoles.AuditOnly);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.RecordPolicyTrialCompletion(step, eventID, source, in settlement, encoded);
        return eventID;
    }

    internal static byte[] EncodePolicyTrialCompletion(in CortexPolicyTrialCompletion settlement)
        => Encoding.ASCII.GetBytes(
            $"POLICY-TRIAL-SETTLEMENT\tid={settlement.QuotaDecisionID}\tactual={settlement.ActualExecutedArmSteps}\trefund={settlement.ReclaimedOrUnused}\tevaluator={(settlement.EvaluatorWorkUnits?.ToString(CultureInfo.InvariantCulture) ?? "na")}\tverifier={settlement.VerifierOutcome}\twall={(settlement.WallMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? "na")}");

    internal static bool TryReadPolicyTrialCompletion(
        ReadOnlySpan<byte> payload,
        out CortexPolicyTrialCompletion settlement)
    {
        settlement = default;
        string[] fields = Encoding.ASCII.GetString(payload).Split('\t');
        if (fields.Length != 7 || fields[0] != "POLICY-TRIAL-SETTLEMENT") return false;
        string[] expectedFields = ["id", "actual", "refund", "evaluator", "verifier", "wall"];
        for (int index = 0; index < expectedFields.Length; index++)
            if (!fields[index + 1].StartsWith(expectedFields[index] + "=", StringComparison.Ordinal)) return false;
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        foreach (string field in fields[1..])
        {
            int separator = field.IndexOf('=');
            if (separator <= 0 || !values.TryAdd(field[..separator], field[(separator + 1)..])) return false;
        }
        string[] required = ["id", "actual", "refund", "evaluator", "verifier", "wall"];
        if (required.Any(key => !values.ContainsKey(key))
            || !ulong.TryParse(values["id"], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong fundingID)
            || !long.TryParse(values["actual"], NumberStyles.Integer, CultureInfo.InvariantCulture, out long actual)
            || !long.TryParse(values["refund"], NumberStyles.Integer, CultureInfo.InvariantCulture, out long refund)
            || actual < 0 || refund < 0
            || !Enum.TryParse(values["verifier"], ignoreCase: false, out CortexPolicyVerifierOutcomes verifier)) return false;
        long? evaluator = values["evaluator"] == "na" ? null
            : long.TryParse(values["evaluator"], NumberStyles.Integer, CultureInfo.InvariantCulture, out long evaluatorValue) && evaluatorValue >= 0 ? evaluatorValue : null;
        if (values["evaluator"] != "na" && evaluator is null) return false;
        long? wall = values["wall"] == "na" ? null
            : long.TryParse(values["wall"], NumberStyles.Integer, CultureInfo.InvariantCulture, out long wallValue) && wallValue >= 0 ? wallValue : null;
        if (values["wall"] != "na" && wall is null) return false;
        settlement = new CortexPolicyTrialCompletion(new CortexPolicyQuotaDecisionID(fundingID), actual, refund, evaluator, verifier, wall);
        return fundingID != 0 && Enum.IsDefined(verifier)
            && EncodePolicyTrialCompletion(in settlement).AsSpan().SequenceEqual(payload);
    }

    internal static TapeEventID AppendPolicyBoundaryReceipt(
        Tape tape,
        Journal journal,
        int step,
        CortexPolicyID policy,
        IPolicyBoundaryDomain domain,
        in PolicyBoundaryForkReceipt receipt)
    {
        if (!policy.Equals(domain.PolicyID) || !domain.PolicyBinding.PolicyID.Equals(policy))
            throw new InvalidDataException("policy-boundary receipt policy does not match its registered domain binding");
        receipt.Validate(domain);
        byte[] encoded = EncodePolicyBoundaryReceipt(policy, domain, in receipt);
        string digest = PolicyBoundaryObligation.ComputeReceiptDigest(in receipt);
        string source = "policy:" + policy.Value;
        ObservationEnvelope observation = BeginObservation(source, Provenances.Execution, TapeEventRoles.AuditOnly);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.RecordPolicyBoundary(step, eventID, source, domain, in receipt, digest, encoded);
        return eventID;
    }

    internal static byte[] EncodePolicyBoundaryReceipt(CortexPolicyID policy, IPolicyBoundaryDomain domain, in PolicyBoundaryForkReceipt receipt)
    {
        if (!policy.Equals(domain.PolicyID) || !domain.PolicyBinding.PolicyID.Equals(policy))
            throw new InvalidDataException("policy-boundary receipt policy does not match its registered domain binding");
        receipt.Validate(domain);
        string arms = string.Join(';', receipt.Arms.Select(static arm =>
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
                arm.ExecutedCanonicalState.Policy.Value, (byte)arm.ExecutedCanonicalState.Kind,
                arm.ExecutedCanonicalState.Version, arm.ExecutedCanonicalState.Value.ToString("X16", CultureInfo.InvariantCulture),
                arm.ExecutedDecisionEventID.Value, arm.ExecutedOutcomeEventID.Value, arm.ExecutedOutcomePayloadSHA256,
                arm.ForcedDivergenceSeed.ToString("X16", CultureInfo.InvariantCulture), arm.Diverged ? 1 : 0)));
        string digest = PolicyBoundaryObligation.ComputeReceiptDigest(in receipt);
        return Encoding.ASCII.GetBytes(
            $"{PolicyBoundaryPrefix}\tid={receipt.Obligation}\tpolicy={policy.Value}\tboundary={receipt.CandidateBoundary}\tbaseline={receipt.BaselineBoundary}\thorizons={string.Join(',', receipt.Horizons)}\tarms={arms}\texecution-schema=7\tcontinuity={(receipt.ContinuityExact ? 1 : 0)}\tmatched-spend={(receipt.MatchedSpend ? 1 : 0)}\tchild-process-completed={(receipt.AllChildrenCompleted ? 1 : 0)}\tforced-null-behavior={(receipt.ForcedNullBehaviorExecuted ? 1 : 0)}\tforced-null-diverged={(receipt.ForcedNullDiverged ? 1 : 0)}\tverified={(receipt.Verified ? 1 : 0)}\tfunding-id={receipt.QuotaDecisionID}\tsource-fingerprint={receipt.SourceDecisionReadoutFingerprint:X16}\tsource-candidate-fingerprint={receipt.SourceDecisionCandidateFingerprint:X16}\tsource-revision={receipt.SourceDecisionReadoutRevision}\tteacher-events={(receipt.TeacherCorroboration is null ? "" : string.Join(',', receipt.TeacherCorroboration.TeacherEventIDs.Select(static id => id.Value)))}\tteacher-evidence={(receipt.TeacherCorroboration?.EvidenceSHA256 ?? "")}\tfold-node={(receipt.TeacherCorroboration?.FoldNodeID.Value ?? "")}\tfold-revision={(receipt.TeacherCorroboration?.FoldRevision.Value ?? 0)}\tteacher-revision={(receipt.TeacherCorroboration?.TeacherRevision.Value ?? 0)}\ttraining-witness={(receipt.TeacherCorroboration?.ReadoutTrainingCorroborationSHA256.Value ?? "")}\texecution-witness={(receipt.ExecutionCorroboration?.PaidDivergenceExecutionCorroborationSHA256.Value ?? "")}\texecution-training={(receipt.ExecutionCorroboration?.ReadoutTrainingCorroborationSHA256.Value ?? "")}\texecution-funding={(receipt.ExecutionCorroboration?.QuotaDecisionID.Value.ToString("X16", CultureInfo.InvariantCulture) ?? "")}\texecution-readout={(receipt.ExecutionCorroboration?.QuotaReadoutFingerprint.ToString("X16", CultureInfo.InvariantCulture) ?? "")}\texecution-fingerprint={(receipt.ExecutionCorroboration?.QuotaCandidateFingerprint.ToString("X16", CultureInfo.InvariantCulture) ?? "")}\texecution-revision={(receipt.ExecutionCorroboration?.FundingCandidateRevision.Value.ToString(CultureInfo.InvariantCulture) ?? "")}\texecution-fork={(receipt.ExecutionCorroboration?.ForkArmSHA256.Value ?? "")}\texecution-child={(receipt.ExecutionCorroboration?.ChildExecutionReceiptSHA256.Value ?? "")}\texecution-dissent-decision={(receipt.ExecutionCorroboration?.ExecutedDivergenceDecisionID.Value.ToString(CultureInfo.InvariantCulture) ?? "")}\texecution-dissent-outcome={(receipt.ExecutionCorroboration?.ExecutedDivergenceOutcomeID.Value ?? "")}\texecution-dissent-outcome-event={(receipt.ExecutionCorroboration?.ExecutedDivergenceOutcomeEventID.Value.ToString(CultureInfo.InvariantCulture) ?? "")}\texecution-dissent-outcome-payload={(receipt.ExecutionCorroboration?.ExecutedDivergenceOutcomePayloadSHA256 ?? "")}\tdigest={digest}");
    }

    public static TapeEventID AppendPolicyExample(
        Tape tape,
        Journal journal,
        int step,
        CortexPolicyID policy,
        int action,
        ReadOnlySpan<MetricSample> features,
        int actionCount)
    {
        if ((uint)action >= (uint)actionCount) throw new ArgumentOutOfRangeException(nameof(action));
        byte[] context = EncodePolicyGrammarContext(policy, features, actionCount);
        byte[] continuation = EncodePolicyGrammarContinuation(action);
        byte[] encoded = new byte[context.Length + continuation.Length];
        context.CopyTo(encoded, 0);
        continuation.CopyTo(encoded, context.Length);
        string source = "policy:" + policy.Value;
        ObservationEnvelope observation = BeginObservation(source, Provenances.Execution, TapeEventRoles.GrammarInput);
        TapeEventID eventID = EndObservation(tape, in observation, encoded);
        journal.RecordPolicyExample(step, eventID, source, action, actionCount, features.Length, encoded);
        return eventID;
    }

    /// Append a policy teacher as separate grammar and audit-only events. The grammar
    /// event contains only semantic state and continuation; the audit-only companion
    /// retains the complete raw frame without expanding the learner's state space.
    internal static PolicyTeacherPacketIDs AppendPolicySemanticExample(
        Tape tape,
        Journal journal,
        int step,
        CortexPolicyID policy,
        int action,
        ReadOnlySpan<MetricSample> rawFeatures,
        int actionCount,
        ReadOnlySpan<MetricID> excludedMetricIDs)
    {
        if ((uint)action >= (uint)actionCount) throw new ArgumentOutOfRangeException(nameof(action));
        ValidateMetricSamples(rawFeatures, "policy semantic teacher features");
        Span<MetricSample> semanticFeatures = stackalloc MetricSample[rawFeatures.Length];
        int semanticFeatureCount = 0;
        for (int index = 0; index < rawFeatures.Length; index++)
        {
            MetricSample sample = rawFeatures[index];
            bool excluded = false;
            for (int excludedIndex = 0; excludedIndex < excludedMetricIDs.Length; excludedIndex++)
            {
                if (sample.MetricID.Equals(excludedMetricIDs[excludedIndex]))
                {
                    excluded = true;
                    break;
                }
            }
            if (!excluded) semanticFeatures[semanticFeatureCount++] = sample;
        }
        if (semanticFeatureCount == 0)
            throw new ArgumentException("semantic policy teacher cannot exclude every metric", nameof(excludedMetricIDs));
        byte[] context = EncodePolicyGrammarContext(policy, semanticFeatures[..semanticFeatureCount], actionCount);
        byte[] continuation = EncodePolicyGrammarContinuation(action);
        byte[] evidence = EncodeMetricFrame(rawFeatures);
        byte[] grammarBytes = new byte[context.Length + continuation.Length];
        context.CopyTo(grammarBytes, 0);
        continuation.CopyTo(grammarBytes, context.Length);
        PolicyTeacherPacketIDs packetIDs = AppendSplitPolicyTeacher(
            tape, policy.Value, grammarBytes, evidence,
            $"semantic-features={semanticFeatureCount}\traw-features={rawFeatures.Length}");
        journal.RecordPolicyExample(step, packetIDs.GrammarEventID, packetIDs.AuditOnlyEventID,
            "policy:" + policy.Value, action, actionCount, semanticFeatureCount, rawFeatures.Length, grammarBytes);
        return packetIDs;
    }

    /// Append a teacher example whose learner identity is the finite policy state. The
    /// canonical context and launchpad continuation are grammar input; raw metric samples
    /// are emitted as a typed audit-only companion.
    public static PolicyTeacherPacketIDs AppendPolicyCanonicalExample(
        Tape tape,
        Journal journal,
        int step,
        CortexPolicyID policy,
        in PolicyCanonicalStateID canonicalState,
        int action,
        ReadOnlySpan<MetricSample> rawFeatures,
        int actionCount)
    {
        if ((uint)action >= (uint)actionCount) throw new ArgumentOutOfRangeException(nameof(action));
        if (!canonicalState.Policy.Equals(policy)) throw new ArgumentException("teacher state belongs to another policy", nameof(canonicalState));
        byte[] context = EncodePolicyCanonicalGrammarContext(policy, in canonicalState, actionCount);
        byte[] continuation = EncodePolicyGrammarContinuation(action);
        byte[] evidence = EncodeMetricFrame(rawFeatures);
        byte[] grammarBytes = new byte[context.Length + continuation.Length];
        context.CopyTo(grammarBytes, 0);
        continuation.CopyTo(grammarBytes, context.Length);
        PolicyTeacherPacketIDs packetIDs = AppendSplitPolicyTeacher(
            tape, policy.Value, grammarBytes, evidence,
            $"state={canonicalState}\traw-features={rawFeatures.Length}");
        journal.RecordPolicyExample(step, packetIDs.GrammarEventID, packetIDs.AuditOnlyEventID,
            "policy:" + policy.Value, action, actionCount, in canonicalState, rawFeatures.Length, grammarBytes);
        return packetIDs;
    }

    /// Append a canonical teacher pair carrying the exact fold corroboration that selected it.
    /// Fold provenance and raw evidence live on the audit-only companion, never in the
    /// grammar event consumed by induction.
    public static PolicyTeacherPacketIDs AppendPolicyCanonicalExample(
        Tape tape,
        Journal journal,
        int step,
        CortexPolicyID policy,
        in PolicyCanonicalStateID canonicalState,
        int action,
        ReadOnlySpan<MetricSample> rawFeatures,
        int actionCount,
        in LoopClosureTeacherPacketProvenance provenance)
    {
        if ((uint)action >= (uint)actionCount) throw new ArgumentOutOfRangeException(nameof(action));
        if (!canonicalState.Policy.Equals(policy)) throw new ArgumentException("teacher state belongs to another policy", nameof(policy));
        provenance.Validate();
        byte[] context = EncodePolicyCanonicalGrammarContext(policy, in canonicalState, actionCount);
        byte[] continuation = EncodePolicyGrammarContinuation(action);
        byte[] evidence = EncodeMetricFrame(rawFeatures);
        byte[] grammarBytes = new byte[context.Length + continuation.Length];
        context.CopyTo(grammarBytes, 0);
        continuation.CopyTo(grammarBytes, context.Length);
        PolicyTeacherPacketIDs packetIDs = AppendSplitPolicyTeacher(
            tape, policy.Value, grammarBytes, evidence,
            $"state={canonicalState}{provenance.EncodePacketFields()}");
        journal.RecordPolicyExample(step, packetIDs.GrammarEventID, packetIDs.AuditOnlyEventID,
            "policy:" + policy.Value, action, actionCount, in canonicalState, rawFeatures.Length, in provenance, grammarBytes);
        return packetIDs;
    }

    internal static byte[] EncodePolicyGrammarContext(
        CortexPolicyID policy,
        ReadOnlySpan<MetricSample> features,
        int actionCount)
    {
        if (actionCount < 2) throw new ArgumentOutOfRangeException(nameof(actionCount));
        ValidateMetricSamples(features, "policy grammar context features");
        byte[] policyBytes = Encoding.UTF8.GetBytes(policy.Value);
        if (policyBytes.Length > ushort.MaxValue) throw new ArgumentException("policy identity exceeds the u16 byte ceiling", nameof(policy));
        const int PolicyLengthDigits = 4;
        int length = PolicyGrammarContextPrefix.Length
            + PolicyField.Length + PolicyLengthDigits + 1 + policyBytes.Length
            + ActionCountField.Length + TypedNumberLength
            + FeaturesField.Length + TypedNumberLength
            + features.Length * SampleLength;
        byte[] encoded = new byte[length];
        int offset = 0;
        WriteBytes(encoded, ref offset, PolicyGrammarContextPrefix);
        WriteBytes(encoded, ref offset, PolicyField);
        WriteHex(encoded, ref offset, (ulong)policyBytes.Length, PolicyLengthDigits);
        encoded[offset++] = (byte)':';
        WriteBytes(encoded, ref offset, policyBytes);
        WriteTypedI64(encoded, ref offset, ActionCountField, actionCount);
        WriteTypedU64(encoded, ref offset, FeaturesField, (ulong)features.Length);
        WriteMetricSamples(encoded, ref offset, features);
        return encoded;
    }

    internal static byte[] EncodePolicyCanonicalGrammarContext(
        CortexPolicyID policy,
        in PolicyCanonicalStateID canonicalState,
        int actionCount)
    {
        if (actionCount < 2) throw new ArgumentOutOfRangeException(nameof(actionCount));
        if (!canonicalState.Policy.Equals(policy)) throw new ArgumentException("canonical state belongs to another policy", nameof(canonicalState));
        // The state atom already carries policy/kind/version/value.  Keep the grammar
        // context byte-exact with GrammarPolicyContextKey so raw metrics cannot split it.
        return canonicalState.Encode();
    }

    internal static byte[] EncodePolicyGrammarContinuation(int action)
    {
        if (action < 0) throw new ArgumentOutOfRangeException(nameof(action));
        byte[] encoded = new byte[ActionField.Length + TypedNumberLength];
        int offset = 0;
        WriteTypedI64(encoded, ref offset, ActionField, action);
        return encoded;
    }

    internal static bool ValidatePolicyGrammarContext(
        ReadOnlySpan<byte> encoded,
        CortexPolicyID expectedPolicy,
        int expectedActionCount,
        int expectedFeatureCount)
    {
        if (expectedActionCount < 2 || expectedFeatureCount <= 0) return false;
        int offset = 0;
        if (!Consume(encoded, PolicyGrammarContextPrefix, ref offset)) return false;
        if (!Consume(encoded, PolicyField, ref offset) || encoded.Length < offset + 5) return false;
        if (!ReadHex(encoded.Slice(offset, 4), out ulong policyLength) || encoded[offset + 4] != (byte)':') return false;
        offset += 5;
        if (policyLength > (ulong)(encoded.Length - offset)) return false;
        string policy = Encoding.UTF8.GetString(encoded.Slice(offset, checked((int)policyLength)));
        if (!string.Equals(policy, expectedPolicy.Value, StringComparison.Ordinal)) return false;
        offset += checked((int)policyLength);
        if (!Consume(encoded, ActionCountField, ref offset) || !ReadTypedI64(encoded, ref offset, out long actionCount)
            || actionCount != expectedActionCount) return false;
        if (!Consume(encoded, FeaturesField, ref offset) || !ReadTypedU64(encoded, ref offset, out ulong featureCount)
            || featureCount != (ulong)expectedFeatureCount) return false;
        int previousMetric = -1;
        for (ulong index = 0; index < featureCount; index++)
        {
            if (encoded.Length < offset + 24 || encoded[offset++] != (byte)'\t') return false;
            if (!ReadHex(encoded.Slice(offset, 4), out ulong metric) || metric > ushort.MaxValue
                || (previousMetric >= 0 && metric <= (ulong)previousMetric)) return false;
            previousMetric = (int)metric;
            offset += 4;
            if (encoded[offset++] != (byte)':' || encoded[offset++] is not ((byte)'i' or (byte)'u' or (byte)'f') || encoded[offset++] != (byte)':') return false;
            if (!ReadHex(encoded.Slice(offset, 16), out _)) return false;
            offset += 16;
        }
        return offset == encoded.Length;

        static bool Consume(ReadOnlySpan<byte> source, ReadOnlySpan<byte> field, ref int cursor)
        {
            if (source.Length - cursor < field.Length || !source.Slice(cursor, field.Length).SequenceEqual(field)) return false;
            cursor += field.Length;
            return true;
        }

        static bool ReadTypedI64(ReadOnlySpan<byte> source, ref int cursor, out long value)
        {
            value = 0;
            if (source.Length < cursor + 18 || source[cursor++] != (byte)'i' || source[cursor++] != (byte)':') return false;
            if (!ReadHex(source.Slice(cursor, 16), out ulong bits)) return false;
            cursor += 16;
            value = unchecked((long)bits);
            return true;
        }

        static bool ReadTypedU64(ReadOnlySpan<byte> source, ref int cursor, out ulong value)
        {
            value = 0;
            if (source.Length < cursor + 18 || source[cursor++] != (byte)'u' || source[cursor++] != (byte)':') return false;
            if (!ReadHex(source.Slice(cursor, 16), out value)) return false;
            cursor += 16;
            return true;
        }

        static bool ReadHex(ReadOnlySpan<byte> source, out ulong value)
        {
            value = 0;
            for (int index = 0; index < source.Length; index++)
            {
                byte digit = source[index];
                int nibble = digit switch
                {
                    >= (byte)'0' and <= (byte)'9' => digit - (byte)'0',
                    >= (byte)'A' and <= (byte)'F' => digit - (byte)'A' + 10,
                    _ => -1,
                };
                if (nibble < 0) return false;
                value = (value << 4) | (uint)nibble;
            }
            return true;
        }
    }

    private static byte[] EncodePolicyDecision(
        in CortexPolicyDecision decision,
        ReadOnlySpan<MetricSample> features,
        int actionCount)
    {
        ValidateMetricSamples(features, "policy decision features");
        int length = PolicyDecisionPrefix.Length
            + DecisionField.Length + TypedNumberLength
            + AuthorityField.Length + TypedNumberLength
            + RevisionField.Length + TypedNumberLength
            + ReadoutFingerprintField.Length + TypedNumberLength
            + CandidateFingerprintField.Length + TypedNumberLength
            + SupportDigestField.Length + TypedNumberLength
            + ActionField.Length + TypedNumberLength
            + LaunchpadActionField.Length + TypedNumberLength
            + RawCandidateActionField.Length + TypedNumberLength
            + SelectedCandidateActionField.Length + TypedNumberLength
            + SelectionCauseField.Length + TypedNumberLength
            + ActionCountField.Length + TypedNumberLength
            + DrillField.Length + TypedNumberLength
            + FeaturesField.Length + TypedNumberLength
            + features.Length * SampleLength;
        byte[] encoded = new byte[length];
        int offset = 0;
        WriteBytes(encoded, ref offset, PolicyDecisionPrefix);
        WriteTypedU64(encoded, ref offset, DecisionField, decision.DecisionID.Value);
        WriteTypedU64(encoded, ref offset, AuthorityField, (byte)decision.Authority);
        WriteTypedU64(encoded, ref offset, RevisionField, decision.GrammarRevision.Value);
        WriteTypedU64(encoded, ref offset, ReadoutFingerprintField, decision.Readout.ReadoutFingerprint);
        WriteTypedU64(encoded, ref offset, CandidateFingerprintField, decision.Readout.ReadoutCandidateFingerprint);
        WriteTypedU64(encoded, ref offset, SupportDigestField, decision.Readout.ReadoutCandidateOccurrenceDigest);
        WriteTypedI64(encoded, ref offset, ActionField, decision.Action);
        WriteTypedI64(encoded, ref offset, LaunchpadActionField, decision.LaunchpadAction);
        WriteTypedI64(encoded, ref offset, RawCandidateActionField, decision.RawCandidateAction);
        WriteTypedI64(encoded, ref offset, SelectedCandidateActionField, decision.SelectedCandidateAction);
        WriteTypedU64(encoded, ref offset, SelectionCauseField, (byte)decision.SelectionCause);
        WriteTypedI64(encoded, ref offset, ActionCountField, actionCount);
        WriteTypedU64(encoded, ref offset, DrillField, decision.RollbackDrill ? 1UL : 0UL);
        WriteTypedU64(encoded, ref offset, FeaturesField, (ulong)features.Length);
        WriteMetricSamples(encoded, ref offset, features);
        return encoded;
    }

    internal static CortexPolicyDecisionPacket DecodePolicyDecision(ReadOnlySpan<byte> encoded)
    {
        string[] fields = Encoding.ASCII.GetString(encoded).Split('\t');
        if (fields.Length == 0 || fields[0] != "POLICY-DECISION")
            throw new InvalidDataException("policy decision packet prefix is invalid");
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        int featureIndex = -1;
        for (int i = 1; i < fields.Length; i++)
        {
            int equals = fields[i].IndexOf('=');
            if (equals <= 0)
            {
                if (featureIndex < 0) throw new InvalidDataException("policy decision packet has a trailing token before features");
                ValidatePacketSample(fields[i]);
                continue;
            }
            if (featureIndex >= 0) throw new InvalidDataException("policy decision packet has an unknown field after features");
            string field = fields[i][..equals];
            if (field == "features") featureIndex = i;
            else if (field is not ("decision" or "authority" or "revision" or "readout-fingerprint" or "candidate-fingerprint" or "support" or "action" or "launchpad-action" or "raw-candidate-action" or "selected-candidate-action" or "selection-cause" or "action-count" or "drill"))
                throw new InvalidDataException($"policy decision packet has unknown field '{field}'");
            if (!values.TryAdd(field, fields[i][(equals + 1)..]))
                throw new InvalidDataException($"policy decision packet repeats field '{field}'");
        }
        ulong decisionID = ParsePacketU64(values, "decision");
        int actionCount = checked((int)ParsePacketI64(values, "action-count"));
        ulong supportDigest = values.ContainsKey("support") ? ParsePacketU64(values, "support") : 0;
        ulong readoutFingerprint = values.ContainsKey("readout-fingerprint") ? ParsePacketU64(values, "readout-fingerprint") : 0;
        ulong candidateFingerprint = values.ContainsKey("candidate-fingerprint") ? ParsePacketU64(values, "candidate-fingerprint") : 0;
        CortexPolicyDecisionReadout readout = new(
            checked((int)ParsePacketI64(values, "launchpad-action")),
            checked((int)ParsePacketI64(values, "raw-candidate-action")),
            checked((int)ParsePacketI64(values, "selected-candidate-action")),
            checked((int)ParsePacketI64(values, "action")),
            (CortexPolicyAuthorities)ParsePacketU64(values, "authority"),
            new global::Cogito.Grammar.GrammarRevisionID(ParsePacketU64(values, "revision")),
            (CortexPolicySelectionCauses)ParsePacketU64(values, "selection-cause"),
            supportDigest,
            candidateFingerprint,
            readoutFingerprint);
        ulong drill = ParsePacketU64(values, "drill");
        ulong featureCount = ParsePacketU64(values, "features");
        if (featureIndex < 0 || featureCount == 0 || featureCount != (ulong)(fields.Length - featureIndex - 1)
            || featureCount > int.MaxValue)
            throw new InvalidDataException("policy decision packet feature count does not match its payload");
        if ((drill != 0) != readout.RollbackDrill)
            throw new InvalidDataException("policy decision packet drill marker disagrees with selection cause");
        MetricSample[] features = new MetricSample[checked((int)featureCount)];
        int previousMetric = -1;
        for (int index = 0; index < features.Length; index++)
        {
            string token = fields[featureIndex + index + 1];
            string[] parts = token.Split(':');
            if (parts.Length != 3 || parts[0].Length != 4 || parts[2].Length != 16
                || parts[1] is not ("i" or "u" or "f")
                || !ushort.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort metricID)
                || metricID <= previousMetric
                || !ulong.TryParse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong bits))
                throw new InvalidDataException("policy decision packet feature payload is malformed");
            previousMetric = metricID;
            NumericValue value = parts[1][0] switch
            {
                'i' => NumericValue.FromI64(unchecked((long)bits)),
                'u' => NumericValue.FromU64(bits),
                'f' => NumericValue.FromF64(BitConverter.Int64BitsToDouble(unchecked((long)bits))),
                _ => throw new InvalidDataException("policy decision packet feature kind is unsupported"),
            };
            features[index] = new MetricSample(new MetricID(metricID), value);
        }
        readout.Validate(actionCount);
        return new CortexPolicyDecisionPacket(new CortexPolicyDecisionID(decisionID), readout, actionCount, features);
    }

    internal static bool TryDecodePolicyDecision(ReadOnlySpan<byte> encoded, out CortexPolicyDecisionPacket packet)
    {
        try
        {
            packet = DecodePolicyDecision(encoded);
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or FormatException or OverflowException or ArgumentException)
        {
            packet = default;
            return false;
        }
    }

    private static long ParsePacketI64(Dictionary<string, string> values, string field)
    {
        if (!values.TryGetValue(field, out string? token) || token.Length != 18 || token[1] != ':')
            throw new InvalidDataException($"policy decision packet is missing typed field '{field}'");
        if (token[0] != 'i') throw new InvalidDataException($"policy decision packet field '{field}' is not signed");
        return unchecked((long)ulong.Parse(token.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    private static ulong ParsePacketU64(Dictionary<string, string> values, string field)
    {
        if (!values.TryGetValue(field, out string? token) || token.Length != 18 || token[1] != ':')
            throw new InvalidDataException($"policy decision packet is missing typed field '{field}'");
        if (token[0] != 'u') throw new InvalidDataException($"policy decision packet field '{field}' is not unsigned");
        return ulong.Parse(token.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static void ValidatePacketSample(string token)
    {
        string[] parts = token.Split(':');
        if (parts.Length != 3 || parts[0].Length != 4 || parts[2].Length != 16
            || parts[1] is not ("i" or "u" or "f"))
            throw new InvalidDataException("policy decision packet feature payload is malformed");
        _ = ushort.Parse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        _ = ulong.Parse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static byte[] EncodePolicyOutcome(
        CortexPolicyDecisionID decisionID,
        ReadOnlySpan<MetricSample> outcomes,
        bool invariantClean,
        long conservedCost)
    {
        ValidateMetricSamples(outcomes, "policy outcome samples");
        int length = PolicyOutcomePrefix.Length
            + DecisionField.Length + TypedNumberLength
            + InvariantField.Length + TypedNumberLength
            + ConservedCostField.Length + TypedNumberLength
            + OutcomesField.Length + TypedNumberLength
            + outcomes.Length * SampleLength;
        byte[] encoded = new byte[length];
        int offset = 0;
        WriteBytes(encoded, ref offset, PolicyOutcomePrefix);
        WriteTypedU64(encoded, ref offset, DecisionField, decisionID.Value);
        WriteTypedU64(encoded, ref offset, InvariantField, invariantClean ? 1UL : 0UL);
        WriteTypedI64(encoded, ref offset, ConservedCostField, conservedCost);
        WriteTypedU64(encoded, ref offset, OutcomesField, (ulong)outcomes.Length);
        WriteMetricSamples(encoded, ref offset, outcomes);
        return encoded;
    }

    private static byte[] EncodeMetricFrame(ReadOnlySpan<MetricSample> samples)
    {
        ValidateMetricSamples(samples, "metric frame samples");

        const int CountHexLength = 4;
        byte[] encoded = new byte[MetricPrefix.Length + CountHexLength + samples.Length * SampleLength];
        int offset = 0;
        WriteBytes(encoded, ref offset, MetricPrefix);
        WriteHex(encoded, ref offset, (ulong)samples.Length, CountHexLength);
        WriteMetricSamples(encoded, ref offset, samples);
        return encoded;
    }

    private static void ValidateMetricSamples(ReadOnlySpan<MetricSample> samples, string packetName)
    {
        if (samples.Length == 0) throw new ArgumentException(packetName + " must contain at least one sample", nameof(samples));
        if (samples.Length > ushort.MaxValue) throw new ArgumentException(packetName + " exceeds the u16 sample-count ceiling", nameof(samples));
        for (int index = 0; index < samples.Length; index++)
        {
            if (index > 0 && samples[index - 1].MetricID.CompareTo(samples[index].MetricID) >= 0)
                throw new ArgumentException(packetName + " must be strictly ordered by MetricID", nameof(samples));
        }
    }

    private static void WriteMetricSamples(Span<byte> destination, ref int offset, ReadOnlySpan<MetricSample> samples)
    {
        for (int index = 0; index < samples.Length; index++)
        {
            MetricSample sample = samples[index];
            destination[offset++] = (byte)'\t';
            WriteHex(destination, ref offset, sample.MetricID.Value, 4);
            destination[offset++] = (byte)':';
            destination[offset++] = GetNumericKindToken(sample.Value.Kind);
            destination[offset++] = (byte)':';
            WriteHex(destination, ref offset, sample.Value.Bits, 16);
        }
    }

    private static void WriteTypedU64(Span<byte> destination, ref int offset, ReadOnlySpan<byte> field, ulong value)
    {
        WriteBytes(destination, ref offset, field);
        destination[offset++] = (byte)'u';
        destination[offset++] = (byte)':';
        WriteHex(destination, ref offset, value, 16);
    }

    private static void WriteTypedI64(Span<byte> destination, ref int offset, ReadOnlySpan<byte> field, long value)
    {
        WriteBytes(destination, ref offset, field);
        destination[offset++] = (byte)'i';
        destination[offset++] = (byte)':';
        WriteHex(destination, ref offset, unchecked((ulong)value), 16);
    }

    private static void WriteBytes(Span<byte> destination, ref int offset, ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(destination[offset..]);
        offset += bytes.Length;
    }

    private static byte GetNumericKindToken(NumericKinds kind) => kind switch
    {
        NumericKinds.I64 => (byte)'i',
        NumericKinds.U64 => (byte)'u',
        NumericKinds.F64 => (byte)'f',
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "unknown numeric kind")
    };

    private static void WriteHex(Span<byte> destination, ref int offset, ulong value, int digits)
    {
        const string Hex = "0123456789ABCDEF";
        for (int shift = (digits - 1) * 4; shift >= 0; shift -= 4)
            destination[offset++] = (byte)Hex[(int)(value >> shift) & 0xF];
    }

    private static ObservationEnvelope BeginObservation(string source, Provenances provenance, TapeEventRoles roles)
        => new(source, provenance, roles);

    private static TapeEventID EndObservation(Tape tape, in ObservationEnvelope observation, byte[] packetBytes)
        => tape.Append(packetBytes, observation.Source, observation.Provenance, observation.Roles);

    private static void RequireSHA(string value, string name)
    {
        if (!IsSHA(value)) throw new InvalidDataException($"{name} digest is malformed");
    }

    private static bool IsSHA(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool TryParseField(string field, string name, out string value)
    {
        string prefix = name + "=";
        if (!field.StartsWith(prefix, StringComparison.Ordinal)) { value = ""; return false; }
        value = field[prefix.Length..];
        return value.Length != 0;
    }

    private readonly struct ObservationEnvelope(string source, Provenances provenance, TapeEventRoles roles)
    {
        public string Source => source;
        public Provenances Provenance => provenance;
        public TapeEventRoles Roles => roles;
    }
}
