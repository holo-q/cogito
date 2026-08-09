namespace Cogito;

using System.Security.Cryptography;
using System.Text;
using Cogito.Grammar;
using Ronmamon;

/// Stable identity for a rung-0 composition episode. The episode is a typed join key,
/// never a journal label: its event set is the packet authority that the later fold must consume.
public readonly record struct LoopClosureCompositionEpisodeID(string Value)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(Value);
    public override string ToString() => Value;
}

/// One closed displaced-evaluation episode and the evidence events that prove it.
public readonly struct LoopClosureCompositionEpisode
{
    public LoopClosureCompositionEpisode(
        LoopClosureCompositionEpisodeID episodeID,
        TapeEventID compositionEventID,
        IReadOnlyList<TapeEventID> evidenceEventIDs,
        GrammarRevisionID preFoldRevision,
        LoopClosureDigest evidenceDigest,
        LoopClosureDigest episodeDigest)
    {
        EpisodeID = episodeID;
        CompositionEventID = compositionEventID;
        EvidenceEventIDs = evidenceEventIDs?.ToArray() ?? throw new ArgumentNullException(nameof(evidenceEventIDs));
        PreFoldRevision = preFoldRevision;
        EvidenceDigest = evidenceDigest;
        EpisodeDigest = episodeDigest;
        Validate();
    }

    public LoopClosureCompositionEpisodeID EpisodeID { get; }
    public TapeEventID CompositionEventID { get; }
    public TapeEventID[] EvidenceEventIDs { get; }
    public GrammarRevisionID PreFoldRevision { get; }
    public LoopClosureDigest EvidenceDigest { get; }
    public LoopClosureDigest EpisodeDigest { get; }

    public static LoopClosureCompositionEpisode Create(
        LoopClosureCompositionEpisodeID episodeID,
        TapeEventID compositionEventID,
        IReadOnlyList<TapeEventID> evidenceEventIDs,
        GrammarRevisionID preFoldRevision)
    {
        TapeEventID[] evidence = NormalizeEventIDs(evidenceEventIDs);
        LoopClosureDigest evidenceDigest = new(ComputeEventDigest(evidence));
        string canonical = CanonicalEpisode(episodeID, compositionEventID, evidence, preFoldRevision, evidenceDigest);
        return new(episodeID, compositionEventID, evidence, preFoldRevision, evidenceDigest,
            new LoopClosureDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))));
    }

    public void Validate()
    {
        if (!EpisodeID.IsValid || CompositionEventID.Value < 0 || PreFoldRevision.Value == 0)
            throw new InvalidDataException("loop-closure derivation episode identity is malformed");
        if (EvidenceEventIDs.Length == 0 || !EvidenceEventIDs.SequenceEqual(NormalizeEventIDs(EvidenceEventIDs)))
            throw new InvalidDataException("loop-closure derivation episode evidence IDs are not canonical");
        if (!EvidenceDigest.IsValid || !string.Equals(EvidenceDigest.Value, ComputeEventDigest(EvidenceEventIDs), StringComparison.Ordinal))
            throw new InvalidDataException("loop-closure derivation episode evidence digest does not match its event set");
        string expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalEpisode(EpisodeID, CompositionEventID, EvidenceEventIDs, PreFoldRevision, EvidenceDigest))));
        if (!EpisodeDigest.IsValid || !string.Equals(EpisodeDigest.Value, expected, StringComparison.Ordinal))
            throw new InvalidDataException("loop-closure derivation episode digest does not match its typed payload");
    }

    internal static TapeEventID[] NormalizeEventIDs(IReadOnlyList<TapeEventID> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        TapeEventID[] result = values.ToArray();
        Array.Sort(result, static (left, right) => left.Value.CompareTo(right.Value));
        for (int index = 0; index < result.Length; index++)
        {
            if (result[index].Value < 0 || (index > 0 && result[index] == result[index - 1]))
                throw new InvalidDataException("loop-closure provenance event IDs must be non-negative and unique");
        }
        return result;
    }

    internal static string ComputeEventDigest(IReadOnlyList<TapeEventID> values)
    {
        TapeEventID[] normalized = NormalizeEventIDs(values);
        StringBuilder canonical = new("event-set|");
        for (int index = 0; index < normalized.Length; index++) canonical.Append(normalized[index].Value).Append('|');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static string CanonicalEpisode(
        LoopClosureCompositionEpisodeID episodeID,
        TapeEventID compositionEventID,
        IReadOnlyList<TapeEventID> evidenceEventIDs,
        GrammarRevisionID preFoldRevision,
        LoopClosureDigest evidenceDigest)
    {
        StringBuilder canonical = new("episode|");
        canonical.Append(episodeID.Value).Append('|').Append(compositionEventID.Value).Append('|').Append(preFoldRevision.Value).Append('|').Append(evidenceDigest.Value).Append('|');
        TapeEventID[] normalized = NormalizeEventIDs(evidenceEventIDs);
        for (int index = 0; index < normalized.Length; index++) canonical.Append(normalized[index].Value).Append('|');
        return canonical.ToString();
    }
}

