namespace Cogito.Exec;

// Fuel — the step budget, a NEW substrate primitive (: Energy is a sampling FIELD, Metabolism a
// novelty-decay REWEIGHTER — layer Fuel as its own thing, do NOT overload either). One unit is spent per VM
// step — per symbol the control stack pops, whether a leaf opcode or a nonterminal CALL — so a self-referential
// loop rule `R = BODY R` HALTS when Fuel drains: Fuel substitutes for TERMINATION, not for the effect-type
// (the ΔH taxonomy is unchanged; Fuel only bounds the unroll a conditional would otherwise bound). Integer,
// monotone-down.
public struct Fuel(int budget)
{
    public int Remaining = budget;
    public readonly bool Used => Remaining <= 0;

    /// Spend one unit; false (and no decrement) once exhausted — the loop reads this as its halt gate.
    public bool TrySpend()
    {
        if (Remaining <= 0) return false;
        Remaining--;
        return true;
    }
}

// The FUEL JOURNAL — per-RULE dynamic executed cost, the run-time twin of a rule's static description-length
// (: "the per-rule FUEL JOURNAL (dynamic executed cost) beside wuses, for MDL-rent"). It is the
// LOAD lane's grow-once tenant, homed in the VM's OWN struct; Pearl.cs stays a breadth/reflection instrument.
// Three per-rule columns:
//   `Calls[r]`     — times rule r was invoked (expanded) this run: its dynamic USE frequency.
//   `BodyFuel[r]`  — Fuel spent on symbols DIRECTLY in r's body, terminals and nonterminal calls alike.
//   `LeafFuel[r]`  — the terminal-op subset of BodyFuel, so call-heavy wrappers and arithmetic-heavy bodies
//                    separate cleanly. A child's cost is not inherited by its parent; this is direct load.
public struct FuelJournal
{
    public readonly long[] Calls;
    public readonly long[] BodyFuel;
    public readonly long[] LeafFuel;

    public FuelJournal(int ruleCount)
    {
        Calls = new long[ruleCount];
        BodyFuel = new long[ruleCount];
        LeafFuel = new long[ruleCount];
    }

    /// The heaviest-executed rule this run (max direct body fuel), or -1 when no rule body spent Fuel.
    public readonly int HottestRule()
    {
        int best = -1; long top = -1;
        for (int r = 0; r < BodyFuel.Length; r++)
            if (BodyFuel[r] > top) { top = BodyFuel[r]; best = r; }
        return top > 0 ? best : -1;
    }

    public readonly long TotalCalls
    {
        get { long n = 0; foreach (var v in Calls) n += v; return n; }
    }

    public readonly long TotalBodyFuel
    {
        get { long n = 0; foreach (var v in BodyFuel) n += v; return n; }
    }

    public readonly long TotalLeafFuel
    {
        get { long n = 0; foreach (var v in LeafFuel) n += v; return n; }
    }

    /// Top rows by direct body fuel, then calls. The arrays stay the authoritative meter; rows are a report view.
    public readonly FuelJournalRow[] TopRows(int take)
    {
        var rows = new List<FuelJournalRow>(Calls.Length);
        for (int r = 0; r < Calls.Length; r++)
            if (Calls[r] != 0 || BodyFuel[r] != 0 || LeafFuel[r] != 0)
                rows.Add(new FuelJournalRow(r, Calls[r], BodyFuel[r], LeafFuel[r]));
        rows.Sort(static (a, b) =>
        {
            int c = b.BodyFuel.CompareTo(a.BodyFuel);
            if (c != 0) return c;
            c = b.Calls.CompareTo(a.Calls);
            return c != 0 ? c : a.Rule.CompareTo(b.Rule);
        });
        if (take >= 0 && rows.Count > take) rows.RemoveRange(take, rows.Count - take);
        return rows.ToArray();
    }
}

public readonly record struct FuelJournalRow(int Rule, long Calls, long BodyFuel, long LeafFuel)
{
    public double BodyFuelPerCall => Calls == 0 ? 0 : (double)BodyFuel / Calls;
}
