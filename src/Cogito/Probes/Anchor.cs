namespace Cogito;

using System.Text;

// ── percolate ── THE PARTITION-COMPILER kill-line (BLUEPRINT face 4 · FRANKENSTEIN "THE REFLECTION LAW").
//
// The homology verdict (StructMatch.cs, commit 847cc75) ruled: structure ALONE does not bridge disjoint
// modalities — the apparent music↔code correspondence is machine-generic (Re-Pair's own shape fingerprint).
// One token stream is ONE witness, and one witness can only report identity or nothing; a real equivalence
// class between two DIFFERENT surfaces needs a SECOND independent witness. This organ imports that witness:
//
//   ANCHOR   a sentinel word (private-use \uE0xx) co-fired into BOTH word-streams next to occurrences of a
//            grounded concept — the experimenter's binder ("this A-word = that B-word"), the literal shared
//            token the disjoint alphabets lacked. AntiUnify.Edges keys on it for free ((word,anchor) yields).
//   BREACH   relaxed anchor-frame edges: two fillers are slot-mates if they fill the same (length, position,
//            anchor-pattern) frame even when every non-anchor context word is surface-disjoint.
//   LOWER    the DUAL-DOMAIN MDL MIN-GATE: a cross-modal slot vests iff it pays bits in BOTH domains
//            independently — min(ΔMDL_A, ΔMDL_B) > floor. One-sided pay = memorization; two-sided = partition.
//   SELF-EXTEND  every vested slot's symbol substitutes into both corpora and joins the anchor set, so round
//            n+1 anti-unifies frames anchored by round-n structure — the supercritical chain.
//
// THE CAPTAIN MATRIX (binder2-style, EVERY cell authored by the experimenter — no LLM anywhere in data or
// loop; deterministic = the Vow): 6 entity families × 8 concepts × 2 surface-disjoint domains, ring relations
// REL_j : F_j→F_{j+1} (3 synonyms/domain) + confuser RELX_j : F_j→F_{j+2} (3 synonyms), glue words, AND-pairs.
// The family×relation incidence is a directed 6-ring, so the schema has a 6-fold rotation AUTOMORPHISM: any
// type-level (structure-only) aligner faces 6 equally-consistent alignments ⟹ pinned at chance = 1/6 ≈ 0.167.
// That authors the homology verdict INTO the testbed — grounding is the only gauge-breaker. The witness channel
// is deliberately noisy (co-fire rate ρ<1, spurious-injection rate η>0): a single anchor is a fallible witness,
// so corroboration must ACCUMULATE — that is what gives percolation a threshold instead of ignition-from-one.
//
// THE KILL-LINE: cross-modal-vested mass vs k ∈ {0,2,4,8,12,16,24} seed-anchors. Pre-registered: k=0 → 0 (the
// measured surface-zero), k=2 → ~0 (subcritical), sharp knee (a linear ramp FALSIFIES percolation), saturation
// by k=24; precision ≥0.5 on vested cross-pairs; held-out transfer (anchors on F0–F3 only) > 0.5 where the
// alignment-alone baseline sits at gauge chance 0.167. k* (the knee) feeds the six-lanes-one-number unifier.
public static class Anchor
{
    const char Sep = '\u001f';                 // slot-key joiner (mirrors AntiUnify's private Sep)
    const char SentinelBase = '\uE000';        // private-use run — sentinel(c) = \uE000+c, collision-free by construction
    const double InjectRate = 0.55;            // ρ — a grounded witness co-fires on this fraction of its concept's occurrences
    const double NoiseRate  = 0.10;            // η — per sentence per domain, a spurious sentinel lands at a random position

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    //  THE CAPTAIN MATRIX — experimenter-authored, every cell (surface-disjoint by guard, not by hope)
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    const int NF = 6, NC = 8, NSyn = 3;

    // families: 0 beasts · 1 tools · 2 colors · 3 weather · 4 body · 5 instruments (distant semantic fields)
    static readonly string[][] FamA =
    [
        "wolf fox deer otter owl crow lynx boar".Split(' '),
        "hammer chisel spanner mallet pliers rasp awl vice".Split(' '),
        "crimson amber teal ochre indigo sage umber cobalt".Split(' '),
        "storm drizzle frost gale thaw sleet haze squall".Split(' '),
        "elbow wrist femur rib spleen tendon cornea sternum".Split(' '),
        "cello oboe viola timpani flute harp bassoon zither".Split(' '),
    ];
    static readonly string[][] FamB =
    [
        "varg refr hjortr otur ugla kraka gaupa golt".Split(' '),
        "hamarr meitill skiptilykill kylfa tong raspr alr klemma".Split(' '),
        "raudleit gulbrun blagraen leirgul dokkbla salvia moldbrun stalbla".Split(' '),
        "stormur sudda frosti hvassvidri leysing slydda mistur rok".Split(' '),
        "olnbogi ulnlidur laerleggur rifbein milta sinar glaera bringubein".Split(' '),
        "knefill obosk fidla pakur floyta harpan fagott sitra".Split(' '),
    ];
    // ring relations REL_j : F_j → F_{(j+1)%6}, 3 synonyms per relation per domain (classes are mintable: 3+3)
    static readonly string[][] RelA =
    [
        "grips wields clutches".Split(' '),
        "stains coats tints".Split(' '),
        "heralds portends foretokens".Split(' '),
        "chills numbs stiffens".Split(' '),
        "plucks strums fingers".Split(' '),
        "soothes lulls charms".Split(' '),
    ];
    static readonly string[][] RelB =
    [
        "gripr veldr klemr".Split(' '),
        "litar hjupar blaer".Split(' '),
        "bodar spair varslar".Split(' '),
        "kaelir dofnar stirdnar".Split(' '),
        "plokkar slaer fingrar".Split(' '),
        "roar svaefir heillar".Split(' '),
    ];
    // confuser skip-relations RELX_j : F_j → F_{(j+2)%6} — same template shape, so an anchor-frame that wildcards
    // the relation word MIXES two object families; disambiguation must be EARNED by vesting the relation classes.
    static readonly string[][] RxA =
    [
        "smears blurs mottles".Split(' '),
        "braves defies endures".Split(' '),
        "adorns daubs marks".Split(' '),
        "detunes warps dampens".Split(' '),
        "startles spooks rouses".Split(' '),
        "outshines rivals mimics".Split(' '),
    ];
    static readonly string[][] RxB =
    [
        "smyr thokar flekkar".Split(' '),
        "tholir trassar herdir".Split(' '),
        "skreytir kladdar merkir".Split(' '),
        "mistillir skekur deyfir".Split(' '),
        "bregdur styggir vekur".Split(' '),
        "yfirskin keppir hermir".Split(' '),
    ];
    static readonly string[] GlueA = ["the", "and"];
    static readonly string[] GlueB = ["hinn", "og"];

