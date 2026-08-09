namespace Cogito;

using System.Globalization;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

// ── THE CONSTANT-FREE SHEFFER HUNT (Tier A) ──  The paper's own open problem (EML.tex:533): eml(x,y) =
// exp(x) − ln(y) is a continuous Sheffer operator only PAIRED with the constant 1 — "whether an EML-type binary
// Sheffer working without pairing with a distinguished constant exists is an open question." Under the
// constant-free frame (a distinguished constant is the author's fingerprint; let the alphabet hold nothing it
// didn't earn) the gate question is: can a cousin operator GENERATE its own constants from bare variables {x,y}?
// Disproving a candidate is non-trivial — the paper's trap: B(x,y) = x − y/2 has B(x,x) = x/2 (not constant), yet
// B(B(x,x),x) = 0 at composition depth 2. Constants hide arbitrarily deep, so "no diagonal constant" proves
// nothing and an eyeball proves less. This file is the systematic bounded-depth sweep: enumerate EVERY well-formed
// RPN program over bare {x,y} (NO '1' terminal — that is the whole point) up to shell Kmax per cousin, and put
// each through the 4-POINT CONSTANT GATE. The product is an EXCLUSION TABLE — "no constant at K≤17" is a
// bounded-depth CERTIFICATE (never "impossible") — and any cousin that DOES mint parks its witnesses for Wave-2
// completeness grading. Tier B and Tier C (the ternary
// T(x,x,x)=1 arity trade) are reserved behind the compatibility --rung flag.
//
// THE GATE (why 4 points): a program is a constant candidate iff its value agrees (sig-9, the sieve's own
// quantizer) across ALL pairs of P1=(γ,A), P2=(G,ζ3), P3=(1/δ,1/α), P4=(γ,ζ3). P1/P2 are the sieve's own
// Schanuel pair; P3 is the grader's small-argument REGIME point (absorption towers collapse there — agreement at
// P1/P2/P4 with certified drift at P3 is ConstA, the asymptotic-constant bucket routed to Tier B); P4 CROSSES
// P1's x with P2's y, killing separable f(x)·g(y) coincidences and unmasking constant-in-one-axis values (an
// x-only program agrees at P1/P4 — shared x=γ — and is then split by P2/P3). REFUTATION is threshold-free (the
// grader's own law): an outward-rounded enclosure of value(Pi)−value(Pj) that EXCLUDES 0 disproves constancy
// with no epsilon to argue about. Scope honesty: ConstE certifies "constant at double resolution at 4
// transcendental points, enclosure-backed" — a sub-2⁻⁵³ absorption tower (the 1 + e^(−e^(e^x)) class, 's
// optimal-logarithm family) is indistinguishable from its limit inside doubles and would mint; that is a Wave-2
// grading question, not a gate bug.

/// The unary FACES a cousin composes — U applied to x, V applied to y. Exp/Ln are the paper's transcendental
/// inverse pair; Neg/Inv are the rational involutions (each its own inverse); Id/Half/Sq complete the trap +
/// calibration vocabulary (Half is the face of the paper's B-trap).
public enum EmlFaces { Exp, Ln, Id, Neg, Inv, Half, Sq }

/// The binary MIXER joining the faced arguments: F(x,y) = M(U(x), V(y)). Sub is the paper's own; Div is the
/// ternary T's ratio shape; Add/Mul complete the field vocabulary for later waves.
public enum EmlMixers { Sub, Div, Add, Mul }

