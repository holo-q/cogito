namespace Cogito;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// The ordinary repository task stream is deliberately separate from the older
/// repository lineage receipts. These three receipts are the in-run custody
/// records that bind the selected candidate to its occurrenceCheck and outcome.
internal static class RepositoryLoopTaskReceiptCodec
{
    // Frozen tape source token; identifier-side name is OccurrenceCheckSource.
    internal const string OccurrenceCheckSource = "repository-verification";
    internal const string ActionPrefix = "repository-loop-action-v1";
    // Frozen wire token; identifier-side name is OccurrenceCheckPrefix.
    internal const string OccurrenceCheckPrefix = "repository-loop-verification-v1";
    internal const string OutcomePrefix = "repository-loop-outcome-v1";

    internal static string Join(params string[] fields) => RepositoryLineageReceiptCodec.Join(fields);

    internal static byte[] Encode(string prefix, string canonical, string digest)
        => Encoding.ASCII.GetBytes(string.Join('\t', prefix,
            $"canonical={Convert.ToBase64String(Encoding.UTF8.GetBytes(canonical))}",
            $"digest={digest}"));

    internal static bool TryDecode(ReadOnlySpan<byte> payload, string prefix, out string canonical, out string digest)
    {
        canonical = digest = "";
        string[] fields = Encoding.ASCII.GetString(payload).Split('\t');
        if (fields.Length != 3 || fields[0] != prefix) return false;
        if (!fields[1].StartsWith("canonical=", StringComparison.Ordinal)
            || !fields[2].StartsWith("digest=", StringComparison.Ordinal)) return false;
        try { canonical = Encoding.UTF8.GetString(Convert.FromBase64String(fields[1]["canonical=".Length..])); }
        catch (FormatException) { return false; }
        digest = fields[2]["digest=".Length..];
        return IsSHA(digest)
            && string.Equals(digest, Digest(prefix, canonical), StringComparison.Ordinal);
    }

    internal static string Digest(string prefix, string canonical)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prefix + "\t" + canonical)));

    internal static bool IsSHA(string value)
        => value is { Length: 64 } && value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static void RequireSHA(string value, string name)
    {
        if (!IsSHA(value)) throw new InvalidDataException($"repository task {name} digest is malformed");
    }

    internal static void RequireText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"repository task {name} is empty");
    }

    internal static void RequireEvent(TapeEventID eventID, string name)
    {
        if (eventID.Value <= 0) throw new InvalidDataException($"repository task {name} event is malformed");
    }
}

internal static class RepositoryLoopTaskSpeciesRules
{
    internal static bool MatchesCandidate(RepositoryLoopClosureTaskSpecies task, RepositoryCandidateSpecies candidate)
        => task switch
        {
            RepositoryLoopClosureTaskSpecies.Locate => candidate is RepositoryCandidateSpecies.ListPrefix or RepositoryCandidateSpecies.OpenPath,
            RepositoryLoopClosureTaskSpecies.Trace => candidate is RepositoryCandidateSpecies.SearchTerm or RepositoryCandidateSpecies.ReadLocus,
            // Read is line-backed evidence. OpenPath has no registered line
            // identity, so admitting it here would force the runtime to invent
            // a source line after the action was already selected.
            RepositoryLoopClosureTaskSpecies.Read => candidate == RepositoryCandidateSpecies.ReadLocus,
            RepositoryLoopClosureTaskSpecies.Answer => candidate == RepositoryCandidateSpecies.AnswerPath,
            RepositoryLoopClosureTaskSpecies.Diagnosis => candidate == RepositoryCandidateSpecies.VerifyPrediction,
            _ => false,
        };

    internal static bool MatchesResult(RepositoryLoopClosureTaskSpecies task, RepositoryLoopClosureResultSpecies result)
        => (task, result) is
            (RepositoryLoopClosureTaskSpecies.Locate, RepositoryLoopClosureResultSpecies.Path)
            or (RepositoryLoopClosureTaskSpecies.Trace, RepositoryLoopClosureResultSpecies.Trace)
            or (RepositoryLoopClosureTaskSpecies.Read, RepositoryLoopClosureResultSpecies.Text)
            or (RepositoryLoopClosureTaskSpecies.Answer, RepositoryLoopClosureResultSpecies.Answer)
            or (RepositoryLoopClosureTaskSpecies.Diagnosis, RepositoryLoopClosureResultSpecies.Diagnosis);
}