/// Grammar publication provenance: the exact event set folded into a revision and its predecessor.
public readonly struct GrammarFoldProvenanceReceipt
{
    public GrammarFoldProvenanceReceipt(
        GrammarRevisionID previousRevision,
        GrammarRevisionID revision,
        IReadOnlyList<TapeEventID> consumedEventIDs,
        IReadOnlyList<LoopClosureDigest> compositionEpisodeDigests,
        LoopClosureDigest consumedEventDigest,
        LoopClosureDigest receiptDigest)
    {
        PreviousRevision = previousRevision;
        Revision = revision;
        ConsumedEventIDs = consumedEventIDs?.ToArray() ?? throw new ArgumentNullException(nameof(consumedEventIDs));
        CompositionEpisodeDigests = compositionEpisodeDigests?.ToArray() ?? throw new ArgumentNullException(nameof(compositionEpisodeDigests));
        ConsumedEventDigest = consumedEventDigest;
        ReceiptDigest = receiptDigest;
        Validate();
    }

    public GrammarRevisionID PreviousRevision { get; }
    public GrammarRevisionID ParentRevision => PreviousRevision;
    public GrammarRevisionID Revision { get; }
    public TapeEventID[] ConsumedEventIDs { get; }
    public TapeEventID FirstConsumedEventID => ConsumedEventIDs[0];
    public TapeEventID LastConsumedEventID => ConsumedEventIDs[^1];
    public LoopClosureDigest ConsumedEventDigest { get; }
    public LoopClosureDigest[] CompositionEpisodeDigests { get; }
    public LoopClosureDigest ReceiptDigest { get; }

    public static GrammarFoldProvenanceReceipt Create(
        GrammarRevisionID previousRevision,
        GrammarRevisionID revision,
        IReadOnlyList<TapeEventID> consumedEventIDs,
        IReadOnlyList<LoopClosureCompositionEpisode> episodes)
    {
        TapeEventID[] consumed = LoopClosureCompositionEpisode.NormalizeEventIDs(consumedEventIDs);
        LoopClosureDigest[] episodeDigests = episodes?.Select(static episode => { episode.Validate(); return episode.EpisodeDigest; }).ToArray()
            ?? throw new ArgumentNullException(nameof(episodes));
        LoopClosureDigest consumedDigest = new(LoopClosureCompositionEpisode.ComputeEventDigest(consumed));
        string canonical = CanonicalReceipt(previousRevision, revision, consumed, episodeDigests, consumedDigest);
        return new(previousRevision, revision, consumed, episodeDigests, consumedDigest,
            new LoopClosureDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))));
    }

    public void Validate()
    {
        if (!(Revision > PreviousRevision) || Revision == GrammarRevisionID.Zero)
            throw new InvalidDataException("grammar fold provenance revision must strictly follow its predecessor");
        if (ConsumedEventIDs.Length == 0 || !ConsumedEventIDs.SequenceEqual(LoopClosureCompositionEpisode.NormalizeEventIDs(ConsumedEventIDs)))
            throw new InvalidDataException("grammar fold provenance consumed event IDs are not canonical");
        if (!ConsumedEventDigest.IsValid || !string.Equals(ConsumedEventDigest.Value, LoopClosureCompositionEpisode.ComputeEventDigest(ConsumedEventIDs), StringComparison.Ordinal))
            throw new InvalidDataException("grammar fold provenance consumed event digest does not match its event set");
        if (CompositionEpisodeDigests.Length == 0 || CompositionEpisodeDigests.Any(static digest => !digest.IsValid))
            throw new InvalidDataException("grammar fold provenance omits derivation episode digests");
        string expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalReceipt(PreviousRevision, Revision, ConsumedEventIDs, CompositionEpisodeDigests, ConsumedEventDigest))));
        if (!ReceiptDigest.IsValid || !string.Equals(ReceiptDigest.Value, expected, StringComparison.Ordinal))
            throw new InvalidDataException("grammar fold provenance receipt digest does not match its typed payload");
    }

    private static string CanonicalReceipt(GrammarRevisionID previousRevision, GrammarRevisionID revision, IReadOnlyList<TapeEventID> consumed, IReadOnlyList<LoopClosureDigest> episodes, LoopClosureDigest consumedDigest)
    {
        StringBuilder canonical = new("fold|");
        canonical.Append(previousRevision.Value).Append('|').Append(revision.Value).Append('|').Append(consumedDigest.Value).Append('|');
        for (int index = 0; index < consumed.Count; index++) canonical.Append(consumed[index].Value).Append('|');
        for (int index = 0; index < episodes.Count; index++) canonical.Append(episodes[index].Value).Append('|');
        return canonical.ToString();
    }
}

