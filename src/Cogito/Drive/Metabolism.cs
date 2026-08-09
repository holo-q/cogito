namespace Cogito;

// ── THE METABOLISM ──  the curiosity organ mounted by drive + mesh + the energy policy.

/// The curiosity-metabolism (λ=0.3): a leaky per-unit novelty decay φ' = φ / (1 + λ·recent_emit) that DEMOTES the
/// over-fired unit before it can basin — the ANTI-COLLAPSE organ (naive autoregressive loopback amplifies the
/// Goodhart-into-repetition; this un-collapses it). MECHANISM-INDEPENDENT: it reweights whatever a generator emits
/// (a Markov walk's per-chunk pick, a coupling field's per-edge score) — every generator that consults it rides
/// the same organ. Floats live sampler-side only (never near consensus); deterministic via the caller's seed.
public sealed class Metabolism(double lambda = 0.3)
{
    private readonly Dictionary<uint, double> _recent = new();

    internal readonly record struct MetabolismCheckpointDelta(KeyValuePair<uint, double>[] Recent)
    {
        internal bool IsEmpty => Recent.Length == 0;
    }

    public double Lambda => lambda;

    internal MetabolismCheckpointDelta CaptureCheckpointDelta()
        => new(_recent.OrderBy(static pair => pair.Key).ToArray());

    internal void ApplyCheckpointDelta(in MetabolismCheckpointDelta delta)
    {
        if (delta.Recent is null) throw new InvalidDataException("metabolism checkpoint delta has no recency table");
        _recent.Clear();
        foreach (KeyValuePair<uint, double> pair in delta.Recent)
        {
            if (!double.IsFinite(pair.Value) || pair.Value < 0)
                throw new InvalidDataException("metabolism checkpoint recency is invalid");
            _recent[pair.Key] = pair.Value;
        }
    }

    internal void CommitCheckpointDelta() { }

    internal static void WriteCheckpointDelta(CkptWriter writer, in MetabolismCheckpointDelta delta)
    {
        writer.U8(1); writer.I32(delta.Recent.Length);
        foreach (KeyValuePair<uint, double> pair in delta.Recent) { writer.U32(pair.Key); writer.F64(pair.Value); }
    }

    internal static MetabolismCheckpointDelta ReadCheckpointDelta(CkptReader reader)
    {
        if (reader.U8() != 1) throw new InvalidDataException("unknown metabolism checkpoint delta version");
        int count = reader.I32(); if (count < 0 || count > 1_000_000) throw new InvalidDataException("metabolism recency table exceeds bound");
        KeyValuePair<uint, double>[] recent = new KeyValuePair<uint, double>[count];
        for (int i = 0; i < count; i++) recent[i] = new(reader.U32(), reader.F64());
        return new(recent);
    }

    /// The novelty weight of a candidate chunk — 1 for the untouched, decaying toward 0 as it over-fires.
    public double Weight(uint chunk) => 1.0 / (1.0 + lambda * _recent.GetValueOrDefault(chunk));

    /// Record that a chunk was just emitted — its next weight drops (rides the marginal compression away from it).
    public void Fired(uint chunk) => _recent[chunk] = _recent.GetValueOrDefault(chunk) + 1.0;

    /// Between-block leak so "recent" stays RECENT — an idiom demoted last block recovers novelty if the drive
    /// moves on. `keep` in (0,1): 1 = never forgive (rigid), 0 = amnesiac. 0.5 halves the memory each block.
    public void Leak(double keep = 0.5)
    {
        foreach (var k in _recent.Keys.ToArray())
        {
            double v = _recent[k] * keep;
            if (v < 1e-6) _recent.Remove(k); else _recent[k] = v;
        }
    }

    // checkpoint — the recency table, key-sorted (Save∘Load∘Save = identity; consumers only Weight()-lookup and
    // Leak()-map it, so the reload's different insertion order can never reach an output).
    public void Save(CkptWriter w)
    {
        w.I32(_recent.Count);
        foreach (var k in _recent.Keys.Order()) { w.U32(k); w.F64(_recent[k]); }
    }

    public void Load(CkptReader r)
    {
        _recent.Clear();
        int n = r.I32();
        for (int i = 0; i < n; i++) { uint k = r.U32(); _recent[k] = r.F64(); }
    }
}