    /// The matrix's answer key: per-domain vocabularies, word → gold class (F0–F5 / R0–R5 / RX0–RX5 / G0–G1),
    /// word → abstract concept id (identity-level), entity word → family index.
    sealed class GoldKey
    {
        public readonly HashSet<string> VocabA = new(StringComparer.Ordinal);
        public readonly HashSet<string> VocabB = new(StringComparer.Ordinal);
        public readonly Dictionary<string, string> Cls = new(StringComparer.Ordinal);
        public readonly Dictionary<string, string> Concept = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int> Fam = new(StringComparer.Ordinal);
    }

    static GoldKey BuildGold()
    {
        var g = new GoldKey();
        void Word(string w, bool domA, string cls, string concept, int fam = -1)
        {
            if (!(domA ? g.VocabA : g.VocabB).Add(w) || (domA ? g.VocabB : g.VocabA).Contains(w))
                throw new InvalidOperationException($"captain matrix violates surface-disjointness: '{w}' duplicated");
            g.Cls[w] = cls; g.Concept[w] = concept;
            if (fam >= 0) g.Fam[w] = fam;
        }
        for (int f = 0; f < NF; f++)
            for (int i = 0; i < NC; i++)
            {
                Word(FamA[f][i], true, $"F{f}", $"F{f}.{i}", f);
                Word(FamB[f][i], false, $"F{f}", $"F{f}.{i}", f);
            }
        for (int j = 0; j < NF; j++)
            for (int s = 0; s < NSyn; s++)
            {
                Word(RelA[j][s], true, $"R{j}", $"R{j}.{s}");
                Word(RelB[j][s], false, $"R{j}", $"R{j}.{s}");
                Word(RxA[j][s], true, $"RX{j}", $"RX{j}.{s}");
                Word(RxB[j][s], false, $"RX{j}", $"RX{j}.{s}");
            }
        Word(GlueA[0], true, "G0", "G0"); Word(GlueB[0], false, "G0", "G0");
        Word(GlueA[1], true, "G1", "G1"); Word(GlueB[1], false, "G1", "G1");
        return g;
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    //  THE EVENT STREAM + THE TWO RENDERS — one abstract world, two surface-disjoint observations
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    /// One abstract event. Kind 0 = REL_j (subj F_j, obj F_{j+1}) · 1 = RELX_j (obj F_{j+2}) · 2 = AND same-family
    /// pair. Each domain renders its OWN independent event stream (two modalities sampling one world-DISTRIBUTION,
    /// never the same moments): a shared stream leaks event-parallelism through per-word count fingerprints — a
    /// frequency-NN aligner scored 0.98 with ZERO anchors on parallel renders (measured) — so independent streams
    /// put ALL grounding where it belongs: in the sentinel channel.
    readonly record struct Ev(int Kind, int J, int Si, int Oi, int Syn);

    static List<Ev> Events(int n, ulong seed)
    {
        ulong rng = seed;
        double U() { rng = rng * 6364136223846793005UL + 1442695040888963407UL; return ((rng >> 11) & 0x1FFFFFFFFFFFFFUL) / (double)(1UL << 53); }
        int R(int m) => (int)(U() * m);
        var evs = new List<Ev>(n);
        for (int i = 0; i < n; i++)
        {
            double r = U();
            if (r < 0.60) evs.Add(new Ev(0, R(NF), R(NC), R(NC), R(NSyn)));
            else if (r < 0.85) evs.Add(new Ev(1, R(NF), R(NC), R(NC), R(NSyn)));
            else { int f = R(NF), x = R(NC), y = R(NC - 1); if (y >= x) y++; evs.Add(new Ev(2, f, x, y, 0)); }
        }
        return evs;
    }

    static string Sentinel(int concept) => ((char)(SentinelBase + concept)).ToString();
    static bool IsSentinel(string w) => w.Length == 1 && w[0] >= SentinelBase && w[0] < (char)(SentinelBase + NF * NC);

    /// Render ONE domain's event stream, firing sentinels on anchored concepts. The witness channel is noisy on
    /// purpose: an anchored occurrence fires with rate ρ (per stream — a fallible observer, not a copied label),
    /// and each sentence independently suffers a spurious sentinel at rate η (mis-registration). k=0 ⇒ no
    /// sentinels exist ⇒ the channel is silent.
    static List<string[]> RenderDomain(List<Ev> evs, bool domA, HashSet<int> anchored, ulong seed)
    {
        ulong rng = seed;
        double U() { rng = rng * 6364136223846793005UL + 1442695040888963407UL; return ((rng >> 11) & 0x1FFFFFFFFFFFFFUL) / (double)(1UL << 53); }
        var anchorIds = anchored.OrderBy(x => x).ToArray();
        var (fam, rel, rx, glue) = domA ? (FamA, RelA, RxA, GlueA) : (FamB, RelB, RxB, GlueB);
        var C = new List<string[]>(evs.Count);
        foreach (var e in evs)
        {
            var s = new List<string>(8);
            void Ent(int f, int idx)
            {
                s.Add(fam[f][idx]);
                int c = f * NC + idx;
                if (anchored.Contains(c) && U() < InjectRate) s.Add(Sentinel(c));
            }
            switch (e.Kind)
            {
                case 0:
                    s.Add(glue[0]); Ent(e.J, e.Si); s.Add(rel[e.J][e.Syn]); s.Add(glue[0]); Ent((e.J + 1) % NF, e.Oi);
                    break;
                case 1:
                    s.Add(glue[0]); Ent(e.J, e.Si); s.Add(rx[e.J][e.Syn]); s.Add(glue[0]); Ent((e.J + 2) % NF, e.Oi);
                    break;
                default:
                    s.Add(glue[0]); Ent(e.J, e.Si); s.Add(glue[1]); Ent(e.J, e.Oi);
                    break;
            }
            if (anchorIds.Length > 0 && U() < NoiseRate)
                s.Insert((int)(U() * (s.Count + 1)), Sentinel(anchorIds[(int)(U() * anchorIds.Length)]));
            C.Add(s.ToArray());
        }
        return C;
    }

    /// k grounded seed-anchors, idx-major round-robin over the first `famLimit` families (maximal spread — the
    /// percolation-friendly placement; the held-out arm restricts famLimit to 4 so F4/F5 are NEVER anchored).
    static HashSet<int> PickAnchors(int k, int famLimit)
    {
        var set = new HashSet<int>();
        for (int idx = 0; idx < NC && set.Count < k; idx++)
            for (int fam = 0; fam < famLimit && set.Count < k; fam++)
                set.Add(fam * NC + idx);
        return set;
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    //  BREACH — the relaxed anchor-frame edges (the imported second witness made a slot key)
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    /// Anti-unify with the context WILDCARDED except for anchors: the frame key = (yield length, blank position,
    /// position-indexed anchor pattern). Two fillers colliding on the same anchor-frame are slot-mates EVEN WHEN
    /// every other context word is surface-disjoint — this is the breach through the one-stream wall. The filler
    /// itself must be a surface word (an anchor is a witness, never a member); a frame with zero anchors carries
    /// no witness and is discarded (so k=0 yields no relaxed edges at all — the measured left endpoint).
    static Dictionary<string, int> AnchorEdges(List<string[]> yields, HashSet<string> anchors)
    {
        var slot = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (var y in yields)
        {
            if (y.Length < 2) continue;
            bool any = false;
            foreach (var w in y) if (anchors.Contains(w)) { any = true; break; }
            if (!any) continue;
            for (int p = 0; p < y.Length; p++)
            {
                if (anchors.Contains(y[p])) continue;
                sb.Clear();
                sb.Append(y.Length).Append(Sep).Append(p).Append(Sep);
                bool has = false;
                for (int i = 0; i < y.Length; i++)
                {
                    if (i == p) continue;
                    if (anchors.Contains(y[i])) { has = true; sb.Append(i).Append(':').Append(y[i]); }
                    sb.Append(Sep);
                }
                if (!has) continue;
                string key = sb.ToString();
                var fillers = slot.TryGetValue(key, out var f) ? f : (slot[key] = new(StringComparer.Ordinal));
                fillers[y[p]] = fillers.GetValueOrDefault(y[p]) + 1;
            }
        }
        var edge = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var fillers in slot.Values)
        {
            if (fillers.Count < 2) continue;
            var ws = fillers.Keys.ToArray(); Array.Sort(ws, StringComparer.Ordinal);
            for (int i = 0; i < ws.Length; i++)
                for (int j = i + 1; j < ws.Length; j++)
                {
                    string k = ws[i] + Sep + ws[j];
                    edge[k] = edge.GetValueOrDefault(k) + Math.Min(fillers[ws[i]], fillers[ws[j]]);
                }
        }
        return edge;
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    //  LOWER — the dual-domain min-gate percolation loop (GrowthLoop's phases, cross-modal vesting)
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    /// Min = the reflection law (pay in BOTH domains independently — corroborated). Max = the ablation arm: a single
    /// witness suffices — the memorization regime the min-gate exists to forbid.
    public enum GateModes { Min, Max }

    sealed record PercOpts(int MaxIter, int Knn, int MinWeight, double Floor, int MaxCand, GateModes Gate);

    readonly record struct PercRound(
        int It, int Cands, int Spanning, int New, int Rejected, int Slots,
        double BestPay, double BestDA, double BestDB, double DA, double DB);

    sealed class PercResult
    {
        public readonly Paradigm Model = new();
        public readonly List<PercRound> Rounds = new();
        public int Edge0Cross;          // raw cross-domain edges on the FIRST round (the breach signal, pre-vest)
        public double Edge0Prec;        // their weight-weighted gold-class precision (what BREACH alone gives)
        public double MdlA0, MdlB0, MdlA, MdlB;
    }

    /// One percolation run: induce the JOINT corpus (streams never mix domains — sentence barriers are hard),
    /// strict Edges + BREACH anchor-frames, mutual-kNN candidates, keep only DOMAIN-SPANNING candidates (leaf-level:
    /// members reach both vocabularies), vest through the dual-domain gate against round-start MDL, substitute the
    /// accepted wave into BOTH corpora, and add each vested symbol to the anchor set (SELF-EXTEND). Deterministic.
    static PercResult Percolate(List<string[]> A0, List<string[]> B0, IEnumerable<string> sentinels, PercOpts o, GoldKey gold)
    {
        var res = new PercResult();
        var model = res.Model;
        var anchors = new HashSet<string>(sentinels, StringComparer.Ordinal);
        var A = new List<List<string>>(A0.Count); foreach (var s in A0) A.Add(new List<string>(s));
        var B = new List<List<string>>(B0.Count); foreach (var s in B0) B.Add(new List<string>(s));
        int counter = 0;

        double mdlA = AntiUnify.TwoPartMdl(A0, A, model.MemberToSlot).Total;
        double mdlB = AntiUnify.TwoPartMdl(B0, B, model.MemberToSlot).Total;
        res.MdlA0 = mdlA; res.MdlB0 = mdlB;

        for (int it = 0; it < o.MaxIter; it++)
        {
            var joint = new List<List<string>>(A.Count + B.Count);
            joint.AddRange(A); joint.AddRange(B);
            var snap = WordRePair.Induce(joint);
            // alignment frames = Re-Pair yields (recurring phrase structure) ∪ the SENTENCES themselves (the full
            // binder geometry). Yields alone starve the breach: Re-Pair fragments a sentence into short chunks
            // whose anchor-frames carry one or two context slots, while the sentence-level frame ⟨A⟩@i exposes
            // every varying position at once (relword, object, subject) — measured: yields-only stalled at one
            // glue slot; sentences ignite the family chain.
            //
            // The STRICT pass drops len-2 yields: a frame whose whole context is ONE word is evidence-free soup
            // ("the ___" pairs every entity with every entity at glue-frequency weight) — measured: soup out-ranked
            // bridges and family co-fill in mutual-kNN, gluing a 55-member 4-family megablob at k=8 and fragmenting
            // k=24 into unpayable identity pairs. The BREACH pass keeps short frames: its key requires a sentinel
            // witness, which is precisely the discipline the soup lacks.
            var strictFrames = new List<string[]>(snap.Yield.Count + A.Count + B.Count);
            foreach (var y in snap.Yield) if (y.Length >= 3) strictFrames.Add(y);
            foreach (var s in A) strictFrames.Add(s.ToArray());
            foreach (var s in B) strictFrames.Add(s.ToArray());
            var breachFrames = new List<string[]>(snap.Yield.Count + A.Count + B.Count);
            breachFrames.AddRange(snap.Yield);
            for (int i = snap.Yield.Count; i < strictFrames.Count; i++) breachFrames.Add(strictFrames[i]);
            var edge = AntiUnify.Edges(strictFrames);
            foreach (var (k, w) in AnchorEdges(breachFrames, anchors)) edge[k] = edge.GetValueOrDefault(k) + w;
            if (it == 0) (res.Edge0Cross, res.Edge0Prec) = EdgeCrossRead(edge, gold);

            var cands = AntiUnify.MintCandidates(edge, o.Knn, o.MinWeight);
            // post-process: strip witness members, drop the fully-slotted, keep only DOMAIN-SPANNING candidates —
            // every vested slot is cross-modal by construction, so the curve's slot count IS the cross-modal count.
            var spanning = new List<List<string>>();
            foreach (var c in cands)
            {
                var m = new List<string>(c.Count);
                foreach (var w in c) if (!IsSentinel(w)) m.Add(w);
                if (m.Count < 2) continue;
                bool allSlotted = true;
                foreach (var w in m) if (!model.MemberToSlot.ContainsKey(w)) { allSlotted = false; break; }
                if (allSlotted) continue;
                bool inA = false, inB = false;
                foreach (var w in m)
                    foreach (var leaf in Leaves(w, model))
                    {
                        if (gold.VocabA.Contains(leaf)) inA = true;
                        else if (gold.VocabB.Contains(leaf)) inB = true;
                        if (inA && inB) break;
                    }
                if (inA && inB) spanning.Add(m);
            }
            spanning.Sort((x, y) => AntiUnify.InternalWeight(y, edge).CompareTo(AntiUnify.InternalWeight(x, edge)));

            var accepted = new List<(string Name, List<string> Members)>();
            var used = new HashSet<string>(StringComparer.Ordinal);
            int gated = 0;
            double dAsum = 0, dBsum = 0;
            double bestPay = double.NegativeInfinity, bestDA = 0, bestDB = 0;
            foreach (var cand in spanning)
            {
                // accepts capped at MaxCand (the wave size); probes at 4× — the top of the weight order is often
                // held by strong-bridge identity PAIRS that can never pay (relabel tax), and a tight probe window
                // let them starve the class candidates below (measured: k=24 died with payers untested).
                if (accepted.Count >= o.MaxCand || gated >= o.MaxCand * 4) break;
                bool overlaps = false;
                foreach (var w in cand) if (used.Contains(w)) { overlaps = true; break; }
                if (overlaps) continue;
                bool meta = cand.All(w => w.StartsWith('['));
                string name = meta ? $"[[X{counter}]]" : $"[X{counter}]"; counter++;
                var set = new HashSet<string>(cand, StringComparer.Ordinal);
                var tA = SubstCopy(A, set, name);
                var tB = SubstCopy(B, set, name);
                var m2s = new Dictionary<string, string>(model.MemberToSlot, StringComparer.Ordinal);
                foreach (var w in cand) m2s[w] = name;
                double dA = mdlA - AntiUnify.TwoPartMdl(A0, tA, m2s).Total;
                double dB = mdlB - AntiUnify.TwoPartMdl(B0, tB, m2s).Total;
                double pay = o.Gate == GateModes.Min ? Math.Min(dA, dB) : Math.Max(dA, dB);
                gated++;
                if (pay > bestPay) { bestPay = pay; bestDA = dA; bestDB = dB; }
                if (pay > o.Floor)
                {
                    accepted.Add((name, cand));
                    foreach (var w in cand) used.Add(w);
                }
                else counter--;                                   // reclaim the rejected name (deterministic counter)
            }

            if (accepted.Count > 0)
            {
                foreach (var (name, mem) in accepted)
                {
                    model.BirthSlot(name, mem);                   // the one slot-creation verb — keeps the mint spine in step
                    var set = new HashSet<string>(mem, StringComparer.Ordinal);
                    A = SubstCopy(A, set, name);
                    B = SubstCopy(B, set, name);
                    anchors.Add(name);                            // SELF-EXTEND — the vested slot is a new witness
                }
                double nA = AntiUnify.TwoPartMdl(A0, A, model.MemberToSlot).Total;
                double nB = AntiUnify.TwoPartMdl(B0, B, model.MemberToSlot).Total;
                dAsum = mdlA - nA; dBsum = mdlB - nB;
                mdlA = nA; mdlB = nB;
            }
            res.Rounds.Add(new PercRound(it, cands.Count, spanning.Count, accepted.Count, gated - accepted.Count,
                model.SlotCount, double.IsNegativeInfinity(bestPay) ? 0 : bestPay, bestDA, bestDB, dAsum, dBsum));
            if (accepted.Count == 0) break;
        }
        model.NextName = counter;
        res.MdlA = mdlA; res.MdlB = mdlB;
        return res;
    }

    static List<List<string>> SubstCopy(List<List<string>> corpus, HashSet<string> members, string name)
    {
        var next = new List<List<string>>(corpus.Count);
        foreach (var s in corpus)
        {
            var row = new List<string>(s.Count);
            foreach (var t in s) row.Add(members.Contains(t) ? name : t);
            next.Add(row);
        }
        return next;
    }

    static IEnumerable<string> Leaves(string m, Paradigm p)
    {
        if (!m.StartsWith('[')) { yield return m; yield break; }
        if (!p.SlotMembers.TryGetValue(m, out var kids)) yield break;
        foreach (var kid in kids)
            foreach (var l in Leaves(kid, p)) yield return l;
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    //  THE READS — breach precision, vested-partition precision, held-out transfer, the gauge baseline
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    /// Raw BREACH signal: cross-domain edges between surface words, weight-weighted gold-class precision. This is
    /// what the anchor witness alone proposes — the min-gate's job is to vest only the partition-grade part of it.
    static (int N, double Prec) EdgeCrossRead(Dictionary<string, int> edge, GoldKey g)
    {
        long tot = 0, ok = 0; int n = 0;
        foreach (var (k, w) in edge)
        {
            int s = k.IndexOf(Sep);
            string a = k[..s], b = k[(s + 1)..];
            if (IsSentinel(a) || IsSentinel(b) || a.StartsWith('[') || b.StartsWith('[')) continue;
            bool cross = (g.VocabA.Contains(a) && g.VocabB.Contains(b)) || (g.VocabB.Contains(a) && g.VocabA.Contains(b));
            if (!cross) continue;
            n++; tot += w;
            if (g.Cls[a] == g.Cls[b]) ok += w;
        }
        return (n, tot > 0 ? (double)ok / tot : 0);
    }

    /// Gold words grouped by their TOP slot — the compiled partition's cells (sorted for determinism).
    static Dictionary<string, List<string>> Groups(Paradigm p, GoldKey g)
    {
        var by = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var w in p.MemberToSlot.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (!g.Cls.ContainsKey(w)) continue;
            string top = p.ResolveTop(w);
            (by.TryGetValue(top, out var l) ? l : by[top] = new List<string>()).Add(w);
        }
        return by;
    }

    /// The vested partition's cross-modal mass + precision: over every partition cell, all (A-word × B-word)
    /// pairs; class-correct = same gold class, identity-correct = same abstract concept. The pair mass is the
    /// percolation ORDER PARAMETER (connected correct mass — finer-grained than the slot count).
    static (int Pairs, int Correct, int Id, double Prec, double IdPrec) PartitionRead(Paradigm p, GoldKey g)
    {
        int pairs = 0, correct = 0, id = 0;
        foreach (var members in Groups(p, g).Values)
        {
            var aside = new List<string>(); var bside = new List<string>();
            foreach (var w in members) (g.VocabA.Contains(w) ? aside : bside).Add(w);
            foreach (var a in aside)
                foreach (var b in bside)
                {
                    pairs++;
                    if (g.Cls[a] == g.Cls[b]) correct++;
                    if (g.Concept[a] == g.Concept[b]) id++;
                }
        }
        return (pairs, correct, id, pairs > 0 ? (double)correct / pairs : 0, pairs > 0 ? (double)id / pairs : 0);
    }

    /// Held-out transfer: for each never-anchored concept in `fams`, does its A-word land in a partition cell whose
    /// B-side entity majority is the RIGHT family? (The self-extension chain is the only path — no anchor ever
    /// touched these families.) Chance under the ring gauge = 1/6.
    static (int Covered, int Correct, int Total) Transfer(Paradigm p, GoldKey g, int[] fams)
    {
        var groups = Groups(p, g);
        int covered = 0, correct = 0, total = fams.Length * NC;
        foreach (int f in fams)
            for (int i = 0; i < NC; i++)
            {
                string wa = FamA[f][i];
                if (!p.MemberToSlot.ContainsKey(wa)) continue;
                var cell = groups[p.ResolveTop(wa)];
                var famCount = new int[NF]; int bEnts = 0;
                foreach (var w in cell)
                    if (g.VocabB.Contains(w) && g.Fam.TryGetValue(w, out var bf)) { famCount[bf]++; bEnts++; }
                if (bEnts == 0) continue;
                covered++;
                int maj = 0;
                for (int x = 1; x < NF; x++) if (famCount[x] > famCount[maj]) maj = x;
                if (maj == f) correct++;
            }
        return (covered, correct, total);
    }

    /// The alignment-alone baseline: a content-blind structural aligner (per-word occupancy over (sentence-length,
    /// position), cosine NN A→B) on the UNANCHORED corpora. The 6-ring automorphism makes every family profile
    /// identical in type-statistics, so this aligner is pinned at gauge chance 1/6 — the authored form of the
    /// homology verdict ("structure alone does not bridge").
    static double BaselineAlign(List<string[]> A, List<string[]> B, GoldKey g)
    {
        var feat = new Dictionary<string, Dictionary<int, double>>(StringComparer.Ordinal);
        void Scan(List<string[]> C)
        {
            foreach (var sent in C)
                for (int p = 0; p < sent.Length; p++)
                {
                    if (!g.Fam.ContainsKey(sent[p])) continue;
                    var f = feat.TryGetValue(sent[p], out var d) ? d : feat[sent[p]] = new Dictionary<int, double>();
                    int key = sent.Length * 32 + p;
                    f[key] = f.GetValueOrDefault(key) + 1;
                }
        }
        Scan(A); Scan(B);
        double Cos(Dictionary<int, double> x, Dictionary<int, double> y)
        {
            double dot = 0, nx = 0, ny = 0;
            foreach (var (k, v) in x) { nx += v * v; if (y.TryGetValue(k, out var u)) dot += v * u; }
            foreach (var v in y.Values) ny += v * v;
            return nx > 0 && ny > 0 ? dot / Math.Sqrt(nx * ny) : 0;
        }
        int ok = 0, tot = 0;
        for (int f = 0; f < NF; f++)
            for (int i = 0; i < NC; i++)
            {
                var fa = feat.GetValueOrDefault(FamA[f][i]);
                if (fa is null) continue;
                double best = double.NegativeInfinity; int bestFam = 0;
                for (int f2 = 0; f2 < NF; f2++)
                    for (int i2 = 0; i2 < NC; i2++)
                    {
                        var fb = feat.GetValueOrDefault(FamB[f2][i2]);
                        if (fb is null) continue;
                        double c = Cos(fa, fb);
                        if (c > best + 1e-12) { best = c; bestFam = f2; }
                    }
                tot++;
                if (bestFam == f) ok++;
            }
        return tot > 0 ? (double)ok / tot : 0;
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    //  THE KILL-LINE VERB — the percolation curve, k*, held-out transfer, the gate ablation
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    /// usage: percolate [--n N] [--seed HEX] [--ks 0,2,4,8,12,16,24] [--iter I] [--cand C] [--knn K] [--minw W]
    ///                  [--floor F] [--heldk K] [--ablatek K] [--verbose]
    public static int Run(string[] args)
    {
        int n        = Args.Int(args, "--n", 800);
        ulong seed   = Args.Seed(args, "--seed", 0xA2C407UL);
        int iter     = Args.Int(args, "--iter", 8);
        int cand     = Args.Int(args, "--cand", 8);
        int knn      = Args.Int(args, "--knn", 3);
        int minw     = Args.Int(args, "--minw", 2);
        double floor = Args.Double(args, "--floor", 1.0);
        int heldK    = Args.Int(args, "--heldk", 16);
        int ablateK  = Args.Int(args, "--ablatek", 12);
        bool verbose = Args.Has(args, "--verbose");
        var ks = Args.Str(args, "--ks", "0,2,4,8,12,16,24").Split(',').Select(int.Parse).ToArray();

        var gold = BuildGold();                                   // throws loud if the matrix leaks a shared surface
        var evsA = Events(n, seed);
        var evsB = Events(n, seed ^ 0xB0B0B0B0UL);                // INDEPENDENT streams — no event-parallelism leak
        var opts = new PercOpts(iter, knn, minw, floor, cand, GateModes.Min);
        // per-arm corpora: the events + render seeds are FIXED across arms — only the anchor set varies (paired).
        (List<string[]> A, List<string[]> B, IEnumerable<string> S) Corpora(int k, int famLimit)
        {
            var anchored = PickAnchors(k, famLimit);
            return (RenderDomain(evsA, true, anchored, seed + 13), RenderDomain(evsB, false, anchored, seed + 29),
                    anchored.OrderBy(x => x).Select(Sentinel));
        }
        void Rounds(PercResult r)
        {
            if (!verbose) return;
            foreach (var q in r.Rounds)
                Trace.Note($"      it {q.It}: cands {q.Cands} → spanning {q.Spanning} → +{q.New}/{q.Rejected} rej · slots {q.Slots} · bestPay {q.BestPay:F0} (dA {q.BestDA:F0} dB {q.BestDB:F0}) · ΔA {q.DA:F0} ΔB {q.DB:F0}");
        }

        var run = Cogito.Run.New("percolate");
        Trace.Note($"percolate · THE PARTITION-COMPILER kill-line — anchor-percolation over AntiUnify (BLUEPRINT face 4) · seed {seed:X}");
        Trace.Note($"  captain matrix: {NF} families × {NC} concepts × 2 surface-DISJOINT domains ({gold.VocabA.Count}+{gold.VocabB.Count} authored words, guard-verified)");
        Trace.Note($"  schema: ring REL_j:F_j→F_j+1 (×{NSyn} syn) + confuser RELX_j:F_j→F_j+2 (×{NSyn}) + AND-pairs · {n} events → {n} sentences/domain");
        Trace.Note($"  the gauge: the family×relation incidence is a directed {NF}-ring ⟹ {NF} rotation automorphisms ⟹ any type-level");
        Trace.Note($"  aligner is pinned at chance 1/{NF} ≈ {1.0 / NF:F3} — the homology verdict authored in; anchors are the ONLY gauge-breaker");
        Trace.Note($"  witness channel: ρ={InjectRate:F2} co-fire · η={NoiseRate:F2} spurious/sentence · seed-anchors = entity concepts, idx-major round-robin");
        Trace.Note($"  BREACH strict-Edges ∪ anchor-frames · LOWER min(ΔMDL_A,ΔMDL_B) > {floor:F1} bits · SELF-EXTEND vested [X] joins the anchor set");
        Trace.Note("");

        // the alignment-alone baseline on the unanchored world (no sentinels anywhere)
        var (a0, b0, _) = Corpora(0, NF);
        double baseline = BaselineAlign(a0, b0, gold);
        Trace.Note($"  alignment-alone baseline (structural NN, no anchors): {baseline:F3}   [gauge chance {1.0 / NF:F3}]");
        Trace.Note("");

        // ── the percolation curve ──
        Trace.Note($"  k   rounds  slots  xpairs  correct  prec   idprec  breach0(n · prec)   ΔMDL_A     ΔMDL_B");
        Trace.Note("  " + new string('─', 96));
        var slotCurve = new int[ks.Length];
        var massCurve = new int[ks.Length];
        var arms = new PercResult[ks.Length];
        var sbTsv = new StringBuilder("k\trounds\tslots\txpairs\tcorrect\tprec\tidprec\tedge0_n\tedge0_prec\tdmdl_a\tdmdl_b\n");
        for (int i = 0; i < ks.Length; i++)
        {
            int k = ks[i];
            var (A, B, S) = Corpora(k, NF);
            var res = Percolate(A, B, S, opts, gold);
            var (pairs, correct, _, prec, idPrec) = PartitionRead(res.Model, gold);
            slotCurve[i] = res.Model.SlotCount;
            massCurve[i] = correct;
            arms[i] = res;
            double dA = res.MdlA0 - res.MdlA, dB = res.MdlB0 - res.MdlB;
            Trace.Note($"  {k,-3} {res.Rounds.Count,5}  {res.Model.SlotCount,5}  {pairs,6}  {correct,7}  {prec,5:F2}  {idPrec,6:F2}  {res.Edge0Cross,7} · {res.Edge0Prec,4:F2}    {dA,9:F0}  {dB,9:F0}");
            Rounds(res);
            sbTsv.AppendLine($"{k}\t{res.Rounds.Count}\t{res.Model.SlotCount}\t{pairs}\t{correct}\t{prec:F4}\t{idPrec:F4}\t{res.Edge0Cross}\t{res.Edge0Prec:F4}\t{dA:F1}\t{dB:F1}");
        }
        run.Write("percolation.tsv", sbTsv.ToString());
        Trace.Note("");

        // ── k* — the knee, on the correct-pair mass (the connected CORRECT partition — percolation's order
        //    parameter) with the slot count as corroboration. jumpFrac reads knee-vs-ramp: one dominant jump = knee. ──
        int Knee(int[] curve)
        {
            int sat = curve.Max();
            if (sat == 0) return -1;
            for (int i = 0; i < ks.Length; i++) if (curve[i] * 2 >= sat) return ks[i];
            return -1;
        }
        double JumpFrac(int[] curve)
        {
            int sat = curve.Max(); int rise = sat - curve[0];
            if (rise <= 0) return 0;
            int jump = 0;
            for (int i = 1; i < ks.Length; i++) jump = Math.Max(jump, curve[i] - curve[i - 1]);
            return (double)jump / rise;
        }
        int kStar = Knee(massCurve), kStarSlots = Knee(slotCurve);
        double jump = JumpFrac(massCurve);
        int satIdx = Array.IndexOf(massCurve, massCurve.Max());
        double precAtSat = arms[satIdx].Model.SlotCount > 0 ? PartitionRead(arms[satIdx].Model, gold).Prec : 0;
        int k2Idx = Array.IndexOf(ks, 2);
        double subFrac = k2Idx >= 0 && massCurve.Max() > 0 ? (double)massCurve[k2Idx] / massCurve.Max() : 0;
        Trace.Note($"  percolation: k* = {(kStar < 0 ? "∅" : kStar.ToString())} (correct-mass ≥50% of saturation) · slot-count k* = {(kStarSlots < 0 ? "∅" : kStarSlots.ToString())} · jumpFrac {jump:F2} ({(jump >= 0.4 ? "KNEE" : "ramp-ish")})");
        Trace.Note("");

        // ── held-out transfer: anchors restricted to F0–F3; F4/F5 are reachable ONLY through self-extension ──
        var (hA, hB, hS) = Corpora(heldK, 4);
        var heldRes = Percolate(hA, hB, hS, opts, gold);
        var t4 = Transfer(heldRes.Model, gold, [4]);
        var t5 = Transfer(heldRes.Model, gold, [5]);
        double tAcc = (double)(t4.Correct + t5.Correct) / (t4.Total + t5.Total);
        double tAccCov = t4.Covered + t5.Covered > 0 ? (double)(t4.Correct + t5.Correct) / (t4.Covered + t5.Covered) : 0;
        Trace.Note($"  held-out transfer (k={heldK} anchors on F0–F3 ONLY): F4 {t4.Correct}/{t4.Total} (cov {t4.Covered}) · F5 {t5.Correct}/{t5.Total} (cov {t5.Covered})");
        Trace.Note($"    overall {t4.Correct + t5.Correct}/{t4.Total + t5.Total} = {tAcc:F2} (on-covered {tAccCov:F2}) vs alignment-alone {baseline:F3} ≈ gauge chance {1.0 / NF:F3}");
        Trace.Note("");

        // ── the gate ablation: min (both witnesses) vs max (one witness suffices) at the same k ──
        var (xA, xB, xS) = Corpora(ablateK, NF);
        var maxRes = Percolate(xA, xB, xS, opts with { Gate = GateModes.Max }, gold);
        var minRes = Percolate(xA, xB, xS, opts, gold);
        var mr = PartitionRead(minRes.Model, gold);
        var xr = PartitionRead(maxRes.Model, gold);
        Trace.Note($"  gate ablation (k={ablateK}): MIN-gate {minRes.Model.SlotCount} slots · prec {mr.Prec:F2}   vs   MAX-gate {maxRes.Model.SlotCount} slots · prec {xr.Prec:F2}");
        Trace.Note($"    (min = corroborated by both domains; max = one witness suffices — the memorization regime the reflection law forbids)");
        Trace.Note("");

        // ── the saturation-arm partition, gold breakdown (the qualitative readout) ──
        Trace.Note($"  the k={ks[satIdx]} partition (top cells, gold breakdown):");
        var satGroups = Groups(arms[satIdx].Model, gold);
        foreach (var (top, members) in satGroups.OrderByDescending(kv => kv.Value.Count).Take(10))
        {
            var tags = members.GroupBy(w => gold.Cls[w]).OrderByDescending(g2 => g2.Count()).Select(g2 => $"{g2.Key}×{g2.Count()}");
            Trace.Note($"    {top,-8} |{members.Count,3}| {string.Join(" ", members.Take(9))}{(members.Count > 9 ? " …" : "")}  → {string.Join(",", tags)}");
        }
        Trace.Note("");

        // ── the pre-registered verdict ──
        bool p0 = massCurve[0] == 0 && slotCurve[0] == 0;
        bool p1 = k2Idx < 0 || subFrac <= 0.10;
        bool p2 = jump >= 0.4 && kStar >= 0;
        bool p3 = precAtSat >= 0.5;
        bool p4 = tAcc > 0.5 && baseline < 0.34;
        bool p5 = mr.Prec >= xr.Prec;
        Trace.Note("  ── VERDICT (pre-registered) ──");
        Trace.Note($"    [{(p0 ? "✓" : "✗")}] k=0 → 0 (the measured surface-zero)                    : {slotCurve[0]} slots, {massCurve[0]} correct pairs");
        Trace.Note($"    [{(p1 ? "✓" : "✗")}] k=2 subcritical (≤10% of saturation mass)             : {subFrac:P0}");
        Trace.Note($"    [{(p2 ? "✓" : "✗")}] sharp knee, not a linear ramp (jumpFrac ≥ 0.4)        : jumpFrac {jump:F2}, k* = {(kStar < 0 ? "∅" : kStar.ToString())}");
        Trace.Note($"    [{(p3 ? "✓" : "✗")}] precision ≥ 0.5 on vested cross-modal pairs           : {precAtSat:F2} at saturation");
        Trace.Note($"    [{(p4 ? "✓" : "✗")}] held-out transfer > 0.5 vs alignment-alone at chance  : {tAcc:F2} vs {baseline:F3} (chance {1.0 / NF:F3})");
        Trace.Note($"    [{(p5 ? "✓" : "✗")}] min-gate ≥ max-gate precision (the separator)         : {mr.Prec:F2} vs {xr.Prec:F2}");
        Trace.Note($"    k* = {(kStar < 0 ? "unmeasured" : kStar.ToString())} → the six-lanes-one-number unifier (vs grok-lock, RLEI minimum-anchor cadence)");

        run.Write("verdict.txt",
            $"k_star={kStar} k_star_slots={kStarSlots} jump_frac={jump:F3} prec_sat={precAtSat:F3} sub_k2={subFrac:F3}\n" +
            $"heldout={tAcc:F3} heldout_covered={tAccCov:F3} baseline={baseline:F3} chance={1.0 / NF:F3}\n" +
            $"gate_min_prec={mr.Prec:F3} gate_max_prec={xr.Prec:F3} gate_min_slots={minRes.Model.SlotCount} gate_max_slots={maxRes.Model.SlotCount}\n" +
            $"preregistered: p0={p0} p1={p1} p2={p2} p3={p3} p4={p4} p5={p5}\n");
        return 0;
    }
}