/// Provenance appended to a canonical teacher packet. These are the matched event IDs,
/// not an audit string, so a reader can prove the packet carries the fold corroboration.
public readonly struct LoopClosureTeacherPacketProvenance
{
    public LoopClosureTeacherPacketProvenance(
        LoopClosureCompositionEpisodeID episodeID,
        GrammarRevisionID foldRevision,
        IReadOnlyList<TapeEventID> matchedEventIDs,
        LoopClosureDigest evidenceDigest,
        LoopClosureDigest corroborationDigest,
        LoopClosureDigest provenanceDigest)
    {
        EpisodeID = episodeID;
        FoldRevision = foldRevision;
        MatchedEventIDs = matchedEventIDs?.ToArray() ?? throw new ArgumentNullException(nameof(matchedEventIDs));
        EvidenceDigest = evidenceDigest;
        CorroborationDigest = corroborationDigest;
        ProvenanceDigest = provenanceDigest;
        Validate();
    }

    public LoopClosureCompositionEpisodeID EpisodeID { get; }
    public GrammarRevisionID FoldRevision { get; }
    public TapeEventID[] MatchedEventIDs { get; }
    public LoopClosureDigest EvidenceDigest { get; }
    public LoopClosureDigest CorroborationDigest { get; }
    public LoopClosureDigest ProvenanceDigest { get; }

    public static LoopClosureTeacherPacketProvenance Create(LoopClosureCompositionEpisodeID episodeID, GrammarRevisionID foldRevision, IReadOnlyList<TapeEventID> matchedEventIDs, LoopClosureDigest evidenceDigest)
    {
        TapeEventID[] ids = LoopClosureCompositionEpisode.NormalizeEventIDs(matchedEventIDs);
        if (!evidenceDigest.IsValid) throw new InvalidDataException("teacher packet evidence digest is malformed");
        // Frozen digest token teacher-witness; identifier-side name is Corroboration.
        StringBuilder corroboration = new("teacher-witness|");
        corroboration.Append(episodeID.Value).Append('|').Append(foldRevision.Value).Append('|').Append(evidenceDigest.Value).Append('|');
        for (int index = 0; index < ids.Length; index++) corroboration.Append(ids[index].Value).Append('|');
        LoopClosureDigest corroborationDigest = new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(corroboration.ToString()))));
        string canonical = CanonicalTeacher(episodeID, foldRevision, ids, evidenceDigest, corroborationDigest);
        return new(episodeID, foldRevision, ids, evidenceDigest, corroborationDigest,
            new LoopClosureDigest(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))));
    }

    public bool ContainsAll(IReadOnlyList<TapeEventID> eventIDs)
    {
        for (int index = 0; index < eventIDs.Count; index++)
        {
            bool found = false;
            for (int candidate = 0; candidate < MatchedEventIDs.Length; candidate++)
                if (MatchedEventIDs[candidate] == eventIDs[index]) { found = true; break; }
            if (!found) return false;
        }
        return true;
    }

    public void Validate()
    {
        if (!EpisodeID.IsValid || FoldRevision == GrammarRevisionID.Zero || MatchedEventIDs.Length == 0 || !MatchedEventIDs.SequenceEqual(LoopClosureCompositionEpisode.NormalizeEventIDs(MatchedEventIDs)))
            throw new InvalidDataException("teacher packet provenance identity is malformed");
        if (!EvidenceDigest.IsValid || !CorroborationDigest.IsValid || !ProvenanceDigest.IsValid)
            throw new InvalidDataException("teacher packet provenance digest is malformed");
        // Frozen digest token teacher-witness; identifier-side name is Corroboration.
        StringBuilder corroboration = new("teacher-witness|");
        corroboration.Append(EpisodeID.Value).Append('|').Append(FoldRevision.Value).Append('|').Append(EvidenceDigest.Value).Append('|');
        for (int index = 0; index < MatchedEventIDs.Length; index++) corroboration.Append(MatchedEventIDs[index].Value).Append('|');
        string expectedCorroboration = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(corroboration.ToString())));
        if (!string.Equals(CorroborationDigest.Value, expectedCorroboration, StringComparison.Ordinal)) throw new InvalidDataException("teacher packet corroboration digest does not match");
        string expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalTeacher(EpisodeID, FoldRevision, MatchedEventIDs, EvidenceDigest, CorroborationDigest))));
        if (!string.Equals(ProvenanceDigest.Value, expected, StringComparison.Ordinal)) throw new InvalidDataException("teacher packet provenance digest does not match");
    }

    /// Canonical packet tail. It is appended after RAW-EVIDENCE so grammar matching still sees the same learner continuation.
    public string EncodePacketFields()
    {
        // Frozen wire field TEACHER-WITNESS; identifier-side name is CorroborationDigest.
        return $"\tFOLD-REVISION={FoldRevision.Value}\tTEACHER-EPISODE={EpisodeID.Value}\tTEACHER-EVENTS={string.Join(',', MatchedEventIDs.Select(static id => id.Value))}\tTEACHER-EVIDENCE={EvidenceDigest.Value}\tTEACHER-WITNESS={CorroborationDigest.Value}\tTEACHER-PROVENANCE={ProvenanceDigest.Value}";
    }

    /// Recover and validate the typed tail from an emitted canonical teacher packet.
    /// This is the custody check used by a reader; it never treats RAW-EVIDENCE as ancestry.
    public static LoopClosureTeacherPacketProvenance DecodePacketFields(ReadOnlySpan<byte> packet)
    {
        string text = Encoding.ASCII.GetString(packet);
        int start = text.IndexOf("\tFOLD-REVISION=", StringComparison.Ordinal);
        if (start < 0) throw new InvalidDataException("teacher packet omits fold provenance");
        string[] fields = text[start..].Split('\t', StringSplitOptions.RemoveEmptyEntries);
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int index = 0; index < fields.Length; index++)
        {
            int equals = fields[index].IndexOf('=');
            if (equals <= 0 || !values.TryAdd(fields[index][..equals], fields[index][(equals + 1)..]))
                throw new InvalidDataException("teacher packet provenance repeats or malforms a field");
        }
        if (!ulong.TryParse(values.GetValueOrDefault("FOLD-REVISION"), out ulong revision)
            || !values.TryGetValue("TEACHER-EPISODE", out string? episode)
            || !values.TryGetValue("TEACHER-EVENTS", out string? eventText)
            || !values.TryGetValue("TEACHER-EVIDENCE", out string? evidence)
            || !values.TryGetValue("TEACHER-WITNESS", out string? corroboration)
            || !values.TryGetValue("TEACHER-PROVENANCE", out string? provenance))
            throw new InvalidDataException("teacher packet provenance is incomplete");
        string[] ids = eventText.Split(',', StringSplitOptions.RemoveEmptyEntries);
        TapeEventID[] eventIDs = new TapeEventID[ids.Length];
        for (int index = 0; index < ids.Length; index++)
            if (!long.TryParse(ids[index], out long value)) throw new InvalidDataException("teacher packet carries an invalid event ID");
            else eventIDs[index] = new TapeEventID(value);
        return new(new LoopClosureCompositionEpisodeID(episode), new GrammarRevisionID(revision), eventIDs, new LoopClosureDigest(evidence), new LoopClosureDigest(corroboration), new LoopClosureDigest(provenance));
    }

    private static string CanonicalTeacher(LoopClosureCompositionEpisodeID episodeID, GrammarRevisionID foldRevision, IReadOnlyList<TapeEventID> ids, LoopClosureDigest evidence, LoopClosureDigest corroboration)
        => $"teacher|{episodeID.Value}|{foldRevision.Value}|{string.Join(',', ids.Select(static id => id.Value))}|{evidence.Value}|{corroboration.Value}";
}