/// One EML-cousin binary operator F(x,y) = M(U(x), V(y)) — a candidate Sheffer under the constant-free ban
/// (eml itself is (Exp,Ln,Sub)). Eval mirrors Eml.Eval's validity law: exp-argument overflow REJECTS (a
/// saturating clamp would alias distinct programs onto one value and forge constants), and every INTERMEDIATE
/// must be finite — checked per FACE, not just on the mix, because with Div/Mul mixers an infinite face value
/// can wash out to a forged finite result (1/(−∞) → 0) that Eml's Sub-only shape never exposed. EvalRect is the
/// enclosure twin: outward-rounded rectangle bounds that may go Blown (0-touch, branch-cut straddle, overflow)
/// but never lie. Inv rides the polar route 1/z = e^(−ln z) — honest polar-box bounds, Blown whenever the rect
/// cannot exclude 0 (EmlRect.Log's own law); Sq/Half are entire, so they take direct interval hulls (a polar
/// route would forge a spurious Blown at 0 — refusing to witness where an honest bound exists is its own lie).
public readonly record struct EmlCousin(EmlFaces U, EmlFaces V, EmlMixers M)
{
    /// One gate application at a complex point — EmlValue.Invalid on overflow or a non-finite intermediate.
    public EmlValue Eval(Complex x, Complex y)
    {
        Complex u = Face(U, x);
        if (!IsFinite(u)) return EmlValue.Invalid;
        Complex v = Face(V, y);
        if (!IsFinite(v)) return EmlValue.Invalid;
        Complex r = M switch
        {
            EmlMixers.Sub => u - v,
            EmlMixers.Div => u / v,
            EmlMixers.Add => u + v,
            _             => u * v,
        };
        return IsFinite(r) ? new EmlValue(r, true) : EmlValue.Invalid;
    }

    /// One gate application on rectangle enclosures — Blown degrades the verdict to UNDECIDED, never to a lie.
    public EmlRect EvalRect(EmlRect x, EmlRect y)
    {
        EmlRect u = FaceRect(U, x), v = FaceRect(V, y);
        return M switch
        {
            EmlMixers.Sub => EmlRect.Sub(u, v),
            EmlMixers.Div => MulRect(u, InvRect(v)),
            EmlMixers.Add => AddRect(u, v),
            _             => MulRect(u, v),
        };
    }

    /// The operator as eye-checkable math — "exp(x) − ln(y)", "x − y/2".
    public string Show() => M switch
    {
        EmlMixers.Sub => $"{FaceShow(U, "x")} − {FaceShow(V, "y")}",
        EmlMixers.Div => $"{FaceShow(U, "x")} / {FaceShow(V, "y")}",
        EmlMixers.Add => $"{FaceShow(U, "x")} + {FaceShow(V, "y")}",
        _             => $"{FaceShow(U, "x")} · {FaceShow(V, "y")}",
    };

    private static Complex Face(EmlFaces f, Complex z) => f switch
    {
        EmlFaces.Exp  => z.Real > Eml.ExpReMax ? new Complex(double.NaN, 0) : Complex.Exp(z),   // the paper's overflow REJECT (Eml.Eval's law)
        EmlFaces.Ln   => Complex.Log(z),
        EmlFaces.Id   => z,
        EmlFaces.Neg  => -z,
        EmlFaces.Inv  => Complex.One / z,
        EmlFaces.Half => z / 2,
        _             => z * z,
    };

    private static EmlRect FaceRect(EmlFaces f, EmlRect z) => f switch
    {
        EmlFaces.Exp  => EmlRect.Exp(z),                          // carries its own >709 guard → Blown
        EmlFaces.Ln   => EmlRect.Log(z),
        EmlFaces.Id   => z,
        EmlFaces.Neg  => NegRect(z),
        EmlFaces.Inv  => InvRect(z),
        EmlFaces.Half => HalfRect(z),
        _             => MulRect(z, z),                           // dependency-blind square hull — sound, never narrower than the exact range
    };

    private static bool IsFinite(Complex c) => double.IsFinite(c.Real) && double.IsFinite(c.Imaginary);

    // negation and halving are outward-exact on interval bounds (sign flip / exponent shift — no rounding step)
    private static EmlIv NegIv(EmlIv a)  => a.IsBlown ? EmlIv.Blown : new(-a.Hi, -a.Lo);
    private static EmlIv HalfIv(EmlIv a) => a.IsBlown ? EmlIv.Blown : new(a.Lo / 2, a.Hi / 2);
    private static EmlRect NegRect(EmlRect z)  => new(NegIv(z.Re), NegIv(z.Im));
    private static EmlRect HalfRect(EmlRect z) => new(HalfIv(z.Re), HalfIv(z.Im));

    /// 1/z by the polar route e^(−ln z) — honest polar-box bounds: Blown whenever the rect touches 0 or straddles
    /// the branch cut (inherited from EmlRect.Log), a-dozen-ulps tight on the point-rects the sweep feeds it.
    private static EmlRect InvRect(EmlRect z) => EmlRect.Exp(NegRect(EmlRect.Log(z)));

    private static EmlRect AddRect(EmlRect a, EmlRect b) => new(EmlIv.Add(a.Re, b.Re), EmlIv.Add(a.Im, b.Im));

    /// Complex product hull — (ar·br − ai·bi, ar·bi + ai·br) with interval ops; sound for any rectangles.
    private static EmlRect MulRect(EmlRect a, EmlRect b)
        => a.IsBlown || b.IsBlown ? EmlRect.Blown
         : new(EmlIv.Sub(EmlIv.Mul(a.Re, b.Re), EmlIv.Mul(a.Im, b.Im)),
               EmlIv.Add(EmlIv.Mul(a.Re, b.Im), EmlIv.Mul(a.Im, b.Re)));

    private static string FaceShow(EmlFaces f, string a) => f switch
    {
        EmlFaces.Exp  => $"exp({a})",
        EmlFaces.Ln   => $"ln({a})",
        EmlFaces.Id   => a,
        EmlFaces.Neg  => $"−{a}",
        EmlFaces.Inv  => $"1/{a}",
        EmlFaces.Half => $"{a}/2",
        _             => $"{a}²",
    };
}