/// The ordinary action selected from the revisioned repository frontier. Its
/// predecessor is the already-emitted repository-selection receipt.
public readonly record struct RepositoryLoopTaskActionReceipt(
    string TaskID,
    RepositoryLoopClosureTaskSpecies TaskSpecies,
    string TaskAuthoritySHA256,
    TapeEventID SelectionEventID,
    string SelectionReceiptSHA256,
    int SelectionOrdinal,
    RepositoryCandidateSpecies CandidateSpecies,
    string CandidateCanonical,
    RepositoryCandidateDigest CandidateDigest,
    RepositoryFrontierRevision FrontierRevision,
    string FrontierAuthoritySHA256,
    string CallSHA256,
    string ReceiptSHA256) : IRepositoryLineageReceipt
{
    public string Kind => "repository-action";
    public string Canonical => RepositoryLoopTaskReceiptCodec.Join(
        TaskID, TaskSpecies.ToString(), TaskAuthoritySHA256,
        SelectionEventID.Value.ToString(CultureInfo.InvariantCulture),
        SelectionReceiptSHA256,
        SelectionOrdinal.ToString(CultureInfo.InvariantCulture),
        CandidateSpecies.ToString(), CandidateCanonical, CandidateDigest.Value.ToString(CultureInfo.InvariantCulture),
        FrontierRevision.Value.ToString(CultureInfo.InvariantCulture), FrontierAuthoritySHA256, CallSHA256);

    public RepositoryCandidate Candidate
        => RepositoryCandidate.TryParseCanonical(CandidateCanonical, out RepositoryCandidate candidate)
            ? candidate : throw new InvalidDataException("repository task action candidate is malformed");

    public static RepositoryLoopTaskActionReceipt Create(
        string taskID,
        RepositoryLoopClosureTaskSpecies taskSpecies,
        string taskAuthoritySHA256,
        TapeEventID selectionEventID,
        string selectionReceiptSHA256,
        int selectionOrdinal,
        RepositoryCandidate candidate,
        RepositoryFrontierRevision frontierRevision,
        string frontierAuthoritySHA256)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        string callSHA256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Tool.ToolCall.Create(candidate.Verb, candidate.Argument).Raw)));
        RepositoryLoopTaskActionReceipt value = new(taskID, taskSpecies, taskAuthoritySHA256, selectionEventID, selectionReceiptSHA256, selectionOrdinal,
            candidate.Species, candidate.Canonical, candidate.Digest, frontierRevision, frontierAuthoritySHA256, callSHA256, "");
        return value with { ReceiptSHA256 = RepositoryLoopTaskReceiptCodec.Digest(RepositoryLoopTaskReceiptCodec.ActionPrefix, value.Canonical) };
    }

    public byte[] Encode()
    {
        Validate();
        return RepositoryLoopTaskReceiptCodec.Encode(RepositoryLoopTaskReceiptCodec.ActionPrefix, Canonical, ReceiptSHA256);
    }

    public static RepositoryLoopTaskActionReceipt Decode(ReadOnlySpan<byte> payload)
    {
        if (!RepositoryLoopTaskReceiptCodec.TryDecode(payload, RepositoryLoopTaskReceiptCodec.ActionPrefix,
                out string canonical, out string digest)
            || !RepositoryLineageReceiptCodec.TrySplit(canonical, out string[] fields) || fields.Length != 12)
            throw new InvalidDataException("repository task action packet is malformed");
        try
        {
            if (!Enum.TryParse(fields[1], out RepositoryLoopClosureTaskSpecies taskSpecies)
                || !long.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long selectionEvent)
                || !int.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int selectionOrdinal)
                || !Enum.TryParse(fields[6], out RepositoryCandidateSpecies candidateSpecies)
                || !ulong.TryParse(fields[8], NumberStyles.None, CultureInfo.InvariantCulture, out ulong candidateDigest)
                || !ulong.TryParse(fields[9], NumberStyles.None, CultureInfo.InvariantCulture, out ulong frontierRevision))
                throw new InvalidDataException("repository task action packet numeric field is malformed");
            RepositoryLoopTaskActionReceipt value = new(fields[0], taskSpecies, fields[2], new TapeEventID(selectionEvent), fields[4], selectionOrdinal, candidateSpecies,
                fields[7], new RepositoryCandidateDigest(candidateDigest), new RepositoryFrontierRevision(frontierRevision), fields[10], fields[11], digest);
            value.Validate();
            return value;
        }
        catch (FormatException error) { throw new InvalidDataException("repository task action packet is malformed", error); }
        catch (OverflowException error) { throw new InvalidDataException("repository task action packet is malformed", error); }
    }

    public static bool TryDecode(ReadOnlySpan<byte> payload, out RepositoryLoopTaskActionReceipt receipt)
    {
        try { receipt = Decode(payload); return true; }
        catch (Exception error) when (error is InvalidDataException or FormatException or OverflowException or ArgumentException)
        { receipt = default; return false; }
    }

    public void Validate()
    {
        RepositoryLoopTaskReceiptCodec.RequireText(TaskID, "task id");
        RepositoryLoopTaskReceiptCodec.RequireSHA(TaskAuthoritySHA256, "task authority");
        RepositoryLoopTaskReceiptCodec.RequireEvent(SelectionEventID, "selection predecessor");
        RepositoryLoopTaskReceiptCodec.RequireSHA(SelectionReceiptSHA256, "selection receipt");
        if (!Enum.IsDefined(TaskSpecies) || SelectionOrdinal < 0 || !Enum.IsDefined(CandidateSpecies) || !CandidateDigest.IsValid || !FrontierRevision.IsValid)
            throw new InvalidDataException("repository task action candidate authority is malformed");
        if (!RepositoryCandidate.TryParseCanonical(CandidateCanonical, out RepositoryCandidate candidate)
            || candidate.Species != CandidateSpecies || candidate.Digest != CandidateDigest)
            throw new InvalidDataException("repository task action candidate identity diverges");
        if (!RepositoryLoopTaskSpeciesRules.MatchesCandidate(TaskSpecies, CandidateSpecies))
            throw new InvalidDataException("repository task action candidate does not match task species");
        RepositoryLoopTaskReceiptCodec.RequireSHA(FrontierAuthoritySHA256, "frontier authority");
        RepositoryLoopTaskReceiptCodec.RequireSHA(CallSHA256, "action tool call");
        string expectedCall = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Tool.ToolCall.Create(candidate.Verb, candidate.Argument).Raw)));
        if (CallSHA256 != expectedCall) throw new InvalidDataException("repository task action call authority diverges");
        RepositoryLoopTaskReceiptCodec.RequireSHA(ReceiptSHA256, "action receipt");
        if (ReceiptSHA256 != RepositoryLoopTaskReceiptCodec.Digest(RepositoryLoopTaskReceiptCodec.ActionPrefix, Canonical))
            throw new InvalidDataException("repository task action receipt digest diverges");
    }
}

