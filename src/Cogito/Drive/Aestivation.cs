namespace Cogito;

using Cogito.Induct;   // Loom — the standing grammar whose parse decides shed


// ── AESTIVATION ──  the consolidation organ: shed / drop / defrag / GC, the metabolism's dormant sleep phase
// (BRIDGE Final Ontology). Its shared kernel is the EVACUATION decision — which resident events leave RAM (shed) or
// leave the view (drop) under the recency + turnover guards. Three drives run an aestivation over their tape
// (Cortex.Consolidate, the mesh self-play loop, the solve mind), and all three decide the same evacuation set the same
// way; the Resplice orchestration around it genuinely differs (single vs multi-loom, pre- vs mid-aestivation) and
// stays at each drive.
internal static class ConsolidationPhase
{
    // the evacuation bounds — one authority for every drive's aestivation (were triplicated with drifted names across
    // Cortex / Mesh / AgentSolve, the same 256/1024 discipline the drive proved at flagship scale).
    internal const int ShedKeepRecentEvents = 256;      // the RESIDENT RECENCY GUARD — an event this recent stays resident regardless (never shed, never dropped), so a working-window read (frontier residual, the vest co-walk's fresh tail) is never starved. Strictly wider than every recent-window organ (GC promotion 160, anti-unify 192, weave partners).
    internal const int DropUnvestedAfterEvents = 1024;  // HYPOTHESIS TURNOVER — an unvested Replay older than this many appends was never corroborated by reality (no independent source re-derived it): drop it (its ReplayCount slot frees mint headroom; the event byte log keeps the forensics). Strictly wider than the shed guard, so an event in the recency window can never be dropped.

    /// Decide the aestivation's evacuation sets under the recency + turnover guards and evacuate them from the tape.
    /// SHED — an EVIDENCE or neutral EXECUTION event whose CURRENT parse through `corroborationLoom` is a single symbol
    /// (the grammar generates it whole → ParsedLenOf ≤ 1) moves its raw bytes to the event byte log and STAYS in the
    /// view: no count, use, or criticality read moves, only the RAM (order-free). DROP — an unvested Replay past the turnover window leaves
    /// the view (a hypothesis reality never corroborated); `dropUnvested` gates it: the drive withholds drops on a
    /// LIGHT aestivation (a drop kills counts only a full-aestivation Resplice can retire — kill→resume exactness), the
    /// mesh and solve aestivations always drop. Both sets are id-ascending (deterministic — the Vow). Returns the
    /// (shed, drop) counts;
    /// the caller owns the surrounding Resplice(s) and the journal line (their text + loom topology genuinely differ).
    internal static TapeEvacuation Evacuate(Tape tape, Loom corroborationLoom, bool dropUnvested)
    {
        var shedSet = new List<TapeEventID>();
        var dropSet = new List<TapeEventID>();
        long keepAbove = tape.NextId - ShedKeepRecentEvents;
        long dropBelow = tape.NextId - DropUnvestedAfterEvents;
        for (int i = 0; i < tape.Count; i++)
        {
            TapeEventID id = tape.ResidentEventIDs[i];
            if (id.Value >= keepAbove) continue;                                       // recency guard — every working-window read stays resident
            Provenances provenance = tape.ProvenanceOf(id);
            if (tape.IsEvidenceAt(i) || provenance == Provenances.Execution)
            {
                if (corroborationLoom.ParsedLenOf(id.Value) is >= 0 and <= 1) shedSet.Add(id);
            }
            else if (provenance == Provenances.Replay && dropUnvested && id.Value < dropBelow)
            {
                dropSet.Add(id);                                                       // only an unvested hypothesis rots
            }
        }
        shedSet.Sort((a, b) => a.Value.CompareTo(b.Value));
        dropSet.Sort((a, b) => a.Value.CompareTo(b.Value));
        return tape.Evacuate(shedSet, dropSet);
    }
}
