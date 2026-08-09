namespace Cogito;

using System.Security.Cryptography;
using System.Text;

/// Arm-neutral, deterministic access to the configured world. The census knows the
/// complete selected-line count, while the cursor advances selected domains in
/// ordinal round-robin order so every file contributes an early opportunity.
internal sealed class AdmissionCursor : IDisposable
{
    private readonly string[] _files;
    private readonly int[] _counts;
    private readonly int[] _linesRead;
    private readonly StreamReader?[] _readers;
    private readonly int _totalItems;
    private int _domainCursor;
    private int _nextItem;
    private bool _disposed;
    private readonly List<TapeEventID> _eventIDs = new();
    private readonly List<int> _eventDomains = new();
    private readonly AdmissionPlan? _plan;
    private int _planCursor;

    internal const string ScheduleID = "domain-roundrobin-v1";

    public AdmissionCursor(string path, string glob, AdmissionPlan? plan = null)
    {
        _files = FileCorpus.GatherFiles(path, glob).ToArray();
        _plan = plan;
        if (_plan is not null)
        {
            _plan.Validate(_files.Length);
            _plan.ValidateWorld(FileCorpus.ComputeWorldSHA256(path, glob));
        }
        _counts = new int[_files.Length];
        _linesRead = new int[_files.Length];
        _readers = new StreamReader?[_files.Length];
        int total = 0;
        for (int domain = 0; domain < _files.Length; domain++)
        {
            foreach (string raw in File.ReadLines(_files[domain]))
                if (raw.Trim().Length != 0) { _counts[domain]++; total++; }
        }
        _totalItems = total;
        _plan?.ValidateCounts(_counts);
    }