/// The oracle occurrenceCheck for one selected repository action. This is not the
/// generic RepositoryOccurrenceCheckReceipt: it carries task/oracle custody and the
/// action packet digest that admitted the occurrenceCheck.
public readonly record struct RepositoryLoopTaskOccurrenceCheckReceipt(
    string TaskID,
    RepositoryLoopClosureTaskSpecies TaskSpecies,
    RepositoryLoopClosureTaskOracleModes OracleMode,
    TapeEventID ActionEventID,
    string ActionPayloadSHA256,
    RepositoryOccurrenceCheckOutcomes Outcome,
    string OracleSHA256,
    RepositoryPrediction? Prediction,
    string TypedPredictionReceiptSHA256,
    string WorldSHA256,
    string AccessSHA256,
    long EvaluatorCost,
    long AccessCost,
    long AccessSequence,
    string AccessEntrySHA256,
    int AccessEntryCount,
    string CallSHA256,
    string EvidenceSHA256,
    string TaskAuthoritySHA256,
    string ReceiptSHA256) : IRepositoryLineageReceipt
{
    // Frozen journal row kind; identifier-side name is OccurrenceCheck.
    public string Kind => "repository-verification";
    public string Canonical => RepositoryLoopTaskReceiptCodec.Join(
        TaskID, TaskSpecies.ToString(), OracleMode.ToString(), ActionEventID.Value.ToString(CultureInfo.InvariantCulture),
        ActionPayloadSHA256, Outcome.ToString(), OracleSHA256, Prediction?.Canonical ?? "none", TypedPredictionReceiptSHA256,
        WorldSHA256, AccessSHA256, EvaluatorCost.ToString(CultureInfo.InvariantCulture), AccessCost.ToString(CultureInfo.InvariantCulture),
        AccessSequence.ToString(CultureInfo.InvariantCulture), AccessEntrySHA256, AccessEntryCount.ToString(CultureInfo.InvariantCulture),
        CallSHA256, EvidenceSHA256, TaskAuthoritySHA256);

    public static RepositoryLoopTaskOccurrenceCheckReceipt Create(
        string taskID,
        RepositoryLoopClosureTaskSpecies taskSpecies,
        in RepositoryLoopClosureTaskOccurrenceCheck occurrenceCheck,
        TapeEventID actionEventID,
        string actionPayloadSHA256,
        string taskAuthoritySHA256)
    {
        RepositoryLoopTaskOccurrenceCheckReceipt value = new(taskID, taskSpecies, occurrenceCheck.Mode, actionEventID,
            actionPayloadSHA256, occurrenceCheck.Outcome, occurrenceCheck.OracleSHA256, occurrenceCheck.Prediction,
            occurrenceCheck.TypedPredictionReceipt?.ReceiptSHA256 ?? "none", occurrenceCheck.WorldSHA256, occurrenceCheck.AccessSHA256,
            occurrenceCheck.EvaluatorCost, occurrenceCheck.AccessCost, occurrenceCheck.AccessSequence, occurrenceCheck.AccessEntrySHA256,
            occurrenceCheck.AccessEntryCount, occurrenceCheck.CallSHA256, occurrenceCheck.EvidenceSHA256, taskAuthoritySHA256, "");
        return value with { ReceiptSHA256 = RepositoryLoopTaskReceiptCodec.Digest(RepositoryLoopTaskReceiptCodec.OccurrenceCheckPrefix, value.Canonical) };
    }

    public byte[] Encode()
    {
        Validate();
        return RepositoryLoopTaskReceiptCodec.Encode(RepositoryLoopTaskReceiptCodec.OccurrenceCheckPrefix, Canonical, ReceiptSHA256);
    }

    public static RepositoryLoopTaskOccurrenceCheckReceipt Decode(ReadOnlySpan<byte> payload)
    {
        if (!RepositoryLoopTaskReceiptCodec.TryDecode(payload, RepositoryLoopTaskReceiptCodec.OccurrenceCheckPrefix,
                out string canonical, out string digest)
            || !RepositoryLineageReceiptCodec.TrySplit(canonical, out string[] fields) || fields.Length != 19)
            throw new InvalidDataException("repository task occurrence check packet is malformed");
        try
        {
            if (!Enum.TryParse(fields[1], out RepositoryLoopClosureTaskSpecies taskSpecies)
                || !Enum.TryParse(fields[2], out RepositoryLoopClosureTaskOracleModes oracleMode)
                || !long.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long actionEvent)
                || !Enum.TryParse(fields[5], out RepositoryOccurrenceCheckOutcomes outcome)
                || !long.TryParse(fields[11], NumberStyles.Integer, CultureInfo.InvariantCulture, out long evaluator)
                || !long.TryParse(fields[12], NumberStyles.Integer, CultureInfo.InvariantCulture, out long accessCost)
                || !long.TryParse(fields[13], NumberStyles.Integer, CultureInfo.InvariantCulture, out long accessSequence)
                || !int.TryParse(fields[15], NumberStyles.Integer, CultureInfo.InvariantCulture, out int accessCount))
                throw new InvalidDataException("repository task occurrence check packet numeric field is malformed");
            RepositoryPrediction? prediction = null;
            if (fields[7] != "none")
            {
                if (!RepositoryPrediction.TryParse(fields[7], out RepositoryPrediction parsed))
                    throw new InvalidDataException("repository task occurrence check prediction is malformed");
                prediction = parsed;
            }
            RepositoryLoopTaskOccurrenceCheckReceipt value = new(fields[0], taskSpecies, oracleMode, new TapeEventID(actionEvent), fields[4], outcome,
                fields[6], prediction, fields[8], fields[9], fields[10], evaluator, accessCost, accessSequence, fields[14], accessCount, fields[16], fields[17], fields[18], digest);
            value.Validate();
            return value;
        }
        catch (FormatException error) { throw new InvalidDataException("repository task occurrence check packet is malformed", error); }
        catch (OverflowException error) { throw new InvalidDataException("repository task occurrence check packet is malformed", error); }
    }

    public static bool TryDecode(ReadOnlySpan<byte> payload, out RepositoryLoopTaskOccurrenceCheckReceipt receipt)
    {
        try { receipt = Decode(payload); return true; }
        catch (Exception error) when (error is InvalidDataException or FormatException or OverflowException or ArgumentException)
        { receipt = default; return false; }
    }

    public void Validate()
    {
        RepositoryLoopTaskReceiptCodec.RequireText(TaskID, "task id");
        RepositoryLoopTaskReceiptCodec.RequireSHA(TaskAuthoritySHA256, "task authority");
        if (!Enum.IsDefined(TaskSpecies) || !Enum.IsDefined(OracleMode) || !Enum.IsDefined(Outcome))
            throw new InvalidDataException("repository task occurrence check species is malformed");
        RepositoryLoopTaskReceiptCodec.RequireEvent(ActionEventID, "action predecessor");
        RepositoryLoopTaskReceiptCodec.RequireSHA(ActionPayloadSHA256, "action payload");
        RepositoryLoopTaskReceiptCodec.RequireSHA(OracleSHA256, "oracle authority");
        RepositoryLoopTaskReceiptCodec.RequireSHA(WorldSHA256, "world authority");
        RepositoryLoopTaskReceiptCodec.RequireSHA(AccessSHA256, "access authority");
        RepositoryLoopTaskReceiptCodec.RequireSHA(CallSHA256, "tool call");
        RepositoryLoopTaskReceiptCodec.RequireSHA(EvidenceSHA256, "occurrence check evidence");
        if (EvaluatorCost < 0 || AccessCost < 0 || AccessEntryCount < 0)
            throw new InvalidDataException("repository task occurrence check costs are malformed");
        if (Outcome == RepositoryOccurrenceCheckOutcomes.Unobserved)
        {
            if (AccessSequence != -1 || AccessEntrySHA256.Length != 0) throw new InvalidDataException("repository task unobserved occurrence check carries access");
        }
        else if (TaskSpecies == RepositoryLoopClosureTaskSpecies.Answer)
        {
            if (AccessSequence != -1 || AccessEntrySHA256.Length != 0)
                throw new InvalidDataException("repository task answer occurrence check fabricates access");
        }
        else if (AccessSequence < 0 || AccessSequence >= AccessEntryCount || !RepositoryLoopTaskReceiptCodec.IsSHA(AccessEntrySHA256))
            throw new InvalidDataException("repository task occurrence check access authority is malformed");
        if (OracleMode == RepositoryLoopClosureTaskOracleModes.TypedPrediction)
        {
            if (Prediction is null
                || Outcome != RepositoryOccurrenceCheckOutcomes.Unobserved
                    && !RepositoryLoopTaskReceiptCodec.IsSHA(TypedPredictionReceiptSHA256)
                || Outcome == RepositoryOccurrenceCheckOutcomes.Unobserved
                    && TypedPredictionReceiptSHA256 != "none"
                    && !RepositoryLoopTaskReceiptCodec.IsSHA(TypedPredictionReceiptSHA256))
                throw new InvalidDataException("repository task typed occurrence check omits prediction custody");
        }
        else if (Prediction is not null || TypedPredictionReceiptSHA256 != "none")
            throw new InvalidDataException("repository task source occurrence check carries typed prediction custody");
        Prediction?.Validate();
        RepositoryLoopTaskReceiptCodec.RequireSHA(ReceiptSHA256, "occurrence check receipt");
        if (ReceiptSHA256 != RepositoryLoopTaskReceiptCodec.Digest(RepositoryLoopTaskReceiptCodec.OccurrenceCheckPrefix, Canonical))
            throw new InvalidDataException("repository task occurrence check receipt digest diverges");
    }
}

