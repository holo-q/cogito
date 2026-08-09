namespace Cogito;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// One deterministic self-quota allocation assigned to one registered policy.
public readonly record struct CortexPolicyReadoutAllocation(
    long Sequence,
    int Step,
    string RosterDigest,
    CortexPolicyID Policy,
    long AvailableBefore,
    long AllocatedUnits,
    long ExpiredUnits,
    long AvailableAfter);

public readonly record struct CortexPolicyReadoutAllocationReceipt(
    CortexPolicyID Policy,
    long AvailableUnits,
    long AllocatedUnits,
    long HeldUnits,
    long UsedUnits,
    long ReclaimedUnits,
    long ExpiredUnits,
    long LastAllocationSequence);

public sealed partial class Cortex
{
    private sealed class PolicyReadoutAllocationState
    {
        public long AvailableUnits;
        public long AllocatedUnits;
        public long HeldUnits;
        public long UsedUnits;
        public long ReclaimedUnits;
        public long ExpiredUnits;
        public long LastAllocationSequence;

        public CortexPolicyReadoutAllocationReceipt Receipt(CortexPolicyID policy)
            => new(policy, AvailableUnits, AllocatedUnits, HeldUnits, UsedUnits, ReclaimedUnits, ExpiredUnits, LastAllocationSequence);
    }

    // Keyframe dialect marker: the legacy allocation section leads with a Bool byte (0/1), so any
    // higher byte value unambiguously selects the shed-horizon dialect.
    private const byte PolicyReadoutAllocationShedDialect = 2;
    private const ulong AllocationDigestSeed = 0xcbf29ce484222325UL;   // FNV-1a 64 offset basis
    private const ulong AllocationDigestPrime = 0x100000001b3UL;

    private readonly List<CortexPolicyID> _policyReadoutRoster = new();
    private readonly Dictionary<CortexPolicyID, PolicyReadoutAllocationState> _policyReadoutAllocationStates = new();
    private readonly List<CortexPolicyReadoutAllocation> _policyReadoutAllocations = new();   // the RESIDENT tail — rows at absolute sequence (_policyReadoutAllocationShedCount, …]
    private bool _policyReadoutRosterSealed;
    private string _policyReadoutRosterDigest = string.Empty;
    // Step zero is the explicit pre-runtime origin: no completed outer step has
    // allocated a unit yet, but the persisted allocation still has a valid cursor.
    private long _policyReadoutLastQuotaStep;
    private int _policyReadoutAllocationCursor;
    private long _policyReadoutAllocationSequence;
    private long _policyReadoutAllocationShedCount;                    // rows dropped from RAM after a durable checkpoint commit; the TSV journal is their only row-level home
    private ulong _policyReadoutAllocationShedDigest = AllocationDigestSeed;   // FNV-1a over the shed rows' canonical TSV renderings — audit-only for the prefix the keyframe no longer carries

    internal long AbsolutePolicyReadoutAllocationCount => _policyReadoutAllocationShedCount + _policyReadoutAllocations.Count;

    private static ulong FoldAllocationDigest(ulong digest, in CortexPolicyReadoutAllocation row)
        => FoldAllocationDigest(digest, FormatPolicyReadoutAllocationRow(in row));

    private static ulong FoldAllocationDigest(ulong digest, string renderedRow)
    {
        foreach (byte b in Encoding.UTF8.GetBytes(renderedRow))
            digest = unchecked((digest ^ b) * AllocationDigestPrime);
        return unchecked((digest ^ (byte)'\n') * AllocationDigestPrime);
    }