    public int TotalItems => _totalItems;
    public int Families => _files.Length;
    public int Cursor => _nextItem;
    public int Remaining => _totalItems - _nextItem;
    public bool IsTerminal => Remaining == 0;
    internal string ActiveScheduleID => _plan?.ScheduleID ?? ScheduleID;
    internal string ActiveScheduleDigest => _plan?.AuthorityDigest ?? DefaultScheduleDigest;
    private static string DefaultScheduleDigest => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ScheduleID + "|")));
    public IReadOnlyList<TapeEventID> EventIDs => _eventIDs;

    public AdmissionReceipt Admit(Tape tape, Journal journal, int step, int requested)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (requested < 0) throw new ArgumentOutOfRangeException(nameof(requested));
        int before = _nextItem;
        int domainStart = _eventDomains.Count;
        long bytes = 0;
        int admitted = 0;
        while (admitted < requested && TryRead(out int index, out int domain, out byte[] source))
        {
            _eventIDs.Add(TapePacketCreator.CommitWorldEncounter(tape, journal, step, source, index, domain, fresh: true, coverage: double.NaN));
            _eventDomains.Add(domain);
            bytes = checked(bytes + source.Length);
            admitted++;
        }
        return new AdmissionReceipt(
            step,
            CursorBefore: before,
            CursorAfter: _nextItem,
            PlannedItems: requested,
            AdmittedItems: admitted,
            AdmittedBytes: bytes,
            RemainingItems: Remaining,
            TotalItems: TotalItems,
            Terminal: IsTerminal,
            CursorDigest: ComputeDigest(),
            Schedule: ActiveScheduleID,
            ScheduleDigest: ActiveScheduleDigest,
            AdmittedDomains: CountDistinctDomains(domainStart, admitted),
            DomainDigest: ComputeDomainDigest(domainStart, admitted));
    }

    /// Rebuild the source position from the tape's already committed world
    /// observations. The expected world digest and active admission plan are
    /// checked before this cursor is created; replaying the same planned prefix
    /// is therefore the exact resume operation, with no duplicate or skipped line.
    public void Restore(int consumedItems, IReadOnlyList<TapeEventID> eventIDs)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (consumedItems < 0 || consumedItems > TotalItems || eventIDs.Count != consumedItems)
            throw new InvalidDataException($"world encounter cursor restore disagrees with tape: cursor={consumedItems}, events={eventIDs.Count}, total={TotalItems}");
        CloseReader();
        _domainCursor = 0;
        _nextItem = 0;
        _planCursor = 0;
        Array.Clear(_linesRead);
        _eventIDs.Clear();
        _eventDomains.Clear();
        _eventIDs.AddRange(eventIDs);
        for (int i = 0; i < consumedItems; i++)
            if (!TryRead(out _, out int domain, out _))
                throw new InvalidDataException($"world encounter cursor ended before restored item {i}");
            else _eventDomains.Add(domain);
        if (_nextItem != consumedItems)
            throw new InvalidDataException("world encounter cursor restore did not reach the tape cursor");
    }

    public string ComputeDigest()
    {
        StringBuilder canonical = new();
        canonical.Append(ActiveScheduleID).Append('|').Append(ActiveScheduleDigest).Append('|').Append(_nextItem).Append('|').Append(_totalItems).Append('|').Append(_domainCursor).Append('|').Append(_planCursor).Append('|');
        foreach (int linesRead in _linesRead) canonical.Append(linesRead).Append(',');
        canonical.Append('|');
        foreach (TapeEventID eventID in _eventIDs) canonical.Append(eventID.Value).Append(',');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    internal static bool VerifyFixture(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        string root = Path.Combine(".tmp", $"world-encounter-cursor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "nested"));
            File.WriteAllText(Path.Combine(root, "a.cs"), "alpha\n\n  beta  \ncharlie\n");
            File.WriteAllText(Path.Combine(root, "nested", "b.md"), "gamma\n  delta\nepsilon\n");
            using Tape straightTape = new();
            Journal straightJournal = new();
            using AdmissionCursor straight = new(root, "*.cs,*.md");
            AdmissionReceipt first = straight.Admit(straightTape, straightJournal, 0, 1);
            AdmissionReceipt second = straight.Admit(straightTape, straightJournal, 1, 1);
            AdmissionReceipt third = straight.Admit(straightTape, straightJournal, 2, 1);
            first.Validate(); second.Validate(); third.Validate();

            using Tape resumedTape = new();
            Journal resumedJournal = new();
            using AdmissionCursor prefix = new(root, "*.cs,*.md");
            AdmissionReceipt prefixAdmission = prefix.Admit(resumedTape, resumedJournal, 0, 1);
            using AdmissionCursor resumed = new(root, "*.cs,*.md");
            resumed.Restore(prefixAdmission.AdmittedItems, TapePacketCreator.ReadWorldEncounterEventIDs(resumedTape));
            AdmissionReceipt resumedSecond = resumed.Admit(resumedTape, resumedJournal, 1, 1);
            AdmissionReceipt resumedThird = resumed.Admit(resumedTape, resumedJournal, 2, 1);
            resumedSecond.Validate(); resumedThird.Validate();
            bool cursorExact = resumed.Cursor == straight.Cursor
                && resumed.Remaining == straight.Remaining
                && resumed.ComputeDigest() == straight.ComputeDigest()
                && resumedTape.Concat().AsSpan().SequenceEqual(straightTape.Concat());
            static string ReadSource(Tape tape, TapeEventID eventID)
                => tape.Resolve(eventID, out byte[] bytes) ? Encoding.UTF8.GetString(bytes) : "";
            bool interleaved = straight.EventIDs.Count >= 3
                && ReadSource(straightTape, straight.EventIDs[0]) == "alpha"
                && ReadSource(straightTape, straight.EventIDs[1]) == "gamma"
                && ReadSource(straightTape, straight.EventIDs[2]) == "  beta";
            bool bounded = first.AdmittedItems == 1 && second.AdmittedItems == 1 && third.AdmittedItems == 1
                && first.AdmittedItems < straight.TotalItems;
            bool receiptBreadth = first.Schedule == ScheduleID && second.Schedule == ScheduleID
                && first.AdmittedDomains == 1 && second.AdmittedDomains == 1
                && !string.Equals(first.DomainDigest, second.DomainDigest, StringComparison.Ordinal)
                && first.ToTsv().Split('\t').Length == 14;
            bool cursorPass = cursorExact && interleaved && bounded && receiptBreadth
                && resumedSecond.CursorAfter == second.CursorAfter
                && resumedThird.CursorAfter == third.CursorAfter;

            TapeEventID[] one = [new(2)];
            TapeEventID[] two = [new(2), new(4)];
            TapeEventID[] three = [new(2), new(4), new(6)];
            ReplayCalc live = ReplayCalc.Mount(0xC0117011UL);
            live.BindWorldOpportunityEvents(one);
            live.BindWorldOpportunityEvents(two);
            using MemoryStream state = new();
            using (CkptWriter writer = new(state)) live.SaveState(writer);
            state.Position = 0;
            ReplayCalc resumedReplay = ReplayCalc.Mount(0xC0117011UL);
            using (CkptReader reader = new(state)) resumedReplay.LoadState(reader);
            static bool Reject(Action action)
            {
                try { action(); return false; }
                catch (InvalidDataException) { return true; }
            }
            bool resumeMismatchRejected = Reject(() => resumedReplay.BindWorldOpportunityEvents([new(2), new(5)]));
            resumedReplay.BindWorldOpportunityEvents(two);
            resumedReplay.BindWorldOpportunityEvents(three);

            bool mutationRejected = Reject(() => resumedReplay.BindWorldOpportunityEvents([new(2), new(5), new(6)]));
            bool reorderRejected = Reject(() => resumedReplay.BindWorldOpportunityEvents([new(4), new(2), new(6)]));
            bool duplicateRejected = Reject(() => resumedReplay.BindWorldOpportunityEvents([new(2), new(4), new(4), new(6)]));
            bool bindPass = resumedReplay.WorldOpportunityCursor == 0
                && resumeMismatchRejected && mutationRejected && reorderRejected && duplicateRejected;
            bool pass = cursorPass && bindPass;
            output.WriteLine($"  world-encounter cursor · total={straight.TotalItems} · first={first.CursorBefore}->{first.CursorAfter} · resume={resumed.Cursor} · remaining={resumed.Remaining} · schedule={(interleaved ? "roundrobin" : "PATH-ORDER")} · incremental=3 · checkpoint=PASS · resume-prefix={(resumeMismatchRejected ? "REJECT" : "ACCEPT")} · mutation={(mutationRejected ? "REJECT" : "ACCEPT")} · reorder={(reorderRejected ? "REJECT" : "ACCEPT")} · duplicate={(duplicateRejected ? "REJECT" : "ACCEPT")} · {(pass ? "PASS" : "FAIL")}");
            return pass;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private bool TryRead(out int index, out int domain, out byte[] bytes)
    {
        if (_plan is not null)
        {
            while (_planCursor < _plan.DomainSequence.Count)
            {
                int candidate = _plan.DomainSequence[_planCursor++];
                if (_linesRead[candidate] >= _counts[candidate]) continue;
                StreamReader reader = _readers[candidate] ??= new StreamReader(_files[candidate], Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                string? raw = reader.ReadLine();
                if (raw is null)
                {
                    _linesRead[candidate] = _counts[candidate];
                    reader.Dispose();
                    _readers[candidate] = null;
                    continue;
                }
                string text = raw.TrimEnd();
                if (text.Trim().Length == 0)
                {
                    _planCursor--;
                    continue;
                }
                _linesRead[candidate]++;
                _domainCursor = candidate;
                index = _nextItem++;
                domain = candidate;
                bytes = Encoding.UTF8.GetBytes(text);
                return true;
            }
            index = 0; domain = 0; bytes = Array.Empty<byte>();
            return false;
        }
        for (int offset = 0; offset < _files.Length; offset++)
        {
            int candidate = (_domainCursor + offset) % _files.Length;
            if (_linesRead[candidate] >= _counts[candidate]) continue;
            StreamReader reader = _readers[candidate] ??= new StreamReader(_files[candidate], Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            while (true)
            {
                string? raw = reader.ReadLine();
                if (raw is null)
                {
                    _linesRead[candidate] = _counts[candidate];
                    reader.Dispose();
                    _readers[candidate] = null;
                    break;
                }
                string text = raw.TrimEnd();
                if (text.Trim().Length == 0) continue;
                _linesRead[candidate]++;
                _domainCursor = (candidate + 1) % _files.Length;
                index = _nextItem++;
                domain = candidate;
                bytes = Encoding.UTF8.GetBytes(text);
                return true;
            }
        }
        index = 0; domain = 0; bytes = Array.Empty<byte>();
        return false;
    }

    private int CountDistinctDomains(int start, int count)
    {
        if (count == 0) return 0;
        bool[] seen = new bool[_files.Length];
        int distinct = 0;
        for (int i = start; i < start + count; i++)
            if (!seen[_eventDomains[i]]) { seen[_eventDomains[i]] = true; distinct++; }
        return distinct;
    }

    private string ComputeDomainDigest(int start, int count)
    {
        if (count == 0) return EmptyDomainDigest();
        StringBuilder canonical = new();
        canonical.Append(ActiveScheduleID).Append('|');
        for (int i = start; i < start + count; i++) canonical.Append(_eventDomains[i]).Append(',');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    internal static string EmptyDomainDigest()
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ScheduleID + "|")));

    internal string EmptyActiveDomainDigest()
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ActiveScheduleID + "|")));

    private void CloseReader()
    {
        for (int i = 0; i < _readers.Length; i++)
        {
            _readers[i]?.Dispose();
            _readers[i] = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseReader();
    }
}