/// Final R4 linkage. Validation is the custody gate consumed by the divergence/report verifier.
public readonly struct LoopClosureR4Provenance
{
    public LoopClosureR4Provenance(
        LoopClosureCompositionEpisode episode,
        GrammarFoldProvenanceReceipt fold,
        LoopClosureTeacherPacketProvenance teacher,
        ReadoutTrainingCorroboration training)
    {
        Episode = episode;
        Fold = fold;
        Teacher = teacher;
        Training = training;
        Validate();
    }

    public LoopClosureCompositionEpisode Episode { get; }
    public GrammarFoldProvenanceReceipt Fold { get; }
    public LoopClosureTeacherPacketProvenance Teacher { get; }
    public ReadoutTrainingCorroboration Training { get; }
    public GrammarRevisionID LearnedReadoutRevision => Training.SelectedCandidateRevision;
    public ulong ReadoutOccurrenceDigest => Training.SelectedCandidateOccurrenceDigest;

    public static LoopClosureR4Provenance Create(
        in LoopClosureCompositionEpisode episode,
        in GrammarFoldProvenanceReceipt fold,
        in LoopClosureTeacherPacketProvenance teacher,
        in ReadoutTrainingCorroboration training)
        => new(episode, fold, teacher, training);

    /// Durable RON authority for the complete R4 chain. The event IDs and digests are
    /// serialized as typed fields, so replay never reconstructs ancestry from journal prose.
    public byte[] Encode()
    {
        Validate();
        LoopClosureR4ProvenanceRON document = new()
        {
            schemaVersion = 3,
            episodeID = Episode.EpisodeID.Value,
            compositionEventID = Episode.CompositionEventID.Value,
            preFoldRevision = Episode.PreFoldRevision.Value,
            evidenceDigest = Episode.EvidenceDigest.Value,
            episodeDigest = Episode.EpisodeDigest.Value,
            previousRevision = Fold.PreviousRevision.Value,
            foldRevision = Fold.Revision.Value,
            consumedEventDigest = Fold.ConsumedEventDigest.Value,
            foldReceiptDigest = Fold.ReceiptDigest.Value,
            teacherFoldRevision = Teacher.FoldRevision.Value,
            teacherEvidenceDigest = Teacher.EvidenceDigest.Value,
            teacherWitnessDigest = Teacher.CorroborationDigest.Value,
            teacherProvenanceDigest = Teacher.ProvenanceDigest.Value,
            trainingPolicy = Training.Policy.Value,
            trainingTeacherPacketEventID = Training.TeacherPacketEventID.Value,
            trainingTeacherCompositionEventID = Training.TeacherCompositionEventID.Value,
            trainingTeacherEvidenceDigest = Training.TeacherEvidenceSHA256.Value,
            trainingSourceEpisodeID = Training.SourceEpisodeID.Value,
            trainingSourceEpisodeDigest = Training.SourceEpisodeSHA256.Value,
            trainingFoldPreviousRevision = Training.ConsumingFoldPreviousRevision.Value,
            trainingFoldRevision = Training.ConsumingFoldRevision.Value,
            trainingFoldConsumedEventDigest = Training.ConsumingFoldConsumedEventSHA256.Value,
            trainingFoldReceiptDigest = Training.ConsumingFoldReceiptSHA256.Value,
            trainingCanonicalPolicy = Training.CanonicalState.Policy.Value,
            trainingCanonicalKind = (byte)Training.CanonicalState.Kind,
            trainingCanonicalVersion = Training.CanonicalState.Version,
            trainingCanonicalValue = Training.CanonicalState.Value,
            trainingContextDigest = Training.ContextDigest,
            trainingContextActionCount = Training.ContextActionCount,
            trainingContextDeliberationDepth = Training.ContextDeliberationDepth,
            trainingCandidateFingerprint = Training.SelectedCandidateFingerprint,
            trainingCandidateOccurrenceDigest = Training.SelectedCandidateOccurrenceDigest,
            trainingCandidateRevision = Training.SelectedCandidateRevision.Value,
            trainingDecisionID = Training.DecisionID.Value,
            trainingDecisionEventID = Training.DecisionEventID.Value,
            trainingWitnessDigest = Training.ReadoutTrainingCorroborationSHA256.Value,
        };
        foreach (TapeEventID id in Episode.EvidenceEventIDs) document.evidenceEventIDs.Add(id.Value);
        foreach (LoopClosureDigest digest in Fold.CompositionEpisodeDigests) document.compositionEpisodeDigests.Add(digest.Value);
        foreach (TapeEventID id in Fold.ConsumedEventIDs) document.consumedEventIDs.Add(id.Value);
        foreach (TapeEventID id in Teacher.MatchedEventIDs) document.teacherEventIDs.Add(id.Value);
        foreach (TapeEventID id in Training.TeacherEvidenceEventIDs) document.trainingTeacherEvidenceEventIDs.Add(id.Value);
        foreach (TapeEventID id in Training.ConsumingFoldConsumedEventIDs) document.trainingFoldConsumedEventIDs.Add(id.Value);
        byte[] first = RonSerializer.SerializeToUtf8(in document);
        byte[] second = RonSerializer.SerializeToUtf8(in document);
        if (!first.AsSpan().SequenceEqual(second)) throw new InvalidDataException("R4 provenance RON encoding is nondeterministic");
        return first;
    }

    public static LoopClosureR4Provenance Decode(ReadOnlySpan<byte> bytes)
    {
        LoopClosureR4ProvenanceRON document = RonSerializer.Deserialize<LoopClosureR4ProvenanceRON>(bytes);
        if (document.schemaVersion != 3) throw new InvalidDataException("R4 provenance schema is unsupported");
        LoopClosureCompositionEpisode episode = new(
            new LoopClosureCompositionEpisodeID(document.episodeID),
            new TapeEventID(document.compositionEventID),
            document.evidenceEventIDs.Select(static value => new TapeEventID(value)).ToArray(),
            new GrammarRevisionID(document.preFoldRevision),
            new LoopClosureDigest(document.evidenceDigest),
            new LoopClosureDigest(document.episodeDigest));
        GrammarFoldProvenanceReceipt fold = new(
            new GrammarRevisionID(document.previousRevision),
            new GrammarRevisionID(document.foldRevision),
            document.consumedEventIDs.Select(static value => new TapeEventID(value)).ToArray(),
            document.compositionEpisodeDigests.Select(static value => new LoopClosureDigest(value)).ToArray(),
            new LoopClosureDigest(document.consumedEventDigest),
            new LoopClosureDigest(document.foldReceiptDigest));
        LoopClosureTeacherPacketProvenance teacher = new(
            new LoopClosureCompositionEpisodeID(document.episodeID),
            new GrammarRevisionID(document.teacherFoldRevision),
            document.teacherEventIDs.Select(static value => new TapeEventID(value)).ToArray(),
            new LoopClosureDigest(document.teacherEvidenceDigest),
            new LoopClosureDigest(document.teacherWitnessDigest),
            new LoopClosureDigest(document.teacherProvenanceDigest));
        PolicyCanonicalStateID canonicalState = new(
            new CortexPolicyID(document.trainingCanonicalPolicy),
            (PolicyCanonicalStateKinds)document.trainingCanonicalKind,
            document.trainingCanonicalVersion,
            document.trainingCanonicalValue);
        ReadoutTrainingCorroboration training = new(
            new CortexPolicyID(document.trainingPolicy),
            new TapeEventID(document.trainingTeacherPacketEventID),
            new TapeEventID(document.trainingTeacherCompositionEventID),
            document.trainingTeacherEvidenceEventIDs.Select(static value => new TapeEventID(value)).ToArray(),
            new LoopClosureDigest(document.trainingTeacherEvidenceDigest),
            new LoopClosureCompositionEpisodeID(document.trainingSourceEpisodeID),
            new LoopClosureDigest(document.trainingSourceEpisodeDigest),
            new GrammarRevisionID(document.trainingFoldPreviousRevision),
            new GrammarRevisionID(document.trainingFoldRevision),
            document.trainingFoldConsumedEventIDs.Select(static value => new TapeEventID(value)).ToArray(),
            new LoopClosureDigest(document.trainingFoldConsumedEventDigest),
            new LoopClosureDigest(document.trainingFoldReceiptDigest),
            in canonicalState,
            document.trainingContextDigest,
            document.trainingContextActionCount,
            document.trainingContextDeliberationDepth,
            document.trainingCandidateFingerprint,
            document.trainingCandidateOccurrenceDigest,
            new GrammarRevisionID(document.trainingCandidateRevision),
            new CortexPolicyDecisionID(document.trainingDecisionID),
            new TapeEventID(document.trainingDecisionEventID),
            new LoopClosureDigest(document.trainingWitnessDigest));
        LoopClosureR4Provenance provenance = new(episode, fold, teacher, training);
        if (!provenance.Encode().AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("R4 provenance RON round-trip changed bytes");
        return provenance;
    }

    public void Validate()
    {
        Episode.Validate(); Fold.Validate(); Teacher.Validate(); Training.Validate();
        if (!(Fold.Revision > Episode.PreFoldRevision))
            throw new InvalidDataException("R4 grammar fold does not postdate the derivation episode");
        bool episodeFolded = false;
        for (int index = 0; index < Fold.CompositionEpisodeDigests.Length; index++)
            if (Fold.CompositionEpisodeDigests[index].Value == Episode.EpisodeDigest.Value) { episodeFolded = true; break; }
        if (!episodeFolded)
            throw new InvalidDataException("R4 grammar fold omits the derivation episode digest");
        TapeEventID[] required = [Episode.CompositionEventID, .. Episode.EvidenceEventIDs];
        if (!Fold.ConsumedEventIDs.ContainsAll(required)
            || !Teacher.MatchedEventIDs.SequenceEqual(LoopClosureCompositionEpisode.NormalizeEventIDs(required)))
            throw new InvalidDataException("R4 provenance teacher event membership is not the exact derivation episode");
        if (Teacher.EpisodeID != Episode.EpisodeID
            || !(Teacher.FoldRevision < Fold.Revision)
            || Teacher.EvidenceDigest != Episode.EvidenceDigest)
            throw new InvalidDataException("R4 teacher packet does not predate and bind the consuming grammar fold");
        if (!(Training.SelectedCandidateRevision > Fold.Revision))
            throw new InvalidDataException("R4 learned readout revision must strictly postdate the grammar fold");
        if (Training.SelectedCandidateOccurrenceDigest == 0) throw new InvalidDataException("R4 learned readout omits support digest");
        if (!Training.Policy.Equals(Training.CanonicalState.Policy))
            throw new InvalidDataException("R4 readout training corroboration policy is malformed");
        if (Training.SourceEpisodeID.Value != Episode.EpisodeID.Value
            || Training.SourceEpisodeSHA256 != Episode.EpisodeDigest
            || Training.ConsumingFoldRevision != Fold.Revision
            || Training.ConsumingFoldPreviousRevision != Fold.PreviousRevision
            || Training.ConsumingFoldReceiptSHA256 != Fold.ReceiptDigest
            || Training.ConsumingFoldConsumedEventSHA256 != Fold.ConsumedEventDigest
            || Training.TeacherPacketEventID.Value < 0
            || !Training.ConsumingFoldConsumedEventIDs.SequenceEqual(Fold.ConsumedEventIDs))
            throw new InvalidDataException("R4 readout training corroboration is not the selected episode/fold");
    }
}

[RonObject]
// Frozen RON field names retain witness vocabulary; identifier-side name is Corroboration.
internal partial class LoopClosureR4ProvenanceRON
{
    public int schemaVersion;
    public string episodeID = "";
    public long compositionEventID;
    public List<long> evidenceEventIDs = new();
    public ulong preFoldRevision;
    public string evidenceDigest = "";
    public string episodeDigest = "";
    public ulong previousRevision;
    public ulong foldRevision;
    public List<long> consumedEventIDs = new();
    public string consumedEventDigest = "";
    public List<string> compositionEpisodeDigests = new();
    public string foldReceiptDigest = "";
    public ulong teacherFoldRevision;
    public List<long> teacherEventIDs = new();
    public string teacherEvidenceDigest = "";
    public string teacherWitnessDigest = "";
    public string teacherProvenanceDigest = "";
    public string trainingPolicy = "";
    public long trainingTeacherPacketEventID;
    public long trainingTeacherCompositionEventID;
    public List<long> trainingTeacherEvidenceEventIDs = new();
    public string trainingTeacherEvidenceDigest = "";
    public string trainingSourceEpisodeID = "";
    public string trainingSourceEpisodeDigest = "";
    public ulong trainingFoldPreviousRevision;
    public ulong trainingFoldRevision;
    public List<long> trainingFoldConsumedEventIDs = new();
    public string trainingFoldConsumedEventDigest = "";
    public string trainingFoldReceiptDigest = "";
    public string trainingCanonicalPolicy = "";
    public byte trainingCanonicalKind;
    public ushort trainingCanonicalVersion;
    public ulong trainingCanonicalValue;
    public ulong trainingContextDigest;
    public int trainingContextActionCount;
    public int trainingContextDeliberationDepth;
    public ulong trainingCandidateFingerprint;
    public ulong trainingCandidateOccurrenceDigest;
    public ulong trainingCandidateRevision;
    public ulong trainingDecisionID;
    public long trainingDecisionEventID;
    public string trainingWitnessDigest = "";
}

internal static class LoopClosureEventIDExtensions
{
    public static bool ContainsAll(this IReadOnlyList<TapeEventID> source, IReadOnlyList<TapeEventID> required)
    {
        for (int index = 0; index < required.Count; index++)
            if (!source.Contains(required[index])) return false;
        return true;
    }
}