/// The generalized well-formed-RPN enumerator — EmlGen.Enumerate's twin with the TERMINAL ALPHABET as a parameter
/// (the constant-free hunt passes bare {x,y}; EmlGen's own {1,x,y} stays untouched in Eml.cs) and a span visitor
/// instead of a per-program string (the sweep grades ~862K programs per cousin × 10 cousins — a string each would
/// be pure GC churn for values read once). Same order (breadth-first by length, lexical within a shell, terminals
/// before the operator) and the same parity prune, which is already alphabet-agnostic — it counts ±1 stack steps,
/// not symbols: valid lengths are odd (T leaves force T−1 binary ops, length = 2T−1), and a prefix at height h
/// with rem tokens left survives iff h−rem ≤ 1 ≤ h+rem with (h+rem) odd. Returns the exact program count so the
/// caller can double-entry it against the closed form Σ Catalan(T−1)·|terminals|^T.
public static class ShefferGen
{
    public delegate void RpnVisit(ReadOnlySpan<char> prog);

    public static long Enumerate(ReadOnlySpan<char> terminals, int maxLen, RpnVisit visit)
    {
        Span<char> buf = stackalloc char[Math.Max(1, maxLen)];
        long n = 0;
        for (int len = 1; len <= maxLen; len += 2)
            n += Build(buf, terminals, 0, 0, len, visit);
        return n;
    }

    private static long Build(Span<char> buf, ReadOnlySpan<char> terminals, int pos, int h, int len, RpnVisit visit)
    {
        if (pos == len)
        {
            if (h != 1) return 0;
            visit(buf[..len]);
            return 1;
        }
        long n = 0;
        int rem = len - pos - 1;
        if (Reachable(h + 1, rem))
            foreach (char t in terminals)
            {
                buf[pos] = t;
                n += Build(buf, terminals, pos + 1, h + 1, len, visit);
            }
        if (h >= 2 && Reachable(h - 1, rem))
        {
            buf[pos] = Eml.Op;
            n += Build(buf, terminals, pos + 1, h - 1, len, visit);
        }
        return n;
    }

    // can a run of `rem` ±1 steps from height `h` land on exactly 1? (range + parity — EmlGen.Reachable's law)
    private static bool Reachable(int h, int rem) => h - rem <= 1 && h + rem >= 1 && ((h + rem) & 1) == 1;

    /// Σ_{T : 2T−1 ≤ maxLen} Catalan(T−1)·alphabet^T — the closed-form count the enumerator must reproduce
    /// (integer-exact through the sweep's range; the recurrence C(n)=C(n−1)·2(2n−1)/(n+1) divides evenly there).
    public static long Expected(int maxLen, int alphabet)
    {
        long total = 0, cat = 1, assigns = 1;
        for (int t = 1; 2 * t - 1 <= maxLen; t++)
        {
            if (t > 1) cat = cat * 2 * (2 * t - 3) / t;
            assigns *= alphabet;
            total += cat * assigns;
        }
        return total;
    }
}

