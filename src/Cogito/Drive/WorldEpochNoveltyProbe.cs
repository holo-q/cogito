namespace Cogito;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// Immutable epoch metadata over ordinary corpus domains. The payloads remain
/// unchanged; only the world cursor's admitted-domain sequence varies by arm.
internal sealed class WorldEpochSchedule
{
    internal const string ScheduleID = "immutable-epoch-prefix-v1";

    private readonly WorldEpochBatch[] _batches;
    private readonly (int Domain, string[] Payloads)[] _domains;

    private WorldEpochSchedule(
        string worldSHA256,
        string order,
        WorldEpochBatch[] batches,
        (int Domain, string[] Payloads)[] domains,
        AdmissionPlan? admissionPlanPlan)
    {
        PayloadMultisetSHA256 = worldSHA256;
        Order = order;
        _batches = batches;
        _domains = domains;
        AdmissionPlan = admissionPlanPlan;
        ScheduleSHA256 = ComputeScheduleSHA256(worldSHA256, order, batches);
    }

    internal string PayloadMultisetSHA256 { get; }
    internal string Order { get; }
    internal string ScheduleSHA256 { get; }
    internal AdmissionPlan? AdmissionPlan { get; }
    internal string ActiveScheduleID => AdmissionPlan?.ScheduleID ?? AdmissionCursor.ScheduleID;
    internal IReadOnlyList<WorldEpochBatch> Batches => _batches;
    internal int TotalItems => _domains.Sum(static d => d.Payloads.Length);

    internal static WorldEpochSchedule Create(
        IReadOnlyList<(int Domain, byte[] Bytes)> source,
        int epochCount,
        IReadOnlyList<int> order)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(order);
        if (source.Count == 0) throw new ArgumentException("epoch world requires at least one payload", nameof(source));
        if (epochCount < 2) throw new ArgumentOutOfRangeException(nameof(epochCount), "epoch schedules require at least two real domains");
        if (order.Count != epochCount) throw new ArgumentException("epoch order must cover every epoch exactly once", nameof(order));

        bool[] seen = new bool[epochCount];
        for (int i = 0; i < order.Count; i++)
        {
            int epoch = order[i];
            if ((uint)epoch >= (uint)epochCount || seen[epoch])
                throw new InvalidDataException("epoch order is not a permutation");
            seen[epoch] = true;
        }

        (int Domain, string[] Payloads)[] domains = GroupDomains(source, epochCount);
        string worldSHA256 = ComputePayloadMultisetSHA256(source);
        List<WorldEpochBatch> batches = new(epochCount);
        int prefix = 0;
        HashSet<string> prior = new(StringComparer.Ordinal);
        for (int orderIndex = 0; orderIndex < order.Count; orderIndex++)
        {
            int epoch = order[orderIndex];
            string[] payloads = domains[epoch].Payloads;
            int noveltyDelta = CountNewPayloads(payloads, prior);
            batches.Add(WorldEpochBatch.Build(epoch, orderIndex, prefix, payloads, noveltyDelta));
            prefix += payloads.Length;
        }

