namespace Cogito;

using System.Text;
using Cogito.Grammar;
using Cogito.Induct;

// ── THE MEMORY CENSUS (diagnostic) ──  the working-set profiler behind Cortex resume probes.
// The RAM question ("does the grammar fit the memory budget?") cannot be answered from the checkpoint size — the
// 6.3MB durable image inflates to GBs of RESIDENT structure (derived indexes, pool postings, per-stride caches,
// the journal's content-addressed second copy of the tape). This organ names WHERE, with numbers, two ways that
// must reconcile (Total Accounting): (1) PHASE BRACKETS — GC.GetTotalMemory(true) deltas around world-build /
// state-load / energy-arm, the source of record per phase; (2) the STRUCTURE WALK — exact item counts × documented
// x64 layout estimates per collection, the decomposition of those deltas. Σ(walk) vs the managed total names the
// unattributed residual honestly. Read-only: a census never mutates an organ (the drive it profiles is untouched).
//
// The walk's byte figures are ESTIMATES (documented formulas below — object headers, dictionary entry layouts,
// List growth slack are approximated); the ITEM COUNTS are exact. The brackets are exact but coarse. Trust the
// brackets for totals, the walk for attribution.

public static class MemStat
{
    /// One census row — a subsystem's part, its exact item count, and its estimated retained bytes.
    public readonly record struct Row(string Sub, string Part, long Items, long Bytes, string Note = "");

    /// Managed-heap total after a forced full collection — the census's source-of-record accumulator. EXPENSIVE
    /// (forces gen2+LOH); only the memstat verb calls it, never a live drive path.
    public static long Managed() => GC.GetTotalMemory(forceFullCollection: true);

    // ── x64 CoreCLR layout constants (estimates, documented) ──
    private const long ObjHdr   = 24;   // object header (8) + method table (8) + min fields pad — the practical floor per heap object
    private const long ArrHdr   = 24;   // array header (obj header + length)
    private const long ListHdr  = 56;   // List<T> object (header + array ref + size/version) + its array's header
    private const long DictSlot = 12;   // Dictionary<K,V> per-entry fixed cost beyond K+V: hashCode+next (8) + bucket int (4)
    private const double DictLoad = 1.3; // prime-capacity headroom over Count (average)