/// The tier-A sweep — `cogito sheffer --rung a`: the exclusion table over 10 pre-registered cousins. Rows = the
/// 4 inverse-pair chiralities — (Exp,Ln)/(Ln,Exp), the paper's transcendental pair in both orders, and
/// (Inv,Inv)/(Neg,Neg), the rational involutions (each its own inverse, one chirality) — × {Sub,Div}, plus two
/// CALIBRATION rows whose constants are known a priori: the paper's trap B=(Id,Half,Sub) (constant only at
/// composition depth 2 — the gate MUST see through the non-constant diagonal) and (Id,Id,Sub) (x−x=0). The
/// calibrations are pre-registered KILL-LINES: (Id,Half,Sub) must mint ConstE 0 at K=5 witness `xxExE`, and
/// (Id,Id,Sub) at K=3 witness `xxE` — if either stays silent the gate is miscalibrated and the table is VOID
/// (exit 1, positive controls before conclusions). Deterministic end to end: pure enumeration, no RNG, fixed
/// probe points — the table is a reproducible certificate. Artifacts land in runs/sheffer_NNNN/.
public static class ShefferSweep
{
    // ── the 4 probe points (PtX[i], PtY[i]) — P1/P2 the sieve's Schanuel pair, P3 the regime point, P4 the cross ──
    private static readonly Complex[] PtX =
    [
        new(EmlSieve.Gamma, 0),                                   // P1.x  γ    Euler–Mascheroni
        new(EmlSieve.Catalan, 0),                                 // P2.x  G    Catalan
        new(1.0 / EmlGrader.FeigenbaumDelta, 0),                  // P3.x  1/δ  regime (small argument)
        new(EmlSieve.Gamma, 0),                                   // P4.x  γ    CROSS — P1's x
    ];

    private static readonly Complex[] PtY =
    [
        new(EmlSieve.Glaisher, 0),                                // P1.y  A    Glaisher–Kinkelin
        new(EmlSieve.Apery, 0),                                   // P2.y  ζ3   Apéry
        new(1.0 / EmlGrader.FeigenbaumAlpha, 0),                  // P3.y  1/α  regime
        new(EmlSieve.Apery, 0),                                   // P4.y  ζ3   CROSS — P2's y
    ];

    // pair indices over the 4 points: HOME pairs among {P1,P2,P4} (comparable argument scale), REGIME pairs vs P3
    private static readonly (int A, int B)[] HomePairs   = [(0, 1), (0, 3), (1, 3)];
    private static readonly (int A, int B)[] RegimePairs = [(0, 2), (1, 2), (3, 2)];

    /// The pre-registered Wave-1 rows (order is the table's order; 8/9 are the kill-line calibrations).
    private static readonly (EmlCousin C, string Tag, string Role)[] Rows =
    [
        (new(EmlFaces.Exp, EmlFaces.Ln,  EmlMixers.Sub), "ExpLn.Sub",  "eml's own face, starved of its 1"),
        (new(EmlFaces.Exp, EmlFaces.Ln,  EmlMixers.Div), "ExpLn.Div",  "the ternary T's ratio shape"),
        (new(EmlFaces.Ln,  EmlFaces.Exp, EmlMixers.Sub), "LnExp.Sub",  ""),
        (new(EmlFaces.Ln,  EmlFaces.Exp, EmlMixers.Div), "LnExp.Div",  ""),
        (new(EmlFaces.Inv, EmlFaces.Inv, EmlMixers.Sub), "InvInv.Sub", ""),
        (new(EmlFaces.Inv, EmlFaces.Inv, EmlMixers.Div), "InvInv.Div", ""),
        (new(EmlFaces.Neg, EmlFaces.Neg, EmlMixers.Sub), "NegNeg.Sub", ""),
        (new(EmlFaces.Neg, EmlFaces.Neg, EmlMixers.Div), "NegNeg.Div", ""),
        (new(EmlFaces.Id,  EmlFaces.Half, EmlMixers.Sub), "IdHalf.Sub", "CALIBRATION — the paper's trap B"),
        (new(EmlFaces.Id,  EmlFaces.Id,  EmlMixers.Sub), "IdId.Sub",   "CALIBRATION — x−x"),
    ];