/// The ordinary source-backed result produced after a task occurrenceCheck. Its
/// predecessor is the occurrenceCheck event; no adjudicator may mint this record.
public readonly record struct RepositoryLoopTaskOutcomeReceipt(
    string TaskID,
    RepositoryLoopClosureTaskSpecies TaskSpecies,
    RepositoryOccurrenceCheckOutcomes VerifierOutcome,
    TapeEventID OccurrenceCheckEventID,
    string OccurrenceCheckPayloadSHA256,
    RepositoryCandidateSpecies CandidateSpecies,
    string CandidateCanonical,
    RepositoryCandidateDigest CandidateDigest,
    RepositoryLoopClosureResultSpecies ResultSpecies,
    string SourcePath,
    int SourceLine,
    long SourceBytes,
    string SourceSHA256,
    ReadOnlyMemory<byte> ResultContent,
    string ResultSHA256,
    string TaskAuthoritySHA256,
    string ReceiptSHA256) : IRepositoryLineageReceipt
{
    public string Kind => "repository-outcome";
    public string Canonical => RepositoryLoopTaskReceiptCodec.Join(
        TaskID, TaskSpecies.ToString(), VerifierOutcome.ToString(), OccurrenceCheckEventID.Value.ToString(CultureInfo.InvariantCulture), OccurrenceCheckPayloadSHA256,
        CandidateSpecies.ToString(), CandidateCanonical, CandidateDigest.Value.ToString(CultureInfo.InvariantCulture), ResultSpecies.ToString(),
        SourcePath, SourceLine.ToString(CultureInfo.InvariantCulture), SourceBytes.ToString(CultureInfo.InvariantCulture), SourceSHA256,
        ResultSHA256, Convert.ToBase64String(ResultContent.ToArray()), TaskAuthoritySHA256);

    public static RepositoryLoopTaskOutcomeReceipt Create(
        string taskID,
        RepositoryLoopClosureTaskSpecies taskSpecies,
        RepositoryOccurrenceCheckOutcomes occurrenceCheckOutcome,
        TapeEventID occurrenceCheckEventID,
        string occurrenceCheckPayloadSHA256,
        RepositoryCandidate candidate,
        RepositoryLoopClosureResultSpecies resultSpecies,
        string sourcePath,
        int sourceLine,
        long sourceBytes,
        string sourceSHA256,
        ReadOnlyMemory<byte> resultContent,
        string taskAuthoritySHA256)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        byte[] owned = resultContent.ToArray();
        RepositoryLoopTaskOutcomeReceipt value = new(taskID, taskSpecies, occurrenceCheckOutcome, occurrenceCheckEventID, occurrenceCheckPayloadSHA256,
            candidate.Species, candidate.Canonical, candidate.Digest, resultSpecies, sourcePath, sourceLine, sourceBytes, sourceSHA256,
            owned, Convert.ToHexStringLower(SHA256.HashData(owned)), taskAuthoritySHA256, "");
        return value with { ReceiptSHA256 = RepositoryLoopTaskReceiptCodec.Digest(RepositoryLoopTaskReceiptCodec.OutcomePrefix, value.Canonical) };
    }

    public byte[] Encode()
    {
        Validate();
        return RepositoryLoopTaskReceiptCodec.Encode(RepositoryLoopTaskReceiptCodec.OutcomePrefix, Canonical, ReceiptSHA256);
    }

    public static RepositoryLoopTaskOutcomeReceipt Decode(ReadOnlySpan<byte> payload)
    {
        if (!RepositoryLoopTaskReceiptCodec.TryDecode(payload, RepositoryLoopTaskReceiptCodec.OutcomePrefix,
                out string canonical, out string digest)
            || !RepositoryLineageReceiptCodec.TrySplit(canonical, out string[] fields) || fields.Length != 16)
            throw new InvalidDataException("repository task outcome packet is malformed");
        try
        {
            if (!Enum.TryParse(fields[1], out RepositoryLoopClosureTaskSpecies taskSpecies)
                || !Enum.TryParse(fields[2], out RepositoryOccurrenceCheckOutcomes occurrenceCheckOutcome)
                || !long.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long occurrenceCheckEvent)
                || !Enum.TryParse(fields[5], out RepositoryCandidateSpecies candidateSpecies)
                || !ulong.TryParse(fields[7], NumberStyles.None, CultureInfo.InvariantCulture, out ulong candidateDigest)
                || !Enum.TryParse(fields[8], out RepositoryLoopClosureResultSpecies resultSpecies)
                || !int.TryParse(fields[10], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sourceLine)
                || !long.TryParse(fields[11], NumberStyles.Integer, CultureInfo.InvariantCulture, out long sourceBytes))
                throw new InvalidDataException("repository task outcome packet numeric field is malformed");
            byte[] content = Convert.FromBase64String(fields[14]);
            RepositoryLoopTaskOutcomeReceipt value = new(fields[0], taskSpecies, occurrenceCheckOutcome, new TapeEventID(occurrenceCheckEvent), fields[4], candidateSpecies,
                fields[6], new RepositoryCandidateDigest(candidateDigest), resultSpecies, fields[9], sourceLine, sourceBytes, fields[12], content, fields[13], fields[15], digest);
            value.Validate();
            return value;
        }
        catch (FormatException error) { throw new InvalidDataException("repository task outcome packet is malformed", error); }
        catch (OverflowException error) { throw new InvalidDataException("repository task outcome packet is malformed", error); }
    }

    public static bool TryDecode(ReadOnlySpan<byte> payload, out RepositoryLoopTaskOutcomeReceipt receipt)
    {
        try { receipt = Decode(payload); return true; }
        catch (Exception error) when (error is InvalidDataException or FormatException or OverflowException or ArgumentException)
        { receipt = default; return false; }
    }

    public void Validate()
    {
        RepositoryLoopTaskReceiptCodec.RequireText(TaskID, "task id");
        RepositoryLoopTaskReceiptCodec.RequireSHA(TaskAuthoritySHA256, "task authority");
        if (!Enum.IsDefined(TaskSpecies) || !Enum.IsDefined(VerifierOutcome) || !Enum.IsDefined(CandidateSpecies) || !Enum.IsDefined(ResultSpecies))
            throw new InvalidDataException("repository task outcome species is malformed");
        RepositoryLoopTaskReceiptCodec.RequireEvent(OccurrenceCheckEventID, "occurrence check predecessor");
        RepositoryLoopTaskReceiptCodec.RequireSHA(OccurrenceCheckPayloadSHA256, "occurrence check payload");
        if (!RepositoryCandidate.TryParseCanonical(CandidateCanonical, out RepositoryCandidate candidate)
            || candidate.Species != CandidateSpecies || candidate.Digest != CandidateDigest)
            throw new InvalidDataException("repository task outcome candidate identity diverges");
        if (!RepositoryLoopTaskSpeciesRules.MatchesCandidate(TaskSpecies, CandidateSpecies)
            || !RepositoryLoopTaskSpeciesRules.MatchesResult(TaskSpecies, ResultSpecies))
            throw new InvalidDataException("repository task outcome species does not match task");
        bool observed = VerifierOutcome != RepositoryOccurrenceCheckOutcomes.Unobserved;
        if (SourceLine < 0 || SourceBytes < 0) throw new InvalidDataException("repository task outcome source locus is malformed");
        if (observed)
        {
            RepositoryLoopTaskReceiptCodec.RequireText(SourcePath, "source path");
            RepositoryLoopTaskReceiptCodec.RequireSHA(SourceSHA256, "source bytes");
            if (ResultContent.Length == 0) throw new InvalidDataException("repository task outcome result is empty");
        }
        else if (SourcePath.Length != 0 || SourceBytes != 0 || SourceSHA256.Length != 0 || ResultContent.Length != 0)
            throw new InvalidDataException("repository task unobserved outcome carries source evidence");
        if (ResultSHA256 != Convert.ToHexStringLower(SHA256.HashData(ResultContent.Span)))
            throw new InvalidDataException("repository task outcome result bytes diverge");
        RepositoryLoopTaskReceiptCodec.RequireSHA(ReceiptSHA256, "outcome receipt");
        if (ReceiptSHA256 != RepositoryLoopTaskReceiptCodec.Digest(RepositoryLoopTaskReceiptCodec.OutcomePrefix, Canonical))
            throw new InvalidDataException("repository task outcome receipt digest diverges");
    }
}