    private static long Str(long chars) => chars <= 0 ? 0 : ObjHdr + 2 * chars;                    // string: header + UTF-16 payload
    private static long Arr(long items, long itemSize) => items < 0 ? 0 : ArrHdr + items * itemSize;
    private static long Dict(long count, long kvBytes) => count <= 0 ? 64 : (long)((DictSlot + kvBytes) * count * DictLoad) + 64;
    private static long List(long items, long itemSize) => ListHdr + (long)(items * itemSize * 1.25);   // ×1.25 avg growth slack

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    //  THE STRUCTURE WALK — exact counts per collection, estimated bytes, one subsystem at a time
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    public static List<Row> Census(Tape tape, MemoryHierarchy memory, in RePairResult g, Journal journal, ICurriculum curriculum, Cogito.Induct.Loom? loom = null, Transitions? trans = null)
    {
        var rows = new List<Row>();

        // ── ENERGY — the per-stride Markov evidence, sealed CSR (grammar-proportional: successor entries carry
        // key+count+log, rows carry start+total+miss+index slot; no run-lifetime growth pressure remains).
        if (trans is not null)
        {
            var (entries, rowCount) = trans.CsrMass;
            rows.Add(new Row("energy", "transitions", entries,
                Arr(entries, 4 + 4 + 8) + Arr(rowCount, 4 + 4 + 8) + Dict(rowCount, 12 + 4),
                $"{rowCount:N0} ctx rows (sealed CSR, logs precomputed)"));
        }

        // ── TAPE — RESIDENT source-of-record spans + the derived resolve/containment indexes + the span-log tables ──
        long spanBytes = 0;
        foreach (var s in tape.ResidentEventBytes) spanBytes += Arr(s.Length, 1);
        rows.Add(new Row("tape", "spans", tape.Count, spanBytes + List(tape.Count, 8 + 8 + 8 + 1),
            $"{tape.ResidentBytes}B resident of {tape.ByteLength}B view · 4 parallel lists"));
        var (buckets, bucketSlots) = tape.ContentIndexMass();
        rows.Add(new Row("tape", "indexes", tape.Count * 2L + bucketSlots,
            Dict(tape.Count, 8 + 8) + Dict(tape.Count, 8 + 4) + Dict(buckets, 8 + 8) + buckets * ListHdr + bucketSlots * 8,
            $"byId+idToIdx {tape.Count}×2 · byContent {buckets} buckets/{bucketSlots} slots"));
        var (logRecs, logBytes) = tape.LogMass();
        if (logRecs > 0)
            rows.Add(new Row("tape", "spanlog", logRecs, Dict(logRecs, 8 + 40) + logRecs * 8,
                $"{Fmt(logBytes)} ON DISK (evacuated bytes) · shed {tape.ShedCount} · dropped {tape.DroppedCount}"));

        // ── LOOM — the persistent induction organ (phase 2: the live-induce state that replaced the re-derive churn) ──
        if (loom is not null)
        {
            var lm = loom.Mass();
            rows.Add(new Row("loom", "arena", lm.ArenaSlots, Arr(lm.ArenaSlots, 4 + 4 + 4 + 1 + 1 + 4) + lm.Segs * 40,
                $"{lm.LiveSyms} live syms · {lm.Segs} segs (compacts at rebase) · 18B/slot incl active-occ index"));
            rows.Add(new Row("loom", "counts", lm.CntKeys,
                Dict(lm.CntKeys, 8 + 4) + Dict(lm.CntKeys, 8 + 8) + lm.CntKeys * ListHdr + lm.OccPosts * 4
                + Arr(lm.HeapCount, 16) + Dict(lm.HeapCount, 8 + 4),
                $"{lm.OccPosts} active occ posts · heap {lm.HeapCount} indexed nodes + key index"));
            rows.Add(new Row("loom", "grammar", loom.RuleCount, Dict(lm.RankEntries, 8 + 8) + Arr(loom.RuleCount, 64) + lm.RankEntries * 16,
                $"{lm.RankEntries} rank entries ({loom.AliasCount} aliases)"));
        }

        // ── GRAMMAR — the live rules + compressed tape (the working artifact the trunk carries) ──
        long patSyms = 0, segs = 0;
        var rules = g.Rules ?? [];
        foreach (var r in rules) { patSyms += r.Pattern.Length; if (r.Segs is { } sg) segs += sg.Length; }
        rows.Add(new Row("grammar", "rules", rules.Length,
            Arr(rules.Length, 64) + rules.Length * ArrHdr + patSyms * 4 + segs * 16,
            $"Σpattern {patSyms} syms · {segs} demoted segs"));
        rows.Add(new Row("grammar", "compressed", g.Compressed?.Length ?? 0, Arr(g.Compressed?.Length ?? 0, 4)));

        // ── MEMORY HIERARCHY — the persistent night-shift indexes ──
        var idx = memory.Index;
        long simPosts = idx.PostingMass();
        rows.Add(new Row("memory", "simhash", idx.Count,
            List(idx.Count, 8 + 8 + 8) + Dict(idx.Count, 4 + 4) + Dict(idx.BucketCount, 2 + 8) + idx.BucketCount * ListHdr + simPosts * 4,
            $"{idx.BucketCount} buckets · {simPosts} postings"));
        var (gramKeys, gramPosts) = memory.Grams.Mass();
        rows.Add(new Row("memory", "grams", gramKeys,
            Dict(gramKeys, 8 + 8) + gramKeys * (ListHdr + ArrHdr) + gramPosts * 16,
            $"8-gram containment postings · {gramPosts} posts (tape-∝)"));
        var (chains, chainSegs) = memory.DemotionMass();
        rows.Add(new Row("memory", "demotions", chains, Dict(chains, 4 + 8) + chains * ArrHdr + chainSegs * 16, $"{chainSegs} segs"));
        long paradigmChars = 0; long members = 0;
        foreach (var kv in memory.ConsolidationPhaseParadigm.SlotMembers)
        {
            paradigmChars += kv.Key.Length;
            members += kv.Value.Count;
            foreach (var m in kv.Value) paradigmChars += m.Length;
        }
        rows.Add(new Row("memory", "paradigm", memory.ConsolidationPhaseParadigm.SlotMembers.Count, Str(paradigmChars) + members * 8, $"{members} members"));

        // ── JOURNAL — the RESIDENT line tail backing journal.log (shed lines live on disk only) ──
        long journalChars = journal.LineChars();
        int journalResident = journal.ResidentLines.Count;
        rows.Add(new Row("journal", "lines", journalResident, List(journalResident, 8) + journalChars * 2 + journalResident * ObjHdr,
            $"{journalChars} chars · {journal.ShedLineCount} shed"));

        // ── CURRICULUM — pool + frontier postings + the EML sieve (campfire = bell + sieve) ──
        switch (curriculum)
        {
            case Campfire cf:
                CensusBell(rows, cf.Bell);
                CensusSieve(rows, cf.SieveOrgan);
                var (pe, ph) = cf.PendingMass();
                rows.Add(new Row("eml", "pending", pe + ph, (pe + ph) * 32, $"E {pe} · H {ph}"));
                break;
            case GrokBell gb: CensusBell(rows, gb); break;
            case FlatPool fp:
                var fm = fp.Frontier.Mass();
                rows.Add(new Row("frontier", "flatpool", fm.DeepKeys + fm.BiKeys, FrontierBytes(fm),
                    $"deep {fm.DeepKeys}k/{fm.DeepPosts}p · bi {fm.BiKeys}k/{fm.BiPosts}p"));
                break;
            default:
                rows.Add(new Row("curriculum", curriculum.GetType().Name, 0, 0, "not instrumented (lane-locked organ)"));
                break;
        }
        return rows;
    }