    // per-program verdict census — the order is the table's severity ladder; Σ over the enum = programs enumerated
    private enum Verdicts : byte { Invalid, RefutedEncl, RefutedSig, ConstU, ConstA, ConstE }

    private readonly record struct PairRead(bool Q9, bool Q12, EmlGrader.Encl E);

    /// Per-cousin sweep result — everything one exclusion-table row prints, plus the parked positives for Tier B.
    private sealed class Lane
    {
        public long Programs;
        public long[][] ByShell = null!;                          // [shell][verdict] — Σ = Programs (Total Accounting)
        public (int K, string Prog, Complex V)? FirstE, FirstA;
        public List<(char Verdict, int K, string Prog, Complex V)> Park = new();   // distinct value-sigs, enumeration order (shortest-first)
        public HashSet<EmlSig> ParkSigs = new();
        public long ParkOverflow;                                 // distinct constant values past the cap — counted, never silently lost
        public long WallMs;

        public long Of(Verdicts v) { long n = 0; foreach (var s in ByShell) n += s[(int)v]; return n; }
    }

    public static int Run(string[] args)
    {
        string tier = Args.Str(args, "--rung", "a").ToLowerInvariant();
        if (tier != "a")
        {
            Console.WriteLine($"sheffer: tier '{tier}' is a later tier (b = asymptotic with rate certificate, c = ternary arity trade) — only --rung a (exact, 4-point gate) is built.");
            return 2;
        }
        int kmax = Math.Clamp(Args.Int(args, "--kmax", 17), 1, 21);
        if ((kmax & 1) == 0) kmax--;                              // shells are odd lengths — snap down, never past the ruler
        int parkCap = Math.Max(1, Args.Int(args, "--park", 64));

        long expected = ShefferGen.Expected(kmax, 2);
        Trace.Note($"sheffer tier A — {Rows.Length} cousins × {expected:N0} constant-free programs (K≤{kmax}) × 4 probe points");

        var lanes = new Lane[Rows.Length];
        Parallel.For(0, Rows.Length, i =>
        {
            lanes[i] = SweepCousin(Rows[i].C, Rows[i].Tag, kmax, parkCap);
            var l = lanes[i];
            Trace.Note($"sheffer {Rows[i].Tag,-10} {l.Programs:N0} programs · E {l.Of(Verdicts.ConstE)} A {l.Of(Verdicts.ConstA)} U {l.Of(Verdicts.ConstU)} · {l.WallMs} ms");
        });

        // ── the enumerator's double-entry: recursion count vs closed form, per cousin ──
        for (int i = 0; i < lanes.Length; i++)
            if (lanes[i].Programs != expected)
            {
                Console.WriteLine($"sheffer: ENUMERATOR SKEW — {Rows[i].Tag} walked {lanes[i].Programs:N0} programs, closed form says {expected:N0}; table not shipped.");
                return 1;
            }

        // ── the kill-lines (pre-registered positive controls — silent ⇒ the gate is miscalibrated, table VOID) ──
        bool gated = kmax >= 5;
        bool killTrap  = gated && FiredAs(lanes[8], 5, "xxExE");
        bool killPlain = gated && FiredAs(lanes[9], 3, "xxE");

        var report = Compose(lanes, kmax, expected, killTrap, killPlain, gated);
        Console.Write(report);

        var run = Cogito.Run.New("sheffer");
        run.Write("table.txt", report);
        run.Write("shells.tsv", ShellsTsv(lanes, kmax));
        run.Write("parked.tsv", ParkedTsv(lanes));

        if (!gated) { Console.WriteLine($"sheffer: kmax {kmax} < 5 — the kill-lines cannot fire; SMOKE ONLY, table ungated."); return 3; }
        return killTrap && killPlain ? 0 : 1;
    }

    /// Did a calibration lane's FIRST ConstE land exactly as pre-registered — shell K, witness program, value 0?
    private static bool FiredAs(Lane l, int k, string prog)
        => l.FirstE is { } w && w.K == k && w.Prog == prog && Complex.Abs(w.V) < 1e-15;