/// Immutable admission order supplied by a focused world assay. It changes only
/// which already-registered domain line is admissionPlaned next; payloads and world
/// identity remain owned by the corpus files.
internal sealed class AdmissionPlan
{
    internal AdmissionPlan(string scheduleID, IReadOnlyList<int> domainSequence)
        : this(scheduleID, domainSequence, "")
    {
    }

    internal AdmissionPlan(string scheduleID, IReadOnlyList<int> domainSequence, string worldSHA256, string? authorityDigest = null)
    {
        ScheduleID = scheduleID;
        DomainSequence = domainSequence.ToArray();
        WorldSHA256 = worldSHA256;
        AuthorityDigest = ComputeAuthorityDigest(scheduleID, worldSHA256, DomainSequence);
        if (authorityDigest is not null && !string.Equals(AuthorityDigest, authorityDigest, StringComparison.Ordinal))
            throw new InvalidDataException("world encounter plan authority digest does not match its schedule");
    }

    internal string ScheduleID { get; }
    internal IReadOnlyList<int> DomainSequence { get; }
    internal string WorldSHA256 { get; }
    internal string AuthorityDigest { get; }

    internal AdmissionPlan BindWorld(string worldSHA256)
        => new(ScheduleID, DomainSequence, worldSHA256);

    internal void ValidateWorld(string worldSHA256)
    {
        if (WorldSHA256.Length != 64 || AuthorityDigest.Length != 64 || !string.Equals(WorldSHA256, worldSHA256, StringComparison.Ordinal))
            throw new InvalidDataException("world encounter plan is not bound to this corpus world");
    }