    private static bool TryParsePolicyReadoutAllocationRow(
        string rendered,
        out CortexPolicyReadoutAllocation allocation)
    {
        string[] fields = rendered.Split('\t');
        if (fields.Length != 8
            || !long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long sequence)
            || !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int step)
            || !long.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out long availableBefore)
            || !long.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out long allocatedUnits)
            || !long.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out long expiredUnits)
            || !long.TryParse(fields[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out long availableAfter))
        {
            allocation = default;
            return false;
        }
        allocation = new(
            sequence, step, fields[2], new CortexPolicyID(fields[3]),
            availableBefore, allocatedUnits, expiredUnits, availableAfter);
        return true;
    }

    /// WRITER-SIDE trim, invoked only after a durable checkpoint landed (never on the load/replay path,
    /// which must reconstruct the exact pre-commit state for the Save∘Load∘Save round-trip invariant). Folds the committed
    /// prefix into the audit-only digest and drops it from RAM; the TSV journal keeps the rows.
    internal void ShedCommittedPolicyReadoutAllocations()
    {
        if (_runtimeRun is null) return;                               // no durable TSV journal — the keyframe tail stays the only home, so nothing may shed
        int committed = _policyReadoutAllocationCheckpointCursor;
        if (committed == 0) return;
        FlushPolicyJournalBuffer();                                    // rows must be ON DISK before their RAM copy dies
        for (int i = 0; i < committed; i++)
            _policyReadoutAllocationShedDigest = FoldAllocationDigest(_policyReadoutAllocationShedDigest, _policyReadoutAllocations[i]);
        _policyReadoutAllocations.RemoveRange(0, committed);
        _policyReadoutAllocationShedCount += committed;
        _policyReadoutAllocationCheckpointCursor = 0;
    }

    private void SealPolicyReadoutRoster()
    {
        if (_policyReadoutRosterSealed) return;
        _policyReadoutRoster.Clear();
        _policyReadoutRoster.AddRange(_policies.Keys);
        _policyReadoutRoster.Sort();
        _policyReadoutRosterDigest = ComputePolicyReadoutRosterDigest(_policyReadoutRoster);
        _policyReadoutAllocationStates.Clear();
        for (int i = 0; i < _policyReadoutRoster.Count; i++)
            _policyReadoutAllocationStates.Add(_policyReadoutRoster[i], new PolicyReadoutAllocationState());
        _policyReadoutRosterSealed = true;
    }

    private void AllocatePolicyReadoutUnits(int step)
    {
        if (step < 0) throw new ArgumentOutOfRangeException(nameof(step));
        SealPolicyReadoutRoster();
        if (_policyReadoutLastQuotaStep < 0)
            _policyReadoutLastQuotaStep = 0;
        if (step < _policyReadoutLastQuotaStep)
            throw new InvalidDataException("policy readout quota step moved backwards");
        if (step == _policyReadoutLastQuotaStep || _policyReadoutRoster.Count == 0)
        {
            _policyReadoutLastQuotaStep = step;
            return;
        }

        long quantum = checked(1L + _config.Learning.Policies.ReadoutDeliberationQuota);
        for (int quotaStep = checked((int)_policyReadoutLastQuotaStep + 1); quotaStep <= step; quotaStep++)
        {
            CortexPolicyID policy = _policyReadoutRoster[_policyReadoutAllocationCursor];
            PolicyReadoutAllocationState allocationState = _policyReadoutAllocationStates[policy];
            long before = allocationState.AvailableUnits;
            long allocated = before < quantum ? 1 : 0;
            long expired = allocated == 0 ? 1 : 0;
            allocationState.AvailableUnits = checked(before + allocated);
            allocationState.AllocatedUnits = checked(allocationState.AllocatedUnits + allocated);
            allocationState.ExpiredUnits = checked(allocationState.ExpiredUnits + expired);
            allocationState.LastAllocationSequence = checked(++_policyReadoutAllocationSequence);
            CortexPolicyReadoutAllocation allocation = new(
                allocationState.LastAllocationSequence, quotaStep, _policyReadoutRosterDigest, policy,
                before, allocated, expired, allocationState.AvailableUnits);
            _policyReadoutAllocations.Add(allocation);
            AppendPolicyReadoutAllocation(in allocation);
            _policyReadoutAllocationCursor = (_policyReadoutAllocationCursor + 1) % _policyReadoutRoster.Count;
        }
        _policyReadoutLastQuotaStep = step;
    }

    private PolicyReadoutAllocationState GetPolicyReadoutAllocationState(CortexPolicyID policy)
    {
        SealPolicyReadoutRoster();
        return _policyReadoutAllocationStates.TryGetValue(policy, out PolicyReadoutAllocationState? allocationState)
            ? allocationState
            : throw new KeyNotFoundException($"policy '{policy}' is absent from the sealed readout roster");
    }

    private long ReadPolicyReadoutRemainingQuota()
    {
        long available = 0;
        foreach (PolicyReadoutAllocationState allocationState in _policyReadoutAllocationStates.Values)
            available = checked(available + allocationState.AvailableUnits);
        return available;
    }

    internal long ReadPolicyReadoutAllocationCount() => AbsolutePolicyReadoutAllocationCount;
    internal long ReadPolicyReadoutLastQuotaStep() => _policyReadoutLastQuotaStep;
    internal string ReadPolicyReadoutRosterDigest() => _policyReadoutRosterDigest;

    internal void CompleteRuntimeStep(int completedStep)
    {
        if (completedStep < 0) throw new ArgumentOutOfRangeException(nameof(completedStep));
        AllocatePolicyReadoutUnits(checked(completedStep + 1));
        if (completedStep + 1 >= _config.Steps)
            FlushPolicyJournalBuffer();
    }

    internal void AppendPolicyReadoutAllocationStates(List<CortexPolicyReadoutAllocationReceipt> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        SealPolicyReadoutRoster();
        for (int i = 0; i < _policyReadoutRoster.Count; i++)
            destination.Add(_policyReadoutAllocationStates[_policyReadoutRoster[i]].Receipt(_policyReadoutRoster[i]));
    }

    internal void AppendPolicyReadoutAllocations(List<CortexPolicyReadoutAllocation> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (_policyReadoutAllocationShedCount > 0)
            throw new InvalidOperationException($"{_policyReadoutAllocationShedCount} allocation rows were shed to the TSV journal; in-RAM enumeration no longer covers the record");
        destination.AddRange(_policyReadoutAllocations);
    }

    private static string ComputePolicyReadoutRosterDigest(IReadOnlyList<CortexPolicyID> roster)
    {
        StringBuilder canonical = new();
        for (int i = 0; i < roster.Count; i++)
            canonical.Append(i).Append(':').Append(roster[i].Value).Append('\n');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private void SavePolicyReadoutAllocation(CkptWriter writer)
    {
        SealPolicyReadoutRoster();
        writer.U8(PolicyReadoutAllocationShedDialect);
        writer.Bool(_policyReadoutRosterSealed);
        writer.Str(_policyReadoutRosterDigest);
        writer.I64(_policyReadoutLastQuotaStep);
        writer.I32(_policyReadoutAllocationCursor);
        writer.I64(_policyReadoutAllocationSequence);
        writer.I32(_policyReadoutRoster.Count);
        for (int i = 0; i < _policyReadoutRoster.Count; i++) writer.Str(_policyReadoutRoster[i].Value);
        writer.I32(_policyReadoutRoster.Count);
        for (int i = 0; i < _policyReadoutRoster.Count; i++)
        {
            CortexPolicyID policy = _policyReadoutRoster[i];
            PolicyReadoutAllocationState allocationState = _policyReadoutAllocationStates[policy];
            writer.Str(policy.Value); writer.I64(allocationState.AvailableUnits); writer.I64(allocationState.AllocatedUnits);
            writer.I64(allocationState.HeldUnits); writer.I64(allocationState.UsedUnits); writer.I64(allocationState.ReclaimedUnits);
            writer.I64(allocationState.ExpiredUnits); writer.I64(allocationState.LastAllocationSequence);
        }
        // The shed prefix rides as horizon + audit-only digest only; the TSV journal owns its rows. The
        // resident tail (rows since the last durable shed) is the only row body the keyframe carries.
        writer.I64(_policyReadoutAllocationShedCount);
        writer.U64(_policyReadoutAllocationShedDigest);
        writer.I32(_policyReadoutAllocations.Count);
        for (int i = 0; i < _policyReadoutAllocations.Count; i++)
        {
            CortexPolicyReadoutAllocation row = _policyReadoutAllocations[i];
            writer.I64(row.Sequence); writer.I32(row.Step); writer.Str(row.RosterDigest); writer.Str(row.Policy.Value);
            writer.I64(row.AvailableBefore); writer.I64(row.AllocatedUnits); writer.I64(row.ExpiredUnits); writer.I64(row.AvailableAfter);
        }
    }

    private void LoadPolicyReadoutAllocation(CkptReader reader)
    {
        // The legacy allocation section leads with a Bool (0/1); the shed dialect leads with its marker byte.
        byte lead = reader.U8();
        bool shedDialect = lead == PolicyReadoutAllocationShedDialect;
        bool sealedRoster = shedDialect ? reader.Bool() : lead switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidDataException($"unknown policy readout allocation dialect byte {lead}"),
        };
        string rosterDigest = reader.Str();
        long lastStep = reader.I64();
        int cursor = reader.I32();
        long sequence = reader.I64();
        int rosterCount = reader.I32();
        if (!sealedRoster || rosterCount < 0 || rosterCount > _policies.Count || lastStep < 0 || sequence < 0)
            throw new InvalidDataException("policy readout roster authority is malformed");
        List<CortexPolicyID> roster = new(rosterCount);
        HashSet<CortexPolicyID> seen = new();
        for (int i = 0; i < rosterCount; i++)
        {
            CortexPolicyID policy = new(reader.Str());
            if (!seen.Add(policy) || !_policies.ContainsKey(policy) || (i > 0 && roster[^1].CompareTo(policy) >= 0))
                throw new InvalidDataException("policy readout roster is not the stable registered ordinal set");
            roster.Add(policy);
        }
        if (rosterCount == 0 || cursor < 0 || cursor >= rosterCount || rosterDigest != ComputePolicyReadoutRosterDigest(roster))
            throw new InvalidDataException("policy readout roster digest or cursor is invalid");
        List<CortexPolicyID> expected = [.. _policies.Keys]; expected.Sort();
        if (!expected.SequenceEqual(roster)) throw new InvalidDataException("policy readout roster differs from registered policies");

        int allocationStateCount = reader.I32();
        if (allocationStateCount != rosterCount) throw new InvalidDataException("policy readout allocation state count differs from roster");
        Dictionary<CortexPolicyID, PolicyReadoutAllocationState> allocationStates = new(allocationStateCount);
        for (int i = 0; i < allocationStateCount; i++)
        {
            CortexPolicyID policy = new(reader.Str());
            PolicyReadoutAllocationState allocationState = new()
            {
                AvailableUnits = reader.I64(), AllocatedUnits = reader.I64(), HeldUnits = reader.I64(),
                UsedUnits = reader.I64(), ReclaimedUnits = reader.I64(), ExpiredUnits = reader.I64(), LastAllocationSequence = reader.I64(),
            };
            if (!seen.Contains(policy) || !allocationStates.TryAdd(policy, allocationState)
                || allocationState.AvailableUnits < 0 || allocationState.AllocatedUnits < 0 || allocationState.HeldUnits < 0 || allocationState.UsedUnits < 0
                || allocationState.ReclaimedUnits < 0 || allocationState.ExpiredUnits < 0 || allocationState.LastAllocationSequence < 0)
                throw new InvalidDataException("policy readout allocation state is malformed or unbound");
        }
        long shedCount = shedDialect ? reader.I64() : 0;
        ulong shedDigest = shedDialect ? reader.U64() : AllocationDigestSeed;
        if (shedCount < 0 || (shedCount == 0 && shedDigest != AllocationDigestSeed))
            throw new InvalidDataException("policy readout allocation shed horizon is malformed");
        int allocationCount = reader.I32();
        if (allocationCount < 0 || shedCount + allocationCount > sequence)
            throw new InvalidDataException("policy readout allocation count is invalid");
        List<CortexPolicyReadoutAllocation> allocations = new(allocationCount);
        long issued = shedCount;                                       // shed rows issued exactly one unit each (allocated+expired==1, validated before they shed)
        // Per-policy closure accumulators — ONE pass over the resident rows, then per-roster compare
        // (the former per-roster × per-row rescan was O(rows × roster)).
        Dictionary<CortexPolicyID, (long Allocated, long Expired, long LastSequence)> closure = new(rosterCount);
        foreach (CortexPolicyID policy in roster) closure[policy] = (0, 0, 0);
        for (int i = 0; i < allocationCount; i++)
        {
            CortexPolicyReadoutAllocation row = new(reader.I64(), reader.I32(), reader.Str(), new CortexPolicyID(reader.Str()),
                reader.I64(), reader.I64(), reader.I64(), reader.I64());
            if (row.Sequence != shedCount + i + 1 || row.Step != shedCount + i + 1 || row.RosterDigest != rosterDigest || !seen.Contains(row.Policy)
                || row.AvailableBefore < 0 || row.AvailableAfter < 0
                || row.AllocatedUnits < 0 || row.ExpiredUnits < 0
                || row.AllocatedUnits + row.ExpiredUnits != 1
                || row.AvailableAfter != row.AvailableBefore + row.AllocatedUnits)
                throw new InvalidDataException("policy readout allocation journal is malformed");
            issued = checked(issued + row.AllocatedUnits + row.ExpiredUnits);
            (long allocated, long expired, _) = closure[row.Policy];
            closure[row.Policy] = (checked(allocated + row.AllocatedUnits), checked(expired + row.ExpiredUnits), row.Sequence);
            allocations.Add(row);
        }
        if (sequence != shedCount + allocationCount || issued != checked(lastStep))
            throw new InvalidDataException("policy readout allocation mint does not equal completed steps");
        if (cursor != (int)(sequence % rosterCount))
            throw new InvalidDataException("policy readout allocation cursor does not follow the contiguous roster rotation");
        long allocatedUnitTotal = 0;
        foreach (PolicyReadoutAllocationState allocationState in allocationStates.Values)
            allocatedUnitTotal = checked(allocatedUnitTotal + allocationState.AllocatedUnits + allocationState.ExpiredUnits);
        if (allocatedUnitTotal != sequence)
            throw new InvalidDataException("policy readout allocation state mint totals do not equal the allocation sequence");
        for (int i = 0; i < roster.Count; i++)
        {
            PolicyReadoutAllocationState allocationState = allocationStates[roster[i]];
            (long allocated, long expired, long lastSequence) = closure[roster[i]];
            // With a shed prefix the allocation state aggregates include shed rows the keyframe no longer carries:
            // the resident closure can only bound them (the digest-custodied TSV holds the row history).
            if (shedCount == 0
                ? allocationState.AllocatedUnits != allocated || allocationState.ExpiredUnits != expired || allocationState.LastAllocationSequence != lastSequence
                : allocationState.AllocatedUnits < allocated || allocationState.ExpiredUnits < expired
                    || allocationState.LastAllocationSequence < lastSequence || allocationState.LastAllocationSequence > sequence)
                throw new InvalidDataException("policy readout allocation state allocation totals do not close its journal");
        }
        _policyReadoutRoster.Clear(); _policyReadoutRoster.AddRange(roster); _policyReadoutRosterDigest = rosterDigest;
        _policyReadoutAllocationStates.Clear(); foreach (KeyValuePair<CortexPolicyID, PolicyReadoutAllocationState> pair in allocationStates) _policyReadoutAllocationStates.Add(pair.Key, pair.Value);
        _policyReadoutAllocations.Clear(); _policyReadoutAllocations.AddRange(allocations);
        _policyReadoutAllocationShedCount = shedCount; _policyReadoutAllocationShedDigest = shedDigest;
        _policyReadoutRosterSealed = true; _policyReadoutLastQuotaStep = lastStep; _policyReadoutAllocationCursor = cursor; _policyReadoutAllocationSequence = sequence;
    }
}