    private static Lane SweepCousin(EmlCousin c, string tag, int kmax, int parkCap)
    {
        int shells = (kmax + 1) / 2;
        var lane = new Lane { ByShell = new long[shells][] };
        for (int s = 0; s < shells; s++) lane.ByShell[s] = new long[6];
        long t0 = Trace.NowTicks;
        int lastLen = 0;

        lane.Programs = ShefferGen.Enumerate([Eml.VarX, Eml.VarY], kmax, prog =>
        {
            if (prog.Length != lastLen)
            {
                lastLen = prog.Length;
                if (lastLen >= 15) Trace.Note($"sheffer {tag} → shell K={lastLen}");
            }
            var verdict = Gate(c, prog, out var v, out var sig);
            lane.ByShell[(prog.Length - 1) / 2][(int)verdict]++;
            if (verdict < Verdicts.ConstU) return;

            // a positive — park it (Wave-2 fuel), canonical-shortest per distinct value-sig, overflow counted
            if (verdict == Verdicts.ConstE && lane.FirstE is null) lane.FirstE = (prog.Length, new string(prog), v);
            if (verdict == Verdicts.ConstA && lane.FirstA is null) lane.FirstA = (prog.Length, new string(prog), v);
            if (lane.ParkSigs.Count >= parkCap) { if (!lane.ParkSigs.Contains(sig)) lane.ParkOverflow++; return; }
            if (lane.ParkSigs.Add(sig))
                lane.Park.Add((verdict == Verdicts.ConstE ? 'E' : verdict == Verdicts.ConstA ? 'A' : 'U', prog.Length, new string(prog), v));
        });
        lane.WallMs = Trace.ElapsedMs(t0);
        return lane;
    }

    /// The 4-point constant gate for one program. Invalid = not finite at all 4 points (a constant witness must
    /// stand everywhere probed). Otherwise the grader's decision tree (EmlGrader.Verdict's law re-expressed for a
    /// constancy claim): refuted at a HOME pair → Refuted (enclosure-certified when some home pair EXCLUDES 0,
    /// quantizer-refuted otherwise); refuted only against the REGIME point → ConstA; all six pairs pass
    /// (enclosure Contains, or Undecided rescued by sig-12) → ConstE; anything else → ConstU (agreement without
    /// a witnessable certificate).
    private static Verdicts Gate(in EmlCousin c, ReadOnlySpan<char> prog, out Complex value, out EmlSig sig)
    {
        Span<Complex> v = stackalloc Complex[4];
        Span<EmlRect> r = stackalloc EmlRect[4];
        value = default; sig = default;
        for (int i = 0; i < 4; i++)
            if (!EvalProg(in c, prog, PtX[i], PtY[i], out v[i], out r[i]))
                return Verdicts.Invalid;
        value = v[0];

        Span<PairRead> home   = stackalloc PairRead[3];
        Span<PairRead> regime = stackalloc PairRead[3];
        for (int i = 0; i < 3; i++)
        {
            home[i]   = Pair(v[HomePairs[i].A],   v[HomePairs[i].B],   r[HomePairs[i].A],   r[HomePairs[i].B]);
            regime[i] = Pair(v[RegimePairs[i].A], v[RegimePairs[i].B], r[RegimePairs[i].A], r[RegimePairs[i].B]);
        }

        bool refuted = false, byEncl = false;
        for (int i = 0; i < 3; i++)
            if (Refuted(home[i])) { refuted = true; byEncl |= home[i].E == EmlGrader.Encl.Excludes; }
        if (refuted) return byEncl ? Verdicts.RefutedEncl : Verdicts.RefutedSig;

        sig = Eml.Signature(new EmlValue(v[0], true), new EmlValue(v[1], true), 9);
        for (int i = 0; i < 3; i++)
            if (Refuted(regime[i])) return Verdicts.ConstA;

        bool all = true;
        for (int i = 0; i < 3; i++) all &= Pass(home[i]) && Pass(regime[i]);
        return all ? Verdicts.ConstE : Verdicts.ConstU;
    }

    private static PairRead Pair(Complex a, Complex b, EmlRect ra, EmlRect rb)
        => new(Eml.AgreeSig(a, b, 9), Eml.AgreeSig(a, b, 12), EmlGrader.EnclAt(a, b, ra, rb));