        List<int> sequence = new(prefix);
        for (int orderIndex = 0; orderIndex < order.Count; orderIndex++)
            for (int i = 0; i < domains[order[orderIndex]].Payloads.Length; i++)
                sequence.Add(order[orderIndex]);
        AdmissionPlan plan = new(
            ScheduleID + ":" + string.Join(',', order),
            sequence);
        return new WorldEpochSchedule(worldSHA256, string.Join(',', order), batches.ToArray(), domains, plan);
    }

    internal static WorldEpochSchedule CreateStationary(IReadOnlyList<(int Domain, byte[] Bytes)> source)
        => CreateStationary(source, boundaries: null);

    /// Builds the ordinary round-robin diet while exposing preregistered
    /// out-of-band prefix boundaries as four custody batches. The batches do
    /// not enter the policy grammar; they only make stationary observations
    /// comparable with epoch and order-null boundary receipts.
    internal static WorldEpochSchedule CreateStationary(
        IReadOnlyList<(int Domain, byte[] Bytes)> source,
        IReadOnlyList<int>? boundaries)
    {
        (int Domain, string[] Payloads)[] domains = GroupDomains(source, distinctDomains: null);
        string worldSHA256 = ComputePayloadMultisetSHA256(source);
        List<string> interleaved = new(source.Count);
        int max = domains.Max(static d => d.Payloads.Length);
        for (int row = 0; row < max; row++)
            for (int domain = 0; domain < domains.Length; domain++)
                if (row < domains[domain].Payloads.Length) interleaved.Add(domains[domain].Payloads[row]);
        string[] payloads = interleaved.ToArray();
        if (boundaries is null)
        {
            HashSet<string> prior = new(StringComparer.Ordinal);
            int noveltyDelta = CountNewPayloads(payloads, prior);
            WorldEpochBatch batch = WorldEpochBatch.Build(0, 0, 0, payloads, noveltyDelta);
            return new WorldEpochSchedule(worldSHA256, "stationary-roundrobin", [batch], domains, admissionPlanPlan: null);
        }
        if (boundaries.Count == 0 || boundaries[^1] != payloads.Length
            || boundaries.Zip(boundaries.Skip(1)).Any(static pair => pair.First >= pair.Second)
            || boundaries.Any(boundary => boundary <= 0 || boundary > payloads.Length))
            throw new InvalidDataException("stationary custody boundaries do not close the round-robin payload sequence");
        List<WorldEpochBatch> batches = new(boundaries.Count);
        HashSet<string> seen = new(StringComparer.Ordinal);
        int before = 0;
        for (int index = 0; index < boundaries.Count; index++)
        {
            int after = boundaries[index];
            string[] batchPayloads = payloads[before..after];
            int noveltyDelta = CountNewPayloads(batchPayloads, seen);
            batches.Add(WorldEpochBatch.Build(index, index, before, batchPayloads, noveltyDelta));
            before = after;
        }
        List<int> sequence = new(payloads.Length);
        for (int row = 0; row < max; row++)
            for (int domain = 0; domain < domains.Length; domain++)
                if (row < domains[domain].Payloads.Length) sequence.Add(domains[domain].Domain);
        AdmissionPlan plan = new(AdmissionCursor.ScheduleID, sequence);
        return new WorldEpochSchedule(worldSHA256, "stationary-roundrobin", batches.ToArray(), domains, plan);
    }

    internal void WriteCorpusDirectory(string path)
    {
        Directory.CreateDirectory(path);
        for (int i = 0; i < _domains.Length; i++)
            File.WriteAllLines(Path.Combine(path, $"domain-{i:D2}.txt"), _domains[i].Payloads, new UTF8Encoding(false));
    }

    internal void Validate()
    {
        if (PayloadMultisetSHA256.Length != 64 || ScheduleSHA256.Length != 64)
            throw new InvalidDataException("epoch schedule digest is not SHA-256");
        int prefix = 0;
        for (int i = 0; i < _batches.Length; i++)
        {
            WorldEpochBatch batch = _batches[i];
            if (batch.OrderIndex != i || batch.PrefixBefore != prefix || batch.PrefixAfter <= prefix)
                throw new InvalidDataException("epoch transitions are not contiguous admitted prefixes");
            prefix = batch.PrefixAfter;
        }
        if (prefix != TotalItems) throw new InvalidDataException("epoch schedule does not close its world prefix");
        AdmissionPlan?.Validate(_domains.Length);
        if (AdmissionPlan is not null) AdmissionPlan.ValidateCounts(_domains.Select(static d => d.Payloads.Length).ToArray());
    }

    internal static string ComputePayloadMultisetSHA256(IReadOnlyList<(int Domain, byte[] Bytes)> source)
    {
        List<string> entries = new(source.Count);
        for (int i = 0; i < source.Count; i++)
            // The canonical assay identity is the payload multiset. Domain labels
            // and admission order belong to the schedule, not to world content.
            entries.Add(Convert.ToHexStringLower(SHA256.HashData(source[i].Bytes)));
        entries.Sort(StringComparer.Ordinal);
        return Digest(string.Join('|', entries));
    }

    private static (int Domain, string[] Payloads)[] GroupDomains(
        IReadOnlyList<(int Domain, byte[] Bytes)> source,
        int? distinctDomains)
    {
        Dictionary<int, List<string>> grouped = new();
        for (int i = 0; i < source.Count; i++)
        {
            if (!grouped.TryGetValue(source[i].Domain, out List<string>? payloads))
                grouped.Add(source[i].Domain, payloads = new List<string>());
            payloads.Add(Encoding.UTF8.GetString(source[i].Bytes));
        }
        if (distinctDomains is int expected && grouped.Count != expected)
            throw new InvalidDataException($"epoch schedule expected {expected} real domains, observed {grouped.Count}");
        return grouped.OrderBy(static pair => pair.Key)
            .Select(static pair => (pair.Key, pair.Value.ToArray()))
            .ToArray();
    }

    private static int CountNewPayloads(IReadOnlyList<string> payloads, HashSet<string> prior)
    {
        int count = 0;
        for (int i = 0; i < payloads.Count; i++)
            if (prior.Add(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloads[i]))))) count++;
        return count;
    }

    private static string ComputeScheduleSHA256(string worldSHA256, string order, IReadOnlyList<WorldEpochBatch> batches)
    {
        StringBuilder canonical = new();
        canonical.Append(ScheduleID).Append('|').Append(worldSHA256).Append('|').Append(order).Append('|');
        foreach (WorldEpochBatch batch in batches)
            canonical.Append(batch.Epoch).Append(':').Append(batch.OrderIndex).Append(':')
                .Append(batch.PrefixBefore).Append(':').Append(batch.PrefixAfter).Append(':')
                .Append(batch.UnseenPayloadDelta).Append(':').Append(batch.BatchSHA256).Append('|');
        return Digest(canonical.ToString());
    }

    private static string Digest(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

internal sealed class WorldEpochBatch
{
    private WorldEpochBatch(int epoch, int orderIndex, int prefixBefore, int prefixAfter, string[] payloads, int unseenPayloadDelta, string batchSHA256)
    {
        Epoch = epoch;
        OrderIndex = orderIndex;
        PrefixBefore = prefixBefore;
        PrefixAfter = prefixAfter;
        Payloads = payloads;
        UnseenPayloadDelta = unseenPayloadDelta;
        BatchSHA256 = batchSHA256;
    }

    internal int Epoch { get; }
    internal int OrderIndex { get; }
    internal int PrefixBefore { get; }
    internal int PrefixAfter { get; }
    internal IReadOnlyList<string> Payloads { get; }
    internal int UnseenPayloadDelta { get; }
    internal string BatchSHA256 { get; }

    internal static WorldEpochBatch Build(int epoch, int orderIndex, int prefixBefore, IReadOnlyList<string> payloads, int unseenPayloadDelta)
    {
        string[] frozen = payloads.ToArray();
        StringBuilder canonical = new();
        canonical.Append(epoch).Append('|').Append(orderIndex).Append('|').Append(prefixBefore).Append('|');
        foreach (string payload in frozen)
            canonical.Append(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))).Append(',');
        string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
        return new WorldEpochBatch(epoch, orderIndex, prefixBefore, checked(prefixBefore + frozen.Length), frozen, unseenPayloadDelta, digest);
    }
}