    private static void CensusBell(List<Row> rows, GrokBell bell)
    {
        var bm = bell.Mass();
        rows.Add(new Row("curriculum", "pool", bm.PoolSpans, bm.PoolBytes + bm.PoolSpans * (ArrHdr + 8 + 8),
            $"{bm.PoolBytes}B corpus payload (byDom + mixPool refs)"));
        long fkeys = 0, fbytes = 0, fposts = 0;
        foreach (var f in bm.Frontiers)
        {
            var m = f.Mass();
            fkeys += m.DeepKeys + m.BiKeys; fposts += m.DeepPosts + m.BiPosts;
            fbytes += FrontierBytes(m);
        }
        rows.Add(new Row("frontier", "perdomain", fkeys, fbytes, $"{bm.Frontiers.Length} domains · {fposts} postings (pool-∝, constant)"));
    }

    // PackedSpanPostings per rail: sorted keys (8B) + CSR starts (4B, K+1) + span ids (4B) — three flat arrays,
    // no per-key Dictionary entry / List / backing-array headers, no per-posting Off (the ~5× layout cut).
    private static long FrontierBytes(in (long DeepKeys, long DeepPosts, long BiKeys, long BiPosts) m)
        => Arr(m.DeepKeys, 8) + Arr(m.DeepKeys + 1, 4) + Arr(m.DeepPosts, 4)
         + Arr(m.BiKeys, 8) + Arr(m.BiKeys + 1, 4) + Arr(m.BiPosts, 4);

    private static void CensusSieve(List<Row> rows, EmlSieve sieve)
    {
        var sm = sieve.Mass();
        rows.Add(new Row("eml", "canon", sieve.DistinctValues, Dict(sieve.DistinctValues, 32 + 8) + Str(sm.CanonChars) * 1, $"{sm.CanonChars} prog chars"));
        rows.Add(new Row("eml", "minted", sm.MintedKeys, Dict(sm.MintedKeys, 8) + sm.MintedChars * 2 + sm.MintedKeys * ObjHdr, $"{sm.MintedChars} chars (dedup set)"));
        rows.Add(new Row("eml", "mintlog", sieve.MintLog.Count, List(sieve.MintLog.Count, 56) + sm.LogChars * 2 + sieve.MintLog.Count * 2 * ObjHdr,
            $"{sm.LogChars} chars · certs ×48B ride along"));
        rows.Add(new Row("eml", "sighits", sm.SigHits, Dict(sm.SigHits, 32 + 4), "basin census (sig → hits)"));
        rows.Add(new Row("eml", "cas", sieve.DistinctCerts, Dict(sieve.DistinctCerts, 56 + 24) + sm.CasRepChars * 2 + sieve.DistinctCerts * ObjHdr,
            $"{sm.CasRepChars} rep chars · +mintCerts {sieve.MintLog.Count}×48B"));
        rows.Add(new Row("eml", "anomalies", sieve.Anomalies.Count, Dict(sieve.Anomalies.Count, 32 + 40), "capped 1024"));
        rows.Add(new Row("eml", "grader", sm.GraderKeys, Dict(sm.GraderKeys, 8 + 8) + sm.GraderChars * 2 + sm.GraderKeys * (ObjHdr + ArrHdr + 2 * 96),
            $"ladder cache — {sm.GraderChars} key chars, 2 ladders/entry"));
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────
    //  RENDER — the aligned table + the Total-Accounting footer
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// Render the census: rows sorted bytes-descending, Σ, the phase-bracket deltas, and the unattributed
    /// residual vs the managed total (the honest gap — transient churn, LOH slack, un-walked structure).
    public static string Render(List<Row> rows, (string Name, long Bytes)[] brackets, long managedTotal)
    {
        var sb = new StringBuilder();
        sb.AppendLine("── MEMORY CENSUS (structure walk — counts exact, bytes estimated) ──");
        long sum = 0;
        foreach (var r in rows.OrderByDescending(r => r.Bytes))
        {
            sum += r.Bytes;
            sb.AppendLine($"  {r.Sub,-10} {r.Part,-11} {Fmt(r.Bytes),10}  items {r.Items,10:N0}  {r.Note}");
        }
        sb.AppendLine($"  {"Σ walk",-22} {Fmt(sum),10}");
        sb.AppendLine("── PHASE BRACKETS (GC.GetTotalMemory(true) deltas — exact, coarse) ──");
        foreach (var (name, bytes) in brackets) sb.AppendLine($"  {name,-22} {Fmt(bytes),10}");
        sb.AppendLine($"  {"managed total",-22} {Fmt(managedTotal),10}");
        sb.AppendLine($"  {"unattributed (total−Σ)",-22} {Fmt(managedTotal - sum),10}   (churn slack · un-walked structure · estimate error)");
        return sb.ToString();
    }

    private static string Fmt(long b) => b switch
    {
        >= 1L << 30 => $"{b / (double)(1L << 30):F2}GB",
        >= 1L << 20 => $"{b / (double)(1L << 20):F1}MB",
        >= 1L << 10 => $"{b / (double)(1L << 10):F1}KB",
        _           => $"{b}B",
    };
}
