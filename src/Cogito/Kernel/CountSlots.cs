namespace Cogito;

using Cogito.Grammar;

// ── COUNT SLOTS — the doubling-tower scanner ──
//
// Re-Pair grammars are DAGs — induction can never mint a self-referencing rule, so a loop's trace (BODY repeated N
// times) compresses NOT into a loop but into a DOUBLING TOWER: BODY², BODY⁴, BODY⁸, … — an O(log N) chain of rules,
// each of the shape [X, X] (a rule whose two children are the SAME symbol). The count-slot is the slot over N (the
// repetition count the tower encodes positionally, by which powers concatenate at a use site).
//
// This is NOT a frame collision and needs none of the anti-unification machinery: the tower's members differ in
// LENGTH (BODY² vs BODY⁴), so a same-length blanked-key frame can never collide them. The detector is a trivial
// grammar-DAG scan for [X,X] repetition chains — the CHEAPEST blur detector, and the most load-bearing: the tower's
// finite unrollings ARE the independently-reflected rules a self-calling rule `R = BODY R` grounds
// on. The slot proposal covers N, the execution VM verifies it (expansion-matching cannot
// reach an infinite expansion), the tower grounds it (the unrollings vest). The census here is the substrate the
// VM reads.
//
// PURE INSTRUMENTATION — reads a grammar, never mutates it. H3-additive: it never enters the reconstruction path,
// so byte-exactness is free. Consumed by the `blur` probe (the tower census) and by the Whorl C tape-VM.

public static class CountSlots
{
    /// One DOUBLING TOWER discovered in a Re-Pair DAG — the count-slot substrate. `Base` is BODY (the repeated
    /// unit: a terminal, or a NON-doubling nonterminal); `BaseSpan` its byte extent. `Height` is the number of
    /// doubling levels, so the members are BODY², BODY⁴, …, BODY^(2^Height) and the deepest member spans
    /// `BaseSpan · 2^Height` bytes. `Chain` holds the rule indices bottom→top: `Chain[0]` = the rule [BODY,BODY],
    /// `Chain[k]` = [Chain[k−1], Chain[k−1]].
    public readonly record struct Tower(Symbol Base, long BaseSpan, int Height, int[] Chain)
    {
        /// The deepest unroll's byte extent (BaseSpan · 2^Height) — the count-slot's abstraction span, the correlation
        /// length a knot minted over this tower would carry at its deepest reflected unroll.
        public long TopSpan
        {
            get
            {
                if (Height < 0 || Height >= 63 || BaseSpan > (long.MaxValue >> Height))
                    throw new OverflowException($"tower span overflows Int64: base={BaseSpan}, height={Height}");
                return BaseSpan << Height;
            }
        }
    }

    /// Scan a grammar's rule DAG for [X,X] doubling chains and assemble MAXIMAL towers — each a TOP double (a
    /// doubling rule whose own symbol nothing further doubles) chased DOWN through the doubling relation to its
    /// base BODY. O(rules): one ExpLens pass + two linear sweeps, ZERO byte materialization. Deterministic
    /// (towers emitted in ascending top-rule-index order). The grammar is read, never mutated.
    ///
    /// A rule counts as a "double" only if it is a live Re-Pair `Expansion` of exactly two identical children — a
    /// SlotClass (pick-one) or a TapeRef (demoted literal) is not a merge and cannot be a tower level. Children of a
    /// Re-Pair merge always precede it in emission order, so `Chain` descends strictly by index (no cycle risk).
    public static List<Tower> Scan(GrammarRule[] rules, uint alphabetSize)
    {
        int n = rules.Length;
        var towers = new List<Tower>();
        if (n == 0) return towers;
        var expLen = Engine.ExpLens(rules, alphabetSize);

        // ── pass 1 — classify each rule: is it [X,X], and if so what is X (value + child-rule index, −1 if terminal). ──
        var isDouble = new bool[n];
        var childVal = new uint[n];
        var childRule = new int[n];      // the child's rule index if X is a nonterminal, else −1
        for (int r = 0; r < n; r++)
        {
            var rule = rules[r];
            if (rule.Kind != RuleBodyKind.Expansion || rule.Pattern.Length != 2) continue;
            if (rule.Pattern[0].Value != rule.Pattern[1].Value) continue;
            isDouble[r] = true;
            uint cv = rule.Pattern[0].Value;
            childVal[r] = cv;
            childRule[r] = cv >= alphabetSize ? (int)(cv - alphabetSize) : -1;
        }

        // ── pass 2 — mark every double that is itself doubled by another double (it has a parent level, not a top). ──
        var hasParent = new bool[n];
        for (int r = 0; r < n; r++)
            if (isDouble[r] && childRule[r] >= 0 && childRule[r] < n && isDouble[childRule[r]])
                hasParent[childRule[r]] = true;

        // ── assemble — from each TOP double, chase the doubling chain down to the base BODY. ──
        for (int r = 0; r < n; r++)
        {
            if (!isDouble[r] || hasParent[r]) continue;
            var topDown = new List<int>();
            int cur = r;
            while (true)
            {
                topDown.Add(cur);
                int c = childRule[cur];
                if (c >= 0 && c < cur && isDouble[c]) cur = c; else break;   // descend only through doubles (strictly lower index)
            }
            int bottom = topDown[^1];
            var baseSym = new Symbol(childVal[bottom]);
            long baseSpan = childRule[bottom] >= 0 ? expLen[childRule[bottom]] : 1;
            topDown.Reverse();                                              // now bottom→top
            towers.Add(new Tower(baseSym, baseSpan, topDown.Count, topDown.ToArray()));
        }
        return towers;
    }

    /// The census aggregate over a tower set — the substrate summary the `blur` probe reports and the tower-census
    /// VM reads. `HeightHistogram[h]` = how many towers reached exactly height h (index 0 unused).
    public readonly record struct Census(int Towers, int MaxHeight, long DeepestSpan, int[] HeightHistogram);

    public static Census Summarize(IReadOnlyList<Tower> towers)
    {
        int maxH = 0; long deepest = 0;
        foreach (var t in towers) { if (t.Height > maxH) maxH = t.Height; if (t.TopSpan > deepest) deepest = t.TopSpan; }
        var hist = new int[maxH + 1];
        foreach (var t in towers) hist[t.Height]++;
        return new Census(towers.Count, maxH, deepest, hist);
    }
}
