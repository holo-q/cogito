namespace Cogito;

internal readonly record struct CampfireCheckpointDelta(
    GrokBellCheckpointDelta Bell,
    EmlSieveCheckpointDelta Eml,
    EmlSamplerCheckpointDelta Sampler,
    int EmlMinted,
    EmlMint[] PendingExact,
    EmlMint[] PendingHypotheses) : ICurriculumCheckpointDelta
{
    public string Kind => "campfire";
    public void Write(CkptWriter writer) => Campfire.WriteCheckpointDelta(writer, in this);
}

public sealed partial class Campfire
{
    ICurriculumCheckpointDelta? ICurriculumCheckpointDeltaOwner.CaptureCheckpointDelta()
        => CaptureCheckpointDelta();

    void ICurriculumCheckpointDeltaOwner.ApplyCheckpointDelta(ICurriculumCheckpointDelta delta, in CheckpointReplayContext replayContext)
    {
        if (!string.Equals(delta.Kind, "campfire", StringComparison.Ordinal))
            throw new InvalidDataException($"curriculum checkpoint delta kind {delta.Kind} does not belong to Campfire");
        CampfireCheckpointDelta typed = delta switch
        {
            CampfireCheckpointDelta value => value,
            OpaqueCurriculumCheckpointDelta value => ReadOpaque(value),
            _ => throw new InvalidDataException($"curriculum checkpoint delta {delta.Kind} does not belong to Campfire"),
        };
        ApplyCheckpointDelta(in typed);

        static CampfireCheckpointDelta ReadOpaque(OpaqueCurriculumCheckpointDelta value)
        {
            using MemoryStream stream = new(value.Payload, writable: false);
            using CkptReader reader = new(stream);
            CampfireCheckpointDelta delta = Campfire.ReadCheckpointDelta(reader);
            if (reader.RemainingBytes != 0) throw new InvalidDataException("campfire checkpoint delta has trailing bytes");
            return delta;
        }
    }

    void ICurriculumCheckpointDeltaOwner.CommitCheckpointDelta(ICurriculumCheckpointDelta captured)
    {
        if (captured is not CampfireCheckpointDelta typed || !string.Equals(captured.Kind, "campfire", StringComparison.Ordinal))
            throw new InvalidDataException($"curriculum checkpoint delta kind {captured.Kind} does not belong to Campfire");
        CommitCheckpointDelta(in typed);
    }

    internal CampfireCheckpointDelta CaptureCheckpointDelta()
        => new(_bell.CaptureCheckpointDelta(), _sieve.CaptureCheckpointDelta(), _sampler.CaptureCheckpointDelta(),
            _emlMinted, _pendingE.ToArray(), _pendingH.ToArray());

    internal void ApplyCheckpointDelta(in CampfireCheckpointDelta delta)
    {
        _bell.ApplyCheckpointDelta(delta.Bell);
        EmlSieveCheckpointDelta eml = delta.Eml;
        _sieve.ApplyCheckpointDelta(in eml);
        EmlSamplerCheckpointDelta sampler = delta.Sampler;
        _sampler.LoadCheckpointDelta(in sampler);
        if (delta.PendingExact is null || delta.PendingHypotheses is null)
            throw new InvalidDataException("campfire pending queue delta is missing");
        if (delta.EmlMinted < 0) throw new InvalidDataException("campfire minted count is negative");
        _emlMinted = delta.EmlMinted;
        _pendingE.Clear(); foreach (EmlMint mint in delta.PendingExact) _pendingE.Enqueue(mint);
        _pendingH.Clear(); foreach (EmlMint mint in delta.PendingHypotheses) _pendingH.Enqueue(mint);
        _chunkRules = null; _chunks = null;
    }

    internal void CommitCheckpointDelta(in CampfireCheckpointDelta delta)
    {
        GrokBellCheckpointDelta bell = delta.Bell;
        _bell.CommitCheckpointDelta(in bell);
        EmlSieveCheckpointDelta eml = delta.Eml;
        _sieve.CommitCheckpointDelta(in eml);
    }

    internal static void WriteCheckpointDelta(CkptWriter w, in CampfireCheckpointDelta delta)
    {
        w.U8(1);
        GrokBellCheckpointDelta bell = delta.Bell;
        EmlSieveCheckpointDelta eml = delta.Eml;
        EmlSamplerCheckpointDelta sampler = delta.Sampler;
        WriteBell(w, in bell);
        EmlSieve.WriteCheckpointDelta(w, in eml);
        EmlSampler.WriteCheckpointDelta(w, in sampler);
        w.I32(delta.EmlMinted);
        w.I32(delta.PendingExact.Length);
        foreach (EmlMint mint in delta.PendingExact) WriteMint(w, in mint);
        w.I32(delta.PendingHypotheses.Length);
        foreach (EmlMint mint in delta.PendingHypotheses) WriteMint(w, in mint);
    }

    internal static CampfireCheckpointDelta ReadCheckpointDelta(CkptReader r)
    {
        if (r.U8() != 1) throw new InvalidDataException("unknown Campfire checkpoint delta version");
        GrokBellCheckpointDelta bell = ReadBell(r);
        EmlSieveCheckpointDelta eml = EmlSieve.ReadCheckpointDelta(r);
        EmlSamplerCheckpointDelta sampler = EmlSampler.ReadCheckpointDelta(r);
        int minted = r.I32();
        EmlMint[] exact = ReadMints(r);
        EmlMint[] hypotheses = ReadMints(r);
        return new(bell, eml, sampler, minted, exact, hypotheses);
    }

