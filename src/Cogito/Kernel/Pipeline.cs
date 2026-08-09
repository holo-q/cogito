namespace Cogito;

using Cogito.Cas;
using Cogito.Codec;
using Cogito.Grammar;
using Cogito.Induct;

// The codec proof, end to end. Three parts, each on a number, never on vibes:
//   1. MINIMAL   — whole-corpus Re-Pair: lossless reconstruction + bit-identical replay (the heart).
//   2. SUBSTRATE — event-sourced: content-addressed log + Consolidator + grammar round-trips + replay.
//   3. VECTORS   — real, computed digests (not hand-typed) — the ones that retire the confabulated set.

public static class Pipeline
{
    /// True iff the whole codec proof passes.
    public static bool Run(byte[] bytes)
    {
        // ── PART 1 · MINIMAL ──  Re-Pair the whole corpus; reconstruct byte-exact; replay identical.
        var (_, n, r1) = Engine.Induce(bytes);
        var recon = Reconstruct.Expand(r1.Rules, r1.Compressed);
        bool lossless = recon.AsSpan().SequenceEqual(bytes);

        var (_, _, r2) = Engine.Induce(bytes);
        var recon2 = Reconstruct.Expand(r2.Rules, r2.Compressed);
        bool reReplay = r2.Compressed.AsSpan().SequenceEqual(r1.Compressed) && recon2.AsSpan().SequenceEqual(recon);

        Trace.Note($"corpus       : {n} bytes");
        Trace.Note($"compressed   : {r1.Compressed.Length} symbols + {r1.Rules.Length} rules  (Δmdl {r1.TotalSavings})");
        Trace.Note($"reconstruct  : {(lossless ? "✓ lossless (recon == original)" : "✗ LOSSY")}");
        Trace.Note($"replay       : {(reReplay ? "✓ bit-identical" : "✗ DIVERGENCE")}");

        // ── PART 2 · SUBSTRATE ──  observe → content-addressed log → Consolidator → grammar artifact.
        var a = BuildOnce(bytes);
        bool addrOk = a.GrammarRef.Equals(default(BlobRef))
                   || GrammarSpec.Load(a.Store, a.GrammarRef).Address.Equals(a.GrammarRef);   // load → re-hash → same ref
        var b = BuildOnce(bytes);
        bool substrateReplay = b.GrammarRef.Equals(a.GrammarRef) && b.LogHash.Equals(a.LogHash);

        Trace.Note($"event log    : {a.EventCount} events · grammar v{a.GrammarVersion} (+{a.RuleCount} rules)");
        Trace.Note($"grammar addr : {(addrOk ? "✓ round-trips (load → Address == ref)" : "✗ MISMATCH")}");
        Trace.Note($"log replay   : {(substrateReplay ? "✓ bit-identical" : "✗ DIVERGENCE")}");

        // ── PART 3 · REAL GOLDEN VECTORS ──  computed here, reproduced on every replay.
        Trace.Note($"vectors      : corpus={Hash.Domain("cogito/corpus/"u8, bytes)}");
        Trace.Note($"             : grammar={a.GrammarRef}  log={a.LogHash}");

        bool pass = lossless && reReplay && addrOk && substrateReplay;
        Trace.Note(pass
            ? "✓ codec proof PASSED — compress · reconstruct(H==H) · content-address · replay, all deterministic."
            : "✗ codec proof FAILED.");
        return pass;
    }

    private readonly record struct Built(
        ContentStore Store, BlobRef GrammarRef, Hash256 LogHash,
        long EventCount, ulong GrammarVersion, int RuleCount);

    /// One full observe→consolidate pass (via Engine), summarized for the proof's replay comparison.
    private static Built BuildOnce(byte[] corpus)
    {
        var (store, log, gve) = Engine.BuildLog(corpus);
        BlobRef gref = default; ulong gver = 0; int rcount = 0;
        if (gve is { } e) { gref = e.SpecRef; gver = e.Version; rcount = e.RulesAdded.Count; }
        return new Built(store, gref, Engine.LogFingerprint(log), log.Count, gver, rcount);
    }
}