internal static class WorldEpochNoveltyProbe
{
    private const int Steps = 128;
    private const int EpochCount = 4;
    private const int ItemsPerDomain = 96;
    private const ulong Seed = 0xC0117011UL;

    internal static bool VerifyScheduleFixture(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        List<(int Domain, byte[] Bytes)> source = BuildSource();
        WorldEpochSchedule stationary = WorldEpochSchedule.CreateStationary(source, WorldNoveltyOpportunityFloor.RegisteredBoundaryPrefixes);
        WorldEpochSchedule epoch = WorldEpochSchedule.Create(source, EpochCount, [0, 1, 2, 3]);
        WorldEpochSchedule shuffled = WorldEpochSchedule.Create(source, EpochCount, [2, 0, 3, 1]);
        stationary.Validate(); epoch.Validate(); shuffled.Validate();
        bool canonicalWorld = stationary.PayloadMultisetSHA256 == epoch.PayloadMultisetSHA256 && epoch.PayloadMultisetSHA256 == shuffled.PayloadMultisetSHA256;
        bool schedulesDiffer = stationary.ScheduleSHA256 != epoch.ScheduleSHA256 && epoch.ScheduleSHA256 != shuffled.ScheduleSHA256;
        bool orderIsOutOfBand = stationary.AdmissionPlan is not null
            && epoch.AdmissionPlan is not null && shuffled.AdmissionPlan is not null
            && stationary.AdmissionPlan.ScheduleID != epoch.AdmissionPlan.ScheduleID
            && epoch.AdmissionPlan.ScheduleID != shuffled.AdmissionPlan.ScheduleID;
        bool stationaryPrefixes = stationary.Batches.Select(static b => b.PrefixAfter).SequenceEqual(WorldNoveltyOpportunityFloor.RegisteredBoundaryPrefixes);
        bool prefixes = epoch.Batches.Select(static b => b.PrefixAfter).SequenceEqual([ItemsPerDomain, ItemsPerDomain * 2, ItemsPerDomain * 3, ItemsPerDomain * 4]);
        bool nullPrefixes = shuffled.Batches.Select(static b => b.PrefixAfter).SequenceEqual([ItemsPerDomain, ItemsPerDomain * 2, ItemsPerDomain * 3, ItemsPerDomain * 4]);
        bool checkpointPlan = VerifyPlanCheckpointRoundTrip(epoch);
        bool resumedSuffix = VerifyResumedSuffix(epoch);
        bool observedOrder = VerifyObservedCursorOrders(source, stationary, epoch, shuffled);
        bool pass = canonicalWorld && schedulesDiffer && orderIsOutOfBand && stationaryPrefixes && prefixes && nullPrefixes && checkpointPlan && resumedSuffix && observedOrder;
        output.WriteLine($"  world novelty schedule · source=data/code census · payloads={source.Count} · canonical-world={(canonicalWorld ? "same" : "DRIFT")} · schedule={(schedulesDiffer ? "distinct" : "COLLAPSED")} · order={(orderIsOutOfBand && observedOrder ? "out-of-band" : "BROKEN")} · prefixes={(stationaryPrefixes && prefixes && nullPrefixes ? "admitted-only" : "BROKEN")} · checkpoint={(checkpointPlan ? "round-trip" : "DRIFT")} · resume={(resumedSuffix ? "suffix-exact" : "DRIFT")} · {(pass ? "PASS" : "FAIL")}");
        return pass;
    }