    private static void WriteBell(CkptWriter w, in GrokBellCheckpointDelta delta)
    {
        w.U8(2);
        w.I32(delta.IngestedEdits.Length);
        foreach (GrokBellMaskEdit edit in delta.IngestedEdits) { w.I32(edit.Domain); w.I32(edit.Index); }
        w.I32(delta.Cursor); w.I32(delta.Round); w.I32(delta.Ingested); w.I32(delta.MixCursor);
        w.I32(delta.LastSpans.Length);
        foreach (int value in delta.LastSpans) w.I32(value);
        w.I32(delta.CachedCv.Length); foreach (double value in delta.CachedCv) w.F64(value);
        w.I32(delta.CachedK.Length); foreach (int value in delta.CachedK) w.I32(value);
        w.I32(delta.Meters.Length);
        foreach (DomainMeterCheckpointDelta meter in delta.Meters)
        {
            w.I32(meter.Spans); w.F64(meter.Cv); w.I32(meter.K); w.F64(meter.BestSym); w.I32(meter.BelowStreak);
            w.I32(meter.StreakResets); w.I32(meter.Crossings); w.I32(meter.FirstCrossRound); w.I32(meter.LockRound); w.I32(meter.LockBytes); w.Bool(meter.WasBelow);
        }
        w.I32(delta.RecentDomains.Length); foreach (int domain in delta.RecentDomains) w.I32(domain);
    }

    private static GrokBellCheckpointDelta ReadBell(CkptReader r)
    {
        byte version = r.U8();
        bool[][] ingested = Array.Empty<bool[]>(); GrokBellMaskEdit[] edits;
        int domains;
        if (version == 1)
        {
            domains = ReadCount(r, "Campfire domain count");
            ingested = new bool[domains][];
            List<GrokBellMaskEdit> legacy = new();
            for (int d = 0; d < domains; d++) { int n = ReadCount(r, "Campfire domain span count"); ingested[d] = new bool[n]; for (int i = 0; i < n; i++) if (r.Bool()) { ingested[d][i] = true; legacy.Add(new(d, i)); } }
            edits = legacy.ToArray();
        }
        else if (version == 2)
        {
            int count = ReadCount(r, "Campfire mask edit count");
            edits = new GrokBellMaskEdit[count];
            for (int i = 0; i < count; i++) edits[i] = new(r.I32(), r.I32());
            domains = ReadCount(r, "Campfire domain count");
        }
        else throw new InvalidDataException("unknown Campfire grokbell checkpoint delta version");
        int cursor = r.I32(), round = r.I32(), total = r.I32(), mix = r.I32();
        int[] lastSpans = ReadInts(r, "Campfire last-span count");
        double[] cachedCv = ReadDoubles(r, "Campfire CV count");
        int[] cachedK = ReadInts(r, "Campfire K count");
        int meterCount = ReadCount(r, "Campfire meter count"); DomainMeterCheckpointDelta[] meters = new DomainMeterCheckpointDelta[meterCount];
        for (int i = 0; i < meterCount; i++) meters[i] = new(r.I32(), r.F64(), r.I32(), r.F64(), r.I32(), r.I32(), r.I32(), r.I32(), r.I32(), r.I32(), r.Bool());
        int[] recent = ReadInts(r, "Campfire recent-domain count");
        return new(ingested, edits, cursor, round, total, mix, lastSpans, cachedCv, cachedK, meters, recent);
    }

    private static int[] ReadInts(CkptReader r, string label)
    { int count = ReadCount(r, label); int[] values = new int[count]; for (int i = 0; i < count; i++) values[i] = r.I32(); return values; }
    private static double[] ReadDoubles(CkptReader r, string label)
    { int count = ReadCount(r, label); double[] values = new double[count]; for (int i = 0; i < count; i++) values[i] = r.F64(); return values; }
    private static int ReadCount(CkptReader r, string label)
    { int count = r.I32(); if (count < 0 || count > 1_000_000) throw new InvalidDataException($"{label} exceeds bound"); return count; }

    private static void WriteMint(CkptWriter w, in EmlMint mint)
    {
        w.Str(mint.Line); w.Str(mint.Prog);
        w.I64(mint.Sig.R1); w.I64(mint.Sig.I1); w.I64(mint.Sig.R2); w.I64(mint.Sig.I2);
        w.U8((byte)mint.Grade); w.Bool(mint.Corrob);
    }

    private static EmlMint[] ReadMints(CkptReader r)
    {
        int count = r.I32();
        if (count < 0 || count > 1_000_000) throw new InvalidDataException("Campfire pending mint queue exceeds bound");
        EmlMint[] mints = new EmlMint[count];
        for (int i = 0; i < count; i++)
            mints[i] = new(r.Str(), r.Str(), new EmlSig(r.I64(), r.I64(), r.I64(), r.I64()), (char)r.U8(), r.Bool());
        return mints;
    }
}