    private static bool Refuted(PairRead p) => p.E == EmlGrader.Encl.Excludes || (p.E == EmlGrader.Encl.Undecided && !p.Q9 && !p.Q12);
    private static bool Pass(PairRead p)    => p.E == EmlGrader.Encl.Contains || (p.E == EmlGrader.Encl.Undecided && p.Q12);

    /// One RPN walk carrying plain value + rectangle enclosure together (EvalLadder's shape) over the cousin's
    /// gate. The enumerator only emits well-formed RPN (its count is cross-checked against the closed form), so
    /// no underflow bookkeeping. False = invalid at this point (overflow / non-finite intermediate).
    private static bool EvalProg(in EmlCousin c, ReadOnlySpan<char> prog, Complex x, Complex y, out Complex value, out EmlRect rect)
    {
        int peak = (prog.Length + 1) / 2;                         // worst case: all leaves first
        Span<Complex> vs = stackalloc Complex[peak];
        Span<EmlRect> rs = stackalloc EmlRect[peak];
        int sp = 0;
        foreach (char t in prog)
        {
            if (t == Eml.Op)
            {
                Complex b = vs[--sp]; EmlRect rb = rs[sp];
                Complex a = vs[--sp]; EmlRect ra = rs[sp];
                var m = c.Eval(a, b);
                if (!m.Finite) { value = default; rect = default; return false; }
                vs[sp] = m.Value;
                rs[sp++] = c.EvalRect(ra, rb);
            }
            else
            {
                Complex leaf = t == Eml.VarX ? x : y;
                rs[sp] = EmlRect.Point(leaf);
                vs[sp++] = leaf;
            }
        }
        value = vs[0]; rect = rs[0];
        return true;
    }

    // ── the report + artifacts ──

    private static string Compose(Lane[] lanes, int kmax, long expected, bool killTrap, bool killPlain, bool gated)
    {
        var sb = new StringBuilder(8192);
        sb.AppendLine($"sheffer tier A — the constant-free exclusion table (Kmax={kmax} · {expected:N0} programs/cousin · terminals {{x,y}}, no 1)");
        sb.AppendLine($"probe points: P1=(γ={F(PtX[0].Real)}, A={F(PtY[0].Real)})  P2=(G={F(PtX[1].Real)}, ζ3={F(PtY[1].Real)})  P3=(1/δ={F(PtX[2].Real)}, 1/α={F(PtY[2].Real)})  P4=(γ, ζ3) [cross]");
        sb.AppendLine($"gate: candidate = sig-9 agreement across all point-pairs · refutation = enclosure of v(Pi)−v(Pj) excludes 0 (threshold-free) · ConstA = home-constant, certified drift at P3");
        sb.AppendLine();
        sb.AppendLine($"{"cousin",-11} {"operator",-17} {"verdict",-44} {"programs",10} {"invalid",9} {"refEncl",10} {"refSig",8} {"cE",5} {"cA",5} {"cU",5} {"parked",7}");
        sb.AppendLine(new string('─', 140));
        foreach (var (lane, row) in lanes.Zip(Rows))
        {
            long cE = lane.Of(Verdicts.ConstE), cA = lane.Of(Verdicts.ConstA), cU = lane.Of(Verdicts.ConstU);
            string verdict =
                lane.FirstE is { } e ? $"ConstE {F2(e.V)} @K={e.K} {e.Prog}"
              : lane.FirstA is { } a ? $"ConstA @K={a.K} {a.Prog} (P3-drift → Tier B)"
              : cU > 0               ? $"UNRESOLVED — {cU} agree-undecided, 0 certified"
              :                        $"EXCLUDED at K≤{kmax}";
            sb.AppendLine($"{row.Tag,-11} {row.C.Show(),-17} {verdict,-44} {lane.Programs,10:N0} {lane.Of(Verdicts.Invalid),9:N0} {lane.Of(Verdicts.RefutedEncl),10:N0} {lane.Of(Verdicts.RefutedSig),8:N0} {cE,5:N0} {cA,5:N0} {cU,5:N0} {lane.Park.Count,7:N0}");
            if (row.Role.Length > 0) sb.AppendLine($"{"",-11} └ {row.Role}{(lane.ParkOverflow > 0 ? $" · +{lane.ParkOverflow:N0} distinct values past the park cap" : "")}");
            else if (lane.ParkOverflow > 0) sb.AppendLine($"{"",-11} └ +{lane.ParkOverflow:N0} distinct values past the park cap");
        }
        sb.AppendLine();

        if (gated)
        {
            sb.AppendLine($"kill-lines: IdHalf.Sub ConstE 0 @K=5 xxExE {(killTrap ? "✓ fired" : "✗ SILENT")} · IdId.Sub ConstE 0 @K=3 xxE {(killPlain ? "✓ fired" : "✗ SILENT")}");
            if (!killTrap || !killPlain)
            {
                sb.AppendLine("KILL-LINE SILENT — the gate is miscalibrated; this table is VOID. Diagnose before trusting any row above.");
                if (lanes[8].FirstE is { } t8) sb.AppendLine($"  IdHalf.Sub actually minted first at K={t8.K} prog {t8.Prog} → {F2(t8.V)}");
                else sb.AppendLine("  IdHalf.Sub minted nothing at all");
                if (lanes[9].FirstE is { } t9) sb.AppendLine($"  IdId.Sub actually minted first at K={t9.K} prog {t9.Prog} → {F2(t9.V)}");
                else sb.AppendLine("  IdId.Sub minted nothing at all");
            }
        }

        // the dichotomy, read off the data — same-face/rational rows vs the transcendental inverse-pair rows
        var rational = string.Join(" · ", Enumerable.Range(4, 4).Select(i => $"{Rows[i].Tag} {(lanes[i].FirstE is { } w ? $"E@K={w.K}" : "silent")}"));
        var trans    = string.Join(" · ", Enumerable.Range(0, 4).Select(i =>
            $"{Rows[i].Tag} {(lanes[i].FirstE is { } w ? $"E@K={w.K}" : lanes[i].FirstA is { } wa ? $"A@K={wa.K}" : lanes[i].Of(Verdicts.ConstU) > 0 ? "unresolved" : "EXCLUDED")}"));
        sb.AppendLine($"dichotomy: rational/same-face — {rational}");
        sb.AppendLine($"           transcendental inverse-pair — {trans}");
        return sb.ToString();
    }