    internal void Validate(int domainCount)
    {
        if (string.IsNullOrWhiteSpace(ScheduleID) || DomainSequence.Count == 0)
            throw new InvalidDataException("world encounter plan is empty");
        for (int i = 0; i < DomainSequence.Count; i++)
            if ((uint)DomainSequence[i] >= (uint)domainCount)
                throw new InvalidDataException($"world encounter plan names unknown domain {DomainSequence[i]}");
    }

    internal void ValidateCounts(IReadOnlyList<int> counts)
    {
        int[] planned = new int[counts.Count];
        for (int i = 0; i < DomainSequence.Count; i++) planned[DomainSequence[i]]++;
        for (int i = 0; i < counts.Count; i++)
            if (planned[i] != counts[i])
                throw new InvalidDataException($"world encounter plan count disagrees for domain {i}: plan={planned[i]} corpus={counts[i]}");
    }

    private static string ComputeAuthorityDigest(string scheduleID, string worldSHA256, IReadOnlyList<int> sequence)
    {
        StringBuilder canonical = new();
        canonical.Append(scheduleID).Append('|').Append(worldSHA256).Append('|');
        for (int i = 0; i < sequence.Count; i++) canonical.Append(sequence[i]).Append(',');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }
}

internal readonly record struct AdmissionReceipt(
    int Step,
    int CursorBefore,
    int CursorAfter,
    int PlannedItems,
    int AdmittedItems,
    long AdmittedBytes,
    int RemainingItems,
    int TotalItems,
    bool Terminal,
    string CursorDigest,
    string Schedule,
    string ScheduleDigest,
    int AdmittedDomains,
    string DomainDigest)
{
    public void Validate()
    {
        if (Step < 0 || CursorBefore < 0 || CursorAfter < CursorBefore
            || PlannedItems < 0 || AdmittedItems < 0 || AdmittedItems > PlannedItems
            || CursorAfter - CursorBefore != AdmittedItems
            || AdmittedBytes < 0 || RemainingItems < 0 || TotalItems < CursorAfter
            || RemainingItems != TotalItems - CursorAfter
            || Terminal != (RemainingItems == 0)
            || CursorDigest.Length != 64
            || string.IsNullOrWhiteSpace(Schedule)
            || ScheduleDigest.Length != 64
            || AdmittedDomains < 0 || AdmittedDomains > AdmittedItems
            || DomainDigest.Length != 64)
            throw new InvalidDataException("world encounter admission record does not close");
    }

    public string ToTsv() => string.Join('\t', Step, CursorBefore, CursorAfter, PlannedItems,
        AdmittedItems, AdmittedBytes, RemainingItems, TotalItems, Terminal ? 1 : 0, CursorDigest,
        Schedule, ScheduleDigest, AdmittedDomains, DomainDigest);

    public string ToRon() => $"(step:{Step},cursor_before:{CursorBefore},cursor_after:{CursorAfter},planned_items:{PlannedItems},admitted_items:{AdmittedItems},admitted_bytes:{AdmittedBytes},remaining_items:{RemainingItems},total_items:{TotalItems},terminal:{(Terminal ? "true" : "false")},cursor_digest:\"{CursorDigest}\",schedule:\"{Schedule}\",schedule_digest:\"{ScheduleDigest}\",admitted_domains:{AdmittedDomains},domain_digest:\"{DomainDigest}\")\n";
}
