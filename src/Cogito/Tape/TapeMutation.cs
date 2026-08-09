namespace Cogito;

/// Monotonic mutation position for the live tape. Revisions are runtime receipts;
/// they are deliberately not checkpoint state because the tape's bytes and ids are
/// the durable source of record.
public readonly record struct TapeRevision(long Value)
{
    public static TapeRevision Initial => new(0);
    public override string ToString() => $"r{Value}";
}

internal readonly record struct TapeMutationCursor(
    long Revision,
    long OrderRevision,
    long ReportedOrderRevision,
    long NextID,
    int ResidentCount,
    int PendingAppends,
    int PendingReflections,
    int PendingShed,
    int PendingDropped)
{
    public override string ToString() =>
        $"rev={Revision} order={OrderRevision}/{ReportedOrderRevision} next={NextID} residents={ResidentCount} pending={PendingAppends}/{PendingReflections}/{PendingShed}/{PendingDropped}";
}

/// Explicit resident/view transitions accumulated by Tape mutation verbs. The
/// arrays name the exact stable ids touched by each transition; consumers never
/// need to scan the tape to infer a delta.
public readonly record struct TapeDelta(
    TapeRevision Revision,
    TapeRevision OrderRevision,
    TapeEventID[] Appended,
    TapeEventID[] Reflected,
    TapeEventID[] Shed,
    TapeEventID[] Dropped)
{
    public static TapeDelta Empty(TapeRevision revision, TapeRevision orderRevision)
        => new(revision, orderRevision, [], [], [], []);

    public bool IsEmpty => Appended.Length == 0 && Reflected.Length == 0 && Shed.Length == 0 && Dropped.Length == 0
        && OrderRevision == TapeRevision.Initial;
}

/// The exact result of one evacuation pass. Shedding preserves the canonical
/// view; dropping removes ids from it. Counts are derived from the id arrays so
/// the receipt cannot disagree with the transition it describes.
public readonly record struct TapeEvacuation(
    TapeRevision Revision,
    TapeEventID[] Shed,
    TapeEventID[] Dropped)
{
    public int ShedCount => Shed.Length;
    public int DroppedCount => Dropped.Length;

    public void Deconstruct(out int shed, out int dropped)
    {
        shed = Shed.Length;
        dropped = Dropped.Length;
    }
}

/// A durable append carries its routing roles beside provenance. Version 1/2
/// rails predate roles and decode as GrammarInput in Tape.ReadCheckpointDelta.
internal readonly record struct TapeCheckpointAppend(
    TapeEventID ID, string Source, byte[] Bytes, byte Provenance,
    TapeEventRoles Roles = TapeEventRoles.GrammarInput);

/// Shed/drop metadata preserves roles even after the resident bytes move to the
/// event log; old rails use the documented GrammarInput default.
internal readonly record struct TapeCheckpointEvacuation(
    TapeEventID ID, string Source, byte Provenance, int Length, long Offset,
    TapeEventRoles Roles = TapeEventRoles.GrammarInput);

/// One moved resident in a reorder epoch. `ID` is the source span and
/// `SlotID` names the target slot by the span that currently occupies it. This
/// is an edit journal, not a second resident image: unchanged spans emit no
/// receipt and replay reconstructs the remaining slots from the live tape.
internal readonly record struct TapeCheckpointReorderEdit(TapeEventID ID, TapeEventID SlotID); // typed reorder epoch edit

internal readonly record struct TapeCheckpointDelta(
    TapeDelta Mutation,
    TapeCheckpointAppend[] Appended,
    TapeCheckpointEvacuation[] Shed,
    TapeCheckpointEvacuation[] Dropped,
    TapeCheckpointReorderEdit[] ReorderEdits,
    bool Reordered,
    bool ReorderAfterEvacuation = true)
{
    internal bool IsEmpty => Mutation.IsEmpty && Appended.Length == 0 && Shed.Length == 0 && Dropped.Length == 0 && ReorderEdits.Length == 0 && !Reordered;
}

/// The checkpoint rail's typed mutation envelope.  The tape owns the event
/// transition; Journal and Reads own their append cursors.  Keeping these
/// receipts as one value means a persistence writer cannot accidentally clear
/// one stream while committing another.
internal readonly record struct TapeJournalReadsCheckpointDelta(
    TapeCheckpointDelta Tape,
    string[] JournalLines,
    int JournalCursor,
    string[] Excursions,
    int ExcursionCursor)
{
    internal bool IsEmpty => Tape.IsEmpty && JournalLines.Length == 0 && Excursions.Length == 0;
}