    private static bool VerifyObservedCursorOrders(
        IReadOnlyList<(int Domain, byte[] Bytes)> source,
        WorldEpochSchedule stationary,
        WorldEpochSchedule epoch,
        WorldEpochSchedule shuffled)
    {
        string root = Path.Combine(".tmp", $"r20-world-observed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string corpus = Path.Combine(root, "world");
            epoch.WriteCorpusDirectory(corpus);
            string runtimeWorld = FileCorpus.ComputeWorldSHA256(corpus, "*.txt");
            List<(int Domain, string Payload)> straight = ObserveCursor(corpus, stationary.AdmissionPlan?.BindWorld(runtimeWorld), source.Count);
            List<(int Domain, string Payload)> grouped = ObserveCursor(corpus, epoch.AdmissionPlan?.BindWorld(runtimeWorld), source.Count);
            List<(int Domain, string Payload)> deranged = ObserveCursor(corpus, shuffled.AdmissionPlan?.BindWorld(runtimeWorld), source.Count);
            return Matches(straight, ExpectedPayloads(stationary), ExpectedDomainsRoundRobin(source))
                && Matches(grouped, ExpectedPayloads(epoch), epoch.AdmissionPlan!.DomainSequence)
                && Matches(deranged, ExpectedPayloads(shuffled), shuffled.AdmissionPlan!.DomainSequence)
                && !straight.Select(static x => x.Payload).SequenceEqual(grouped.Select(static x => x.Payload));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static List<(int Domain, string Payload)> ObserveCursor(string corpus, AdmissionPlan? plan, int count)
    {
        using Tape tape = new();
        Journal journal = new();
        using AdmissionCursor cursor = new(corpus, "*.txt", plan);
        cursor.Admit(tape, journal, 0, count).Validate();
        List<(int Domain, string Payload)> observed = new(count);
        foreach (TapeEventView view in tape.GetEventViews())
        {
            if (view.Source != "world:encounter" || !tape.Resolve(view.Id, out byte[] receipt)
                || !TapePacketCreator.TryReadWorldEncounterObservation(receipt, out TapeEventID observation)
                || !TapePacketCreator.TryReadAdmissionPlanDomain(receipt, out int domain)
                || !tape.Resolve(observation, out byte[] payload)) continue;
            observed.Add((domain, Encoding.UTF8.GetString(payload)));
        }
        return observed;
    }

    private static IReadOnlyList<string> ExpectedPayloads(WorldEpochSchedule schedule)
        => schedule.Batches.SelectMany(static batch => batch.Payloads).ToArray();

    private static IReadOnlyList<int> ExpectedDomainsRoundRobin(IReadOnlyList<(int Domain, byte[] Bytes)> source)
    {
        Dictionary<int, int> counts = new();
        for (int i = 0; i < source.Count; i++) counts[source[i].Domain] = counts.GetValueOrDefault(source[i].Domain) + 1;
        int max = counts.Values.Max();
        List<int> sequence = new(source.Count);
        for (int row = 0; row < max; row++)
            foreach (int domain in counts.Keys.OrderBy(static value => value))
                if (row < counts[domain]) sequence.Add(domain);
        return sequence;
    }

    private static bool Matches(IReadOnlyList<(int Domain, string Payload)> observed, IReadOnlyList<string> expectedPayloads, IReadOnlyList<int> expectedDomains)
    {
        if (observed.Count != expectedPayloads.Count || observed.Count != expectedDomains.Count) return false;
        for (int i = 0; i < observed.Count; i++)
            if (observed[i].Domain != expectedDomains[i] || observed[i].Payload != expectedPayloads[i]) return false;
        return true;
    }

    private static bool VerifyPlanCheckpointRoundTrip(WorldEpochSchedule schedule)
    {
        string root = Path.Combine(".tmp", $"r20-world-checkpoint-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string corpus = Path.Combine(root, "world");
            schedule.WriteCorpusDirectory(corpus);
            string runtimeWorld = FileCorpus.ComputeWorldSHA256(corpus, "*.txt");
            AdmissionPlan boundPlan = schedule.AdmissionPlan?.BindWorld(runtimeWorld)
                ?? throw new InvalidDataException("epoch schedule lacks an encounter plan");
            CortexRunConfig config = new(
                CorpusPath: corpus,
                ExpectedWorldSHA256: runtimeWorld,
                Curriculum: "eml",
                AdmissionPlan: boundPlan);
            CortexRunConfig restored = Checkpoint.PeekConfig(Checkpoint.EncodeConfig(config));
            AdmissionPlan? actual = restored.AdmissionPlan;
            bool roundTrip = actual is not null
                && restored.CorpusPath == corpus
                && restored.ExpectedWorldSHA256 == runtimeWorld
                && boundPlan.ScheduleID == actual.ScheduleID
                && boundPlan.WorldSHA256 == actual.WorldSHA256
                && boundPlan.AuthorityDigest == actual.AuthorityDigest
                && boundPlan.DomainSequence.SequenceEqual(actual.DomainSequence);
            if (!roundTrip) return false;
            bool sequenceMutationRejected = false;
            try
            {
                int[] mutated = boundPlan.DomainSequence.ToArray();
                int different = Array.FindIndex(mutated, value => value != mutated[0]);
                if (different < 0) throw new InvalidDataException("epoch plan lacks a second domain");
                (mutated[0], mutated[different]) = (mutated[different], mutated[0]);
                _ = new AdmissionPlan(boundPlan.ScheduleID, mutated, boundPlan.WorldSHA256, boundPlan.AuthorityDigest);
            }
            catch (InvalidDataException) { sequenceMutationRejected = true; }
            bool worldMutationRejected = false;
            try
            {
                _ = new AdmissionPlan(boundPlan.ScheduleID, boundPlan.DomainSequence, new string('0', 64), boundPlan.AuthorityDigest);
            }
            catch (InvalidDataException) { worldMutationRejected = true; }
            return sequenceMutationRejected && worldMutationRejected;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static bool VerifyResumedSuffix(WorldEpochSchedule schedule)
    {
        if (schedule.AdmissionPlan is null) return false;
        string root = Path.Combine(".tmp", $"r20-world-plan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string corpus = Path.Combine(root, "world");
            schedule.WriteCorpusDirectory(corpus);
            string runtimeWorld = FileCorpus.ComputeWorldSHA256(corpus, "*.txt");
            AdmissionPlan boundPlan = schedule.AdmissionPlan.BindWorld(runtimeWorld);
            return VerifyResumedSuffix(corpus, "*.txt", boundPlan);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// Replays a registered world prefix through a checkpoint image, then proves
    /// that the resumed admission suffix has identical cursor custody and bytes.
    /// This receipt is in-memory: it never launches Cortex or writes a run.
    internal static bool VerifyResumedSuffix(string corpus, string glob, AdmissionPlan boundPlan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(corpus);
        ArgumentException.ThrowIfNullOrWhiteSpace(glob);
        ArgumentNullException.ThrowIfNull(boundPlan);
        int prefixItems = Math.Min(10, boundPlan.DomainSequence.Count - 1);
        int suffixItems = Math.Min(9, boundPlan.DomainSequence.Count - prefixItems);
        if (prefixItems <= 0 || suffixItems <= 0) return false;

        using Tape straightTape = new();
        Journal straightJournal = new();
        using AdmissionCursor straight = new(corpus, glob, boundPlan);
        AdmissionReceipt first = straight.Admit(straightTape, straightJournal, 0, prefixItems);
        TapeEventID[] prefixIDs = TapePacketCreator.ReadWorldEncounterEventIDs(straightTape).ToArray();
        if (first.CursorAfter != prefixItems || prefixIDs.Length != prefixItems) return false;

        byte[] prefixImage;
        using (MemoryStream stream = new())
        {
            using (CkptWriter writer = new(stream)) straightTape.Save(writer);
            prefixImage = stream.ToArray();
        }
        using Tape resumedTape = new();
        using (MemoryStream stream = new(prefixImage))
        using (CkptReader reader = new(stream))
            resumedTape.Load(reader);
        Journal resumedJournal = new();
        using AdmissionCursor resumed = new(corpus, glob, boundPlan);
        resumed.Restore(prefixIDs.Length, prefixIDs);
        AdmissionReceipt straightSuffix = straight.Admit(straightTape, straightJournal, 1, suffixItems);
        AdmissionReceipt resumedSuffix = resumed.Admit(resumedTape, resumedJournal, 1, suffixItems);
        if (straightSuffix.CursorAfter != resumedSuffix.CursorAfter || straightSuffix.CursorDigest != resumedSuffix.CursorDigest)
            return false;

        TapeEventID[] straightIDs = TapePacketCreator.ReadWorldEncounterEventIDs(straightTape).Skip(prefixIDs.Length).ToArray();
        TapeEventID[] resumedIDs = TapePacketCreator.ReadWorldEncounterEventIDs(resumedTape).Skip(prefixIDs.Length).ToArray();
        if (straightIDs.Length != resumedIDs.Length) return false;
        for (int i = 0; i < straightIDs.Length; i++)
        {
            if (!straightTape.Resolve(straightIDs[i], out byte[] left) || !resumedTape.Resolve(resumedIDs[i], out byte[] right)
                || !left.AsSpan().SequenceEqual(right)) return false;
        }
        return true;
    }

    internal static bool VerifyFixture(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        string root = Path.Combine(".tmp", $"r20-world-novelty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            List<(int Domain, byte[] Bytes)> source = BuildSource();
            WorldEpochSchedule stationary = WorldEpochSchedule.CreateStationary(source, WorldNoveltyOpportunityFloor.RegisteredBoundaryPrefixes);
            WorldEpochSchedule epoch = WorldEpochSchedule.Create(source, EpochCount, [0, 1, 2, 3]);
            WorldEpochSchedule shuffled = WorldEpochSchedule.Create(source, EpochCount, [2, 0, 3, 1]);
            stationary.Validate(); epoch.Validate(); shuffled.Validate();

            string corpusPath = Path.Combine(root, "world");
            epoch.WriteCorpusDirectory(corpusPath);
            string runtimeWorldSHA256 = FileCorpus.ComputeWorldSHA256(corpusPath, "*.txt");
            WorldEpochArmResult stationaryResult = RunArm(root, corpusPath, runtimeWorldSHA256, "stationary", stationary);
            WorldEpochArmResult epochResult = RunArm(root, corpusPath, runtimeWorldSHA256, "epoch", epoch);
            WorldEpochArmResult shuffledResult = RunArm(root, corpusPath, runtimeWorldSHA256, "epoch-order-null", shuffled);

            bool sameWorld = epoch.PayloadMultisetSHA256 == stationary.PayloadMultisetSHA256 && epoch.PayloadMultisetSHA256 == shuffled.PayloadMultisetSHA256
                && stationaryResult.RuntimeWorldSHA256 == runtimeWorldSHA256
                && epochResult.RuntimeWorldSHA256 == runtimeWorldSHA256
                && shuffledResult.RuntimeWorldSHA256 == runtimeWorldSHA256;
            bool distinctSchedules = epoch.ScheduleSHA256 != stationary.ScheduleSHA256 && epoch.ScheduleSHA256 != shuffled.ScheduleSHA256;
            bool stationaryPrefixes = HasAdmittedBoundaries(stationaryResult.TransitionPrefixes, stationary.Batches);
            bool epochPrefixes = HasAdmittedBoundaries(epochResult.TransitionPrefixes, epoch.Batches);
            bool shuffledPrefixes = HasAdmittedBoundaries(shuffledResult.TransitionPrefixes, shuffled.Batches);
            bool metadataAbsent = !File.ReadAllText(Path.Combine(epochResult.RunDirectory, "config.txt"), Encoding.UTF8).Contains(WorldEpochSchedule.ScheduleID, StringComparison.Ordinal);
            bool candidateDiffered = epochResult.CandidateDivergences > 0;
            bool pass = sameWorld && distinctSchedules && stationaryPrefixes && epochPrefixes && shuffledPrefixes && metadataAbsent;
            output.WriteLine($"  world novelty probe · source=data/code frozen census · steps={Steps} · epochs={EpochCount} · payload_multiset={(sameWorld ? "same" : "DRIFT")} · runtime_world={(sameWorld ? runtimeWorldSHA256[..12] : "DRIFT")} · schedule={(distinctSchedules ? "distinct" : "COLLAPSED")} · prefix={(stationaryPrefixes && epochPrefixes && shuffledPrefixes ? "admitted-only" : "BROKEN")} · metadata={(metadataAbsent ? "out-of-band" : "LEAKED")} · reads={epochResult.ReadRows} · canonical={epochResult.CanonicalStateRows} · stationary_candidates={stationaryResult.CandidatePresent} · epoch_candidates={epochResult.CandidatePresent} · epoch_divergences={epochResult.CandidateDivergences} · order_null_candidates={shuffledResult.CandidatePresent} · mechanism={(candidateDiffered ? "PRESENT" : "NOT_OBSERVED")} · infrastructure={(pass ? "PASS" : "FAIL")}");
            return pass;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static WorldEpochArmResult RunArm(string root, string corpusPath, string runtimeWorldSHA256, string arm, WorldEpochSchedule schedule)
    {
        string runDirectory = Path.Combine(root, arm);
        Run run = Run.Create(runDirectory);
        AdmissionPlan? boundPlan = schedule.AdmissionPlan?.BindWorld(runtimeWorldSHA256);
        string scheduleAuthority = boundPlan?.AuthorityDigest
            ?? Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(AdmissionCursor.ScheduleID + "|")));
        run.Write("world-epoch-schedule.ron", $"(schedule:\"{schedule.ActiveScheduleID}\", schedule_sha256:\"{schedule.ScheduleSHA256}\", schedule_authority_sha256:\"{scheduleAuthority}\", payload_multiset_sha256:\"{schedule.PayloadMultisetSHA256}\", runtime_world_sha256:\"{runtimeWorldSHA256}\", order:\"{schedule.Order}\")\n");
        Cortex cortex = new(new CortexConfig
        {
            RunName = "r20-world-novelty-" + arm,
            Seed = Seed,
            Steps = Steps,
            AdmissionPlan = boundPlan,
            Curriculum = new CortexEmlCurriculum
            {
                Corpus = new CogitoCorpus { Path = corpusPath, Glob = "*.txt", ExpectedWorldSHA256 = runtimeWorldSHA256 },
                IntakeBatch = 4,
                Actions = EmlActionSelections.ProcedureGuarded,
                Rung0 = EmlRung0Modes.Armed,
                Deliberation = EmlDeliberationModes.Adaptive,
            },
            Durability = new CortexDurabilityConfig { CheckpointEvery = 0, CurveEvery = 1 },
        });
        int exitCode = cortex.Run(run);
        if (exitCode != 0) throw new InvalidDataException($"world novelty arm '{arm}' exited with code {exitCode}");

        List<int> prefixes = new();
        string admissionPath = run.PathOf("world-admission.tsv");
        if (File.Exists(admissionPath))
            foreach (string line in File.ReadLines(admissionPath).Skip(1))
            {
                string[] fields = line.Split('\t');
                if (fields.Length >= 3 && int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int prefix)) prefixes.Add(prefix);
            }
        int present = 0;
        int divergence = 0;
        int canonicalStates = 0;
        string journalPath = run.PathOf("journal.log");
        if (File.Exists(journalPath))
            foreach (string line in File.ReadLines(journalPath))
            {
                if (line.Contains("\tpolicy-trial-rearm\t", StringComparison.Ordinal)
                    && line.Contains("\tstate=", StringComparison.Ordinal)) canonicalStates++;
                if (!line.Contains("\torganic-comparison\t", StringComparison.Ordinal)) continue;
                Dictionary<string, string> fields = ParseFields(line);
                if (!fields.TryGetValue("raw", out string? rawToken) || !int.TryParse(rawToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out int raw)
                    || !fields.TryGetValue("launchpad", out string? launchpadToken) || !int.TryParse(launchpadToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out int launchpad)) continue;
                if (raw < 0) continue;
                present++;
                if (raw != launchpad) divergence++;
            }
        int reads = File.Exists(run.PathOf("curve.tsv")) ? Math.Max(0, File.ReadAllLines(run.PathOf("curve.tsv")).Length - 1) : 0;
        return new WorldEpochArmResult(run.Dir, prefixes, reads, canonicalStates, present, divergence, runtimeWorldSHA256);
    }

    private static Dictionary<string, string> ParseFields(string line)
    {
        Dictionary<string, string> fields = new(StringComparer.Ordinal);
        foreach (string token in line.Split('\t'))
        {
            int separator = token.IndexOf('=');
            if (separator > 0) fields[token[..separator]] = token[(separator + 1)..];
        }
        return fields;
    }

    private static bool HasAdmittedBoundaries(IReadOnlyList<int> admittedPrefixes, IReadOnlyList<WorldEpochBatch> batches)
    {
        for (int i = 0; i < batches.Count; i++)
            if (!admittedPrefixes.Contains(batches[i].PrefixAfter)) return false;
        return true;
    }

    private static List<(int Domain, byte[] Bytes)> BuildSource()
    {
        string sourceRoot = Path.Combine("data", "code");
        List<string> files = FileCorpus.GatherFiles(sourceRoot, CogitoCorpus.DefaultGlob);
        List<(int Domain, byte[] Bytes)> source = new(EpochCount * ItemsPerDomain);
        int domain = 0;
        for (int fileIndex = 0; fileIndex < files.Count && domain < EpochCount; fileIndex++)
        {
            List<byte[]> lines = new(ItemsPerDomain);
            foreach (string raw in File.ReadLines(files[fileIndex]))
            {
                string text = raw.TrimEnd();
                if (text.Trim().Length == 0) continue;
                lines.Add(Encoding.UTF8.GetBytes(text));
                if (lines.Count == ItemsPerDomain) break;
            }
            if (lines.Count < ItemsPerDomain) continue;
            for (int i = 0; i < lines.Count; i++) source.Add((domain, lines[i]));
            domain++;
        }
        if (domain != EpochCount)
            throw new InvalidDataException($"data/code frozen census requires {EpochCount} domains with {ItemsPerDomain} ordinary lines");
        return source;
    }
}

internal readonly record struct WorldEpochArmResult(
    string RunDirectory,
    IReadOnlyList<int> TransitionPrefixes,
    int ReadRows,
    int CanonicalStateRows,
    int CandidatePresent,
    int CandidateDivergences,
    string RuntimeWorldSHA256);