    private static string ShellsTsv(Lane[] lanes, int kmax)
    {
        var sb = new StringBuilder(4096);
        sb.AppendLine("cousin\tK\tprograms\tinvalid\trefuted_encl\trefuted_sig\tconstE\tconstA\tconstU");
        foreach (var (lane, row) in lanes.Zip(Rows))
            for (int s = 0; s < lane.ByShell.Length; s++)
            {
                var b = lane.ByShell[s];
                long total = 0; foreach (var n in b) total += n;
                sb.AppendLine($"{row.Tag}\t{2 * s + 1}\t{total}\t{b[(int)Verdicts.Invalid]}\t{b[(int)Verdicts.RefutedEncl]}\t{b[(int)Verdicts.RefutedSig]}\t{b[(int)Verdicts.ConstE]}\t{b[(int)Verdicts.ConstA]}\t{b[(int)Verdicts.ConstU]}");
            }
        return sb.ToString();
    }

    private static string ParkedTsv(Lane[] lanes)
    {
        var sb = new StringBuilder(4096);
        sb.AppendLine("cousin\tverdict\tK\tprog\tvalue");
        foreach (var (lane, row) in lanes.Zip(Rows))
            foreach (var (verdict, k, prog, val) in lane.Park)
                sb.AppendLine($"{row.Tag}\t{verdict}\t{k}\t{prog}\t{F2(val)}");
        return sb.ToString();
    }

    private static string F(double v) => v.ToString("G7", CultureInfo.InvariantCulture);

    // round-trip-exact complex render (EmlGrader.AnomalyLabel's shape without the corr: prefix)
    private static string F2(Complex v)
        => v.Imaginary == 0
            ? v.Real.ToString("R", CultureInfo.InvariantCulture)
            : $"{v.Real.ToString("R", CultureInfo.InvariantCulture)}{(v.Imaginary < 0 ? "" : "+")}{v.Imaginary.ToString("R", CultureInfo.InvariantCulture)}i";
}
