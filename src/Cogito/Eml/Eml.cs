namespace Cogito;

using System.Numerics;
using System.Diagnostics;
using System.Text;
using Cogito.Grammar;
using Cogito.Induct;


// ── THE EML SUBSTRATE ──  eml(x,y) = exp(x) − ln(y), paired with the constant 1, is a continuous Sheffer operator:
// the NAND of elementary mathematics (Odrzywołek 2024, docs/papers/eml/EML.tex). One binary gate + one terminal
// generate the whole scientific calculator — e = eml(1,1), eˣ = eml(x,1), ln x = eml(1,eml(eml(1,x),1)), and so on
// for π, i, +, ×, √, the trig/hyperbolic families. It is the most cogito-shaped computational substrate there is
//: an RPN-EML program is a string over a ~two-token alphabet, so the byte-tape IS the token-tape
// and Engine.Induce chunks recurring RPN substrings directly — a rule expanding to ln's RPN string IS ln, rule
// depth = formula depth, and the compression ladder becomes mathematical abstraction (constants → arithmetic →
// functions → identities). This file is the PURE substrate: the alphabet, the clamped complex evaluator, the
// witness-ladder GRADER (enclosure arithmetic + the one witness-grading authority), the numeric equivalence SIEVE
// (grade-gated at mint), the THEOREM CAS (EmlCert — mints content-addressed by their (grade, limit, rate-law)
// certificate, ), and THREE-RAIL generation (EmlSampler: chunk-bias for depth · uniform ε for
// support · the systematic ε-enumeration sweep for coverage). ReplayCalc.cs is the cogito env that dreams over it.

/// The RPN-EML alphabet — the paper's grammar S → 1 | eml(S,S) plus input variables, as single ASCII bytes so a
/// program is just a byte string cogito's byte-Re-Pair induces over natively. eml is non-commutative (chirality
/// matters): in RPN it pops y (second-pushed) then x (first-pushed) and pushes exp(x) − ln(y).
public static class Eml
{
    public const char One  = '1';   // the distinguished constant — neutralizes the ln term via ln 1 = 0
    public const char Op   = 'E';   // the binary Sheffer gate eml: pop y, pop x → push exp(x) − ln(y)
    public const char VarX = 'x';   // input variable x
    public const char VarY = 'y';   // input variable y

    /// exp overflow guard. exp(709.78…) ≈ double.MaxValue, so a real part past this overflows to +∞. The paper
    /// CLAMPS the exp argument for gradient stability; the SIEVE instead REJECTS the whole expression as invalid on
    /// the overflow side — a saturated clamp would alias distinct overflowing programs onto one value and forge a
    /// spurious identity, corrupting the sieve (the verdict must be discrete — the four-discretizers law). The
    /// underflow side (large-negative Re → exp → 0) is KEPT: it is the paper's intended extended-real behaviour
    /// (ln 0 = −∞, e^−∞ = 0), on which real formulas depend, and 0 is finite so it never poisons a value.
    public const double ExpReMax = 700.0;

    /// The longest program the stack-machine evaluates — bounds the stackalloc and rejects pathological input.
    public const int MaxProgramLen = 512;

    public static bool IsToken(char c) => c is One or Op or VarX or VarY;

    /// Evaluate an RPN-EML program at the point (x, y) over ℂ on the principal branch. INVALID (Finite=false) on any
    /// of: malformed RPN (stack underflow, unknown token, or not a single final value), exp-argument overflow
    /// (Re > ExpReMax), or a non-finite intermediate (ln 0 = −∞, ∞−∞ = NaN — the paper's complex-NaN guard).
    /// Deterministic — no RNG, no globals, no allocation (the Vow holds byte-for-byte across runs).
    public static EmlValue Eval(ReadOnlySpan<char> prog, Complex x, Complex y)
    {
        if (prog.Length == 0 || prog.Length > MaxProgramLen) return EmlValue.Invalid;
        Span<Complex> stack = stackalloc Complex[prog.Length];
        int sp = 0;
        foreach (char c in prog)
        {
            switch (c)
            {
                case One:  stack[sp++] = Complex.One; break;
                case VarX: stack[sp++] = x; break;
                case VarY: stack[sp++] = y; break;
                case Op:
                    if (sp < 2) return EmlValue.Invalid;
                    Complex b = stack[--sp];                              // y — second pushed, first popped
                    Complex a = stack[--sp];                              // x — first pushed
                    if (a.Real > ExpReMax) return EmlValue.Invalid;       // exp would overflow to +∞
                    Complex v = Complex.Exp(a) - Complex.Log(b);
                    if (!IsFinite(v)) return EmlValue.Invalid;
                    stack[sp++] = v;
                    break;
                default: return EmlValue.Invalid;                        // not an RPN-EML token
            }
        }
        if (sp != 1) return EmlValue.Invalid;
        return IsFinite(stack[0]) ? new EmlValue(stack[0], true) : EmlValue.Invalid;
    }

    private static bool IsFinite(Complex c) => double.IsFinite(c.Real) && double.IsFinite(c.Imaginary);

    /// The dual-point equivalence SIGNATURE — the discrete key the sieve buckets on. Two programs share a signature
    /// iff their complex values agree to `sig` significant figures at BOTH probe points; the DUAL point kills the
    /// ULP fluke a single point admits (a coincidental agreement would have to recur at an independent second
    /// transcendental substitution). Significant-figure rounding IS a relative-epsilon test, and quantizing here is
    /// what turns the analysis-plane float into the discrete equivalence verdict cogito mints onto its corpus.
    public static EmlSig Signature(EmlValue p1, EmlValue p2, int sig)
        => new(Q(p1.Value.Real, sig), Q(p1.Value.Imaginary, sig), Q(p2.Value.Real, sig), Q(p2.Value.Imaginary, sig));

    /// Quantize a real to a scale-relative integer key: (decade, mantissa rounded to `sig` figures) packed into one
    /// long. Deterministic; two reals agreeing to `sig` figures pack identically except at rare decade boundaries,
    /// where the dual-point requirement renders a split harmless (at worst a value re-registers as its own canonical
    /// entry — never a forged merge). `sig` ≤ 9 keeps |mantissa| < 10¹⁰ so the pack is collision-free.
    private static long Q(double v, int sig)
    {
        if (v == 0.0 || !double.IsFinite(v)) return 0;
        var (decade, m) = DecadeMant(v, sig);
        return (long)(decade + 512) * 10_000_000_000L + m;               // decade offset ‖ mantissa
    }

    // the quantizer core — (decade, sig-figure integer mantissa). ONE authority for the sieve's packed Q and the
    // regrade ladder's unpacked AgreeSig, so both express the same equivalence relation at equal sig.
    private static (int Decade, long Mant) DecadeMant(double v, int sig)
    {
        int decade = (int)Math.Floor(Math.Log10(Math.Abs(v)));
        double mant = v / Math.Pow(10, decade);                          // (−10,−1] ∪ [1,10)
        return (decade, (long)Math.Round(mant * Math.Pow(10, sig - 1)));
    }

    /// Do two reals agree to `sig` significant figures under the sieve's OWN quantizer? The regrade ladder's
    /// RESOLUTION tiers (sig 6/9/12) — an unpacked tuple compare, so sig may exceed the packed key's ≤9 bound.
    /// Zero/non-finite collapse to one class, mirroring Q's `return 0`.
    public static bool AgreeSig(double a, double b, int sig)
    {
        bool za = a == 0.0 || !double.IsFinite(a), zb = b == 0.0 || !double.IsFinite(b);
        if (za || zb) return za == zb;
        return DecadeMant(a, sig) == DecadeMant(b, sig);
    }

    public static bool AgreeSig(Complex a, Complex b, int sig)
        => AgreeSig(a.Real, b.Real, sig) && AgreeSig(a.Imaginary, b.Imaginary, sig);

    /// Eval + enclosure + absorption in ONE walk — the regrade ladder's evaluator. Same control flow as Eval with
    /// the Invalid conditions read off the PLAIN values, so `Plain` is bit-for-bit what the sieve itself saw (the
    /// quantizer tiers read it); the rectangle stack rides along enclosing the TRUE value at the exact double probe
    /// point (the enclosure tier); the absorption witness is taken at the one addition site exp(a) − ln(b).
    public static EmlLadder EvalLadder(ReadOnlySpan<char> prog, Complex x, Complex y)
    {
        if (prog.Length == 0 || prog.Length > MaxProgramLen) return EmlLadder.Invalid;
        Span<Complex> stack = stackalloc Complex[prog.Length];
        Span<EmlRect> rects = stackalloc EmlRect[prog.Length];
        int sp = 0;
        double minRatio = 1.0; int subEps = 0;
        foreach (char c in prog)
        {
            switch (c)
            {
                case One:  rects[sp] = EmlRect.Point(Complex.One); stack[sp++] = Complex.One; break;
                case VarX: rects[sp] = EmlRect.Point(x); stack[sp++] = x; break;
                case VarY: rects[sp] = EmlRect.Point(y); stack[sp++] = y; break;
                case Op:
                    if (sp < 2) return EmlLadder.Invalid;
                    Complex b = stack[--sp]; EmlRect rb = rects[sp];
                    Complex a = stack[--sp]; EmlRect ra = rects[sp];
                    if (a.Real > ExpReMax) return EmlLadder.Invalid;
                    Complex expA = Complex.Exp(a), lnB = Complex.Log(b);
                    Complex v = expA - lnB;
                    if (!IsFinite(v)) return EmlLadder.Invalid;
                    double mE = Complex.Abs(expA), mL = Complex.Abs(lnB);
                    if (mE > 0 && mL > 0 && mE != mL)
                        minRatio = Math.Min(minRatio, Math.Min(mE, mL) / Math.Max(mE, mL));
                    // sub-eps absorption: the subtraction was a bitwise no-op on the dominant term while the other
                    // term was nonzero (as a true real) — invisible to EVERY resolution tier; the ln-tower mechanism.
                    if (mL > 0 && v == expA) { subEps++; minRatio = 0; }
                    else if (v == -lnB) { subEps++; minRatio = 0; }      // exp side vanished (bit-absorbed or underflow-to-0; true e^Re(a) > 0 always)
                    rects[sp] = EmlRect.Sub(EmlRect.Exp(ra), EmlRect.Log(rb));
                    stack[sp++] = v;
                    break;
                default: return EmlLadder.Invalid;
            }
        }
        if (sp != 1) return EmlLadder.Invalid;
        return IsFinite(stack[0]) ? new EmlLadder(new EmlValue(stack[0], true), rects[0], minRatio, subEps) : EmlLadder.Invalid;
    }
}

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
//  THE WITNESS-LADDER SUBSTRATE — enclosure arithmetic for the retro-regrade (resolution × regime × enclosure)
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

/// One ladder evaluation: the PLAIN value (bit-identical to Eval — the sieve's own semantics), the rectangle
/// ENCLOSURE of the true value at the exact double probe point, and the ABSORPTION WITNESS. MinRatio = the
/// smallest nonzero |minor|/|major| across the program's E-ops (how lopsided the subtraction got; 0 once a term
/// vanished bitwise); SubEpsOps = ops where a nonzero term vanished BITWISE from the result — the absorption no
/// resolution tier can ever see, the mechanism behind every ln-tower false-exact.
public readonly record struct EmlLadder(EmlValue Plain, EmlRect Rect, double MinRatio, int SubEpsOps)
{
    public static readonly EmlLadder Invalid = new(EmlValue.Invalid, EmlRect.Blown, 1.0, 0);
}

/// One outward-rounded interval — an axis of the rectangle enclosure. The enclosure is the principled tier the
/// magic-sig quantizer approximates: an enclosure of lhs−rhs that EXCLUDES 0 is a threshold-free DISPROOF of
/// exactness. Non-finite bounds mark a BLOWN interval (branch-cut hull, overflow): verdicts on it degrade to
/// UNDECIDED — the enclosure can fail to witness, but it cannot lie.
public readonly record struct EmlIv(double Lo, double Hi)
{
    public static readonly EmlIv Blown = new(double.NegativeInfinity, double.PositiveInfinity);
    public static EmlIv Point(double v) => new(v + 0.0, v + 0.0);        // +0.0 canonicalizes −0.0 (atan2 branch honesty)
    public bool IsBlown => !double.IsFinite(Lo) || !double.IsFinite(Hi);
    public bool Contains(double v) => Lo <= v && v <= Hi;
    public double Width => Hi - Lo;
    public double AbsMax => Math.Max(Math.Abs(Lo), Math.Abs(Hi));

    // outward rounding: 4 ulps covers correctly-rounded ops with headroom for libm's exp/log/sincos/atan2 (≲2 ulp)
    internal static double Dn(double x) => Math.BitDecrement(Math.BitDecrement(Math.BitDecrement(Math.BitDecrement(x))));
    internal static double Up(double x) => Math.BitIncrement(Math.BitIncrement(Math.BitIncrement(Math.BitIncrement(x))));

    public static EmlIv Add(EmlIv a, EmlIv b) => a.IsBlown || b.IsBlown ? Blown : new(Dn(a.Lo + b.Lo), Up(a.Hi + b.Hi));
    public static EmlIv Sub(EmlIv a, EmlIv b) => a.IsBlown || b.IsBlown ? Blown : new(Dn(a.Lo - b.Hi), Up(a.Hi - b.Lo));

    public static EmlIv Mul(EmlIv a, EmlIv b)
    {
        if (a.IsBlown || b.IsBlown) return Blown;
        double p1 = a.Lo * b.Lo, p2 = a.Lo * b.Hi, p3 = a.Hi * b.Lo, p4 = a.Hi * b.Hi;
        return new(Dn(Math.Min(Math.Min(p1, p2), Math.Min(p3, p4))), Up(Math.Max(Math.Max(p1, p2), Math.Max(p3, p4))));
    }

    public static EmlIv Exp(EmlIv a)
        => a.IsBlown || a.Hi > 709.0 ? Blown                             // e^709.79 overflows — the enclosure gives up (plain may still stand)
         : new(Math.Max(0, Dn(Math.Exp(a.Lo))), Up(Math.Exp(a.Hi)));

    public static EmlIv LogP(EmlIv a) => a.IsBlown || a.Lo <= 0 ? Blown : new(Dn(Math.Log(a.Lo)), Up(Math.Log(a.Hi)));

    /// cos over an interval — exact extrema handling: +1 at even multiples of π inside, −1 at odd, else endpoint hull.
    public static EmlIv Cos(EmlIv a)
    {
        if (a.IsBlown) return Blown;
        if (a.Width >= 2 * Math.PI) return new(-1, 1);
        double c1 = Math.Cos(a.Lo), c2 = Math.Cos(a.Hi);
        double lo = Math.Min(c1, c2), hi = Math.Max(c1, c2);
        if (HasMultiple(a, 0)) hi = 1;
        if (HasMultiple(a, Math.PI)) lo = -1;
        return new(Math.Max(-1, Dn(lo)), Math.Min(1, Up(hi)));
    }

    public static EmlIv Sin(EmlIv a) => a.IsBlown ? Blown : Cos(Sub(Point(Math.PI / 2), a));   // sin x = cos(π/2 − x); the π/2-double offset sits inside the 4-ulp widening

    // does the interval contain a point ≡ phase (mod 2π)?
    private static bool HasMultiple(EmlIv a, double phase)
    {
        double k = Math.Ceiling((a.Lo - phase) / (2 * Math.PI));
        return phase + 2 * Math.PI * k <= a.Hi;
    }
}

/// A rectangle enclosure of a complex value. Componentwise intervals (not a disk) on purpose: an exactly-real
/// chain keeps Im = [0,0], which is what lets ln(negative real) stay decided on the principal branch (+iπ)
/// instead of hulling both branches — the deep-tower journals this substrate exists to regrade live on such chains.
public readonly record struct EmlRect(EmlIv Re, EmlIv Im)
{
    public static readonly EmlRect Blown = new(EmlIv.Blown, EmlIv.Blown);
    public static EmlRect Point(Complex v) => new(EmlIv.Point(v.Real), EmlIv.Point(v.Imaginary));
    public bool IsBlown => Re.IsBlown || Im.IsBlown;
    public bool ContainsZero => Re.Contains(0) && Im.Contains(0);
    public double MaxWidth => Math.Max(Re.Width, Im.Width);

    public static EmlRect Sub(EmlRect a, EmlRect b) => new(EmlIv.Sub(a.Re, b.Re), EmlIv.Sub(a.Im, b.Im));

    /// exp over a rectangle: e^Re · (cos Im, sin Im) — the factor intervals' product hull (re and im vary
    /// independently over a rectangle, so the product set is enclosed).
    public static EmlRect Exp(EmlRect z)
    {
        if (z.IsBlown) return Blown;
        var m = EmlIv.Exp(z.Re);
        return m.IsBlown ? Blown : new(EmlIv.Mul(m, EmlIv.Cos(z.Im)), EmlIv.Mul(m, EmlIv.Sin(z.Im)));
    }

    /// principal Log over a rectangle. Re = ln|z| (|z| via nearest/farthest rect point — Hypot, no |z|² overflow);
    /// Im = the atan2 corner hull (extremes sit on corners: along any rect edge atan2 is monotone). BLOWN when the
    /// rectangle touches 0 (ln unbounded) or has points on BOTH sides of the branch cut (the principal value jumps
    /// 2π inside the enclosure). The load-bearing exception: an exactly-real negative rectangle sits ON the cut but
    /// carries no ambiguity — principal assigns +iπ to the whole segment.
    public static EmlRect Log(EmlRect z)
    {
        if (z.IsBlown) return Blown;
        double reN = z.Re.Contains(0) ? 0 : Math.Min(Math.Abs(z.Re.Lo), Math.Abs(z.Re.Hi));
        double imN = z.Im.Contains(0) ? 0 : Math.Min(Math.Abs(z.Im.Lo), Math.Abs(z.Im.Hi));
        double magLo = double.Hypot(reN, imN);
        double magHi = double.Hypot(z.Re.AbsMax, z.Im.AbsMax);
        var mag = new EmlIv(Math.Max(0, EmlIv.Dn(magLo)), EmlIv.Up(magHi));
        if (mag.Lo <= 0) return Blown;                                   // encloses 0 — ln unbounded
        var re = EmlIv.LogP(mag);
        EmlIv im;
        if (z.Im.Lo == 0 && z.Im.Hi == 0)
            im = z.Re.Lo > 0 ? new EmlIv(0, 0)
               : z.Re.Hi < 0 ? new EmlIv(EmlIv.Dn(Math.PI), EmlIv.Up(Math.PI))   // exactly-real negative: principal +iπ
               : EmlIv.Blown;                                            // unreachable — the mag guard excluded 0
        else if (z.Im.Lo < 0 && z.Im.Hi >= 0 && z.Re.Lo < 0)
            im = EmlIv.Blown;                                            // straddles the cut — both branches enclosed, honest give-up
        else
        {
            double a1 = Math.Atan2(z.Im.Lo, z.Re.Lo), a2 = Math.Atan2(z.Im.Lo, z.Re.Hi);
            double a3 = Math.Atan2(z.Im.Hi, z.Re.Lo), a4 = Math.Atan2(z.Im.Hi, z.Re.Hi);
            im = new(EmlIv.Dn(Math.Min(Math.Min(a1, a2), Math.Min(a3, a4))), EmlIv.Up(Math.Max(Math.Max(a1, a2), Math.Max(a3, a4))));
        }
        return im.IsBlown ? Blown : new(re, im);
    }
}

/// One RPN-EML evaluation outcome — the complex value + whether it is finite (a valid discovery witness). Floats
/// live here (the analysis plane); the discrete verdict is the EmlSig the sieve derives, never the raw value.
public readonly record struct EmlValue(Complex Value, bool Finite)
{
    public static readonly EmlValue Invalid = new(default, false);
}

/// The dual-point value signature — four packed integers (Re/Im at each of the two probe points). Value equality
/// + auto hash make it the sieve's bucket key: same EmlSig ⟺ equivalent under the Schanuel-backed numeric sieve.
/// (Named EmlSig, not Sig, to stay clear of SimHash's `Sig(ulong Bits)` — a different signature in the same namespace.)
public readonly record struct EmlSig(long R1, long I1, long R2, long I2);

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
//  THE WITNESS GRADER — the ONE witness-grading authority (live mint-gate + retro census, same law)
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

/// One claim's verdict under the three-axis witness ladder. `Grade` is the RG scaling class of the residual:
/// E exact (passes every tier at every point) · A asymptotic (true at the minting scale, refuted in the
/// small-argument regime — its `Corr3` residual is the machine's next target) · S scale-local (refuted at the
/// minting points themselves — a quantizer coincidence) · D domain-restricted (a point is unreachable —
/// overflow/singularity; true-at-scale, untestable beyond) · U unwitnessable (enclosure blown, quantizer
/// ambiguous). The absorption witness (`SubEpsHome`/`MinRatioHome`) names the MECHANISM: a term that vanished
/// bitwise under the subtraction — invisible to every resolution tier, the ln-tower false-exact engine.
/// `Rhs1`/`Rhs2` carry the rhs/reference side's plain home values — the LIMIT the claim is about, which the
/// theorem certificate (EmlCert) canonicalizes.
public readonly record struct EmlVerdict(
    char Grade, bool HomeValid, bool P3Valid,
    double Rel1, double Rel2, double Rel3, Complex Corr3,
    bool Q9Home, bool Q12Home, bool Q9P3, bool Q12P3,
    int SubEpsHome, double MinRatioHome, string EnclCols,
    Complex Rhs1, Complex Rhs2)
{
    public bool Absorbed => SubEpsHome > 0 || MinRatioHome < 1e-9;    // a term fell below the minted quantizer
    public bool Taint    => Absorbed && Grade is 'A' or 'S';         // PROVEN absorption artifact
    public bool Suspect  => Absorbed && Grade is not ('A' or 'S');   // mechanism witnessed, falsity not (yet) witnessable
}

/// THE THEOREM CERTIFICATE — the crown move: content-address THEOREMS by their (limit, rate-law)
/// class the way the CAS content-addresses bytes by hash. Canonical form, three fields:
///   GRADE   the witness ladder's verdict (E/A/S/D/U) — an exact route and an asymptotic approach to the same
///           value are different theorems;
///   LIMIT   the value the claim is ABOUT — the rhs/reference side's dual-point home value, quantized at the
///           sieve's own sig for EXACT claims (exactness keeps the native ruler) and at the coarser FamilySig for
///           everything else, so an asymptotic FAMILY groups across nearby home sigs;
///   RATE    for A-grades, the rate-law — the DECADE BAND of the Corr3 drift at the regime point, per component
///           (the closed-form error law's EXPONENT class: 's RATE CLASS = TOWER HEIGHT, since each extra gate
///           stacks ≤1 exponential and shifts the drift by decades — decoration perturbs the mantissa, never the
///           decade, so baroque same-height variants merge while deeper towers stay distinct tiers; a 2πi
///           branch-drift separates from a real lnδ-drift by its Im band). A drift whose RELATIVE magnitude sits
///           below the sieve's own quantizer (Rel3 < 10^(1−sig)) is SUB-RESOLUTION — proven ≠ 0 by the enclosure
///           yet below the witness ruler, so it carries no readable law and keys ONE band (the certificate is
///           exactly as sharp as the witness that grades it). Both refinements are measured law, not taste:
///           mantissa-keying minted a class per NOISE SAMPLE (2904 A-mints → 1949 classes), and per-component
///           decade-keying still split the ~1e-10 noise floor into (−10,−11,−12,0)-band shrapnel (→ 1042).
/// Two syntactically wild expressions with the same certificate are ONE entry — deduplication (same certificate
/// = same discovery), pricing (the certificate IS the value), and novelty (new = new certificate) all key on
/// this, so a paraphrase cannot mint value: the counterfeit detector is the mint itself. Value-equality of the
/// record IS the content hash; Hex() prints it as a stable foreign key.
public readonly record struct EmlCert(char Grade, EmlSig Limit, long RateRe, long RateIm)
{
    public const int FamilySig = 4;   // the family resolution — an asymptotic family's members sit within sig-4 of their shared limit

    /// SUB-RESOLUTION drift band — the enclosure proved the drift ≠ 0, but its relative magnitude sits below the
    /// sieve's own quantizer, so no law is readable in it: every such drift is ONE rate class.
    public const long SubResolution = long.MinValue + 1;

    /// Canonicalize a graded claim — the ONE authority (the live mint-gate, the checkpoint CAS-rebuild, and the
    /// retro semantic audit all call here; sieveSig clamps to 9, the packed key's collision-free bound).
    public static EmlCert Of(in EmlVerdict v, int sieveSig)
    {
        int sg = Math.Min(sieveSig, 9);
        int lim = v.Grade == 'E' ? sg : FamilySig;
        var l = Eml.Signature(new EmlValue(v.Rhs1, true), new EmlValue(v.Rhs2, true), lim);
        if (v.Grade != 'A') return new EmlCert(v.Grade, l, 0, 0);
        if (v.Rel3 < Math.Pow(10, 1 - sg)) return new EmlCert('A', l, SubResolution, 0);   // the witness ruler's floor
        return new EmlCert('A', l, RateKey(v.Corr3.Real), RateKey(v.Corr3.Imaginary));
    }

    // the rate-law band of one drift component — its decade (order of magnitude), sign dropped: the law's
    // MAGNITUDE class. A vanished/non-finite component keys the sentinel (distinct from decade 0 = an O(1) drift).
    private static long RateKey(double v)
        => v == 0.0 || !double.IsFinite(v) ? long.MinValue : (long)Math.Floor(Math.Log10(Math.Abs(v)));

    /// The raw content-address — FNV-1a 64 over the canonical fields (stable across runs/processes). Ordering by
    /// this ulong is the same order as ordinal comparison of Hex(): fixed-width lowercase hex is monotone in value.
    public ulong HashKey()
    {
        ulong h = 14695981039346656037UL;
        void Mix(ulong x) { for (int i = 0; i < 8; i++) { h ^= (x >> (i * 8)) & 0xFF; h *= 1099511628211UL; } }
        Mix(Grade);
        Mix((ulong)Limit.R1); Mix((ulong)Limit.I1); Mix((ulong)Limit.R2); Mix((ulong)Limit.I2);
        Mix((ulong)RateRe); Mix((ulong)RateIm);
        return h;
    }

    /// The content-address in printable form — a foreign key for cross-run journals and the population's
    /// corroboration graph. Sort/compare on HashKey(); materialize hex only at field emission.
    public string Hex() => HashKey().ToString("x16");
}

public readonly record struct EmlPredictionID(int Value);

internal readonly record struct EmlExactRPNForm(
    string Program,
    EmlCert Certificate,
    EmlPredictionID PredictionID,
    string SourceDigest);

public enum EmlExactFormAdmissionStatuses
{
    Accepted,
    CandidateInvalid,
    CertificateNotExact,
    ClassMissing,
    CandidateMatchesIncumbent,
    CandidateAlreadyAdmitted,
    CertificateMismatch,
    RepresentativeWouldChange,
}

public readonly record struct EmlExactFormAdmission(
    EmlExactFormAdmissionStatuses Status,
    EmlPredictionID PredictionID,
    EmlCert Certificate,
    string? IncumbentRPN,
    EmlEvaluatorInterval Evaluation,
    ulong LawProofDigest,
    bool FirstCapture,
    bool RepresentativeChanged)
{
    public bool Accepted => Status == EmlExactFormAdmissionStatuses.Accepted;
}

public enum EmlCertificateChanges
{
    ClassOpened,
    RepresentativeImproved,
    TargetCaptured,
    PredictionChallenged,
    RateClassViolated,
    PredictionRegraded,
    LawAdmitted,
    ProofAttached,
}

public readonly record struct EmlCertificateDelta(
    EmlCertificateChanges Change,
    EmlPredictionID PredictionID,
    EmlCert? Before,
    EmlCert? After,
    EmlEvaluatorInterval Evaluation,
    int DescriptionBits)
{
    public long EvaluatorCalls => Evaluation.Calls;
}

/// One journal claim parsed from its minted line — the lhs program, the grade byte the tape carries (` = ` exact /
/// ` ~ ` non-exact, both 3 bytes wide), and the rhs with its kind resolved (pure RPN identity vs label). The ONE
/// line-parsing authority: the sieve's checkpoint CAS-rebuild, the retro census (EmlRegrade), and the semantic-
/// compression audit all read journal lines through here, so a line-format drift breaks all three the same loud way.
public readonly record struct EmlPrediction(string Line, string Lhs, string Rhs, bool RhsRpn, bool Tilde)
{
    public static bool TryParse(string line, out EmlPrediction c)
    {
        c = default;
        int cut = line.IndexOf(" = ", StringComparison.Ordinal);
        int cutT = line.IndexOf(" ~ ", StringComparison.Ordinal);
        bool tilde = cutT >= 0 && (cut < 0 || cutT < cut);
        if (tilde) cut = cutT;
        if (cut <= 0 || cut + 3 >= line.Length) return false;
        string lhs = line[..cut], rhs = line[(cut + 3)..];
        bool rhsRpn = true;
        foreach (char ch in rhs) if (!Eml.IsToken(ch)) { rhsRpn = false; break; }
        c = new EmlPrediction(line, lhs, rhs, rhsRpn, tilde);
        return true;
    }
}

/// The witness-refinement ladder as a stateful grader — VERIFICATION IS RENORMALIZATION: a
/// claim's witness-grade is the scaling class of its residual under three orthogonal witness axes, evaluated at
/// three probe points (the sieve's own P1/P2 + the small-argument REGIME point P3, Feigenbaum reciprocals):
///   RESOLUTION  sig ∈ {9,12} under the sieve's own quantizer (the refined ruler);
///   REGIME      the P3 translation — deep exp-towers collapse at small argument, absorbed terms surface
///               (resolution is weak, regime is categorical: 4641/6408 absorptions survive sig=12 and die here);
///   ENCLOSURE   outward-rounded rectangle arithmetic — an enclosure of lhs−rhs excluding 0 DISPROVES exactness
///               threshold-free; the per-op absorption witness rides the same walk (Eml.EvalLadder).
/// ONE authority, two mounts: EmlSieve grades every mint LIVE (the grade-gate), EmlRegrade re-grades existing
/// journals retroactively (the census) — both call here, so the live `=`/`~` byte and the retro census can never
/// drift. The per-program ladder cache is derived state (pure function of the program) — never checkpointed.
public sealed class EmlGrader
{
    /// Feigenbaum δ, α — the regime point is their reciprocals (0.2141693…, 0.3995352…): small argument, believed
    /// algebraically independent, not in the exp-log class (the Schanuel-sieve spirit, same standard as γ/A/G/ζ3).
    public const double FeigenbaumDelta = 4.669201609102990;
    public const double FeigenbaumAlpha = 2.502907875095892;

    internal enum Encl { Excludes, Contains, Undecided }

    private readonly (Complex X, Complex Y)[] _pts;
    private readonly EmlEvaluatorClock _clock;
    private EmlDeliberationLease? _deliberationLease;
    private const int LadderCacheVersion = 1;
    private readonly EmlLadderCacheProfile _probeProfile;
    private readonly Dictionary<EmlLadderCacheKey, EmlLadder[]> _cache = new();   // canonical finite RPN → per-point ladders
    private Dictionary<EmlLadderCacheKey, EmlLadder[]>? _speculativeCache;
    private const int CacheCap = 4096;                   // bounded scratch — cleared whole on overflow (deterministic; entries are pure recomputes)

    private readonly record struct EmlLadderCacheProfile(
        long P1X,
        long P1XI,
        long P1Y,
        long P1YI,
        long P2X,
        long P2XI,
        long P2Y,
        long P2YI,
        long P3X,
        long P3XI,
        long P3Y,
        long P3YI);

    private readonly record struct EmlLadderCacheKey(string Program, EmlLadderCacheProfile ProbeProfile, int Version);

    public EmlGrader() : this(new EmlEvaluatorClock()) { }

    public EmlGrader(EmlEvaluatorClock clock)
        : this(1.0 / FeigenbaumDelta, 1.0 / FeigenbaumAlpha, clock) { }

    public EmlGrader(EmlEvaluatorClock clock, EmlDeliberationLease? deliberationLease)
        : this(1.0 / FeigenbaumDelta, 1.0 / FeigenbaumAlpha, clock)
    {
        _deliberationLease = deliberationLease;
    }

    public EmlGrader(double p3x, double p3y) : this(p3x, p3y, new EmlEvaluatorClock()) { }

    public EmlGrader(double p3x, double p3y, EmlEvaluatorClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _pts =
        [
            (new Complex(EmlSieve.Gamma, 0),   new Complex(EmlSieve.Glaisher, 0)),
            (new Complex(EmlSieve.Catalan, 0), new Complex(EmlSieve.Apery, 0)),
            (new Complex(p3x, 0),              new Complex(p3y, 0)),
        ];
        _probeProfile = ComputeProbeProfile(_pts);
    }

    internal void BindDeliberation(EmlDeliberationLease? deliberationLease)
        => _deliberationLease = deliberationLease;

    internal EmlEvaluatorClock Clock => _clock;

    public (Complex X, Complex Y)[] Points
        => (ValueTuple<Complex, Complex>[])_pts.Clone();

    /// MemStat census read — the ladder cache's key count + Σ key chars (bounded by CacheCap; per-entry payload is
    /// _pts.Length EmlLadders). Counts only.
    internal (long Keys, long Chars) CacheMass()
    {
        long chars = 0;
        foreach (EmlLadderCacheKey k in _cache.Keys) chars += k.Program.Length;
        return (_cache.Count, chars);
    }

    internal (long Keys, long Chars) SpeculativeCacheMass()
    {
        if (_speculativeCache is null) return (0, 0);
        long chars = 0;
        foreach (EmlLadderCacheKey k in _speculativeCache.Keys) chars += k.Program.Length;
        return (_speculativeCache.Count, chars);
    }

    internal void BeginSpeculativeCache()
    {
        if (_speculativeCache is not null) throw new InvalidOperationException("nested EML grader cache speculation is not supported");
        _speculativeCache = new Dictionary<EmlLadderCacheKey, EmlLadder[]>();
    }

    internal void CommitSpeculativeCache()
    {
        if (_speculativeCache is null) return;
        foreach ((EmlLadderCacheKey key, EmlLadder[] ladders) in _speculativeCache)
        {
            if (_cache.ContainsKey(key)) continue;
            if (_cache.Count >= CacheCap) _cache.Clear();
            _cache[key] = ladders;
        }
        _speculativeCache = null;
    }

    internal void EndSpeculativeCache() => _speculativeCache = null;

    internal EmlResidualExpressionEvaluation EvaluateFinite(string canonicalRPN)
    {
        EmlLadder[] ladders = Ladders(canonicalRPN);
        return new EmlResidualExpressionEvaluation(ladders[0], ladders[1], ladders[2], 0);
    }

    /// Grade an identity claim — both sides pure RPN.
    public EmlVerdict GradeRpn(string lhs, string rhs) => Verdict(Ladders(lhs), Ladders(rhs));

    /// Grade a value-hit claim — rhs is a reference evaluation (bench target / atlas entry).
    public EmlVerdict GradeRef(string lhs, Func<Complex, Complex, Complex> f)
    {
        var r = new EmlLadder[_pts.Length];
        for (int i = 0; i < _pts.Length; i++)
        {
            Complex v = f(_pts[i].X, _pts[i].Y);
            bool fin = double.IsFinite(v.Real) && double.IsFinite(v.Imaginary);
            r[i] = fin ? new EmlLadder(new EmlValue(v, true), RefRect(v), 1.0, 0) : EmlLadder.Invalid;
        }
        return Verdict(Ladders(lhs), r);
    }

    /// Grade a constant-hit claim — rhs is a fixed value (an anomaly-register correction target).
    public EmlVerdict GradeConst(string lhs, Complex v)
    {
        EmlLadder[] r = new EmlLadder[_pts.Length];
        for (int i = 0; i < _pts.Length; i++) r[i] = new EmlLadder(new EmlValue(v, true), RefRect(v), 1.0, 0);
        return Verdict(Ladders(lhs), r);
    }

    public EmlVerdict GradeResidual(string program, EmlResidualWitness witness)
    {
        EmlLadder[] residual =
        [
            CreateResidualLadder(witness.P1),
            CreateResidualLadder(witness.P2),
            CreateResidualLadder(witness.P3),
        ];
        return Verdict(Ladders(program), residual);
    }

    public bool TryDescribeResidual(
        in EmlPrediction claim,
        Dictionary<string, Func<Complex, Complex, Complex>> references,
        out EmlResidualWitness witness)
    {
        EmlLadder[] left = Ladders(claim.Lhs);
        if (!TryReadRightLadders(in claim, references, out EmlLadder[] right))
        {
            witness = default;
            return false;
        }
        EmlResidualProbe p1 = SubtractLadders(in left[0], in right[0]);
        EmlResidualProbe p2 = SubtractLadders(in left[1], in right[1]);
        EmlResidualProbe p3 = SubtractLadders(in left[2], in right[2]);
        if (!IsFinite(p1.Value) || !IsFinite(p2.Value) || !IsFinite(p3.Value))
        {
            witness = default;
            return false;
        }
        witness = new EmlResidualWitness(p1, p2, p3);
        return true;
    }

    /// Grade a claim from its TEXT alone — the retro mount (checkpoint CAS-rebuild, regrade census, semantic
    /// audit). Rhs dispatch: pure RPN → identity; a charted label → reference; a self-describing `corr:` anomaly
    /// label → constant. False = the rhs label resolves nowhere (not one of ours).
    public bool TryGrade(in EmlPrediction c, Dictionary<string, Func<Complex, Complex, Complex>> refs, out EmlVerdict v)
    {
        if (c.RhsRpn) { v = GradeRpn(c.Lhs, c.Rhs); return true; }
        if (refs.TryGetValue(c.Rhs, out var fn)) { v = GradeRef(c.Lhs, fn); return true; }
        if (TryParseAnomalyLabel(c.Rhs, out var cv)) { v = GradeConst(c.Lhs, cv); return true; }
        v = default;
        return false;
    }

    private bool TryReadRightLadders(
        in EmlPrediction claim,
        Dictionary<string, Func<Complex, Complex, Complex>> references,
        out EmlLadder[] ladders)
    {
        if (claim.RhsRpn)
        {
            ladders = Ladders(claim.Rhs);
            return true;
        }
        if (TryParseAnomalyLabel(claim.Rhs, out Complex constant))
        {
            ladders = new EmlLadder[_pts.Length];
            for (int i = 0; i < _pts.Length; i++)
                ladders[i] = new EmlLadder(new EmlValue(constant, true), EmlRect.Point(constant), 1.0, 0);
            return true;
        }
        if (!references.TryGetValue(claim.Rhs, out Func<Complex, Complex, Complex>? reference))
        {
            ladders = Array.Empty<EmlLadder>();
            return false;
        }
        ladders = new EmlLadder[_pts.Length];
        for (int i = 0; i < _pts.Length; i++)
        {
            Complex value = reference(_pts[i].X, _pts[i].Y);
            if (!IsFinite(value))
            {
                ladders = Array.Empty<EmlLadder>();
                return false;
            }
            ladders[i] = new EmlLadder(new EmlValue(value, true), RefRect(value), 1.0, 0);
        }
        return true;
    }

    private static EmlResidualProbe SubtractLadders(in EmlLadder left, in EmlLadder right)
        => new(left.Plain.Value - right.Plain.Value, EmlRect.Sub(left.Rect, right.Rect));

    private static EmlLadder CreateResidualLadder(EmlResidualProbe probe)
        => new(new EmlValue(probe.Value, true), probe.Enclosure, 1.0, 0);

    private static bool IsFinite(Complex value)
        => double.IsFinite(value.Real) && double.IsFinite(value.Imaginary);

    /// A program's per-point ladders, cached (mint attempts repeat programs heavily — the cache makes a repeat
    /// verdict two lookups).
    public EmlLadder[] Ladders(string prog)
    {
        _deliberationLease?.ReserveLogicalProgramPoints(3);
        EmlLadderCacheKey key = new(prog, _probeProfile, LadderCacheVersion);
        Dictionary<EmlLadderCacheKey, EmlLadder[]> cache = _speculativeCache ?? _cache;
        if (cache.TryGetValue(key, out EmlLadder[]? got)
            || (_speculativeCache is not null && _cache.TryGetValue(key, out got)))
        {
            _clock.RecordLadderRequest(cacheHit: true);
            return got;
        }
        _clock.RecordLadderRequest(cacheHit: false);
        _deliberationLease?.ReserveExecutedProgramPoints(3);
        if (cache.Count >= CacheCap) cache.Clear();
        var r = new EmlLadder[_pts.Length];
        for (int i = 0; i < _pts.Length; i++)
        {
            _clock.RecordExecutedLadderProgramPointEvaluation();
            r[i] = Eml.EvalLadder(prog, _pts[i].X, _pts[i].Y);
        }
        return cache[key] = r;
    }

    private static EmlLadderCacheProfile ComputeProbeProfile((Complex X, Complex Y)[] points)
    {
        return new EmlLadderCacheProfile(
            BitConverter.DoubleToInt64Bits(points[0].X.Real),
            BitConverter.DoubleToInt64Bits(points[0].X.Imaginary),
            BitConverter.DoubleToInt64Bits(points[0].Y.Real),
            BitConverter.DoubleToInt64Bits(points[0].Y.Imaginary),
            BitConverter.DoubleToInt64Bits(points[1].X.Real),
            BitConverter.DoubleToInt64Bits(points[1].X.Imaginary),
            BitConverter.DoubleToInt64Bits(points[1].Y.Real),
            BitConverter.DoubleToInt64Bits(points[1].Y.Imaginary),
            BitConverter.DoubleToInt64Bits(points[2].X.Real),
            BitConverter.DoubleToInt64Bits(points[2].X.Imaginary),
            BitConverter.DoubleToInt64Bits(points[2].Y.Real),
            BitConverter.DoubleToInt64Bits(points[2].Y.Imaginary));
    }

    // the reference chart is a one-shot closed-form evaluation — grant it a 1e-13 relative slop enclosure
    private static EmlRect RefRect(Complex v)
    {
        static EmlIv Iv(double x) { double s = Math.Max(Math.Abs(x) * 1e-13, 1e-300); return new(x - s, x + s); }
        return new EmlRect(Iv(v.Real), Iv(v.Imaginary));
    }

    // ── the tiers, per point → the grade decision tree (the retro census's law, verbatim — one authority) ──
    private static EmlVerdict Verdict(EmlLadder[] L, EmlLadder[] R)
    {
        bool homeValid = L[0].Plain.Finite && L[1].Plain.Finite && R[0].Plain.Finite && R[1].Plain.Finite;
        bool p3Valid = L[2].Plain.Finite && R[2].Plain.Finite;
        var q9 = new bool[3]; var q12 = new bool[3]; var encl = new Encl[3]; var rel = new double[3];
        for (int i = 0; i < 3; i++)
        {
            bool v = L[i].Plain.Finite && R[i].Plain.Finite;
            q9[i]  = v && Eml.AgreeSig(L[i].Plain.Value, R[i].Plain.Value, 9);
            q12[i] = v && Eml.AgreeSig(L[i].Plain.Value, R[i].Plain.Value, 12);
            encl[i] = EnclAt(L[i], R[i]);
            rel[i] = v ? RelResid(L[i], R[i]) : double.NaN;
        }

        bool Refuted(int i) => encl[i] == Encl.Excludes || (encl[i] == Encl.Undecided && !q9[i] && !q12[i]);
        bool Pass(int i) => encl[i] == Encl.Contains || (encl[i] == Encl.Undecided && q12[i]);

        char grade = !homeValid                     ? 'D'
                   : Refuted(0) || Refuted(1)       ? 'S'
                   : !p3Valid                       ? 'D'
                   : Refuted(2)                     ? 'A'
                   : Pass(0) && Pass(1) && Pass(2)  ? 'E'
                   : 'U';

        // ── the absorption witness at the minting points (both sides; ref side carries none) ──
        double minRatio = Math.Min(Math.Min(L[0].MinRatio, L[1].MinRatio), Math.Min(R[0].MinRatio, R[1].MinRatio));
        int subEps = L[0].SubEpsOps + L[1].SubEpsOps + R[0].SubEpsOps + R[1].SubEpsOps;
        Complex corr3 = p3Valid ? L[2].Plain.Value - R[2].Plain.Value : default;

        return new EmlVerdict(grade, homeValid, p3Valid, rel[0], rel[1], rel[2], corr3,
                              q9[0] && q9[1], q12[0] && q12[1], q9[2], q12[2], subEps, minRatio,
                              $"{EnclCol(encl[0])}{EnclCol(encl[1])}{EnclCol(encl[2])}",
                              R[0].Plain.Value, R[1].Plain.Value);
    }

    internal static Encl EnclAt(EmlLadder l, EmlLadder r)
    {
        if (!l.Plain.Finite || !r.Plain.Finite) return Encl.Undecided;
        var d = EmlRect.Sub(l.Rect, r.Rect);
        if (d.IsBlown) return Encl.Undecided;
        if (!d.ContainsZero) return Encl.Excludes;
        double scale = Math.Max(Complex.Abs(l.Plain.Value), Complex.Abs(r.Plain.Value));
        return d.MaxWidth <= Math.Max(scale * 1e-10, 1e-14) ? Encl.Contains : Encl.Undecided;   // contains-0 only counts as a PASS when the enclosure is tight
    }

    internal static Encl EnclAt(Complex leftValue, Complex rightValue, EmlRect leftRect, EmlRect rightRect)
    {
        if (!double.IsFinite(leftValue.Real) || !double.IsFinite(leftValue.Imaginary)
            || !double.IsFinite(rightValue.Real) || !double.IsFinite(rightValue.Imaginary)) return Encl.Undecided;
        EmlRect difference = EmlRect.Sub(leftRect, rightRect);
        if (difference.IsBlown) return Encl.Undecided;
        if (!difference.ContainsZero) return Encl.Excludes;
        double scale = Math.Max(Complex.Abs(leftValue), Complex.Abs(rightValue));
        return difference.MaxWidth <= Math.Max(scale * 1e-10, 1e-14) ? Encl.Contains : Encl.Undecided;
    }

    internal static double RelResid(EmlLadder l, EmlLadder r)
    {
        double scale = Math.Max(Complex.Abs(l.Plain.Value), Complex.Abs(r.Plain.Value));
        return scale == 0 ? 0 : Complex.Abs(l.Plain.Value - r.Plain.Value) / scale;
    }

    internal static EmlLadder ReferenceLadder(Complex value)
        => double.IsFinite(value.Real) && double.IsFinite(value.Imaginary)
            ? new EmlLadder(new EmlValue(value, true), EmlRect.Point(value), 1.0, 0)
            : EmlLadder.Invalid;

    private static char EnclCol(Encl e) => e switch { Encl.Excludes => 'x', Encl.Contains => 'o', _ => '?' };

    // ── the anomaly label codec — the label IS its value (run-independent, regrade re-derives the reference) ──

    /// `corr:<G17>` (complex → `re<±im>i`) — space-free (the mint line's rhs), invariant, round-trip exact (R-format).
    public static string AnomalyLabel(Complex v)
        => v.Imaginary == 0
            ? $"corr:{v.Real.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}"
            : $"corr:{v.Real.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}{(v.Imaginary < 0 ? "" : "+")}{v.Imaginary.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}i";

    /// Parse an anomaly label back to its value — the regrade path's reference resolver. False for non-`corr:` labels.
    public static bool TryParseAnomalyLabel(string label, out Complex v)
    {
        v = default;
        if (!label.StartsWith("corr:", StringComparison.Ordinal)) return false;
        var body = label.AsSpan(5);
        if (body.Length == 0) return false;
        if (body[^1] == 'i')
        {
            body = body[..^1];
            int cut = -1;                                       // the LAST sign that isn't an exponent's or the leading one
            for (int i = body.Length - 1; i > 0; i--)
                if ((body[i] == '+' || body[i] == '-') && body[i - 1] is not ('e' or 'E')) { cut = i; break; }
            if (cut < 0) return false;
            if (!double.TryParse(body[..cut], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var re)) return false;
            if (!double.TryParse(body[cut..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var im)) return false;
            v = new Complex(re, im);
            return true;
        }
        if (!double.TryParse(body, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r)) return false;
        v = new Complex(r, 0);
        return true;
    }
}

/// Which slice of the scientific calculator a target lives in (drives the distinct-constants-reached sparkline and
/// the bench sectioning). Constants ignore the inputs; Functions read x; Operators read x and y.
public enum EmlCats { Constant, Function, Operator }

/// One named calculator primitive and its reference value. `PaperK` is the paper's shortest-RPN result where the
/// leaf-count table reports one; -1 means the target was unreported, not that its search timed out. `PaperTimedOut`
/// marks only the ">K" rows whose exhaustive search did not complete.
public readonly record struct EmlTarget(string Label, EmlCats Cat, int PaperK, bool PaperTimedOut, Func<Complex, Complex, Complex> Ref);

/// One minted discovery in the sieve's append-only journal — the line as it landed on the tape, the offered program
/// that fired it (its length IS the discovery's K-shell), the dual-point sig of the value it lives on (the
/// rarity + rediscovery keys), the LIVE witness-grade the witness ladder assigned at mint time (E/A/S/D/U —
/// the grade rides IN THE LINE as its `=`/`~` byte, and in provenance as Witnessed/Dream), and the NOVEL-
/// CORROBORATION bit: the mint's VALUE landed on a REGISTERED named target (bench primitive or anomaly-correction)
/// that had NO E-witness yet — a target named BEFORE the program arrived, first-captured now, so the hit is an
/// independent second witness at the frontier ('s bench-as-corroboration, Weitzman-shaped: the reward
/// the accretors weight pays first-captures, never re-visits). The novelty readout scores these; the journal
/// survives DrainNewMints.
public readonly record struct EmlMint(string Line, string Prog, EmlSig Sig, char Grade, bool Corrob);

/// One claim-addressed residual target. Resolution state is deliberately not carried here: closure packets are
/// the authority for whether an obligation was solved, while this register remains only the stable source address.
public readonly record struct EmlOfferContext(IReadOnlyList<TapeEventID>? OpportunityEventIDs = null)
{
    public bool HasWorldOpportunity => OpportunityEventIDs is { Count: > 0 };
}

public readonly record struct EmlObligation(
    EmlPredictionID SourcePredictionID,
    string Identity,
    IReadOnlyList<TapeEventID>? OpportunityEventIDs = null,
    TapeEventID? MintEventID = null);

/// The target species is part of the address contract.  Residual targets are
/// A-grade source claims owned by EmlObligation; exact derivation targets are
/// E-grade carrier claims owned by EmlExactCompositionObligation.
public enum EmlObligationTargetSpecies : byte
{
    Residual,
    ExactComposition,
}

/// One exact theorem-use target.  This is deliberately not an EmlObligation:
/// an exact carrier must never be resolved through the A-grade residual path.
/// Supports are world event IDs and are retained in canonical order.
public readonly record struct EmlExactCompositionObligation(
    EmlPredictionID SourcePredictionID,
    string Identity,
    string SourceDigest,
    string CarrierRPN,
    EmlCert SourceCertificate,
    IReadOnlyList<TapeEventID>? SupportEventIDs = null,
    TapeEventID? MintEventID = null)
{
    public EmlObligationTargetSpecies Species => EmlObligationTargetSpecies.ExactComposition;
    public IReadOnlyList<TapeEventID> Supports => SupportEventIDs ?? Array.Empty<TapeEventID>();
}

/// Stable action address shared by residual and exact SolveHole targets.
public readonly record struct EmlObligationTarget(
    EmlObligationTargetSpecies Species,
    EmlPredictionID SourcePredictionID)
{
    public static EmlObligationTarget Residual(EmlPredictionID sourcePredictionID)
        => new(EmlObligationTargetSpecies.Residual, sourcePredictionID);

    public static EmlObligationTarget ExactComposition(EmlPredictionID sourcePredictionID)
        => new(EmlObligationTargetSpecies.ExactComposition, sourcePredictionID);
}

public enum EmlObligationProofKinds
{
    FiniteRPN,
    ProcessFunction,
    Rung0ComposedForm,
}

public enum EmlObligationClosureStatuses
{
    Accepted,
    UnknownObligation,
    WrongKind,
    DuplicateAttachment,
    Suppressed,
    StalePolicy,
    InvalidPolicy,
    OccurrenceCheckRejected,
}

/// Finite and process proofs have different policy units. Keeping them as concrete records prevents a finite proof
/// from carrying a meaningless fuel field and makes the process per-probe/total distinction explicit.
public readonly record struct EmlFiniteObligationProofPolicy(int SignatureDigits, int WitnessVersion, string VerifierRevision);
public readonly record struct EmlProcessObligationProofPolicy(
    int SignatureDigits,
    long FuelPerProbe,
    int ProbeCount,
    int FunctionVersion,
    int CompositionVersion,
    string VerifierRevision)
{
    public long FuelBudget => checked(FuelPerProbe * ProbeCount);
}

public readonly record struct EmlFiniteObligationProofEvidence(
    EmlEvaluatorInterval Evaluator,
    long WallTicks,
    string CandidateDigest,
    string AttachmentDigest,
    EmlCert Before,
    EmlCert After)
{
    public double WallMilliseconds => WallTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
}

public readonly record struct EmlProcessObligationProofEvidence(
    EmlEvaluatorInterval Evaluator,
    long WallTicks,
    long FuelPerProbe,
    long FuelTotal,
    string CandidateDigest,
    string AttachmentDigest,
    string CertificateDigest,
    EmlCert Before,
    EmlCert After)
{
    public double WallMilliseconds => WallTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
}

/// Exact admission witness for a rung-0 derived form.  The proof/audit/admission identities
/// are content-addressed and the canonical claim and guard package stay in the packet so a
/// report consumer cannot turn a zero counter into a different claim by inference.
public readonly record struct EmlRung0ComposedFormObligationEvidence(
    EmlPredictionID ObligationPredictionID,
    string ObligationID,
    EmlPredictionID ComposedPredictionID,
    string LhsRPN,
    string RhsRPN,
    string GuardPackageDigest,
    string ProofID,
    string AuditID,
    string ProofSHA256,
    string AuditSHA256,
    string AdmissionID,
    string ClosureID,
    string Comparator,
    EmlEvaluatorInterval Evaluator,
    EmlEvaluatorInterval ComparatorEvaluation,
    string CandidateDigest,
    string AttachmentDigest,
    EmlCert Before,
    EmlCert After,
    string AdmissionPathCanonical = "",
    string AdmissionPathFingerprint = "")
{
    public long MainEvaluatorCalls => Evaluator.Calls;
}

/// Durable proof packet for one obligation attempt. Accepted packets close the obligation; rejected packets are
/// returned by admission with their typed reason before scheduling can treat the obligation as solved.
public readonly record struct EmlObligationClosure(
    EmlPredictionID SourcePredictionID,
    string ObligationID,
    string AttemptID,
    string AttachmentID,
    EmlObligationClosureStatuses Status,
    string SourceDigest,
    EmlObligationProofKinds Kind,
    EmlFiniteObligationProofPolicy? FinitePolicy,
    EmlProcessObligationProofPolicy? ProcessPolicy,
    EmlFiniteObligationProofEvidence? FiniteEvidence,
    EmlProcessObligationProofEvidence? ProcessEvidence,
    string Reason,
    EmlRung0ComposedFormObligationEvidence? Rung0ComposedFormEvidence = null,
    EmlObligationTargetSpecies Species = EmlObligationTargetSpecies.Residual)
{
    public bool Closed => Status == EmlObligationClosureStatuses.Accepted;
}

public readonly record struct EmlObligationClosureResult(
    EmlObligationClosure Closure,
    EmlCertificateDelta? Delta)
{
    public bool Accepted => Closure.Closed;
    public long ProcessFuel => Closure.ProcessEvidence?.FuelTotal ?? 0;
}

public readonly record struct EmlResidualProbe(Complex Value, EmlRect Enclosure);

public readonly record struct EmlResidualWitness(
    EmlResidualProbe P1,
    EmlResidualProbe P2,
    EmlResidualProbe P3);

public readonly record struct EmlResidualProof(
    EmlPredictionID SourcePredictionID,
    string Program,
    EmlCert Certificate);

public readonly record struct EmlProcessResidualProof(
    EmlPredictionID SourcePredictionID,
    EmlProcessFunction Function,
    EmlResidualCompositionLaws? CompositionLaw,
    string Digest,
    EmlCert Certificate,
    long ProcessFuel);

/// A resolved obligation read. The residual is the source claim's lhs-rhs function at every witness regime, not a
/// frozen P3 decimal. Value and Label remain the compact observatory projection of P3; Witness is the proof target.
public readonly record struct EmlObligationResolution(
    EmlPredictionID SourcePredictionID,
    EmlSig ResidualSignature,
    string Label,
    Complex Value,
    EmlResidualWitness Corroboration,
    int ClosureCount,
    IReadOnlyList<TapeEventID>? OpportunityEventIDs = null,
    TapeEventID? MintEventID = null)
{
    public bool Closed => ClosureCount > 0;
}

/// Compatibility view for existing observatory reads. It is rebuilt from claim-addressed obligations on demand and
/// is never persisted or retained by the sieve.
public readonly record struct EmlAnomaly(string Label, Complex Value, int Hits);

/// The thin classical reference BEYOND the paper's bench — named constants and the log/exp/power laws, as plain
/// reference evaluations. NOT a prover and NOT a rewrite system: the chart is value-level, the dual-point sig is
/// the whole verdict. The discovery readout splits mints on it — a mint landing on a charted value REDISCOVERED
/// named mathematics; the remaining high-novelty mints are the frontier (true-but-unnamed equivalences).
public static class EmlAtlas
{
    public static readonly (string Label, Func<Complex, Complex, Complex> Ref)[] Entries =
    [
        // ── classic constants in the exp-ln closure ──
        ("ln2",      (x, y) => new Complex(Math.Log(2), 0)),
        ("e^2",      (x, y) => new Complex(Math.E * Math.E, 0)),
        ("1/e",      (x, y) => new Complex(1 / Math.E, 0)),
        ("sqrt(e)",  (x, y) => new Complex(Math.Sqrt(Math.E), 0)),
        ("e^e",      (x, y) => new Complex(Math.Pow(Math.E, Math.E), 0)),
        ("e^(1/e)",  (x, y) => new Complex(Math.Pow(Math.E, 1 / Math.E), 0)),
        ("2e",       (x, y) => new Complex(2 * Math.E, 0)),
        ("e/2",      (x, y) => new Complex(Math.E / 2, 0)),
        ("e+1",      (x, y) => new Complex(Math.E + 1, 0)),
        ("e-1",      (x, y) => new Complex(Math.E - 1, 0)),
        ("-e",       (x, y) => new Complex(-Math.E, 0)),
        ("pi^2",     (x, y) => new Complex(Math.PI * Math.PI, 0)),
        ("2pi",      (x, y) => new Complex(2 * Math.PI, 0)),
        ("pi/2",     (x, y) => new Complex(Math.PI / 2, 0)),
        ("sqrt(pi)", (x, y) => new Complex(Math.Sqrt(Math.PI), 0)),
        ("e^pi",     (x, y) => new Complex(Math.Pow(Math.E, Math.PI), 0)),   // Gelfond's constant
        ("pi^e",     (x, y) => new Complex(Math.Pow(Math.PI, Math.E), 0)),
        ("e*pi",     (x, y) => new Complex(Math.E * Math.PI, 0)),
        ("e+pi",     (x, y) => new Complex(Math.E + Math.PI, 0)),
        ("ln(pi)",   (x, y) => new Complex(Math.Log(Math.PI), 0)),
        ("i*pi",     (x, y) => new Complex(0, Math.PI)),                     // ln(−1) — Euler's identity's value core
        ("-i",       (x, y) => new Complex(0, -1)),
        ("2i",       (x, y) => new Complex(0, 2)),
        ("e^i",      (x, y) => Complex.Exp(Complex.ImaginaryOne)),
        ("3",        (x, y) => new Complex(3, 0)),
        ("4",        (x, y) => new Complex(4, 0)),
        ("1/3",      (x, y) => new Complex(1.0 / 3, 0)),
        ("1/4",      (x, y) => new Complex(0.25, 0)),
        // ── the log/exp/power laws as recognizable forms ──
        ("exp(x+y)", (x, y) => Complex.Exp(x + y)),
        ("exp(x-y)", (x, y) => Complex.Exp(x - y)),
        ("exp(2x)",  (x, y) => Complex.Exp(2 * x)),
        ("ln(xy)",   (x, y) => Complex.Log(x * y)),
        ("ln(x/y)",  (x, y) => Complex.Log(x / y)),
        ("ln(x^2)",  (x, y) => Complex.Log(x * x)),
        ("y-x",      (x, y) => y - x),
        ("y/x",      (x, y) => y / x),
        ("y^x",      (x, y) => Complex.Pow(y, x)),
    ];
}

/// RPN-EML → human-readable math. STRUCTURAL, with only the paper's own reductions folded (ln 1 = 0 elision,
/// exp(1) = e, the ln pattern eml(1,eml(eml(1,u),1)) = ln u); everything else renders as exp(A) - ln(B), so a
/// detour the machine took stays VISIBLE — the render never proves, it only names what the eye can check.
public static class EmlRender
{
    public static string Render(string prog)
    {
        EmlTree.Node? root = Parse(prog);
        return root is null ? prog : RenderNode(root);
    }

    internal static EmlTree.Node? Parse(string prog)
    {
        return EmlTree.TryParseRPN(prog, out EmlTree? tree) ? tree!.Root : null;
    }

    internal static string ToRpn(EmlTree.Node node) => new EmlTree(node).RenderRPN();

    private static bool IsOne(EmlTree.Node? node) => node is { Token: Eml.One };

    private static string RenderNode(EmlTree.Node node)
    {
        if (!node.IsGate) return node.Token.ToString();
        // the paper's ln reduction: eml(1, eml(eml(1,u),1)) = ln u
        if (IsOne(node.Left)
            && node.Right is { Token: Eml.Op, Right.Token: Eml.One, Left: { Token: Eml.Op } inner }
            && IsOne(inner.Left))
            return $"ln({RenderNode(inner.Right!)})";
        if (IsOne(node.Right))
            return IsOne(node.Left) ? "e" : $"exp({RenderNode(node.Left!)})";     // ln 1 = 0 elision; exp(1) = e
        if (IsOne(node.Left)) return $"(e - ln({RenderNode(node.Right!)}))";
        return $"(exp({RenderNode(node.Left!)}) - ln({RenderNode(node.Right!)}))";
    }
}

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
//  THE SIEVE — the discovery journal (the paper's numeric method as stateful state)
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

/// The numeric equivalence sieve as a running discovery journal. Offer it candidate RPN programs; it evaluates each
/// at the two fixed algebraically-independent transcendental probe points, quantizes to a dual-point EmlSig, and:
///   • recognizes the paper's calculator TARGETS (the minimal-program bench — shortest RPN per primitive vs paper),
///   • registers each distinct value's SHORTEST program as canonical, minting an IDENTITY line `lhs = rhs` when a
///     later program collides onto an existing canonical value (identities recurring as RPN text ARE the corpus
///     cogito re-induces — the compression fuel that grows subroutines).
/// Every mint is de-duplicated (each discovered line lands once — the chunking signal is the DIVERSITY of RPN
/// substrings across distinct identities, not repetition). Deterministic: offer the same programs, get the same
/// journal. The probe points are the paper's own: P1 = (γ, A) Euler–Mascheroni / Glaisher–Kinkelin; P2 = (G, ζ(3))
/// Catalan / Apéry — a second independent transcendental substitution, so a single-point coincidence cannot pass.
///
/// WITNESS DEPTH — what a mint IS and is not: the dual point kills ULP flukes, not SCALE-ABSORPTION. Both probe
/// points sit at comparable argument scale, so a deep exp-tower dwarfs a subtracted ln-term below the quantizer at
/// BOTH points at once (often below double eps — bit-absorbed, invisible at ANY sig), and the ungated sieve then
/// minted asymptotic/scale-local laws AS identities (72% of the flagship journal, the retro census). The paper
/// scopes this method as CANDIDATE-finding with verification separate (EML.tex:203-204) — so THE GRADE-GATE is
/// wired in: every fresh mint runs the witness ladder (EmlGrader — resolution sig 9/12 × regime P3 × enclosure +
/// absorption witness) LIVE, the grade rides in the line's alphabet (` = ` exact / ` ~ ` non-exact) and routes
/// provenance (EXACT → Witnessed evidence; the rest → Dream at ε until vested), and an ASYMPTOTIC's correction
/// value registers as a self-generated recognition target (the anomaly register). `cogito dreamcalc --regrade`
/// (EmlRegrade) is the same ladder mounted retroactively over any existing journal.
public sealed partial class EmlSieve
{
    // the two probe points — algebraically-independent transcendentals, none in the exp-log class (Schanuel sieve):
    public const double Gamma    = 0.5772156649015329;   // γ  Euler–Mascheroni      (P1.x — the paper's own choice)
    public const double Glaisher = 1.2824271291006226;   // A  Glaisher–Kinkelin     (P1.y — the paper's own choice)
    public const double Catalan  = 0.9159655941772190;   // G  Catalan               (P2.x — the independent 2nd point)
    public const double Apery    = 1.2020569031595943;   // ζ(3) Apéry               (P2.y — the independent 2nd point)

    private static readonly Complex P1x = new(Gamma, 0),    P1y = new(Glaisher, 0);
    private static readonly Complex P2x = new(Catalan, 0),  P2y = new(Apery, 0);

    private readonly int _sig;
    private readonly EmlEvaluatorClock _evaluatorClock = new();
    private readonly EmlGrader _grader;
    private readonly EmlTarget[] _targets;
    private readonly Dictionary<string, Func<Complex, Complex, Complex>> _claimReferences;
    private readonly Dictionary<EmlSig, List<int>> _targetBySig = new(); // dual-point EmlSig → target indices (recognition)
    // ── THE HELD-OUT SPLIT (emlbench) — a target is TRAIN (recognized, steers the bench columns) or HELD (recognized
    // for the GENERALIZATION read only: its first E-capture is tallied, but it never enters the bench-hit census the
    // generator's reward reads). Both are recognized post-hoc — the point is that generation is target-blind either
    // way (it dreams; the sieve recognizes), so the held targets test whether the developed grammar REACHED named
    // mathematics it was never pointed at. `_isHeld[i]` marks the held targets; null mask ⇒ every target is train
    // (byte-identical to the pre-holdout sieve — the observatory/trunk mounts never pass a mask). ──
    private readonly bool[]? _isHeld;                                    // per-target: held out of the train census (null = none held)
    private readonly HashSet<int> _heldCaptured = new();                // held target indices that reached an E-witness (the generalization tally)
    private readonly int _heldTargetCount;
    private readonly int[] _bestK;                                       // shortest program length found per target (−1 = unfound)
    private readonly string[] _bestProg;                                 // the shortest program per target
    private readonly Dictionary<EmlSig, string> _canon = new();          // value EmlSig → its shortest program seen (the identity anchor)
    private readonly HashSet<string> _minted = new();                    // de-dup: prog\u0001rhs pre-keys, grade-independent (checked BEFORE grading — the ladder runs once per unique mint)
    private readonly List<EmlMint> _newMints = new();                    // this-Draw's fresh mints — ReplayCalc drains onto the tape (grade rides along for provenance routing)
    private readonly Dictionary<int, int> _discByLen = new();            // program length → # of discoveries (the noisy-TV read)
    private readonly List<EmlMint> _mintLog = new();                     // append-only discovery journal (the novelty readout's spine)
    // Exact claims are parsed and digested once, at mint time.  The old read paths
    // reparsed and rehashed the entire mint journal on every law-frontier pass.
    private readonly List<EmlExactRPNForm> _exactRPNForms = new();
    private readonly List<EmlExactRPNForm> _exactRPNLhsForms = new();
    private readonly List<EmlAntiUnify.PredictionTree> _lawPredictionTrees = new();  // discovery trees, parsed once at mint time (mint order — DiscoverCandidates' tie-break contract)
    private readonly Dictionary<EmlPredictionID, EmlExactRPNForm> _exactRPNLhsByPrediction = new();
    private readonly Dictionary<string, EmlExactRPNForm> _exactRPNLhsByProgram = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Program, EmlCert Certificate), EmlExactRPNForm> _exactRPNLhsByProgramAndCertificate = new();
    private int _exactRPNPredictionCount;
    private readonly Dictionary<EmlMint, EmlPredictionID> _claimByMint = new();
    private readonly List<IReadOnlyList<TapeEventID>> _mintOpportunityEvents = new();
    private readonly Dictionary<EmlSig, int> _sigHits = new();           // per-value BASIN size: finite offers that landed on each sig (rarity)
    private readonly List<EmlObligation> _obligations = new();           // persisted source addresses, in registration order
    private readonly List<EmlExactCompositionObligation> _exactCompositionObligations = new();
    private readonly Dictionary<EmlPredictionID, IReadOnlyList<TapeEventID>> _obligationOpportunityEvents = new();
    private readonly Dictionary<EmlPredictionID, TapeEventID> _obligationMintEvents = new();
    private IReadOnlyList<TapeEventID> _pendingOpportunityEvents = Array.Empty<TapeEventID>();
    private readonly Dictionary<EmlSig, int> _obligationByResidual = new(); // derived residual sig → obligation index; rebuilt from current source claims on Load
    private readonly Dictionary<EmlPredictionID, int> _exactCompositionBySource = new();
    private readonly Dictionary<EmlPredictionID, int> _obligationBySource = new();
    private readonly List<EmlObligationClosure> _obligationClosures = new(); // persisted proof packets; accepted packets are the resolution authority
    private readonly Dictionary<string, int> _obligationClosureKeys = new(StringComparer.Ordinal);
    private readonly EmlDeliberationJournal _deliberationJournal = new(); // immutable search admissions and settlements
    private readonly List<EmlResidualProof> _residualProofs = new();
    private readonly HashSet<string> _residualProofKeys = new(StringComparer.Ordinal);
    private readonly List<EmlProcessResidualProof> _processResidualProofs = new();
    private readonly HashSet<string> _processResidualProofKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _processProofWireVersions = new(StringComparer.Ordinal);
    private readonly List<EmlComposedFormProof> _derivedFormProofs = new();
    private readonly HashSet<string> _derivedFormProofKeys = new(StringComparer.Ordinal);
    private readonly List<EmlCertificateDelta> _newSemanticDeltas = new();
    private readonly EmlGrader _obligationGrader = new();                // derived-read grader; separate clock keeps reads observational
    private readonly int[] _gradeCounts = new int[5];                    // mint census by grade (E/A/S/D/U — the grade-histogram read)
    private readonly SemanticCAS<EmlCert, string> _cas = new(CompareCertRepresentatives); // THE THEOREM CAS — certificate → class (MDL rep · members · first capture); derived from the mint log, never checkpointed
    private readonly List<EmlCert> _mintCerts = new();                   // per-mint certificate, parallel to _mintLog (same derived-on-Load law as the grade census)
    private readonly List<SemanticCASAdmission<EmlCert, string>> _mintAdmissions = new();
    private SpeculativeTransaction? _activeSpeculativeTransaction;
    private const int ObligationCap = 1024;                              // registry bound — first-come (mint order), so the cap is deterministic
    private const uint ObligationClosureTag = 0x324C424F;                // OBL2 — source addresses plus typed closure packets
    private const uint DeliberationJournalTag = 0x31304A45;               // EJ01 — typed per-obligation search journal
    private const uint ResidualProofTag = 0x46505252;                     // RRPF — checked residual-program attachments
    private const uint ProcessResidualProofTag = 0x46525050;              // PPRF — checked residual-process attachments
    private const uint ComposedFormProofTag = 0x30465244;                  // DRF0 — zero-evaluator guarded derivation proofs
    private const uint ComposedFormProofTagV2 = 0x32465244;                // DRF2 — carries audit selection species
    private const uint MintOpportunityTag = 0x314F504D;                   // MPO1 — per-mint world opportunity bindings
    private const uint ExactCompositionTargetTag = 0x31584445;             // EDX1 — exact derivation target register
    private const uint MintPredictionEventTag = 0x3143454D;                    // MEC1 — persisted mint claim event bindings
    private const uint ComposedPredictionEventTag = 0x31434544;                 // DEC1 — persisted rung-0 derivation claim event bindings
    private readonly Dictionary<EmlPredictionID, TapeEventID> _claimMintEvents = new();
    private readonly Dictionary<EmlPredictionID, TapeEventID> _claimCompositionEvents = new();
    private bool _legacyComposedFormAuditHash;

    public EmlSieve(int sig) : this(sig, BuildTargets(), null) { }

    /// The held-out-aware constructor (emlbench). `trainMask[i]==false` HOLDS target i out of the train census —
    /// still recognized (its capture tallied for the generalization read), but never counted in the bench-hit
    /// columns the generator's reward reads. `trainMask==null` (every other mount) ⇒ every target is train, byte-
    /// identical to the plain sieve. The mask length must equal the target count (TargetCount) — a shorter mask is
    /// a programming error the ctor refuses loudly.
    public EmlSieve(int sig, bool[]? trainMask) : this(sig, BuildTargets(), trainMask) { }

    internal EmlSieve(int sig, EmlTarget[] catalog, bool[]? trainMask = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _targets = (EmlTarget[])catalog.Clone();
        _claimReferences = BuildPredictionReferences(_targets);
        _grader = new EmlGrader(_evaluatorClock);
        _sig = sig;
        if (trainMask is not null && trainMask.Length != _targets.Length)
            throw new ArgumentException($"trainMask length {trainMask.Length} ≠ target count {_targets.Length}", nameof(trainMask));
        _isHeld = trainMask is null ? null : Array.ConvertAll(trainMask, t => !t);
        if (_isHeld is not null)
            for (int i = 0; i < _isHeld.Length; i++)
                if (_isHeld[i]) _heldTargetCount++;
        _bestK = new int[_targets.Length];
        _bestProg = new string[_targets.Length];
        Array.Fill(_bestK, -1);
        for (int i = 0; i < _targets.Length; i++)
        {
            var s = SigOfRef(_targets[i].Ref);
            (_targetBySig.TryGetValue(s, out var l) ? l : _targetBySig[s] = new()).Add(i);
        }
    }

    /// The named-target count — the holdout mask's required length (emlbench builds a mask of exactly this size).
    public static int TargetCount => BuildTargets().Length;

    internal EmlGrader Grader => _grader;

    /// Domain-specific representative selection for theorem certificates. The CAS container owns admission and
    /// census; EML owns what counts as a cheaper representative inside an outcome class.
    internal static string CertRepresentative(EmlCert cert, string lhs, string rhs)
    {
        string rep = lhs;
        bool rhsRpn = cert.Grade == 'E' && rhs.Length > 0;
        if (rhsRpn) foreach (char ch in rhs) if (!Eml.IsToken(ch)) { rhsRpn = false; break; }
        if (rhsRpn && CompareCertRepresentatives(rhs, rep) < 0) rep = rhs;
        return rep;
    }

    internal static int CompareCertRepresentatives(string left, string right)
    {
        int byLength = left.Length.CompareTo(right.Length);
        return byLength != 0 ? byLength : string.CompareOrdinal(left, right);
    }

    /// Deterministic target split shared by emlbench and the trunk EML curriculum. `Mask[i] == true` means TRAIN;
    /// false means HELD, still recognized for the generalization tally but absent from the reward/bench census.
    public static (bool[] Mask, int Held, int Train) HoldoutMask(double holdout, ulong seed)
    {
        int n = TargetCount;
        var mask = new bool[n];
        int held = 0;
        double p = Math.Clamp(holdout, 0, 1);
        var rng = seed == 0 ? 0x9E3779B97F4A7C15UL : seed ^ 0x5EED_10D0_10D0_10D0UL;
        for (int i = 0; i < n; i++)
        {
            rng = EmlGen.Lcg(rng);
            bool train = p <= 0 || ((rng >> 33) % 1_000_000) >= (ulong)(p * 1_000_000);
            mask[i] = train;
            if (!train) held++;
        }
        return (mask, held, n - held);
    }

    // ── THE HELD-OUT READS (emlbench generalization) — the held targets that reached an E-witness, and the count.
    // Empty/zero when no mask was passed (every target is train). ──
    public IReadOnlyCollection<int> HeldCaptured => _heldCaptured;
    public int HeldCapturedCount => _heldCaptured.Count;
    public int HeldTargetCount => _heldTargetCount;
    public int TrainTargetCount => _targets.Length - _heldTargetCount;

    /// The dual-point signature of a reference function at the sieve's probe points — how a target/atlas entry
    /// becomes recognizable in sig-space.
    public EmlSig SigOfRef(Func<Complex, Complex, Complex> f)
        => Eml.Signature(new EmlValue(f(P1x, P1y), true), new EmlValue(f(P2x, P2y), true), _sig);

    /// The CHARTED region of value-space — every paper target + classic-atlas entry keyed by dual-point sig. The
    /// readout's rediscovery/frontier split: a mint whose sig lands here re-found named mathematics; off-chart
    /// mints are the frontier.
    public Dictionary<EmlSig, string> ChartedBySig()
    {
        var d = new Dictionary<EmlSig, string>();
        foreach (var t in _targets) d.TryAdd(SigOfRef(t.Ref), t.Label);
        foreach (var (label, f) in EmlAtlas.Entries) d.TryAdd(SigOfRef(f), label);
        return d;
    }

    // ── live reads (the sparkline columns + the summary) ──
    public int Identities { get; private set; }                         // # identity lines minted
    public int ValueHits { get; private set; }                          // # value-recognition lines minted (expr = TARGET)
    public int DistinctValues => _canon.Count;                          // distinct finite values reached (the space explored)
    public int KFrontier { get; private set; }                          // max program length that produced ≥1 discovery
    public IReadOnlyList<EmlTarget> Targets => _targets;
    public int BestK(int t) => _bestK[t];
    public string? BestProg(int t) => _bestProg[t];
    public IReadOnlyDictionary<int, int> DiscoveriesByLen => _discByLen;
    public IReadOnlyList<EmlMint> MintLog => _mintLog;                  // every mint ever, in mint order (survives Drain)
    internal IReadOnlyList<EmlAntiUnify.PredictionTree> LawPredictionTrees => _lawPredictionTrees;  // exact RhsRpn claims as parsed discovery trees, mint order
    public long FiniteOffers { get; private set; }                      // total finite candidates seen — the rarity denominator
    public EmlEvaluatorClock EvaluatorClock => _evaluatorClock;
    public int SigHitsOf(EmlSig s) => _sigHits.GetValueOrDefault(s);    // how often generation stumbled onto this value
    public string? CanonOf(EmlSig s) => _canon.GetValueOrDefault(s);    // the shortest program found for a value (null = never reached)

    /// The named CONSTANTS reached so far (the distinct-constants-reached sparkline) — how much of {1, 0, −1, 2, e,
    /// π, i, √2, …} the dream has bottled. E-witnessed only, like every bench read (bestK's own gate).
    public int ConstantsHit()
    {
        int c = 0;
        for (int i = 0; i < _targets.Length; i++) if (_targets[i].Cat == EmlCats.Constant && _bestK[i] >= 0) c++;
        return c;
    }

    /// Named bench targets E-captured so far (the paper-32 stick) — the observatory's bench column and the lift
    /// organ's per-window anchor.
    public int TargetsHit()
    {
        int n = 0;
        for (int i = 0; i < _targets.Length; i++) if (_bestK[i] >= 0) n++;
        return n;
    }

    /// Fresh mints produced since the last Drain — ReplayCalc appends these to the tape (grade → provenance), then Drains.
    public IReadOnlyList<EmlMint> NewMints => _newMints;
    public void DrainNewMints() => _newMints.Clear();

    /// A grade-tier CORPUS off the mint journal, newline-joined — the unique-line read a tier renorm should see
    /// (the tape repeats the MIX axiom; the log is the journal). One authority for the observatory's exact-tier
    /// report and the lift organ's per-window grok sense.
    public byte[] TierBytes(Func<EmlMint, bool> keep)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var m in _mintLog) if (keep(m)) { sb.Append(m.Line); sb.Append('\n'); }
        return System.Text.Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// Mint census by grade — the live grade-histogram read (E/A/S/D/U).
    public int GradeCount(char grade) => _gradeCounts[GradeIdx(grade)];

    /// Novel-corroborated EXACT mints — FIRST E-witnessed captures of registered named targets (the second-witness
    /// census; these are the mints the accretors re-ingest at weight — each target pays at most once).
    public int CorrobExact() { int n = 0; foreach (var m in _mintLog) if (m.Corrob && m.Grade == 'E') n++; return n; }

    /// Prediction-addressed residual targets, in deterministic source-mint order.
    public IReadOnlyList<EmlObligation> Obligations => _obligations;
    /// Exact theorem-use targets, in deterministic source-mint order.
    public IReadOnlyList<EmlExactCompositionObligation> ExactCompositionObligations => _exactCompositionObligations;
    public IReadOnlyList<EmlObligationClosure> ObligationClosures => _obligationClosures;
    public EmlDeliberationJournal DeliberationJournal => _deliberationJournal;

    internal EmlDeliberationLease ReserveDeliberation(
        in EmlObligationResolution obligation,
        EmlDeliberationQuota quota,
        string discoveryEpoch = "mint-frontier",
        string solverRevision = "eml-hole-solver-v1",
        string verifierRevision = "eml-hole-verifier-v1")
        => _deliberationJournal.Reserve(
            in obligation,
            quota,
            discoveryEpoch,
            KFrontier.ToString(System.Globalization.CultureInfo.InvariantCulture),
            solverRevision,
            verifierRevision);

    public string ObligationIdentity(EmlPredictionID sourcePredictionID)
    {
        if (_obligationBySource.TryGetValue(sourcePredictionID, out int index))
            return _obligations[index].Identity;
        throw new KeyNotFoundException($"EmlSieve has no obligation sourced by claim {sourcePredictionID.Value}");
    }

    public string ExactCompositionObligationIdentity(EmlPredictionID sourcePredictionID)
    {
        if (_exactCompositionBySource.TryGetValue(sourcePredictionID, out int index))
            return _exactCompositionObligations[index].Identity;
        throw new KeyNotFoundException($"EmlSieve has no exact derivation target sourced by claim {sourcePredictionID.Value}");
    }

    public bool TryResolveTarget(in EmlObligationTarget target, out EmlObligationResolution residual,
        out EmlExactCompositionObligation exact)
    {
        residual = default;
        exact = default;
        if (target.Species == EmlObligationTargetSpecies.Residual)
            return TryResolveObligation(target.SourcePredictionID, out residual);
        return TryReadExactCompositionObligation(target.SourcePredictionID, out exact);
    }

    internal bool HasExactCompositionObligation(EmlPredictionID sourcePredictionID)
        => _exactCompositionBySource.ContainsKey(sourcePredictionID);

    internal bool TryReadExactCompositionObligation(EmlPredictionID sourcePredictionID, out EmlExactCompositionObligation obligation)
    {
        if (_exactCompositionBySource.TryGetValue(sourcePredictionID, out int index))
        {
            obligation = _exactCompositionObligations[index];
            return true;
        }
        obligation = default;
        return false;
    }

    public bool TryResolveExactCompositionObligation(EmlPredictionID sourcePredictionID, out EmlExactCompositionObligation obligation)
        => TryReadExactCompositionObligation(sourcePredictionID, out obligation);

    internal void BindMintEvent(in EmlMint mint, TapeEventID eventID)
    {
        if (eventID.Value < 0) throw new ArgumentOutOfRangeException(nameof(eventID));
        if (_claimByMint.TryGetValue(mint, out EmlPredictionID claimID))
        {
            RejectExactEventRebind(claimID, eventID);
            bool newBinding = !_claimMintEvents.ContainsKey(claimID);
            _claimMintEvents[claimID] = eventID;
            if (newBinding) _checkpointPredictionMintEvents.Add(new EmlPredictionMintEventDelta(claimID, eventID));
            if (_obligationBySource.TryGetValue(claimID, out int obligationIndex)
                && !_obligationMintEvents.ContainsKey(claimID))
            {
                _obligationMintEvents[claimID] = eventID;
                _obligations[obligationIndex] = _obligations[obligationIndex] with { MintEventID = eventID };
            }
        }
    }

    internal void BindPredictionEvent(EmlPredictionID claimID, TapeEventID eventID)
    {
        if (eventID.Value < 0) throw new ArgumentOutOfRangeException(nameof(eventID));
        RejectExactEventRebind(claimID, eventID);
        bool newBinding = !_claimMintEvents.ContainsKey(claimID);
        _claimMintEvents[claimID] = eventID;
        if (newBinding) _checkpointPredictionMintEvents.Add(new EmlPredictionMintEventDelta(claimID, eventID));
        if (_obligationBySource.TryGetValue(claimID, out int obligationIndex)
            && !_obligationMintEvents.ContainsKey(claimID))
        {
            _obligationMintEvents[claimID] = eventID;
            _obligations[obligationIndex] = _obligations[obligationIndex] with { MintEventID = eventID };
        }
    }

    internal void BindComposedPredictionEvent(EmlPredictionID claimID, TapeEventID eventID)
    {
        if (claimID.Value < 0 || eventID.Value < 0) throw new ArgumentOutOfRangeException(nameof(eventID));
        if ((uint)claimID.Value >= (uint)_mintLog.Count)
            throw new InvalidDataException("derived claim event binding is outside the mint journal");
        if (_claimCompositionEvents.TryGetValue(claimID, out TapeEventID existing))
        {
            if (existing != eventID) throw new InvalidOperationException("derived claim event is already bound");
            return;
        }
        if (_claimMintEvents.ContainsKey(claimID))
            throw new InvalidOperationException("claim already has a mint admission event");
        _claimCompositionEvents.Add(claimID, eventID);
        _checkpointPredictionCompositionEvents.Add(new EmlPredictionCompositionEventDelta(claimID, eventID));
    }

    private void RejectExactEventRebind(EmlPredictionID claimID, TapeEventID eventID)
    {
        if (_exactCompositionBySource.ContainsKey(claimID)
            && _claimMintEvents.TryGetValue(claimID, out TapeEventID existingEvent)
            && existingEvent != eventID)
            throw new InvalidOperationException(
                $"exact derivation claim {claimID.Value} already owns mint event {existingEvent.Value}; cannot rebind to {eventID.Value}");
    }

    internal bool TryReadObligationEvents(
        EmlPredictionID sourcePredictionID,
        out IReadOnlyList<TapeEventID> opportunityEvents,
        out TapeEventID mintEventID)
    {
        opportunityEvents = _obligationOpportunityEvents.TryGetValue(sourcePredictionID, out IReadOnlyList<TapeEventID>? found)
            ? found : Array.Empty<TapeEventID>();
        return _obligationMintEvents.TryGetValue(sourcePredictionID, out mintEventID);
    }

    internal bool TryReadTargetEvents(
        EmlPredictionID sourcePredictionID,
        out IReadOnlyList<TapeEventID> opportunityEvents,
        out TapeEventID mintEventID,
        out EmlObligationTargetSpecies species)
    {
        if (TryReadObligationEvents(sourcePredictionID, out opportunityEvents, out mintEventID))
        {
            species = EmlObligationTargetSpecies.Residual;
            return opportunityEvents.Count > 0;
        }
        if (_exactCompositionBySource.TryGetValue(sourcePredictionID, out int index))
        {
            EmlExactCompositionObligation target = _exactCompositionObligations[index];
            opportunityEvents = target.Supports;
            if (target.MintEventID is TapeEventID exactMint)
            {
                mintEventID = exactMint;
                species = EmlObligationTargetSpecies.ExactComposition;
                return opportunityEvents.Count > 0;
            }
        }
        opportunityEvents = Array.Empty<TapeEventID>();
        mintEventID = default;
        species = default;
        return false;
    }

    internal bool TryReadMintOpportunityEvents(EmlPredictionID claimID, out IReadOnlyList<TapeEventID> opportunityEvents)
    {
        if ((uint)claimID.Value < (uint)_mintOpportunityEvents.Count)
        {
            opportunityEvents = _mintOpportunityEvents[claimID.Value];
            return opportunityEvents.Count > 0;
        }
        opportunityEvents = Array.Empty<TapeEventID>();
        return false;
    }

    internal bool TryReadPredictionMintEvent(EmlPredictionID claimID, out TapeEventID mintEvent)
        => _claimMintEvents.TryGetValue(claimID, out mintEvent);

    internal bool TryReadPredictionAdmission(EmlPredictionID claimID, out EmlSourcePredictionAdmission admission)
    {
        if (_claimMintEvents.TryGetValue(claimID, out TapeEventID mintEvent))
        {
            admission = new(EmlSourcePredictionAdmissionSpecies.MintPacket, mintEvent);
            return true;
        }
        if (_claimCompositionEvents.TryGetValue(claimID, out TapeEventID derivationEvent))
        {
            admission = new(EmlSourcePredictionAdmissionSpecies.Rung0CompositionPacket, derivationEvent);
            return true;
        }
        admission = default;
        return false;
    }

    internal bool TryReadRung0BasisLawDigests(EmlPredictionID sourcePredictionID, out IReadOnlyList<ulong> basisLawDigests)
    {
        for (int i = 0; i < _derivedFormProofs.Count; i++)
        {
            EmlComposedFormProof stored = _derivedFormProofs[i];
            if (stored.SourcePredictionID != sourcePredictionID) continue;
            ulong[] digests = stored.Proof.Steps
                .Select(static step => step.BasisLawDigest)
                .Where(static digest => digest != 0)
                .Distinct().Order().ToArray();
            basisLawDigests = digests;
            return digests.Length > 0;
        }
        basisLawDigests = Array.Empty<ulong>();
        return false;
    }

    private bool HasMintEvent(EmlPredictionID sourcePredictionID) => _obligationMintEvents.ContainsKey(sourcePredictionID);

    public bool IsObligationClosed(EmlPredictionID sourcePredictionID)
    {
        for (int i = 0; i < _obligationClosures.Count; i++)
            if (_obligationClosures[i].SourcePredictionID == sourcePredictionID && _obligationClosures[i].Closed) return true;
        return false;
    }

    public int ClosureCount(EmlPredictionID sourcePredictionID)
    {
        int count = 0;
        for (int i = 0; i < _obligationClosures.Count; i++)
            if (_obligationClosures[i].SourcePredictionID == sourcePredictionID && _obligationClosures[i].Closed) count++;
        return count;
    }

    /// Read-only closure authority for loop-closure consumers. Validation runs before the packet
    /// leaves the sieve, so callers cannot construct a theory witness from an unbound counter or
    /// a stale derived-form record.
    internal bool TryReadRung0ComposedFormClosure(
        EmlPredictionID sourcePredictionID,
        out EmlRung0ComposedFormObligationEvidence evidence)
    {
        ValidateObligationClosures(_legacyComposedFormAuditHash);
        for (int i = 0; i < _obligationClosures.Count; i++)
        {
            EmlObligationClosure closure = _obligationClosures[i];
            if (closure.SourcePredictionID == sourcePredictionID
                && closure.Closed
                && closure.Kind == EmlObligationProofKinds.Rung0ComposedForm
                && closure.Rung0ComposedFormEvidence is EmlRung0ComposedFormObligationEvidence found)
            {
                if (found.ProofSHA256.Length == 0 || found.AuditSHA256.Length == 0)
                {
                    EmlRung0ComposedFormProof? witness = FindRung0ComposedFormProof(found.ProofID, sourcePredictionID);
                    if (witness is EmlRung0ComposedFormProof restored)
                        found = found with
                        {
                            ProofSHA256 = EmlRung0Checkpoint.ProofSHA256(restored.Proof),
                            AuditSHA256 = _legacyComposedFormAuditHash
                                ? EmlRung0Checkpoint.LegacyAuditSHA256(restored.Audit)
                                : EmlRung0Checkpoint.AuditSHA256(restored.Audit),
                        };
                }
                evidence = found;
                return true;
            }
        }
        evidence = default;
        return false;
    }

    public IReadOnlyList<EmlResidualProof> ResidualProofs => _residualProofs;
    public IReadOnlyList<EmlProcessResidualProof> ProcessResidualProofs => _processResidualProofs;
    internal IReadOnlyList<EmlComposedFormProof> ComposedFormProofs => _derivedFormProofs;
    internal bool HasAcceptedComposedFormProofs => _derivedFormProofs.Count > 0;

    internal void StageRung0Audit(EmlLawStore store, in EmlRung0Audit audit, bool promoteRetained = false)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (_activeSpeculativeTransaction is SpeculativeTransaction transaction)
            transaction.RecordRung0Audit(store, in audit, promoteRetained);
        else
        {
            if (promoteRetained) store.PromoteRung0Audit(in audit);
            else store.RecordRung0Audit(in audit);
        }
    }

    public EmlCert GetPredictionCertificate(EmlPredictionID claimID)
    {
        if ((uint)claimID.Value >= (uint)_mintCerts.Count)
            throw new ArgumentOutOfRangeException(nameof(claimID), claimID.Value, "claim ID is outside the mint journal");
        return _mintCerts[claimID.Value];
    }

    public int CountExactRPNPredictions()
        => _exactRPNPredictionCount;

    public void AppendExactRPNPrograms(List<string> programs, int maximum)
    {
        HashSet<string> unique = new(programs, StringComparer.Ordinal);
        for (int i = 0; i < _exactRPNForms.Count && unique.Count < maximum; i++)
        {
            string program = _exactRPNForms[i].Program;
            if (unique.Add(program)) programs.Add(program);
        }
    }

    internal void AppendExactRPNForms(List<EmlExactRPNForm> forms)
        => forms.AddRange(_exactRPNForms);

    internal void AppendExactRPNLhsForms(List<EmlExactRPNForm> forms)
        => forms.AddRange(_exactRPNLhsForms);

    internal IReadOnlyList<EmlExactRPNForm> ExactRPNForms => _exactRPNForms;

    internal IReadOnlyList<EmlExactRPNForm> ExactRPNLhsForms => _exactRPNLhsForms;

    internal bool TryReadExactRPNLhsForm(EmlPredictionID claimID, out EmlExactRPNForm form)
        => _exactRPNLhsByPrediction.TryGetValue(claimID, out form);

    internal bool TryCreateRewriteCarrier(
        in EmlExactRPNForm form,
        out EmlRewritePredictionCarrier carrier)
    {
        int index = form.PredictionID.Value;
        if (_exactRPNLhsByPrediction.TryGetValue(form.PredictionID, out EmlExactRPNForm canonical)
            && canonical == form
            && (uint)index < (uint)_mintLog.Count
            && (uint)index < (uint)_mintCerts.Count)
        {
            EmlMint mint = _mintLog[index];
            if (mint.Grade == 'E' && _mintCerts[index] == form.Certificate)
            {
                carrier = EmlRewritePredictionCarrier.Create(form.PredictionID, form.SourceDigest, P1x, P1y);
                return true;
            }
        }
        carrier = default;
        return false;
    }

    public IReadOnlyList<EmlCertificateDelta> NewSemanticDeltas => _newSemanticDeltas;

    public void DrainSemanticDeltas() => _newSemanticDeltas.Clear();

    /// Compatibility projection for observatory reads. Residual fields are re-derived from the current source claim.
    public IReadOnlyDictionary<EmlSig, EmlAnomaly> Anomalies
    {
        get
        {
            Dictionary<EmlSig, EmlAnomaly> anomalies = new();
            for (int i = 0; i < _obligations.Count; i++)
            {
                EmlObligationResolution resolution = ResolveObligation(_obligations[i]);
                anomalies.Add(
                    resolution.ResidualSignature,
                    new EmlAnomaly(resolution.Label, resolution.Value, resolution.ClosureCount));
            }
            return anomalies;
        }
    }

    public int AnomalyHits
    {
        get
        {
            int closures = 0;
            for (int i = 0; i < _obligationClosures.Count; i++)
                if (_obligationClosures[i].Closed) closures++;
            return closures;
        }
    }

    public EmlObligationResolution ResolveObligation(EmlPredictionID sourcePredictionID)
    {
        if (TryResolveObligation(sourcePredictionID, out EmlObligationResolution resolution)) return resolution;
        throw new KeyNotFoundException($"EmlSieve has no obligation sourced by claim {sourcePredictionID.Value}");
    }

    public bool TryResolveObligation(EmlPredictionID sourcePredictionID, out EmlObligationResolution resolution)
    {
        if (_obligationBySource.TryGetValue(sourcePredictionID, out int index))
        {
            resolution = ResolveObligation(_obligations[index]);
            return true;
        }
        resolution = default;
        return false;
    }

    public bool TryAdmitExactForm(
        string candidateRPN,
        EmlCert expected,
        ulong lawProofDigest,
        out EmlExactFormAdmission result)
    {
        long evaluatorStart = _evaluatorClock.ProgramPointEvaluations;
        if (expected.Grade != 'E')
            return RejectExactForm(
                EmlExactFormAdmissionStatuses.CertificateNotExact,
                expected,
                null,
                evaluatorStart,
                lawProofDigest,
                out result);
        if (!_cas.Classes.TryGetValue(expected, out SemanticCASClass<string> cls))
            return RejectExactForm(
                EmlExactFormAdmissionStatuses.ClassMissing,
                expected,
                null,
                evaluatorStart,
                lawProofDigest,
                out result);

        string incumbentRPN = cls.Rep;
        if (string.Equals(candidateRPN, incumbentRPN, StringComparison.Ordinal))
            return RejectExactForm(
                EmlExactFormAdmissionStatuses.CandidateMatchesIncumbent,
                expected,
                incumbentRPN,
                evaluatorStart,
                lawProofDigest,
                out result);

        string mintKey = candidateRPN + "\u0001" + incumbentRPN;
        if (_minted.Contains(mintKey))
            return RejectExactForm(
                EmlExactFormAdmissionStatuses.CandidateAlreadyAdmitted,
                expected,
                incumbentRPN,
                evaluatorStart,
                lawProofDigest,
                out result);

        _evaluatorClock.RecordOfferRequest();
        _evaluatorClock.RecordOfferProgramPointEvaluation();
        EmlValue p1 = Eml.Eval(candidateRPN, P1x, P1y);
        _evaluatorClock.RecordOfferProgramPointEvaluation();
        EmlValue p2 = Eml.Eval(candidateRPN, P2x, P2y);
        if (!p1.Finite || !p2.Finite)
            return RejectExactForm(
                EmlExactFormAdmissionStatuses.CandidateInvalid,
                expected,
                incumbentRPN,
                evaluatorStart,
                lawProofDigest,
                out result);

        EmlVerdict verdict = _grader.GradeRpn(candidateRPN, incumbentRPN);
        EmlCert certificate = EmlCert.Of(in verdict, _sig);
        if (verdict.Grade != 'E' || certificate != expected)
            return RejectExactForm(
                EmlExactFormAdmissionStatuses.CertificateMismatch,
                certificate,
                incumbentRPN,
                evaluatorStart,
                lawProofDigest,
                out result);

        string offeredRepresentative = CertRepresentative(expected, candidateRPN, incumbentRPN);
        if (CompareCertRepresentatives(offeredRepresentative, incumbentRPN) < 0)
            return RejectExactForm(
                EmlExactFormAdmissionStatuses.RepresentativeWouldChange,
                certificate,
                incumbentRPN,
                evaluatorStart,
                lawProofDigest,
                out result);

        EmlSig candidateSig = Eml.Signature(p1, p2, _sig);
        string line = candidateRPN + " = " + incumbentRPN;
        EmlMint mint = new(line, candidateRPN, candidateSig, 'E', Corrob: false);
        EmlPredictionID claimID = new(_mintLog.Count);

        _minted.Add(mintKey);
        _activeSpeculativeTransaction?.RecordMinted(mintKey);
        _mintLog.Add(mint);
        IReadOnlyList<TapeEventID> opportunities = _pendingOpportunityEvents.Count == 0
            ? Array.Empty<TapeEventID>()
            : _pendingOpportunityEvents.Distinct().OrderBy(static id => id.Value).ToArray();
        _mintOpportunityEvents.Add(opportunities);
        _activeSpeculativeTransaction?.RecordCAS(certificate);
        _mintCerts.Add(certificate);
        _gradeCounts[GradeIdx('E')]++;
        SemanticCASAdmission<EmlCert, string> admission = _cas.Admit(
            certificate,
            offeredRepresentative,
            claimID.Value);
        if (admission.FirstCapture || admission.RepresentativeChanged)
            throw new InvalidOperationException("exact-form admission changed its existing semantic class");
        _mintAdmissions.Add(admission);
        IndexMint(claimID, mint, certificate);

        EmlEvaluatorInterval evaluation = _evaluatorClock.MeasureFrom(evaluatorStart);
        result = new EmlExactFormAdmission(
            EmlExactFormAdmissionStatuses.Accepted,
            claimID,
            certificate,
            incumbentRPN,
            evaluation,
            lawProofDigest,
            admission.FirstCapture,
            admission.RepresentativeChanged);
        return true;
    }

    internal bool TryCreateRewriteCarrier(
        string antecedentRPN,
        out EmlRewritePredictionCarrier carrier,
        out EmlCert certificate)
        => TryCreateRewriteCarrier(antecedentRPN, expectedCertificate: null, out carrier, out certificate);

    internal bool TryCreateRewriteCarrier(
        string antecedentRPN,
        EmlCert? expectedCertificate,
        out EmlRewritePredictionCarrier carrier,
        out EmlCert certificate)
    {
        EmlExactRPNForm form;
        if (expectedCertificate is EmlCert expected)
        {
            if (!_exactRPNLhsByProgramAndCertificate.TryGetValue((antecedentRPN, expected), out form))
            {
                carrier = default;
                certificate = default;
                return false;
            }
        }
        else if (!_exactRPNLhsByProgram.TryGetValue(antecedentRPN, out form))
        {
            carrier = default;
            certificate = default;
            return false;
        }
        if (!TryCreateRewriteCarrier(in form, out carrier))
        {
            certificate = default;
            return false;
        }
        certificate = form.Certificate;
        return certificate.Grade == 'E';
    }

    internal bool TryAdmitComposedForm(
        in EmlRung0Proof proof,
        in EmlRung0Audit audit,
        out EmlComposedFormAdmission result)
    {
        long evaluatorStart = _evaluatorClock.ProgramPointEvaluations;
        if (!proof.IsValidShape || !IsValidComposedAudit(in proof, in audit))
            return RejectComposedForm(EmlComposedFormAdmissionStatuses.InvalidProof, in proof, evaluatorStart, out result);
        if (audit.Status == EmlRung0AuditStatuses.Disagreed)
            return RejectComposedForm(EmlComposedFormAdmissionStatuses.AuditDisagreed, in proof, evaluatorStart, out result);
        int sourceIndex = proof.PredictionID.Value;
        if ((uint)sourceIndex >= (uint)_mintLog.Count || (uint)sourceIndex >= (uint)_mintCerts.Count)
            return RejectComposedForm(EmlComposedFormAdmissionStatuses.SourceMissing, in proof, evaluatorStart, out result);
        EmlMint sourceMint = _mintLog[sourceIndex];
        EmlCert certificate = _mintCerts[sourceIndex];
        if (sourceMint.Grade != 'E' || certificate.Grade != 'E'
            || !EmlPrediction.TryParse(sourceMint.Line, out EmlPrediction sourcePrediction)
            || !string.Equals(sourcePrediction.Lhs, proof.AntecedentRPN, StringComparison.Ordinal)
            || !string.Equals(Digest(sourceMint.Line), proof.SourceDigest, StringComparison.Ordinal))
            return RejectComposedForm(EmlComposedFormAdmissionStatuses.SourceNotExact, in proof, evaluatorStart, out result);
        if (string.Equals(proof.ConsequentRPN, proof.AntecedentRPN, StringComparison.Ordinal))
            return RejectComposedForm(EmlComposedFormAdmissionStatuses.CandidateMatchesSource, in proof, evaluatorStart, out result);

        string proofKey = sourceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "\u0001" + proof.Digest.ToString("X16", System.Globalization.CultureInfo.InvariantCulture);
        string mintKey = proof.ConsequentRPN + "\u0001" + proof.AntecedentRPN;
        if (_derivedFormProofKeys.Contains(proofKey) || _minted.Contains(mintKey))
            return RejectComposedForm(EmlComposedFormAdmissionStatuses.CandidateAlreadyAdmitted, in proof, evaluatorStart, out result);
        if (!_cas.Classes.TryGetValue(certificate, out SemanticCASClass<string> sourceClass))
            return RejectComposedForm(EmlComposedFormAdmissionStatuses.SourceNotExact, in proof, evaluatorStart, out result);
        string offeredRepresentative = CertRepresentative(certificate, proof.ConsequentRPN, sourceClass.Rep);
        if (CompareCertRepresentatives(offeredRepresentative, sourceClass.Rep) < 0)
            return RejectComposedForm(EmlComposedFormAdmissionStatuses.RepresentativeWouldChange, in proof, evaluatorStart, out result);

        string line = proof.ConsequentRPN + " = " + proof.AntecedentRPN;
        EmlPredictionID claimID = new(_mintLog.Count);
        EmlMint mint = new(line, proof.ConsequentRPN, sourceMint.Sig, 'E', Corrob: false);
        _minted.Add(mintKey);
        _activeSpeculativeTransaction?.RecordMinted(mintKey);
        _mintLog.Add(mint);
        IReadOnlyList<TapeEventID> opportunities = (uint)proof.PredictionID.Value < (uint)_mintOpportunityEvents.Count
            ? _mintOpportunityEvents[proof.PredictionID.Value]
            : Array.Empty<TapeEventID>();
        _mintOpportunityEvents.Add(opportunities);
        _activeSpeculativeTransaction?.RecordCAS(certificate);
        _mintCerts.Add(certificate);
        _gradeCounts[GradeIdx('E')]++;
        SemanticCASAdmission<EmlCert, string> admission = _cas.Admit(certificate, offeredRepresentative, claimID.Value);
        if (admission.FirstCapture || admission.RepresentativeChanged)
            throw new InvalidOperationException("derived form changed its existing semantic class");
        _mintAdmissions.Add(admission);
        _activeSpeculativeTransaction?.RecordComposedFormProofKey(proofKey);
        _derivedFormProofKeys.Add(proofKey);
        _derivedFormProofs.Add(new EmlComposedFormProof(proof.PredictionID, claimID, proof.ConsequentRPN, certificate, proof, audit));
        IndexMint(claimID, mint, certificate);
        EmlEvaluatorInterval evaluation = _evaluatorClock.MeasureFrom(evaluatorStart);
        if (evaluation.Calls != 0) throw new InvalidOperationException("rung-0 admission touched the main evaluator");
        _newSemanticDeltas.Add(new EmlCertificateDelta(
            EmlCertificateChanges.LawAdmitted,
            claimID,
            certificate,
            certificate,
            evaluation,
            checked(proof.ConsequentRPN.Length * 8)));
        string proofID = proof.Digest.ToString("X16", System.Globalization.CultureInfo.InvariantCulture);
        string auditID = proofID + ":audit";
        string admissionID = proofID + ":admission:" + claimID.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        EmlRung0AdmissionPath admissionPath = EmlRung0AdmissionPath.Create(proof.PredictionID, in proof);
        result = new EmlComposedFormAdmission(
            EmlComposedFormAdmissionStatuses.Accepted,
            claimID,
            certificate,
            evaluation,
            proof.Digest,
            admissionID,
            proofID,
            auditID)
        {
            AdmissionPath = admissionPath,
        };
        return true;
    }

    /// Materialize the rung-0 admission as the obligation's closure authority.  The closure is
    /// emitted at the same turnstile as the derived mint; callers must not mark the search changed
    /// without this packet because `IsObligationClosed` reads only durable closures.
    internal EmlRung0ComposedFormProof AdmitRung0ComposedFormClosure(
        in EmlRung0Proof proof,
        in EmlRung0Audit audit,
        in EmlComposedFormAdmission admission,
        EmlPredictionID sourcePredictionID,
        out EmlRung0FunnelReceipt receipt)
    {
        if (!admission.Accepted || admission.Evaluation.Calls != 0)
            throw new InvalidDataException("rung-0 closure requires an accepted zero-evaluator admission");
        if (!TryReadTargetEvents(sourcePredictionID, out IReadOnlyList<TapeEventID> opportunityEvents, out TapeEventID mintEventID,
                out EmlObligationTargetSpecies targetSpecies)
            || opportunityEvents.Count == 0 || !HasMintEvent(sourcePredictionID))
        {
            if (!TryReadExactCompositionObligation(sourcePredictionID, out EmlExactCompositionObligation exact)
                || exact.MintEventID is not TapeEventID)
                throw new InvalidDataException("rung-0 closure requires a causal world opportunity event");
            opportunityEvents = exact.Supports;
            mintEventID = exact.MintEventID!.Value;
            targetSpecies = EmlObligationTargetSpecies.ExactComposition;
        }
        if (proof.PredictionID != sourcePredictionID || audit.ProofDigest != proof.Digest)
            throw new InvalidDataException("rung-0 closure proof and audit do not bind to the obligation");
        EmlRung0AdmissionPath expectedPath = EmlRung0AdmissionPath.Create(sourcePredictionID, in proof);
        if (!admission.AdmissionPath.IsBound || !admission.AdmissionPath.Matches(expectedPath))
            throw new InvalidDataException("rung-0 closure admission path is not the exact admitted claim");
        if (!TryReadTargetIdentity(sourcePredictionID, out string obligationID, out EmlObligationTargetSpecies registeredSpecies)
            || registeredSpecies != targetSpecies)
            throw new InvalidDataException("rung-0 closure source target is not registered with the expected species");
        string proofID = admission.ProofID.Length == 0
            ? proof.Digest.ToString("X16", System.Globalization.CultureInfo.InvariantCulture)
            : admission.ProofID;
        string auditID = admission.AuditID.Length == 0 ? proofID + ":audit" : admission.AuditID;
        string admissionID = admission.AdmissionID.Length == 0
            ? proofID + ":admission:" + admission.PredictionID.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : admission.AdmissionID;
        EmlPredictionID derivedPredictionID = admission.PredictionID;
        EmlCert sourceCertificate = GetPredictionCertificate(sourcePredictionID);
        string guardPackageDigest = proof.Steps.Count == 0 ? "" : proof.Steps[^1].GuardWitness.Digest.ToString("X16", System.Globalization.CultureInfo.InvariantCulture);
        string candidateDigest = Digest(proof.AntecedentRPN + "|" + proof.ConsequentRPN + "|guard=" + guardPackageDigest);
        string closureID = ComputeAttachmentID(obligationID, EmlObligationProofKinds.Rung0ComposedForm, candidateDigest);
        EmlEvaluatorClock comparatorClock = new();
        long comparatorStart = comparatorClock.ProgramPointEvaluations;
        EmlRung0AdmissionPath admissionPath = admission.AdmissionPath;
        EmlVerdict comparatorVerdict = admissionPath.Grade(new EmlGrader(comparatorClock));
        EmlEvaluatorInterval comparatorEvaluation = comparatorClock.MeasureFrom(comparatorStart);
        if (comparatorVerdict.Grade != 'E' || comparatorEvaluation.Calls <= 0)
            throw new InvalidDataException("rung-0 closure comparator did not perform positive ordinary grading work");
        EmlRung0ComposedFormProof witness = new(
            sourcePredictionID,
            obligationID,
            derivedPredictionID,
            proof.AntecedentRPN,
            proof.ConsequentRPN,
            guardPackageDigest,
            proofID,
            auditID,
            admissionID,
            closureID,
            "EmlGrader.GradeRpn",
            // Rung-0 admission is a zero-additional-evaluator path. Keep its canonical
            // interval independent of the caller's ambient clock so checkpoint reconstruction
            // can reproduce the witness exactly.
            EmlEvaluatorInterval.EmptyAt(0),
            comparatorEvaluation,
            proof,
            audit);
        if (!witness.IsExactZeroAdmission)
            throw new InvalidDataException("rung-0 closure witness does not prove zero-vs-positive admission paths");
        string attachmentDigest = Digest(obligationID + "|candidate|" + EmlObligationProofKinds.Rung0ComposedForm + "|" + candidateDigest);
        EmlRung0ComposedFormObligationEvidence evidence = new(
            witness.ObligationPredictionID,
            witness.ObligationID,
            witness.ComposedPredictionID,
            witness.LhsRPN,
            witness.RhsRPN,
            witness.GuardPackageDigest,
            witness.ProofID,
            witness.AuditID,
            EmlRung0Checkpoint.ProofSHA256(proof),
            EmlRung0Checkpoint.AuditSHA256(audit),
            witness.AdmissionID,
            witness.ClosureID,
            witness.Comparator,
            witness.AdmissionEvaluation,
            witness.ComparatorEvaluation,
            candidateDigest,
            attachmentDigest,
            sourceCertificate,
            admission.Certificate,
            witness.AdmissionPath.GuardPackageCanonical,
            witness.AdmissionPath.GuardPackageFingerprint);
        EmlObligationClosure closure = new(
            sourcePredictionID,
            obligationID,
            ComputeAttemptID(obligationID, EmlObligationProofKinds.Rung0ComposedForm, null, null, candidateDigest),
            closureID,
            EmlObligationClosureStatuses.Accepted,
            SourceDigest(sourcePredictionID),
            EmlObligationProofKinds.Rung0ComposedForm,
            null,
            null,
            null,
            null,
            "accepted",
            evidence,
            targetSpecies);
        if (!_obligationClosureKeys.TryAdd(closure.AttachmentID, _obligationClosures.Count))
            throw new InvalidDataException("duplicate rung-0 derived-form closure attachment");
        _activeSpeculativeTransaction?.RecordClosureKey(closure.AttachmentID);
        _obligationClosures.Add(closure);
        receipt = new EmlRung0FunnelReceipt(
            EmlRung0FunnelStages.Closure,
            sourcePredictionID,
            obligationID,
            proof.Steps.Count == 0 ? default : proof.Steps[^1].RuleID,
            true,
            "accepted",
            proofID,
            auditID,
            admissionID,
            closureID,
            admission.Evaluation);
        return witness;
    }

    internal bool HasObligation(EmlPredictionID sourcePredictionID)
        => _obligationBySource.ContainsKey(sourcePredictionID) || _exactCompositionBySource.ContainsKey(sourcePredictionID);

    internal bool TryReadTargetIdentity(EmlPredictionID sourcePredictionID, out string identity, out EmlObligationTargetSpecies species)
    {
        if (_obligationBySource.TryGetValue(sourcePredictionID, out int index))
        {
            identity = _obligations[index].Identity;
            species = EmlObligationTargetSpecies.Residual;
            return true;
        }
        if (_exactCompositionBySource.TryGetValue(sourcePredictionID, out int exactIndex))
        {
            identity = _exactCompositionObligations[exactIndex].Identity;
            species = EmlObligationTargetSpecies.ExactComposition;
            return true;
        }
        identity = "";
        species = default;
        return false;
    }

    private static bool IsValidComposedAudit(in EmlRung0Proof proof, in EmlRung0Audit audit)
    {
        if (audit.ProofDigest != proof.Digest || audit.EvaluatorCalls < 0 || audit.Rules.Count == 0) return false;
        if (!Enum.IsDefined(audit.Selection)) return false;
        bool selected = EmlRung0Digest.SelectNumericAudit(proof.Digest)
            || audit.Selection == EmlRung0AuditSelectionSpecies.MinimumOne;
        bool validStatus = audit.Status switch
        {
            EmlRung0AuditStatuses.NotSelected => !selected && audit.EvaluatorCalls == 0
                && !audit.NumericVerified && audit.GuardVerified,
            EmlRung0AuditStatuses.Agreed => selected && audit.EvaluatorCalls > 0
                && audit.NumericVerified && audit.GuardVerified,
            EmlRung0AuditStatuses.Disagreed => selected && audit.EvaluatorCalls > 0
                && (!audit.NumericVerified || !audit.GuardVerified),
            _ => false,
        };
        if (!validStatus) return false;
        HashSet<EmlRuleID> proofRules = new();
        for (int i = 0; i < proof.Steps.Count; i++) proofRules.Add(proof.Steps[i].RuleID);
        return proofRules.SetEquals(audit.Rules);
    }

    private bool RejectComposedForm(
        EmlComposedFormAdmissionStatuses status,
        in EmlRung0Proof proof,
        long evaluatorStart,
        out EmlComposedFormAdmission result)
    {
        EmlEvaluatorInterval evaluation = _evaluatorClock.MeasureFrom(evaluatorStart);
        if (evaluation.Calls != 0) throw new InvalidOperationException("rejected rung-0 admission touched the main evaluator");
        result = new EmlComposedFormAdmission(status, default, default, evaluation, proof.Digest);
        return false;
    }

    private bool RejectExactForm(
        EmlExactFormAdmissionStatuses status,
        EmlCert certificate,
        string? incumbentRPN,
        long evaluatorStart,
        ulong lawProofDigest,
        out EmlExactFormAdmission result)
    {
        EmlEvaluatorInterval evaluation = _evaluatorClock.MeasureFrom(evaluatorStart);
        result = new EmlExactFormAdmission(
            status,
            default,
            certificate,
            incumbentRPN,
            evaluation,
            lawProofDigest,
            FirstCapture: false,
            RepresentativeChanged: false);
        return false;
    }

    public EmlObligationClosureResult AdmitResidualProof(
        EmlPredictionID sourcePredictionID,
        string program,
        long evaluatorStart,
        EmlDeliberationLease? deliberationLease = null)
    {
        long wallStart = System.Diagnostics.Stopwatch.GetTimestamp();
        EmlFiniteObligationProofPolicy policy = new(_sig, 1, "eml-finite-residual-v2");
        int obligationIndex = _obligationBySource.GetValueOrDefault(sourcePredictionID, -1);
        if (obligationIndex < 0)
        {
            return CreateClosureResult(sourcePredictionID, EmlObligationProofKinds.FiniteRPN, policy, null,
                EmlObligationClosureStatuses.UnknownObligation, evaluatorStart, wallStart, 0, "", default,
                "obligation source is not registered");
        }
        string proofKey = sourcePredictionID.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "\u0001" + program;
        if (_residualProofKeys.Contains(proofKey))
        {
            EmlObligation existingObligation = _obligations[obligationIndex];
            return CreateClosureResult(sourcePredictionID, EmlObligationProofKinds.FiniteRPN, policy, null,
                EmlObligationClosureStatuses.DuplicateAttachment, evaluatorStart, wallStart, 0,
                AttachmentDigest(existingObligation, EmlObligationProofKinds.FiniteRPN, program), default,
                "finite attachment was already admitted");
        }

        EmlObligation obligation = _obligations[obligationIndex];
        EmlObligationResolution resolution = ResolveObligation(obligation);
        EmlMint sourceMint = _mintLog[sourcePredictionID.Value];
        deliberationLease?.ReserveVerifierProgramPoints(1);
        if (!EmlPrediction.TryParse(sourceMint.Line, out EmlPrediction sourcePrediction)
            || !_grader.TryGrade(in sourcePrediction, _claimReferences, out EmlVerdict sourceVerdict)
            || sourceVerdict.Grade != 'A')
            throw new InvalidDataException($"residual proof source {sourcePredictionID.Value} is no longer an asymptotic claim");
        EmlResidualExpression expression = EmlResidualExpression.CreateFiniteRPN(program);
        EmlResidualExpressionEvaluation expressionEvaluation = expression.Evaluate(
            _evaluatorClock,
            deliberationLease is null ? _grader : new EmlGrader(_evaluatorClock, deliberationLease),
            deliberationLease);
        EmlResidualWitness witness = resolution.Corroboration;
        EmlHoleRepairOccurrenceCheck verification = EmlHoleSolver.VerifyExpression(
            expression,
            in expressionEvaluation,
            in witness,
            _grader);
        if (!verification.Accepted)
        {
            return CreateClosureResult(sourcePredictionID, EmlObligationProofKinds.FiniteRPN, policy, null,
                EmlObligationClosureStatuses.OccurrenceCheckRejected, evaluatorStart, wallStart, 0,
                AttachmentDigest(obligation, EmlObligationProofKinds.FiniteRPN, program), default,
                verification.Detail);
        }

        EmlCert before = EmlCert.Of(in sourceVerdict, _sig);
        EmlCert after = new('E', resolution.ResidualSignature, 0, 0);
        EmlEvaluatorInterval evaluation = _evaluatorClock.MeasureFrom(evaluatorStart);
        EmlCertificateDelta delta = new(
            EmlCertificateChanges.ProofAttached,
            sourcePredictionID,
            before,
            after,
            evaluation,
            checked(program.Length * 8));
        _activeSpeculativeTransaction?.RecordResidualProofKey(proofKey);
        _residualProofKeys.Add(proofKey);
        _residualProofs.Add(new EmlResidualProof(sourcePredictionID, program, after));
        _newSemanticDeltas.Add(delta);
        return CreateClosureResult(sourcePredictionID, EmlObligationProofKinds.FiniteRPN, policy, null,
            EmlObligationClosureStatuses.Accepted, evaluatorStart, wallStart, 0,
            AttachmentDigest(obligation, EmlObligationProofKinds.FiniteRPN, program), after, "accepted", delta,
            beforeCertificate: before);
    }

    public EmlObligationClosureResult RejectWrongObligationKind(
        EmlPredictionID sourcePredictionID,
        EmlObligationProofKinds expectedKind,
        EmlObligationProofKinds offeredKind,
        long evaluatorStart)
    {
        if (expectedKind == offeredKind)
            throw new ArgumentException("wrong-kind rejection requires distinct expected and offered kinds");
        long wallStart = System.Diagnostics.Stopwatch.GetTimestamp();
        if (offeredKind == EmlObligationProofKinds.FiniteRPN)
            return CreateClosureResult(sourcePredictionID, offeredKind,
                new EmlFiniteObligationProofPolicy(_sig, 1, "eml-finite-residual-v2"), null,
                EmlObligationClosureStatuses.WrongKind, evaluatorStart, wallStart, 0, "", default,
                $"offered {offeredKind}, expected {expectedKind}");
        return CreateClosureResult(sourcePredictionID, offeredKind, null,
            new EmlProcessObligationProofPolicy(_sig, 0, 3, EmlProcessFunctions.AlgorithmVersion, 0, "eml-process-residual-v3"),
            EmlObligationClosureStatuses.WrongKind, evaluatorStart, wallStart, 0, "", default,
            $"offered {offeredKind}, expected {expectedKind}");
    }

    public bool TryAdmitResidualProof(
        EmlPredictionID sourcePredictionID,
        string program,
        long evaluatorStart,
        out EmlCertificateDelta delta,
        EmlDeliberationLease? deliberationLease = null)
    {
        EmlObligationClosureResult result = AdmitResidualProof(sourcePredictionID, program, evaluatorStart, deliberationLease);
        delta = result.Delta.GetValueOrDefault();
        return result.Accepted;
    }

    public EmlObligationClosureResult AdmitProcessResidualProof(
        EmlPredictionID sourcePredictionID,
        in EmlProcessFunction function,
        EmlResidualComposition? derivation,
        long evaluatorStart,
        EmlDeliberationLease? deliberationLease = null)
    {
        long wallStart = System.Diagnostics.Stopwatch.GetTimestamp();
        EmlProcessObligationProofPolicy policy = new(
            _sig, function.Fuel, 3, EmlProcessFunctions.AlgorithmVersion,
            derivation is null ? 0 : 1, "eml-process-residual-v3");
        if (function.Algorithm == EmlProcessFunctionAlgorithms.ExponentialSeries
            && derivation is not { Law: EmlResidualCompositionLaws.ExponentialTail })
        {
            return CreateClosureResult(sourcePredictionID, EmlObligationProofKinds.ProcessFunction, null, policy,
                EmlObligationClosureStatuses.OccurrenceCheckRejected, evaluatorStart, wallStart, 0, "", default,
                "exponential-series process requires an ExponentialTail structural derivation");
        }
        if (function.Fuel <= 0)
        {
            return CreateClosureResult(sourcePredictionID, EmlObligationProofKinds.ProcessFunction, null, policy, EmlObligationClosureStatuses.InvalidPolicy,
                evaluatorStart, wallStart, 0, "", default, "process proof fuel must be positive");
        }
        int obligationIndex = _obligationBySource.GetValueOrDefault(sourcePredictionID, -1);
        if (obligationIndex < 0)
        {
            return CreateClosureResult(sourcePredictionID, EmlObligationProofKinds.ProcessFunction, null, policy, EmlObligationClosureStatuses.UnknownObligation,
                evaluatorStart, wallStart, 0, "", default, "obligation source is not registered");
        }

        EmlObligation obligation = _obligations[obligationIndex];
        EmlProcessFunctionCertificate certificate = EmlProcessFunctions.Certify(in function, deliberationLease);
        EmlProcessFunctionCheck certificateCheck = EmlProcessFunctionChecker.Check(in certificate, deliberationLease);
        if (!certificateCheck.Accepted)
            throw new InvalidDataException($"process residual certificate failed: {certificateCheck.Detail}");
        string proofKey = sourcePredictionID.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "\u0001" + certificate.Digest;
        if (_processResidualProofKeys.Contains(proofKey))
        {
            return CreateClosureResult(sourcePredictionID, EmlObligationProofKinds.ProcessFunction, null, policy, EmlObligationClosureStatuses.DuplicateAttachment,
                evaluatorStart, wallStart, 0,
                AttachmentDigest(obligation, EmlObligationProofKinds.ProcessFunction, certificate.Digest), default,
                "process attachment was already admitted", descriptorDigest: certificate.Digest);
        }

        EmlObligationResolution resolution = ResolveObligation(obligation);
        EmlMint sourceMint = _mintLog[sourcePredictionID.Value];
        if (!EmlPrediction.TryParse(sourceMint.Line, out EmlPrediction sourcePrediction))
        {
            return CreateClosureResult(sourcePredictionID, EmlObligationProofKinds.ProcessFunction, null, policy, EmlObligationClosureStatuses.OccurrenceCheckRejected,
                evaluatorStart, wallStart, 0,
                AttachmentDigest(obligation, EmlObligationProofKinds.ProcessFunction, certificate.Digest), default,
                "process proof source claim is malformed", descriptorDigest: certificate.Digest);
        }
        EmlResidualWitness witness = resolution.Corroboration;
        EmlProcessResidualOccurrenceCheck verification = EmlProcessResidualVerifier.Verify(
            sourcePredictionID,
            in sourcePrediction,
            in witness,
            in function,
            derivation,
            _claimReferences,
            _evaluatorClock,
            deliberationLease);
        long processFuel = checked(
            verification.Process.P1.FuelSpent
            + verification.Process.P2.FuelSpent
            + verification.Process.P3.FuelSpent);
        bool sourceGradeValid = _grader.TryGrade(in sourcePrediction, _claimReferences, out EmlVerdict sourceVerdict)
            && sourceVerdict.Grade == 'A';
        if (!verification.Accepted)
        {
            return CreateClosureResult(sourcePredictionID, EmlObligationProofKinds.ProcessFunction, null, policy, EmlObligationClosureStatuses.OccurrenceCheckRejected,
                evaluatorStart, wallStart, processFuel,
                AttachmentDigest(obligation, EmlObligationProofKinds.ProcessFunction, certificate.Digest), default,
                verification.Detail, descriptorDigest: certificate.Digest);
        }

        if (!sourceGradeValid)
            throw new InvalidDataException($"process residual proof source {sourcePredictionID.Value} is no longer an asymptotic claim");

        EmlCert before = EmlCert.Of(in sourceVerdict, _sig);
        EmlCert after = new('E', resolution.ResidualSignature, 0, 0);
        EmlEvaluatorInterval evaluatorWork = _evaluatorClock.MeasureFrom(evaluatorStart);
        EmlResidualExpression expression = EmlResidualExpression.CreateProcessFunction(in function);
        int proofBits = checked(expression.RenderCanonical().Length * 8);
        EmlCertificateDelta delta = new(
            EmlCertificateChanges.ProofAttached,
            sourcePredictionID,
            before,
            after,
            evaluatorWork,
            proofBits);
        _activeSpeculativeTransaction?.RecordProcessResidualProofKey(proofKey);
        _processResidualProofKeys.Add(proofKey);
        _processResidualProofs.Add(new EmlProcessResidualProof(
            sourcePredictionID,
            function,
            derivation?.Law,
            certificate.Digest,
            after,
            processFuel));
        _newSemanticDeltas.Add(delta);
        return CreateClosureResult(sourcePredictionID, EmlObligationProofKinds.ProcessFunction, null, policy, EmlObligationClosureStatuses.Accepted,
            evaluatorStart, wallStart, processFuel,
            AttachmentDigest(obligation, EmlObligationProofKinds.ProcessFunction, certificate.Digest), after,
            "accepted", delta, certificate.Digest, before);
    }

    public bool TryAdmitProcessResidualProof(
        EmlPredictionID sourcePredictionID,
        in EmlProcessFunction function,
        EmlResidualComposition? derivation,
        long evaluatorStart,
        out EmlCertificateDelta delta,
        out long processFuel,
        EmlDeliberationLease? deliberationLease = null)
    {
        EmlObligationClosureResult result = AdmitProcessResidualProof(sourcePredictionID, in function, derivation, evaluatorStart, deliberationLease);
        delta = result.Delta.GetValueOrDefault();
        processFuel = result.ProcessFuel;
        return result.Accepted;
    }

    private EmlObligationClosureResult CreateClosureResult(
        EmlPredictionID sourcePredictionID,
        EmlObligationProofKinds kind,
        EmlFiniteObligationProofPolicy? finitePolicy,
        EmlProcessObligationProofPolicy? processPolicy,
        EmlObligationClosureStatuses status,
        long evaluatorStart,
        long wallStart,
        long fuelSpent,
        string attachmentDigest,
        EmlCert certificate,
        string reason,
        EmlCertificateDelta? delta = null,
        string descriptorDigest = "",
        EmlCert beforeCertificate = default)
    {
        EmlObligation? found = _obligationBySource.TryGetValue(sourcePredictionID, out int foundIndex)
            ? _obligations[foundIndex]
            : null;
        string obligationID = found?.Identity ?? "unknown";
        string candidateDigest = descriptorDigest.Length == 0
            ? (attachmentDigest.Length == 0 ? "none" : attachmentDigest)
            : descriptorDigest;
        string attemptID = ComputeAttemptID(obligationID, kind, finitePolicy, processPolicy, candidateDigest);
        string attachmentID = attachmentDigest.Length == 0 ? "" : ComputeAttachmentID(obligationID, kind, candidateDigest);
        EmlEvaluatorInterval evaluator = _evaluatorClock.MeasureFrom(evaluatorStart);
        long wallTicks = System.Diagnostics.Stopwatch.GetTimestamp() - wallStart;
        EmlFiniteObligationProofEvidence? finiteEvidence = kind == EmlObligationProofKinds.FiniteRPN
            ? new EmlFiniteObligationProofEvidence(evaluator, wallTicks, candidateDigest, attachmentDigest, beforeCertificate, certificate)
            : null;
        EmlProcessObligationProofEvidence? processEvidence = kind == EmlObligationProofKinds.ProcessFunction
            ? new EmlProcessObligationProofEvidence(
                evaluator, wallTicks, processPolicy?.FuelPerProbe ?? 0, fuelSpent, candidateDigest,
                attachmentDigest, candidateDigest, beforeCertificate, certificate)
            : null;
        EmlObligationClosure closure = new(
            sourcePredictionID,
            obligationID,
            attemptID,
            attachmentID,
            status,
            SourceDigest(sourcePredictionID),
            kind,
            finitePolicy,
            processPolicy,
            finiteEvidence,
            processEvidence,
            reason);
        if (found.HasValue)
        {
            if (closure.Closed)
            {
                if (attachmentID.Length == 0) throw new InvalidDataException("accepted EML obligation closure has no attachment digest");
                _activeSpeculativeTransaction?.RecordClosureKey(attachmentID);
                if (!_obligationClosureKeys.TryAdd(attachmentID, _obligationClosures.Count))
                    throw new InvalidDataException($"duplicate EML obligation closure attachment {attachmentID}");
            }
            _obligationClosures.Add(closure);
        }
        return new EmlObligationClosureResult(closure, delta);
    }

    private static string AttachmentDigest(EmlObligation obligation, EmlObligationProofKinds kind, string candidate)
        => AttachmentDigest(obligation.Identity, kind, candidate);

    private static string AttachmentDigest(string obligationIdentity, EmlObligationProofKinds kind, string candidate)
        => Digest(obligationIdentity + "|candidate|" + kind + "|" + candidate);

    private string SourceDigest(EmlPredictionID sourcePredictionID)
        => (uint)sourcePredictionID.Value < (uint)_mintLog.Count ? Digest(_mintLog[sourcePredictionID.Value].Line) : "";

    private static string ComputeAttemptID(string obligationID, EmlObligationProofKinds kind,
        EmlFiniteObligationProofPolicy? finitePolicy, EmlProcessObligationProofPolicy? processPolicy,
        string candidateDigest)
    {
        string policyDigest = kind switch
        {
            EmlObligationProofKinds.FiniteRPN => Digest($"finite|{finitePolicy?.SignatureDigits}|{finitePolicy?.WitnessVersion}|{finitePolicy?.VerifierRevision}"),
            EmlObligationProofKinds.ProcessFunction => Digest($"process|{processPolicy?.SignatureDigits}|{processPolicy?.FuelPerProbe}|{processPolicy?.ProbeCount}|{processPolicy?.FunctionVersion}|{processPolicy?.CompositionVersion}|{processPolicy?.VerifierRevision}"),
            EmlObligationProofKinds.Rung0ComposedForm => Digest("rung0-derived-form|zero-evaluator-admission-v1"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "unknown EML obligation proof kind"),
        };
        return Digest(obligationID + "|proof-attempt|" + kind + "|" + policyDigest + "|" + candidateDigest);
    }

    private static string ComputeAttachmentID(string obligationID, EmlObligationProofKinds kind, string candidateDigest)
        => Digest(obligationID + "|attachment|" + kind + "|" + candidateDigest);

    private static string Digest(string value)
        => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value)));

    private string ComputeObligationIdentity(EmlPredictionID sourcePredictionID, in EmlObligationResolution resolution)
    {
        if ((uint)sourcePredictionID.Value >= (uint)_mintLog.Count)
            throw new InvalidDataException($"cannot identify missing EML obligation source {sourcePredictionID.Value}");
        EmlMint source = _mintLog[sourcePredictionID.Value];
        return Digest("eml-obligation-v2|" + source.Line + "|" + FormatSig(source.Sig) + "|"
            + FormatSig(resolution.ResidualSignature) + "|sig=" + _sig.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string FormatSig(EmlSig sig)
        => $"{sig.R1:X16}{sig.I1:X16}{sig.R2:X16}{sig.I2:X16}";

    // ── THE THEOREM CAS reads ──
    public int DistinctCerts => _cas.Count;                              // distinct certificate-classes — the SEMANTIC distinct-count (paraphrase cannot move it)
    public IReadOnlyDictionary<EmlCert, SemanticCASClass<string>> Cas => _cas.Classes; // the full CAS — the audit + the readout iterate it
    public EmlCert MintCert(int mintIdx) => _mintCerts[mintIdx];         // the certificate of mint #i (parallel to MintLog)
    public bool FirstCapture(int mintIdx) => _cas[_mintCerts[mintIdx]].FirstCapture == mintIdx; // the counterfeit detector: was this mint its class's FIRST?

    public void AppendCanonicalPrograms(List<string> programs, int maximum)
    {
        if (maximum < 0) throw new ArgumentOutOfRangeException(nameof(maximum));
        List<string> ordered = new(_canon.Values);
        ordered.Sort(CompareCertRepresentatives);
        int count = Math.Min(maximum, ordered.Count);
        for (int i = 0; i < count; i++) programs.Add(ordered[i]);
    }

    /// Distinct THEOREM classes — the FULL-FRONTIER CENSUS core: E identities + rate-law'd A asymptotics.
    /// S (refuted coincidence) and D/U (unwitnessable) classes are recorded non-theorems and stay out. Composed
    /// state like the CAS itself (RebuildCas recounts on Load).
    public int TheoremClasses { get; private set; }

    /// Distinct EXACT certificate-classes — the VESTED tier of the census, and the lift organ's box-completion
    /// clock (measured law: the A-tier is an absorption-noise font that never plateaus — newA 50–70/window
    /// SUSTAINED while newE runs 0–24 sparse; only the E-lattice completes, 's vesting lattice). Composed like
    /// TheoremClasses.
    public int ExactClasses { get; private set; }
    internal int SignatureDigits => _sig;

    /// The first-capture bit of a FRESH (undrained) mint — Mint appends to _newMints and _mintLog in lockstep, so
    /// new-mint i sits at mint index |log|−|new|+i. The accretors' cert-novelty read (first ×W, paraphrase ×0).
    public bool NewMintFirst(int newIdx) => FirstCapture(_mintLog.Count - _newMints.Count + newIdx);
    public bool NewMintRepresentativeChanged(int newIdx)
        => _mintAdmissions[_mintLog.Count - _newMints.Count + newIdx].RepresentativeChanged;

    public EmlCert NewMintCert(int newIdx)
        => _mintCerts[_mintLog.Count - _newMints.Count + newIdx];

    public EmlPredictionID NewMintPredictionID(int newIdx)
        => new(_mintLog.Count - _newMints.Count + newIdx);

    internal byte[] CaptureAdmissionState()
    {
        using MemoryStream stream = new();
        using (CkptWriter writer = new(stream)) Save(writer);
        return stream.ToArray();
    }

    /// Begin an in-memory admission fork.  The fork records only writes made by the
    /// speculative operation; rollback truncates append-only journals and restores
    /// touched map entries/scalars.  The law store is deliberately outside this
    /// transaction because rung-0 law evidence is persistent across trials.
    internal SpeculativeTransaction BeginSpeculativeTransaction()
    {
        if (_activeSpeculativeTransaction is not null)
            throw new InvalidOperationException("nested EML sieve speculation is not supported");
        SpeculativeTransaction transaction = new(this);
        _activeSpeculativeTransaction = transaction;
        return transaction;
    }

    internal sealed class SpeculativeTransaction : IDisposable
    {
        private readonly EmlSieve _owner;
        private readonly EmlEvaluatorClockSnapshot _clock;
        private readonly EmlMint[] _newMintPrefix;
        private readonly int _mintLog;
        private readonly int _exactRPNForms;
        private readonly int _exactRPNLhsForms;
        private readonly int _lawPredictionTrees;
        private readonly int _exactRPNPredictionCount;
        private readonly int _mintCerts;
        private readonly int _mintAdmissions;
        private readonly int _obligations;
        private readonly int _closures;
        private readonly int _residualProofs;
        private readonly int _processProofs;
        private readonly int _derivedProofs;
        private readonly EmlCertificateDelta[] _semanticDeltaPrefix;
        private readonly int _admissions;
        private readonly int _phases;
        private readonly int _settlements;
        private readonly int _theoremClasses;
        private readonly int _exactClasses;
        private readonly int _identities;
        private readonly int _valueHits;
        private readonly int _kFrontier;
        private readonly long _finiteOffers;
        private readonly Dictionary<EmlSig, string?> _canon = new();
        private readonly Dictionary<EmlSig, int?> _sigHits = new();
        private readonly Dictionary<int, int?> _discByLen = new();
        private readonly Dictionary<EmlSig, int?> _obligationByResidual = new();
        private readonly Dictionary<EmlPredictionID, int?> _obligationBySource = new();
        private readonly Dictionary<EmlPredictionID, int?> _exactCompositionBySource = new();
        private readonly Dictionary<string, int?> _closureKeys = new(StringComparer.Ordinal);
        private readonly List<(EmlLawStore Store, EmlRung0Audit Audit, bool Promote)> _rung0AuditDebt = new();
        internal int PublishedRung0Audits { get; private set; }
        private readonly Dictionary<EmlCert, SemanticCASClass<string>?> _cas = new();
        private readonly HashSet<string> _minted = new(StringComparer.Ordinal);
        private readonly HashSet<int> _heldCaptured = new();
        private readonly List<(int Index, int BestK, string? BestProgram)> _bestTargets = new();
        private readonly HashSet<int> _bestTargetSeen = new();
        private readonly HashSet<string> _residualProofKeys = new(StringComparer.Ordinal);
        private readonly HashSet<string> _processResidualProofKeys = new(StringComparer.Ordinal);
        private readonly HashSet<string> _derivedFormProofKeys = new(StringComparer.Ordinal);
        private readonly HashSet<string> _processProofWireVersions = new(StringComparer.Ordinal);
        private readonly Dictionary<EmlMint, EmlPredictionID?> _claimByMint = new();
        private readonly Dictionary<EmlPredictionID, EmlExactRPNForm?> _exactRPNLhsByPrediction = new();
        private readonly Dictionary<string, EmlExactRPNForm?> _exactRPNLhsByProgram = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Program, EmlCert Certificate), EmlExactRPNForm?> _exactRPNLhsByProgramAndCertificate = new();
        private bool _completed;
        private long _previewEvaluatorCalls;
        private long _committedEvaluatorCalls;
        private long _previewWallTicks;
        private long _commitWallTicks;
        private long _rollbackWallTicks;
        private long _serializeLoads;
        private long _serializeBytes;
        private long _restores;
        private long _restoreBytes;
        private readonly long _startedAt = Stopwatch.GetTimestamp();

        internal SpeculativeTransaction(EmlSieve owner)
        {
            _owner = owner;
            owner._grader.BeginSpeculativeCache();
            _clock = owner._evaluatorClock.Capture();
            _newMintPrefix = owner._newMints.ToArray();
            _mintLog = owner._mintLog.Count;
            _exactRPNForms = owner._exactRPNForms.Count;
            _exactRPNLhsForms = owner._exactRPNLhsForms.Count;
            _lawPredictionTrees = owner._lawPredictionTrees.Count;
            _exactRPNPredictionCount = owner._exactRPNPredictionCount;
            _mintCerts = owner._mintCerts.Count;
            _mintAdmissions = owner._mintAdmissions.Count;
            _obligations = owner._obligations.Count;
            _closures = owner._obligationClosures.Count;
            _residualProofs = owner._residualProofs.Count;
            _processProofs = owner._processResidualProofs.Count;
            _derivedProofs = owner._derivedFormProofs.Count;
            _semanticDeltaPrefix = owner._newSemanticDeltas.ToArray();
            _admissions = owner._deliberationJournal.Admissions.Count;
            _phases = owner._deliberationJournal.Phases.Count;
            _settlements = owner._deliberationJournal.Settlements.Count;
            _theoremClasses = owner.TheoremClasses;
            _exactClasses = owner.ExactClasses;
            _identities = owner.Identities;
            _valueHits = owner.ValueHits;
            _kFrontier = owner.KFrontier;
            _finiteOffers = owner.FiniteOffers;
        }

        public long PreviewEvaluatorCalls => _previewEvaluatorCalls;
        public long CommittedEvaluatorCalls => _committedEvaluatorCalls;
        public long PreviewWallTicks => _previewWallTicks;
        public long CommitWallTicks => _commitWallTicks;
        public long RollbackWallTicks => _rollbackWallTicks;
        public long SerializeLoads => _serializeLoads;
        public long SerializeBytes => _serializeBytes;
        public long Restores => _restores;
        public long RestoreBytes => _restoreBytes;

        internal void RecordPreview(long calls) => _previewEvaluatorCalls = checked(_previewEvaluatorCalls + calls);
        internal void RecordCommitted(long calls) => _committedEvaluatorCalls = checked(_committedEvaluatorCalls + calls);
        internal void RecordSerializeLoad(long bytes)
        {
            _serializeLoads = checked(_serializeLoads + 1);
            _serializeBytes = checked(_serializeBytes + bytes);
        }
        internal void RecordRestore(long bytes)
        {
            _restores = checked(_restores + 1);
            _restoreBytes = checked(_restoreBytes + bytes);
        }
        internal void RecordCanon(EmlSig key)
        {
            if (!_canon.ContainsKey(key))
                _canon[key] = _owner._canon.TryGetValue(key, out string? old) ? old : null;
        }
        internal void RecordSigHit(EmlSig key)
        {
            if (!_sigHits.ContainsKey(key))
                _sigHits[key] = _owner._sigHits.TryGetValue(key, out int old) ? old : null;
        }
        internal void RecordDiscByLen(int key)
        {
            if (!_discByLen.ContainsKey(key))
                _discByLen[key] = _owner._discByLen.TryGetValue(key, out int old) ? old : null;
        }
        internal void RecordObligationResidual(EmlSig key)
        {
            if (!_obligationByResidual.ContainsKey(key))
                _obligationByResidual[key] = _owner._obligationByResidual.TryGetValue(key, out int old) ? old : null;
        }
        internal void RecordObligationSource(EmlPredictionID key)
        {
            if (!_obligationBySource.ContainsKey(key))
                _obligationBySource[key] = _owner._obligationBySource.TryGetValue(key, out int old) ? old : null;
        }
        internal void RecordExactCompositionSource(EmlPredictionID key)
        {
            if (!_exactCompositionBySource.ContainsKey(key))
                _exactCompositionBySource[key] = _owner._exactCompositionBySource.TryGetValue(key, out int old) ? old : null;
        }
        internal void RecordMintPrediction(EmlMint key)
        {
            if (!_claimByMint.ContainsKey(key))
                _claimByMint[key] = _owner._claimByMint.TryGetValue(key, out EmlPredictionID old) ? old : null;
        }
        internal void RecordExactRpnPrediction(EmlPredictionID key)
        {
            if (!_exactRPNLhsByPrediction.ContainsKey(key))
                _exactRPNLhsByPrediction[key] = _owner._exactRPNLhsByPrediction.TryGetValue(key, out EmlExactRPNForm old) ? old : null;
        }
        internal void RecordExactRpnProgram(string key)
        {
            if (!_exactRPNLhsByProgram.ContainsKey(key))
                _exactRPNLhsByProgram[key] = _owner._exactRPNLhsByProgram.TryGetValue(key, out EmlExactRPNForm old) ? old : null;
        }
        internal void RecordExactRpnProgramAndCertificate((string Program, EmlCert Certificate) key)
        {
            if (!_exactRPNLhsByProgramAndCertificate.ContainsKey(key))
                _exactRPNLhsByProgramAndCertificate[key] = _owner._exactRPNLhsByProgramAndCertificate.TryGetValue(key, out EmlExactRPNForm old) ? old : null;
        }
        internal void RecordClosureKey(string key)
        {
            if (!_closureKeys.ContainsKey(key))
                _closureKeys[key] = _owner._obligationClosureKeys.TryGetValue(key, out int old) ? old : null;
        }
        internal void RecordRung0Audit(EmlLawStore store, in EmlRung0Audit audit, bool promoteRetained)
        {
            for (int i = 0; i < _rung0AuditDebt.Count; i++)
            {
                (EmlLawStore priorStore, EmlRung0Audit prior, bool priorPromote) = _rung0AuditDebt[i];
                if (!ReferenceEquals(priorStore, store) || prior.ProofDigest != audit.ProofDigest) continue;
                if (prior.Status != audit.Status
                    || prior.EvaluatorCalls != audit.EvaluatorCalls
                    || prior.NumericVerified != audit.NumericVerified
                    || prior.GuardVerified != audit.GuardVerified
                    || prior.Selection != audit.Selection
                    || !prior.Rules.SequenceEqual(audit.Rules))
                    throw new InvalidDataException("speculative rung-0 audit debt disagrees for one proof");
                if (priorPromote != promoteRetained)
                    throw new InvalidDataException("speculative rung-0 audit debt disagrees on promotion");
                return;
            }
            if (!promoteRetained && store.TryGetRung0Audit(audit.ProofDigest, out EmlRung0Audit retained))
            {
                if (retained.Equals(audit)) return;
                throw new InvalidDataException("speculative rung-0 audit mutation is not a typed promotion");
            }
            _rung0AuditDebt.Add((store, audit, promoteRetained));
        }
        internal void RecordCAS(EmlCert cert)
        {
            if (!_cas.ContainsKey(cert))
                _cas[cert] = _owner._cas.Classes.TryGetValue(cert, out SemanticCASClass<string> old) ? old : null;
        }
        internal void RecordMinted(string key) => _minted.Add(key);
        internal void RecordResidualProofKey(string key) => _residualProofKeys.Add(key);
        internal void RecordProcessResidualProofKey(string key) => _processResidualProofKeys.Add(key);
        internal void RecordComposedFormProofKey(string key) => _derivedFormProofKeys.Add(key);
        internal void RecordProcessProofWireVersion(string key) => _processProofWireVersions.Add(key);
        internal void RecordHeldCaptured(int index)
        {
            if (!_owner._heldCaptured.Contains(index)) _heldCaptured.Add(index);
        }
        internal void RecordBestTarget(int index)
        {
            if (_bestTargetSeen.Add(index)) _bestTargets.Add((index, _owner._bestK[index], _owner._bestProg[index]));
        }

        internal void Commit()
        {
            for (int i = 0; i < _rung0AuditDebt.Count; i++)
            {
                (EmlLawStore store, EmlRung0Audit audit, bool promote) = _rung0AuditDebt[i];
                if (promote) store.PromoteRung0Audit(in audit);
                else store.RecordRung0Audit(in audit);
                PublishedRung0Audits++;
            }
            Complete();
            _committedEvaluatorCalls = _previewEvaluatorCalls;
            _commitWallTicks = Stopwatch.GetTimestamp() - _startedAt - _previewWallTicks;
            _owner._grader.CommitSpeculativeCache();
            _owner._activeSpeculativeTransaction = null;
        }

        internal void Rollback()
        {
            if (_completed) return;
            long operationEnd = Stopwatch.GetTimestamp();
            _previewWallTicks = operationEnd - _startedAt;
            _owner._newMints.Clear();
            _owner._newMints.AddRange(_newMintPrefix);
            Truncate(_owner._mintLog, _mintLog);
            Truncate(_owner._exactRPNForms, _exactRPNForms);
            Truncate(_owner._exactRPNLhsForms, _exactRPNLhsForms);
            Truncate(_owner._lawPredictionTrees, _lawPredictionTrees);
            Truncate(_owner._mintOpportunityEvents, _mintLog);
            Truncate(_owner._mintCerts, _mintCerts);
            Truncate(_owner._mintAdmissions, _mintAdmissions);
            Truncate(_owner._obligations, _obligations);
            Truncate(_owner._obligationClosures, _closures);
            Truncate(_owner._residualProofs, _residualProofs);
            Truncate(_owner._processResidualProofs, _processProofs);
            Truncate(_owner._derivedFormProofs, _derivedProofs);
            _owner._newSemanticDeltas.Clear();
            _owner._newSemanticDeltas.AddRange(_semanticDeltaPrefix);
            foreach ((int index, int bestK, string? bestProgram) in _bestTargets)
            {
                _owner._bestK[index] = bestK;
                _owner._bestProg[index] = bestProgram!;
            }
            foreach ((EmlSig key, string? old) in _canon) RestoreString(_owner._canon, key, old);
            foreach ((EmlSig key, int? old) in _sigHits) RestoreInt(_owner._sigHits, key, old);
            foreach ((int key, int? old) in _discByLen) RestoreInt(_owner._discByLen, key, old);
            foreach ((EmlSig key, int? old) in _obligationByResidual) RestoreInt(_owner._obligationByResidual, key, old);
            foreach ((EmlPredictionID key, int? old) in _obligationBySource) RestoreInt(_owner._obligationBySource, key, old);
            foreach ((EmlPredictionID key, int? old) in _exactCompositionBySource) RestoreInt(_owner._exactCompositionBySource, key, old);
            foreach ((EmlMint key, EmlPredictionID? old) in _claimByMint) RestorePrediction(_owner._claimByMint, key, old);
            foreach ((EmlPredictionID key, EmlExactRPNForm? old) in _exactRPNLhsByPrediction) RestoreForm(_owner._exactRPNLhsByPrediction, key, old);
            foreach ((string key, EmlExactRPNForm? old) in _exactRPNLhsByProgram) RestoreForm(_owner._exactRPNLhsByProgram, key, old);
            foreach (((string Program, EmlCert Certificate) key, EmlExactRPNForm? old) in _exactRPNLhsByProgramAndCertificate) RestoreForm(_owner._exactRPNLhsByProgramAndCertificate, key, old);
            foreach ((string key, int? old) in _closureKeys) RestoreInt(_owner._obligationClosureKeys, key, old);
            foreach ((EmlCert cert, SemanticCASClass<string>? old) in _cas)
            {
                if (old is SemanticCASClass<string> value) _owner._cas.Set(cert, value);
                else _owner._cas.Remove(cert);
            }
            foreach (string key in _minted) _owner._minted.Remove(key);
            foreach (string key in _residualProofKeys) _owner._residualProofKeys.Remove(key);
            foreach (string key in _processResidualProofKeys) _owner._processResidualProofKeys.Remove(key);
            foreach (string key in _derivedFormProofKeys) _owner._derivedFormProofKeys.Remove(key);
            foreach (string key in _processProofWireVersions) _owner._processProofWireVersions.Remove(key);
            foreach (int index in _heldCaptured) _owner._heldCaptured.Remove(index);
            _owner.TheoremClasses = _theoremClasses;
            _owner.ExactClasses = _exactClasses;
            _owner.Identities = _identities;
            _owner.ValueHits = _valueHits;
            _owner.KFrontier = _kFrontier;
            _owner.FiniteOffers = _finiteOffers;
            _owner._exactRPNPredictionCount = _exactRPNPredictionCount;
            Array.Clear(_owner._gradeCounts);
            for (int i = 0; i < _owner._mintLog.Count; i++) _owner._gradeCounts[GradeIdx(_owner._mintLog[i].Grade)]++;
            _owner._deliberationJournal.RollbackTo(_admissions, _phases, _settlements);
            _owner._evaluatorClock.Restore(in _clock);
            _owner._grader.EndSpeculativeCache();
            _rollbackWallTicks = Stopwatch.GetTimestamp() - operationEnd;
            _owner._activeSpeculativeTransaction = null;
            _completed = true;
        }

        public void Dispose() { if (!_completed) Rollback(); }

        private void Complete() { _previewWallTicks = Stopwatch.GetTimestamp() - _startedAt; _completed = true; }

        private static void Truncate<T>(List<T> list, int count)
        {
            if (list.Count < count) throw new InvalidOperationException("speculative transaction list rewound before its boundary");
            if (list.Count > count) list.RemoveRange(count, list.Count - count);
        }

        private static void RestoreString<TKey>(Dictionary<TKey, string> dictionary, TKey key, string? old)
            where TKey : notnull
        {
            if (old is null) dictionary.Remove(key); else dictionary[key] = old;
        }

        private static void RestoreInt<TKey>(Dictionary<TKey, int> dictionary, TKey key, int? old)
            where TKey : notnull
        {
            if (old is null) dictionary.Remove(key); else dictionary[key] = old.Value;
        }

        private static void RestorePrediction(Dictionary<EmlMint, EmlPredictionID> dictionary, EmlMint key, EmlPredictionID? old)
        {
            if (old is null) dictionary.Remove(key); else dictionary[key] = old.Value;
        }

        private static void RestoreForm<TKey>(Dictionary<TKey, EmlExactRPNForm> dictionary, TKey key, EmlExactRPNForm? old)
            where TKey : notnull
        {
            if (old is null) dictionary.Remove(key); else dictionary[key] = old.Value;
        }
    }

    internal void RestoreAdmissionState(byte[] image, in EmlEvaluatorClockSnapshot consumedClock)
    {
        using MemoryStream stream = new(image, writable: false);
        using (CkptReader reader = new(stream)) Load(reader);
        _evaluatorClock.Restore(in consumedClock, writesCheckpoint: true);
    }

    /// Offer one candidate RPN program to the sieve — the single point where a float value becomes a discrete
    /// discovery. Evaluates at both probe points; if finite at both, recognizes any calculator target (bench +
    /// value-hit mint), any registered anomaly (the self-generated bench), and registers/relates the value against
    /// the canonical table (identity mint). Every fresh mint is GRADED live by the witness ladder (the grade-gate:
    /// `=` exact vs `~` non-exact rides in the line's alphabet; an A-grade's correction registers a new anomaly
    /// target). THE BENCH IS E-WITNESSED ONLY: a target's bestK/hit census counts a program iff the ladder graded
    /// its value-hit EXACT — an absorbed impostor landing on a target's sig rides `~` and must never claim the
    /// paper's table (a live K=13 "inv BEAT" was exactly such pollution: sig-recognized, ladder-refuted).
    /// No-op for an invalid or already-seen program.
    public void Offer(string prog, in EmlOfferContext context)
    {
        _pendingOpportunityEvents = context.OpportunityEventIDs ?? Array.Empty<TapeEventID>();
        try { OfferCore(prog); }
        finally { _pendingOpportunityEvents = Array.Empty<TapeEventID>(); }
    }

    public void Offer(string prog) => Offer(prog, default);

    private void OfferCore(string prog)
    {
        _evaluatorClock.RecordOfferRequest();
        _evaluatorClock.RecordOfferProgramPointEvaluation();
        var p1 = Eml.Eval(prog, P1x, P1y);
        _evaluatorClock.RecordOfferProgramPointEvaluation();
        var p2 = Eml.Eval(prog, P2x, P2y);
        if (!p1.Finite || !p2.Finite) return;                            // overflow / NaN / malformed → not a witness
        var sig = Eml.Signature(p1, p2, _sig);
        FiniteOffers++;
        _activeSpeculativeTransaction?.RecordSigHit(sig);
        _sigHits[sig] = _sigHits.GetValueOrDefault(sig) + 1;             // basin size under the CURRENT policy (rarity read)
        bool discovered = false;

        // TARGET recognition — the bench + the `expr = LABEL` value-hit. `corrob` marks NOVEL corroboration
        // (Weitzman: the reward pays the FIRST E-witness of a target, never the re-visit — bestK<0 ⟺ un-witnessed).
        if (_targetBySig.TryGetValue(sig, out var hits))
            foreach (int t in hits)
            {
                bool held = _isHeld is not null && _isHeld[t];
                // a HELD target never enters the train census (its bestK stays −1 so TargetsHit/bench columns are
                // train-only), but its FIRST E-capture is tallied for the generalization read. corrob stays false
                // on a held target — the second-witness reward is a TRAIN-census concept (a held target is invisible
                // to the generator's reward, exactly the blindness the holdout tests).
                char g = Mint(prog, _targets[t].Label, sig, corrob: !held && _bestK[t] < 0, () => _grader.GradeRef(prog, _targets[t].Ref));
                if (g == '\0') continue;
                ValueHits++; discovered = true;
                if (g == 'E')
                {
                    if (held)
                    {
                        _activeSpeculativeTransaction?.RecordHeldCaptured(t);
                        _heldCaptured.Add(t);                              // the generalization tally — reached a held target's value
                    }
                    else if (_bestK[t] < 0 || prog.Length < _bestK[t])
                    {
                        _activeSpeculativeTransaction?.RecordBestTarget(t);
                        _bestK[t] = prog.Length; _bestProg[t] = prog;
                    }
                }
            }

        // canonical registration + IDENTITY mint (`lhs = rhs` / `lhs ~ rhs`, both pure RPN)
        if (_canon.TryGetValue(sig, out var canon))
        {
            if (prog != canon && Mint(prog, canon, sig, corrob: false, () => _grader.GradeRpn(prog, canon)) != '\0') { Identities++; discovered = true; }
            if (prog.Length < canon.Length)
            {
                _activeSpeculativeTransaction?.RecordCanon(sig);
                _canon[sig] = prog;                                      // keep the shortest as the anchor
            }
        }
        else
        {
            _activeSpeculativeTransaction?.RecordCanon(sig);
            _canon[sig] = prog; discovered = true;                        // a new value reached is itself a discovery
        }

        if (discovered)
        {
            KFrontier = Math.Max(KFrontier, prog.Length);
            _activeSpeculativeTransaction?.RecordDiscByLen(prog.Length);
            _discByLen[prog.Length] = _discByLen.GetValueOrDefault(prog.Length) + 1;
        }
    }

    /// Seed one explicit synthetic asymptotic claim for structural-process assays. The claim
    /// enters the same mint, certificate, obligation, and checkpoint paths as a live A-grade;
    /// its label is intentionally outside the corpus so no live closure credit can attach.
    internal EmlPredictionID SeedSyntheticObligation(string lhs, string rhs)
    {
        EmlVerdict verdict = _grader.GradeRpn(lhs, rhs);
        if (verdict.Grade != 'A')
            throw new InvalidDataException($"synthetic obligation did not grade asymptotic: {lhs} ~ {rhs}");
        EmlSig signature = Eml.Signature(new EmlValue(verdict.Rhs1, true), new EmlValue(verdict.Rhs2, true), _sig);
        if (Mint(lhs, rhs, signature, corrob: false, () => verdict) != 'A')
            throw new InvalidDataException("synthetic obligation mint was deduplicated or changed grade");
        return new EmlPredictionID(_mintLog.Count - 1);
    }

    /// THE GRADE-GATE — the single minting point. De-dup FIRST on the grade-independent (prog, rhs) pre-key (the
    /// ladder runs once per unique mint), then grade, then land the line with its grade byte in the alphabet:
    /// ` = ` for EXACT, ` ~ ` for everything else (both 3 bytes, so line-offset parsers hold) — the grammar chunks
    /// the difference and rewrite-safety is grammatical. An A-grade's correction value registers a new anomaly
    /// target the same instant (the machine writes its own next bench from its own incompleteness). Returns the
    /// minted grade, or '\0' when deduped — the caller's census updates hang off the grade.
    private char Mint(string prog, string rhs, EmlSig sig, bool corrob, Func<EmlVerdict> grade)
    {
        string mintKey = prog + "\u0001" + rhs;
        if (!_minted.Add(mintKey)) return '\0';                           // already discovered — the corpus stays de-duped
        _activeSpeculativeTransaction?.RecordMinted(mintKey);
        var v = grade();
        string line = $"{prog} {(v.Grade == 'E' ? '=' : '~')} {rhs}";
        var m = new EmlMint(line, prog, sig, v.Grade, corrob);
        _newMints.Add(m);
        _mintLog.Add(m);
        IReadOnlyList<TapeEventID> opportunities = _pendingOpportunityEvents.Count == 0
            ? Array.Empty<TapeEventID>()
            : _pendingOpportunityEvents.Distinct().OrderBy(static id => id.Value).ToArray();
        _mintOpportunityEvents.Add(opportunities);
        _gradeCounts[GradeIdx(v.Grade)]++;
        RegisterCert(EmlCert.Of(in v, _sig), prog, rhs);           // the theorem CAS — the same verdict, zero extra ladder cost
        IndexMint(new EmlPredictionID(_mintLog.Count - 1), m, _mintCerts[^1]);
        if (v.Grade == 'A') RegisterObligation(new EmlPredictionID(_mintLog.Count - 1), in v, _pendingOpportunityEvents);
        return v.Grade;
    }

    private void IndexMint(EmlPredictionID claimID, EmlMint mint, EmlCert certificate)
    {
        _activeSpeculativeTransaction?.RecordMintPrediction(mint);
        if (!_claimByMint.TryAdd(mint, claimID))
            throw new InvalidDataException($"EML mint journal repeats mint value at claim {claimID.Value}");
        if (mint.Grade != 'E' || certificate.Grade != 'E') return;
        if (!EmlPrediction.TryParse(mint.Line, out EmlPrediction claim))
        {
            // Exact indexes are derived state, but a journal marked exact must still
            // be structurally readable. Refuse the write instead of creating a
            // partial index that would make later law custody silently disappear.
            throw new InvalidDataException($"EML exact mint is not a claim: {mint.Line}");
        }
        if (!claim.RhsRpn) return;
        string sourceDigest = Digest(mint.Line);
        EmlExactRPNForm lhs = new(claim.Lhs, certificate, claimID, sourceDigest);
        EmlExactRPNForm rhs = new(claim.Rhs, certificate, claimID, sourceDigest);
        _activeSpeculativeTransaction?.RecordExactRpnPrediction(claimID);
        _activeSpeculativeTransaction?.RecordExactRpnProgram(lhs.Program);
        _activeSpeculativeTransaction?.RecordExactRpnProgramAndCertificate((lhs.Program, lhs.Certificate));
        if (!_exactRPNLhsByPrediction.TryAdd(claimID, lhs))
            throw new InvalidDataException($"EML exact index repeats claim {claimID.Value}");
        _exactRPNLhsForms.Add(lhs);
        _exactRPNForms.Add(lhs);
        _exactRPNForms.Add(rhs);
        _exactRPNLhsByProgram.TryAdd(lhs.Program, lhs);
        _exactRPNLhsByProgramAndCertificate.TryAdd((lhs.Program, lhs.Certificate), lhs);
        _exactRPNPredictionCount = checked(_exactRPNPredictionCount + 1);
        if (EmlAntiUnify.CreatePredictionTree(certificate, claim.Lhs, claim.Rhs, claimID) is { } tree)
            _lawPredictionTrees.Add(tree);
    }

    private void RebuildExactRPNForms()
    {
        _exactRPNForms.Clear();
        _exactRPNLhsForms.Clear();
        _exactRPNLhsByPrediction.Clear();
        _exactRPNLhsByProgram.Clear();
        _exactRPNLhsByProgramAndCertificate.Clear();
        _lawPredictionTrees.Clear();
        _claimByMint.Clear();
        _exactRPNPredictionCount = 0;
        if (_mintCerts.Count != _mintLog.Count)
            throw new InvalidDataException("EML exact index rebuild requires mint certificates parallel to the mint journal");
        for (int i = 0; i < _mintLog.Count; i++)
            IndexMint(new EmlPredictionID(i), _mintLog[i], _mintCerts[i]);
    }

    // admit a mint into the theorem CAS; the live sieve and retro audit share this representative law.
    private void RegisterCert(EmlCert cert, string prog, string rhs)
    {
        _activeSpeculativeTransaction?.RecordCAS(cert);
        _mintCerts.Add(cert);
        if (cert.Grade is 'E' or 'A' && !_cas.Contains(cert))                     // a new theorem-class opens — the census grows
        {
            TheoremClasses++;
            if (cert.Grade == 'E') ExactClasses++;
        }
        SemanticCASAdmission<EmlCert, string> admission = _cas.Admit(
            cert,
            CertRepresentative(cert, prog, rhs),
            _mintCerts.Count - 1);
        _mintAdmissions.Add(admission);
    }

    /// The label→reference chart — every rhs label a mint line can carry (the paper bench + the classic atlas;
    /// anomaly `corr:` labels resolve from their own text, not from here). The retro graders' resolver: the
    /// checkpoint CAS-rebuild, the regrade census, and the semantic audit all dispatch labels through this one map.
    public static Dictionary<string, Func<Complex, Complex, Complex>> LabelChart()
        => BuildPredictionReferences(BuildTargets());

    private static Dictionary<string, Func<Complex, Complex, Complex>> BuildPredictionReferences(EmlTarget[] catalog)
    {
        Dictionary<string, Func<Complex, Complex, Complex>> refs = new(StringComparer.Ordinal);
        foreach (EmlTarget target in catalog) refs.TryAdd(target.Label, target.Ref);
        foreach ((string label, Func<Complex, Complex, Complex> reference) in EmlAtlas.Entries)
            refs.TryAdd(label, reference);
        return refs;
    }

    /// Register an asymptotic mint's correction value as a recognition target. Only the source claim address enters
    /// the durable register; the verdict supplies the derived lookup entry without repeating the ladder at mint time.
    /// Skips numerically-degenerate corrections (≈0 = below the witness, non-finite), values already NAMED (a bench
    /// target or an existing obligation on the same sig), and everything past the registry cap (first-come, mint order).
    private void RegisterObligation(EmlPredictionID sourcePredictionID, in EmlVerdict verdict, IReadOnlyList<TapeEventID> opportunityEvents)
    {
        Complex corr = verdict.Corr3;
        if (_obligations.Count + _exactCompositionObligations.Count >= ObligationCap) return;
        if (!double.IsFinite(corr.Real) || !double.IsFinite(corr.Imaginary) || Complex.Abs(corr) < 1e-12) return;
        EmlObligationResolution resolution = ResolveObligation(new EmlObligation(sourcePredictionID, "pending"), _claimReferences);
        IReadOnlyList<TapeEventID> copiedOpportunities = opportunityEvents.Count == 0
            ? Array.Empty<TapeEventID>()
            : opportunityEvents.Distinct().OrderBy(static id => id.Value).ToArray();
        EmlObligation obligation = new(sourcePredictionID, ComputeObligationIdentity(sourcePredictionID, resolution), copiedOpportunities);
        if (_obligationByResidual.ContainsKey(resolution.ResidualSignature)) return;
        _activeSpeculativeTransaction?.RecordObligationResidual(resolution.ResidualSignature);
        _activeSpeculativeTransaction?.RecordObligationSource(sourcePredictionID);
        if (!_obligationBySource.TryAdd(sourcePredictionID, _obligations.Count))
            throw new InvalidDataException($"EML obligation source {sourcePredictionID.Value} is already registered");
        _obligationByResidual.Add(resolution.ResidualSignature, _obligations.Count);
        _obligations.Add(obligation);
        _obligationOpportunityEvents[sourcePredictionID] = copiedOpportunities;
    }

    /// Open the exact theorem-use queue for one real E-grade carrier claim.
    /// Callers must have already found a guarded rank-reducing rewrite; this
    /// method only records the authoritative source/world binding and refuses
    /// derived-form output mints so the queue cannot recursively self-feed.
    internal bool RegisterExactCompositionObligation(
        EmlPredictionID sourcePredictionID,
        IReadOnlyList<TapeEventID> supportEventIDs,
        TapeEventID mintEventID)
    {
        if (_exactCompositionBySource.ContainsKey(sourcePredictionID)) return false;
        if (_obligations.Count + _exactCompositionObligations.Count >= ObligationCap) return false;
        int sourceIndex = sourcePredictionID.Value;
        if ((uint)sourceIndex >= (uint)_mintLog.Count || sourceIndex >= _mintCerts.Count) return false;
        if (supportEventIDs is null || supportEventIDs.Count == 0 || mintEventID.Value < 0) return false;
        if (!_claimMintEvents.TryGetValue(sourcePredictionID, out TapeEventID boundMintEvent) || boundMintEvent != mintEventID) return false;
        if (_derivedFormProofs.Any(proof => proof.ComposedPredictionID == sourcePredictionID)) return false;
        EmlMint sourceMint = _mintLog[sourceIndex];
        if (sourceMint.Grade != 'E' || !EmlPrediction.TryParse(sourceMint.Line, out EmlPrediction sourcePrediction)
            || !sourcePrediction.RhsRpn || _mintCerts[sourceIndex].Grade != 'E') return false;
        TapeEventID[] supports = supportEventIDs.Distinct().OrderBy(static id => id.Value).ToArray();
        if (supports.Length == 0 || supports.Length != supportEventIDs.Count || supports.Any(static id => id.Value < 0)) return false;
        if ((uint)sourceIndex >= (uint)_mintOpportunityEvents.Count
            || !supports.SequenceEqual(_mintOpportunityEvents[sourceIndex])) return false;
        string sourceDigest = Digest(sourceMint.Line);
        string carrierRPN = sourcePrediction.Lhs;
        string identity = ComputeExactCompositionIdentity(sourcePredictionID, sourceDigest, carrierRPN, _mintCerts[sourceIndex], supports, mintEventID);
        EmlExactCompositionObligation obligation = new(
            sourcePredictionID, identity, sourceDigest, carrierRPN, _mintCerts[sourceIndex], supports, mintEventID);
        _activeSpeculativeTransaction?.RecordExactCompositionSource(sourcePredictionID);
        _exactCompositionBySource.Add(sourcePredictionID, _exactCompositionObligations.Count);
        _exactCompositionObligations.Add(obligation);
        return true;
    }

    private static string ComputeExactCompositionIdentity(
        EmlPredictionID sourcePredictionID,
        string sourceDigest,
        string carrierRPN,
        EmlCert sourceCertificate,
        IReadOnlyList<TapeEventID> supports,
        TapeEventID? mintEventID)
        => Digest(string.Join('|',
            "exact-derivation", sourcePredictionID.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sourceDigest, carrierRPN, sourceCertificate.Hex(),
            string.Join(',', supports.Select(static id => id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            mintEventID?.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"));

    private static int GradeIdx(char g) => g switch { 'E' => 0, 'A' => 1, 'S' => 2, 'D' => 3, _ => 4 };

    // ── CHECKPOINT ──  the whole discovery journal (the trunk mounts ReplayCalc/Campfire, so the sieve rides the
    // CURR section). The structural half (_targets/_targetBySig) is ctor-rebuilt; everything accreted by Offer is
    // serialized. Dictionaries and the dedupe set are written KEY-SORTED (EmlSig by its four packed longs, keys
    // by ordinal) so a reloaded sieve re-saves byte-identically — Save∘Load∘Save = identity, the Vow. The mint
    // log and _newMints keep their own order (mint order IS state); the grade census is recomputed from the mint
    // log on Load (derived, never stored); the grader's ladder cache is a pure recompute (never stored).
    public void Save(CkptWriter w)
    {
        w.I32(Identities); w.I32(ValueHits); w.I32(KFrontier); w.I64(FiniteOffers);
        w.I32(AnomalyHits);
        w.I32(_bestK.Length);
        for (int i = 0; i < _bestK.Length; i++) { w.I32(_bestK[i]); if (_bestK[i] >= 0) w.Str(_bestProg[i]); }
        w.I32(_canon.Count);
        foreach (var k in SortedSigs(_canon.Keys)) { WriteSig(w, k); w.Str(_canon[k]); }
        w.I32(_sigHits.Count);
        foreach (var k in SortedSigs(_sigHits.Keys)) { WriteSig(w, k); w.I32(_sigHits[k]); }
        w.I32(_minted.Count);
        foreach (var s in _minted.Order(StringComparer.Ordinal)) w.Str(s);
        w.I32(_discByLen.Count);
        foreach (var k in _discByLen.Keys.Order()) { w.I32(k); w.I32(_discByLen[k]); }
        w.I32(_mintLog.Count);
        foreach (var m in _mintLog) { w.Str(m.Line); w.Str(m.Prog); WriteSig(w, m.Sig); w.U8((byte)m.Grade); w.Bool(m.Corrob); }
        w.Section(MintOpportunityTag);
        w.I32(_mintOpportunityEvents.Count);
        foreach (IReadOnlyList<TapeEventID> events in _mintOpportunityEvents)
        {
            w.I32(events.Count);
            for (int i = 0; i < events.Count; i++) w.I64(events[i].Value);
        }
        w.Section(MintPredictionEventTag);
        w.I32(_claimMintEvents.Count);
        foreach ((EmlPredictionID claimID, TapeEventID eventID) in _claimMintEvents.OrderBy(static pair => pair.Key.Value))
        {
            w.I32(claimID.Value); w.I64(eventID.Value);
        }
        w.Section(ComposedPredictionEventTag);
        w.I32(_claimCompositionEvents.Count);
        foreach ((EmlPredictionID claimID, TapeEventID eventID) in _claimCompositionEvents.OrderBy(static pair => pair.Key.Value))
        {
            w.I32(claimID.Value); w.I64(eventID.Value);
        }
        w.Section(ObligationClosureTag);
        w.I32(8); // v8 persists claim-to-mint event custody for exact target validation
        w.I32(_obligations.Count);
        for (int i = 0; i < _obligations.Count; i++)
        {
            EmlObligation obligation = _obligations[i];
            w.I32(obligation.SourcePredictionID.Value);
            w.Str(obligation.Identity);
            IReadOnlyList<TapeEventID> opportunityEvents = obligation.OpportunityEventIDs ?? Array.Empty<TapeEventID>();
            w.I32(opportunityEvents.Count);
            for (int j = 0; j < opportunityEvents.Count; j++) w.I64(opportunityEvents[j].Value);
            w.Bool(obligation.MintEventID.HasValue);
            if (obligation.MintEventID is TapeEventID mintEventID) w.I64(mintEventID.Value);
        }
        w.I32(_exactCompositionObligations.Count);
        for (int i = 0; i < _exactCompositionObligations.Count; i++)
        {
            EmlExactCompositionObligation target = _exactCompositionObligations[i];
            w.I32(target.SourcePredictionID.Value); w.Str(target.Identity); w.Str(target.SourceDigest);
            w.Str(target.CarrierRPN); WriteCert(w, target.SourceCertificate);
            IReadOnlyList<TapeEventID> supports = target.Supports;
            w.I32(supports.Count);
            for (int j = 0; j < supports.Count; j++) w.I64(supports[j].Value);
            w.Bool(target.MintEventID.HasValue);
            if (target.MintEventID is TapeEventID mintEventID) w.I64(mintEventID.Value);
        }
        w.I32(_obligationClosures.Count);
        for (int i = 0; i < _obligationClosures.Count; i++)
        {
            EmlObligationClosure closure = _obligationClosures[i];
            w.I32(closure.SourcePredictionID.Value);
            w.Str(closure.ObligationID); w.Str(closure.AttemptID); w.Str(closure.AttachmentID);
            w.U8((byte)closure.Status); w.Str(closure.SourceDigest); w.U8((byte)closure.Kind);
            w.Bool(closure.FinitePolicy.HasValue);
            if (closure.FinitePolicy is EmlFiniteObligationProofPolicy finitePolicy)
            {
                w.I32(finitePolicy.SignatureDigits); w.I32(finitePolicy.WitnessVersion); w.Str(finitePolicy.VerifierRevision);
            }
            w.Bool(closure.ProcessPolicy.HasValue);
            if (closure.ProcessPolicy is EmlProcessObligationProofPolicy processPolicy)
            {
                w.I32(processPolicy.SignatureDigits); w.I64(processPolicy.FuelPerProbe); w.I32(processPolicy.ProbeCount);
                w.I32(processPolicy.FunctionVersion); w.I32(processPolicy.CompositionVersion); w.Str(processPolicy.VerifierRevision);
            }
            w.Bool(closure.FiniteEvidence.HasValue);
            if (closure.FiniteEvidence is EmlFiniteObligationProofEvidence finiteEvidence)
            {
                w.I64(finiteEvidence.Evaluator.Start); w.I64(finiteEvidence.Evaluator.End); w.I64(finiteEvidence.WallTicks);
                w.Str(finiteEvidence.CandidateDigest); w.Str(finiteEvidence.AttachmentDigest); WriteCert(w, finiteEvidence.Before); WriteCert(w, finiteEvidence.After);
            }
            w.Bool(closure.ProcessEvidence.HasValue);
            if (closure.ProcessEvidence is EmlProcessObligationProofEvidence processEvidence)
            {
                w.I64(processEvidence.Evaluator.Start); w.I64(processEvidence.Evaluator.End); w.I64(processEvidence.WallTicks);
                w.I64(processEvidence.FuelPerProbe); w.I64(processEvidence.FuelTotal); w.Str(processEvidence.CandidateDigest);
                w.Str(processEvidence.AttachmentDigest); w.Str(processEvidence.CertificateDigest); WriteCert(w, processEvidence.Before); WriteCert(w, processEvidence.After);
            }
            w.Str(closure.Reason);
            w.U8((byte)closure.Species);
            w.Bool(closure.Rung0ComposedFormEvidence.HasValue);
            if (closure.Rung0ComposedFormEvidence is EmlRung0ComposedFormObligationEvidence rung0Evidence)
            {
                w.I32(rung0Evidence.ObligationPredictionID.Value); w.Str(rung0Evidence.ObligationID);
                w.I32(rung0Evidence.ComposedPredictionID.Value); w.Str(rung0Evidence.LhsRPN); w.Str(rung0Evidence.RhsRPN);
                w.Str(rung0Evidence.GuardPackageDigest); w.Str(rung0Evidence.ProofID); w.Str(rung0Evidence.AuditID);
                w.Str(rung0Evidence.ProofSHA256); w.Str(rung0Evidence.AuditSHA256);
                w.Str(rung0Evidence.AdmissionID); w.Str(rung0Evidence.ClosureID); w.Str(rung0Evidence.Comparator);
                w.I64(rung0Evidence.Evaluator.Start); w.I64(rung0Evidence.Evaluator.End);
                w.I64(rung0Evidence.ComparatorEvaluation.Start); w.I64(rung0Evidence.ComparatorEvaluation.End);
                w.Str(rung0Evidence.CandidateDigest); w.Str(rung0Evidence.AttachmentDigest);
                WriteCert(w, rung0Evidence.Before); WriteCert(w, rung0Evidence.After);
                w.Str(rung0Evidence.AdmissionPathCanonical); w.Str(rung0Evidence.AdmissionPathFingerprint);
            }
        }
        _deliberationJournal.Save(w);
        w.Section(ResidualProofTag);
        w.I32(_residualProofs.Count);
        for (int i = 0; i < _residualProofs.Count; i++)
        {
            EmlResidualProof proof = _residualProofs[i];
            w.I32(proof.SourcePredictionID.Value);
            w.Str(proof.Program);
            w.U8((byte)proof.Certificate.Grade);
            WriteSig(w, proof.Certificate.Limit);
            w.I64(proof.Certificate.RateRe);
            w.I64(proof.Certificate.RateIm);
        }
        w.Section(ProcessResidualProofTag);
        w.I32(_processResidualProofs.Count);
        for (int i = 0; i < _processResidualProofs.Count; i++)
        {
            EmlProcessResidualProof proof = _processResidualProofs[i];
            string proofKey = proof.SourcePredictionID.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "\u0001" + proof.Digest;
            int wireVersion = _processProofWireVersions.GetValueOrDefault(proofKey, EmlProcessFunctions.AlgorithmVersion);
            w.I32(proof.SourcePredictionID.Value);
            w.I32((int)proof.Function.Algorithm);
            w.I32(wireVersion);
            if (wireVersion == 1)
            {
                EmlProcessInputSlots input = proof.Function.DenominatorRPN switch
                {
                    "x" => EmlProcessInputSlots.X,
                    "y" => EmlProcessInputSlots.Y,
                    _ => throw new InvalidDataException("legacy v1 process proof does not identify an input slot"),
                };
                w.I32((int)input);
                w.I64(proof.Function.Fuel);
            }
            else
            {
                w.Str(proof.Function.NumeratorRPN);
                w.Str(proof.Function.DenominatorRPN);
                w.I64(proof.Function.Fuel);
                w.Bool(proof.CompositionLaw is not null);
                if (proof.CompositionLaw is EmlResidualCompositionLaws derivationLaw) w.I32((int)derivationLaw);
            }
            w.Str(proof.Digest);
            w.U8((byte)proof.Certificate.Grade);
            WriteSig(w, proof.Certificate.Limit);
            w.I64(proof.Certificate.RateRe);
            w.I64(proof.Certificate.RateIm);
            w.I64(proof.ProcessFuel);
        }
        w.Section(ComposedFormProofTagV2);
        w.I32(_derivedFormProofs.Count);
        for (int i = 0; i < _derivedFormProofs.Count; i++)
        {
            EmlComposedFormProof proof = _derivedFormProofs[i];
            w.I32(proof.SourcePredictionID.Value);
            w.I32(proof.ComposedPredictionID.Value);
            w.Str(proof.Program);
            WriteCert(w, proof.Certificate);
            EmlRung0Checkpoint.WriteProof(w, proof.Proof);
            EmlRung0Checkpoint.WriteAudit(w, proof.Audit);
        }
        w.I32(_newMints.Count);                                          // drained within every Draw, so 0 at any trunk checkpoint — serialized for shape-faithfulness
        foreach (var m in _newMints) { w.Str(m.Line); w.Str(m.Prog); WriteSig(w, m.Sig); w.U8((byte)m.Grade); w.Bool(m.Corrob); }
        _evaluatorClock.Save(w);
    }

    public void Load(CkptReader r)
    {
        Identities = r.I32(); ValueHits = r.I32(); KFrontier = r.I32(); FiniteOffers = r.I64();
        int savedAnomalyHits = r.I32();
        _heldCaptured.Clear();   // derived (emlbench-only; never checkpointed) — reset like the CAS/grade census below
        int nt = r.I32();
        if (nt != _bestK.Length) throw new InvalidDataException($"EmlSieve checkpoint skew: {nt} bench targets checkpointed, {_bestK.Length} rebuilt — the paper table changed under a live run");
        for (int i = 0; i < nt; i++) { _bestK[i] = r.I32(); _bestProg[i] = _bestK[i] >= 0 ? r.Str() : null!; }
        _canon.Clear();
        int nc = r.I32();
        for (int i = 0; i < nc; i++) { var k = ReadSig(r); _canon[k] = r.Str(); }
        _sigHits.Clear();
        int nh = r.I32();
        for (int i = 0; i < nh; i++) { var k = ReadSig(r); _sigHits[k] = r.I32(); }
        _minted.Clear();
        int nm = r.I32();
        for (int i = 0; i < nm; i++) _minted.Add(r.Str());
        _discByLen.Clear();
        int nd = r.I32();
        for (int i = 0; i < nd; i++) { int k = r.I32(); _discByLen[k] = r.I32(); }
        _mintLog.Clear();
        _mintOpportunityEvents.Clear();
        _claimMintEvents.Clear();
        _claimCompositionEvents.Clear();
        _claimByMint.Clear();
        _exactRPNForms.Clear();
        _exactRPNLhsForms.Clear();
        _exactRPNLhsByPrediction.Clear();
        _exactRPNLhsByProgram.Clear();
        _exactRPNLhsByProgramAndCertificate.Clear();
        _exactRPNPredictionCount = 0;
        Array.Clear(_gradeCounts);
        int nl = r.I32();
        for (int i = 0; i < nl; i++)
        {
            var m = new EmlMint(r.Str(), r.Str(), ReadSig(r), (char)r.U8(), r.Bool());
            _mintLog.Add(m);
            _mintOpportunityEvents.Add(Array.Empty<TapeEventID>());
            _gradeCounts[GradeIdx(m.Grade)]++;                           // the census is derived — recount, never trust a stored copy
        }
        if (r.TryExpect(MintOpportunityTag))
        {
            int count = r.I32();
            if (count != _mintLog.Count) throw new InvalidDataException("EML mint opportunity section is not parallel to the mint journal");
            for (int i = 0; i < count; i++)
            {
                int n = r.I32();
                if (n < 0 || n > 1024) throw new InvalidDataException("EML mint opportunity event count is invalid");
                TapeEventID[] events = new TapeEventID[n];
                for (int j = 0; j < n; j++) events[j] = new TapeEventID(r.I64());
                _mintOpportunityEvents[i] = events.Distinct().OrderBy(static id => id.Value).ToArray();
            }
        }
        if (r.TryExpect(MintPredictionEventTag))
        {
            int count = r.I32();
            if (count < 0 || count > _mintLog.Count) throw new InvalidDataException("EML mint claim event count is invalid");
            for (int i = 0; i < count; i++)
            {
                EmlPredictionID claimID = new(r.I32());
                TapeEventID eventID = new(r.I64());
                if ((uint)claimID.Value >= (uint)_mintLog.Count || eventID.Value < 0 || !_claimMintEvents.TryAdd(claimID, eventID))
                    throw new InvalidDataException("EML mint claim event binding is invalid");
            }
        }
        if (r.TryExpect(ComposedPredictionEventTag))
        {
            int count = r.I32();
            if (count < 0 || count > _mintLog.Count) throw new InvalidDataException("EML derived claim event count is invalid");
            for (int i = 0; i < count; i++)
            {
                EmlPredictionID claimID = new(r.I32());
                TapeEventID eventID = new(r.I64());
                if ((uint)claimID.Value >= (uint)_mintLog.Count || eventID.Value < 0 || !_claimCompositionEvents.TryAdd(claimID, eventID))
                    throw new InvalidDataException("EML derived claim event binding is invalid");
            }
        }
        _obligations.Clear();
        _obligationBySource.Clear();
        _exactCompositionObligations.Clear();
        _exactCompositionBySource.Clear();
        _obligationOpportunityEvents.Clear();
        _obligationMintEvents.Clear();
        _obligationByResidual.Clear();
        _obligationClosures.Clear();
        _obligationClosureKeys.Clear();
        if (r.TryExpect(ObligationClosureTag))
        {
            int closureSchema = r.I32();
            if (closureSchema is not (2 or 3 or 4 or 5 or 6 or 7 or 8))
                throw new InvalidDataException($"unsupported EML obligation closure schema v{closureSchema}");
            int obligationCount = r.I32();
            if (obligationCount < 0 || obligationCount > ObligationCap)
                throw new InvalidDataException($"EmlSieve checkpoint carries {obligationCount} obligations; expected 0..{ObligationCap}");
            for (int i = 0; i < obligationCount; i++)
            {
                EmlPredictionID sourcePredictionID = new(r.I32());
                string identity = r.Str();
                List<TapeEventID> opportunityEvents = [];
                TapeEventID? mintEventID = null;
                if (closureSchema >= 4)
                {
                    int opportunityCount = r.I32();
                    if (opportunityCount < 0 || opportunityCount > 1024)
                        throw new InvalidDataException("EML obligation opportunity event count is invalid");
                    for (int j = 0; j < opportunityCount; j++) opportunityEvents.Add(new TapeEventID(r.I64()));
                    if (r.Bool()) mintEventID = new TapeEventID(r.I64());
                }
                EmlObligation savedObligation = new(sourcePredictionID, identity, opportunityEvents, mintEventID);
                EmlObligationResolution resolution = ResolveObligation(savedObligation, _claimReferences);
                if (!string.Equals(identity, ComputeObligationIdentity(sourcePredictionID, in resolution), StringComparison.Ordinal))
                    throw new InvalidDataException($"EML obligation {sourcePredictionID.Value} identity mismatch");
                _obligations.Add(savedObligation);
                if (!_obligationBySource.TryAdd(sourcePredictionID, _obligations.Count - 1))
                    throw new InvalidDataException("EML checkpoint repeats an obligation source claim");
                _obligationOpportunityEvents[sourcePredictionID] = opportunityEvents;
                if (mintEventID is TapeEventID savedMintEvent) _obligationMintEvents[sourcePredictionID] = savedMintEvent;
            }
            if (closureSchema >= 7)
            {
                int exactCount = r.I32();
                if (exactCount < 0 || exactCount > ObligationCap)
                    throw new InvalidDataException($"EmlSieve checkpoint carries {exactCount} exact derivation targets; expected 0..{ObligationCap}");
                if (obligationCount + exactCount > ObligationCap)
                    throw new InvalidDataException($"EmlSieve checkpoint carries {obligationCount + exactCount} obligation targets; expected 0..{ObligationCap}");
                for (int i = 0; i < exactCount; i++)
                {
                    EmlPredictionID sourcePredictionID = new(r.I32());
                    string identity = r.Str(); string sourceDigest = r.Str(); string carrierRPN = r.Str();
                    EmlCert sourceCertificate = ReadCert(r);
                    int supportCount = r.I32();
                    if (supportCount <= 0 || supportCount > 1024)
                        throw new InvalidDataException("EML exact derivation target support set is invalid");
                    TapeEventID[] supports = new TapeEventID[supportCount];
                    for (int j = 0; j < supportCount; j++) supports[j] = new TapeEventID(r.I64());
                    if (!supports.SequenceEqual(supports.OrderBy(static id => id.Value)) || supports.Distinct().Count() != supports.Length)
                        throw new InvalidDataException("EML exact derivation target supports are not canonical");
                    TapeEventID? mintEventID = r.Bool() ? new TapeEventID(r.I64()) : null;
                    EmlExactCompositionObligation target = new(sourcePredictionID, identity, sourceDigest, carrierRPN, sourceCertificate, supports, mintEventID);
                    if ((uint)sourcePredictionID.Value >= (uint)_mintLog.Count
                        || (uint)sourcePredictionID.Value >= (uint)_mintOpportunityEvents.Count
                        || _mintLog[sourcePredictionID.Value].Grade != 'E'
                        || !EmlPrediction.TryParse(_mintLog[sourcePredictionID.Value].Line, out EmlPrediction sourcePrediction)
                        || !sourcePrediction.RhsRpn || !string.Equals(sourcePrediction.Lhs, carrierRPN, StringComparison.Ordinal)
                        || !string.Equals(sourceDigest, Digest(_mintLog[sourcePredictionID.Value].Line), StringComparison.Ordinal)
                        || supports.Any(static id => id.Value < 0)
                        || !supports.SequenceEqual(_mintOpportunityEvents[sourcePredictionID.Value])
                        || mintEventID is not TapeEventID savedMintEvent || savedMintEvent.Value < 0
                        || !_claimMintEvents.TryGetValue(sourcePredictionID, out TapeEventID boundMintEvent) || boundMintEvent != savedMintEvent
                        || !string.Equals(identity, ComputeExactCompositionIdentity(sourcePredictionID, sourceDigest, carrierRPN, sourceCertificate, supports, mintEventID), StringComparison.Ordinal))
                        throw new InvalidDataException("EML exact derivation target source identity mismatch");
                    if (!_exactCompositionBySource.TryAdd(sourcePredictionID, _exactCompositionObligations.Count))
                        throw new InvalidDataException("EML checkpoint repeats an exact derivation target source claim");
                    _exactCompositionObligations.Add(target);
                }
            }
            int closureCount = r.I32();
            if (closureCount < 0 || closureCount > ObligationCap * 8)
                throw new InvalidDataException($"EmlSieve checkpoint carries {closureCount} obligation closures");
            for (int i = 0; i < closureCount; i++)
            {
                EmlPredictionID sourcePredictionID = new(r.I32());
                string obligationID = r.Str(); string attemptID = r.Str(); string attachmentID = r.Str();
                EmlObligationClosureStatuses status = (EmlObligationClosureStatuses)r.U8();
                if (!Enum.IsDefined(status)) throw new InvalidDataException("EML obligation closure carries an unknown status");
                string sourceDigest = r.Str(); EmlObligationProofKinds kind = (EmlObligationProofKinds)r.U8();
                EmlFiniteObligationProofPolicy? finitePolicy = r.Bool()
                    ? new EmlFiniteObligationProofPolicy(r.I32(), r.I32(), r.Str()) : null;
                EmlProcessObligationProofPolicy? processPolicy = r.Bool()
                    ? new EmlProcessObligationProofPolicy(r.I32(), r.I64(), r.I32(), r.I32(), r.I32(), r.Str()) : null;
                EmlFiniteObligationProofEvidence? finiteEvidence = r.Bool()
                    ? new EmlFiniteObligationProofEvidence(new EmlEvaluatorInterval(r.I64(), r.I64()), r.I64(), r.Str(), r.Str(), ReadCert(r), ReadCert(r)) : null;
                EmlProcessObligationProofEvidence? processEvidence = r.Bool()
                    ? new EmlProcessObligationProofEvidence(new EmlEvaluatorInterval(r.I64(), r.I64()), r.I64(), r.I64(), r.I64(), r.Str(), r.Str(), r.Str(), ReadCert(r), ReadCert(r)) : null;
                string reason = r.Str();
                EmlObligationTargetSpecies species = closureSchema >= 7
                    ? (EmlObligationTargetSpecies)r.U8()
                    : EmlObligationTargetSpecies.Residual;
                if (!Enum.IsDefined(species)) throw new InvalidDataException("EML obligation closure carries an unknown target species");
                EmlRung0ComposedFormObligationEvidence? rung0Evidence = null;
                if (closureSchema >= 3 && r.Bool())
                {
                    EmlPredictionID witnessSource = new(r.I32()); string witnessObligation = r.Str();
                    EmlPredictionID witnessComposed = new(r.I32()); string lhs = r.Str(); string rhs = r.Str();
                    string guard = r.Str(); string proofID = r.Str(); string auditID = r.Str();
                    string proofSHA256 = closureSchema >= 5 ? r.Str() : "";
                    string auditSHA256 = closureSchema >= 5 ? r.Str() : "";
                    string admissionID = r.Str(); string closureID = r.Str(); string comparator = r.Str();
                    EmlEvaluatorInterval admissionEvaluation = new(r.I64(), r.I64());
                    EmlEvaluatorInterval comparatorEvaluation = new(r.I64(), r.I64());
                    string rung0CandidateDigest = r.Str(); string rung0AttachmentDigest = r.Str();
                    EmlCert before = ReadCert(r); EmlCert after = ReadCert(r);
                    string admissionPathCanonical = closureSchema >= 6 ? r.Str() : "";
                    string admissionPathFingerprint = closureSchema >= 6 ? r.Str() : "";
                    rung0Evidence = new EmlRung0ComposedFormObligationEvidence(
                        witnessSource, witnessObligation, witnessComposed, lhs, rhs, guard,
                        proofID, auditID, proofSHA256, auditSHA256, admissionID, closureID, comparator, admissionEvaluation, comparatorEvaluation,
                        rung0CandidateDigest, rung0AttachmentDigest, before, after,
                        admissionPathCanonical, admissionPathFingerprint);
                }
                bool typed = kind switch
                {
                    EmlObligationProofKinds.FiniteRPN => finitePolicy.HasValue && !processPolicy.HasValue && finiteEvidence.HasValue && !processEvidence.HasValue && rung0Evidence is null,
                    EmlObligationProofKinds.ProcessFunction => processPolicy.HasValue && !finitePolicy.HasValue && processEvidence.HasValue && !finiteEvidence.HasValue && rung0Evidence is null,
                    EmlObligationProofKinds.Rung0ComposedForm => !finitePolicy.HasValue && !processPolicy.HasValue && !finiteEvidence.HasValue && !processEvidence.HasValue && rung0Evidence.HasValue,
                    _ => false,
                };
                if (!typed) throw new InvalidDataException($"EML obligation closure {i} has mismatched typed payload");
                if (sourcePredictionID.Value < 0 || sourcePredictionID.Value >= _mintLog.Count)
                    throw new InvalidDataException($"EML obligation closure {i} source claim is outside the mint journal");
                if (!TryReadTargetIdentity(sourcePredictionID, out string loadedTargetIdentity, out EmlObligationTargetSpecies loadedTargetSpecies)
                    || loadedTargetSpecies != species
                    || !string.Equals(loadedTargetIdentity, obligationID, StringComparison.Ordinal)
                    || !string.Equals(sourceDigest, Digest(_mintLog[sourcePredictionID.Value].Line), StringComparison.Ordinal))
                    throw new InvalidDataException($"EML obligation closure {i} source identity mismatch");
                string candidateDigest = finiteEvidence?.CandidateDigest ?? processEvidence?.CandidateDigest ?? rung0Evidence?.CandidateDigest ?? "none";
                if (!string.Equals(attemptID, ComputeAttemptID(obligationID, kind, finitePolicy, processPolicy, candidateDigest), StringComparison.Ordinal))
                    throw new InvalidDataException($"EML obligation closure {i} attempt identity mismatch");
                string expectedAttachment = attachmentID.Length == 0 ? "" : ComputeAttachmentID(obligationID, kind, candidateDigest);
                if (!string.Equals(attachmentID, expectedAttachment, StringComparison.Ordinal))
                    throw new InvalidDataException($"EML obligation closure {i} attachment identity mismatch");
                EmlObligationClosure closure = new(sourcePredictionID, obligationID, attemptID, attachmentID, status,
                    sourceDigest, kind, finitePolicy, processPolicy, finiteEvidence, processEvidence, reason, rung0Evidence, species);
                if (closure.Closed && !_obligationClosureKeys.TryAdd(attachmentID, _obligationClosures.Count))
                    throw new InvalidDataException($"duplicate EML obligation closure attachment {attachmentID}");
                _obligationClosures.Add(closure);
            }
        }
        else
            throw new InvalidDataException("EML obligation closure section is missing; CORTEX7 checkpoints never treat legacy captures as solved");
        if (!_deliberationJournal.TryLoad(r))
            throw new InvalidDataException("EML deliberation journal section is missing; search checkpoints require EJ01");
        _residualProofs.Clear();
        _residualProofKeys.Clear();
        if (r.TryExpect(ResidualProofTag))
        {
            int proofCount = r.I32();
            if (proofCount < 0) throw new InvalidDataException("EmlSieve checkpoint carries a negative residual proof count");
            for (int i = 0; i < proofCount; i++)
            {
                EmlPredictionID sourcePredictionID = new(r.I32());
                string program = r.Str();
                EmlCert certificate = new((char)r.U8(), ReadSig(r), r.I64(), r.I64());
                string proofKey = sourcePredictionID.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "\u0001" + program;
                if (!_residualProofKeys.Add(proofKey))
                    throw new InvalidDataException($"EmlSieve checkpoint repeats residual proof {sourcePredictionID.Value}:{program}");
                _residualProofs.Add(new EmlResidualProof(sourcePredictionID, program, certificate));
            }
        }
        _processResidualProofs.Clear();
        _processResidualProofKeys.Clear();
        _processProofWireVersions.Clear();
        if (r.TryExpect(ProcessResidualProofTag))
        {
            int proofCount = r.I32();
            if (proofCount < 0)
                throw new InvalidDataException("EmlSieve checkpoint carries a negative process residual proof count");
            for (int i = 0; i < proofCount; i++)
            {
                EmlPredictionID sourcePredictionID = new(r.I32());
                EmlProcessFunctionAlgorithms algorithm = (EmlProcessFunctionAlgorithms)r.I32();
                int savedVersion = r.I32();
                if (savedVersion is not (1 or 2 or EmlProcessFunctions.AlgorithmVersion))
                    throw new InvalidDataException($"EmlSieve checkpoint process residual proof {sourcePredictionID.Value} has unknown saved version {savedVersion}");
                bool migratedDescriptor = savedVersion is 1 or 2;
                EmlProcessFunction function;
                if (savedVersion == 1 && algorithm == EmlProcessFunctionAlgorithms.NegativeLogSeries)
                {
                    EmlProcessInputSlots input = (EmlProcessInputSlots)r.I32();
                    long fuel = r.I64();
                    function = EmlProcessFunctions.CreateNegativeLog(input, fuel);
                }
                else
                {
                    string numerator = r.Str();
                    string denominator = r.Str();
                    long fuel = r.I64();
                    function = new EmlProcessFunction(
                        algorithm,
                        EmlProcessFunctions.AlgorithmVersion,
                        numerator,
                        denominator,
                        fuel);
                }
                // Version 1 had the compact input-slot descriptor and no derivation marker. Version 2 already
                // carried the marker, but its descriptor digest predates the v3 certificate schema.
                bool carriesCompositionLaw = savedVersion >= 2 && r.Bool();
                EmlResidualCompositionLaws? derivationLaw = carriesCompositionLaw
                    ? (EmlResidualCompositionLaws)r.I32()
                    : null;
                if (algorithm == EmlProcessFunctionAlgorithms.ExponentialSeries
                    && derivationLaw != EmlResidualCompositionLaws.ExponentialTail)
                    throw new InvalidDataException($"EmlSieve checkpoint exponential-series proof {sourcePredictionID.Value} lacks ExponentialTail structural derivation");
                string digest = r.Str();
                EmlCert certificate = new((char)r.U8(), ReadSig(r), r.I64(), r.I64());
                long processFuel = r.I64();
                EmlProcessFunctionCertificate reconstructed = EmlProcessFunctions.Certify(in function);
                EmlProcessFunctionCheck check = EmlProcessFunctionChecker.Check(in reconstructed);
                bool legacyDigestValid = migratedDescriptor
                    && EmlProcessFunctionEncoding.MatchesLegacyDigest(digest, savedVersion, in function, in reconstructed);
                if (!check.Accepted || (!migratedDescriptor && !string.Equals(digest, reconstructed.Digest, StringComparison.Ordinal))
                    || (migratedDescriptor && !legacyDigestValid))
                    throw new InvalidDataException($"EmlSieve checkpoint process residual proof {sourcePredictionID.Value} failed reconstruction");
                if (migratedDescriptor)
                    Trace.Note($"eml process migration · source={sourcePredictionID.Value} · v{savedVersion}->v{EmlProcessFunctions.AlgorithmVersion} · legacy-digest={(legacyDigestValid ? "ok" : "rejected")} · wire=v{savedVersion}");
                EmlObligationResolution resolution = ResolveObligation(sourcePredictionID);
                EmlEvaluatorClock proofClock = new();
                EmlResidualComposition? derivation = null;
                if (derivationLaw is EmlResidualCompositionLaws structuralLaw)
                {
                    EmlMint derivationMint = _mintLog[sourcePredictionID.Value];
                    if (!EmlPrediction.TryParse(derivationMint.Line, out EmlPrediction derivationPrediction)
                        || !(structuralLaw == EmlResidualCompositionLaws.ExponentialTail
                            ? EmlResidualDeriver.TryDeriveExponentialTail(
                                sourcePredictionID,
                                in derivationPrediction,
                                function.Fuel,
                                out EmlResidualComposition reconstructedComposition)
                            : EmlResidualDeriver.TryDeriveSharedExponentialArgument(
                                sourcePredictionID,
                                in derivationPrediction,
                                function.Fuel,
                                out reconstructedComposition))
                        || reconstructedComposition.Law != structuralLaw
                        || reconstructedComposition.Process != function)
                        throw new InvalidDataException($"EmlSieve checkpoint process residual proof {sourcePredictionID.Value} failed structural reconstruction");
                    derivation = reconstructedComposition;
                }
                EmlMint sourceMint = _mintLog[sourcePredictionID.Value];
                if (!EmlPrediction.TryParse(sourceMint.Line, out EmlPrediction sourcePrediction))
                    throw new InvalidDataException($"EmlSieve checkpoint process residual proof {sourcePredictionID.Value} has an invalid source claim");
                if (migratedDescriptor && derivationLaw is null
                    && (function.Algorithm == EmlProcessFunctionAlgorithms.ExponentialSeries
                        ? EmlResidualDeriver.TryDeriveExponentialTail(
                            sourcePredictionID,
                            in sourcePrediction,
                            function.Fuel,
                            out EmlResidualComposition migratedComposition)
                        : EmlResidualDeriver.TryDeriveSharedExponentialArgument(
                            sourcePredictionID,
                            in sourcePrediction,
                            function.Fuel,
                            out migratedComposition))
                    && migratedComposition.Process == function)
                {
                    derivation = migratedComposition;
                    derivationLaw = migratedComposition.Law;
                }
                EmlResidualWitness witness = resolution.Corroboration;
                EmlProcessResidualOccurrenceCheck verification = EmlProcessResidualVerifier.Verify(
                    sourcePredictionID,
                    in sourcePrediction,
                    in witness,
                    in function,
                    derivation,
                    _claimReferences,
                    proofClock);
                long reconstructedFuel = checked(
                    verification.Process.P1.FuelSpent
                    + verification.Process.P2.FuelSpent
                    + verification.Process.P3.FuelSpent);
                EmlCert expectedCertificate = new('E', resolution.ResidualSignature, 0, 0);
                if (!verification.Accepted || certificate != expectedCertificate
                    || processFuel != reconstructedFuel)
                    throw new InvalidDataException(
                        $"EmlSieve checkpoint process residual proof {sourcePredictionID.Value} failed witness verification: "
                        + $"detail={verification.Detail}; saved-version={savedVersion}; migrated={migratedDescriptor}; "
                        + $"legacy-digest={(legacyDigestValid ? "ok" : "no")}; "
                        + $"certificate={(certificate == expectedCertificate ? "ok" : "mismatch")}; "
                        + $"fuel={processFuel}/{reconstructedFuel}");
                string proofKey = sourcePredictionID.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "\u0001" + digest;
                if (!_processResidualProofKeys.Add(proofKey))
                    throw new InvalidDataException($"EmlSieve checkpoint repeats process residual proof {sourcePredictionID.Value}:{digest}");
                if (migratedDescriptor)
                    _processProofWireVersions.Add(proofKey, savedVersion);
                _processResidualProofs.Add(new EmlProcessResidualProof(
                    sourcePredictionID,
                    function,
                    derivationLaw,
                    digest,
                    certificate,
                    processFuel));
            }
        }
        _derivedFormProofs.Clear();
        _derivedFormProofKeys.Clear();
        bool derivedFormProofsHaveSelection = r.TryExpect(ComposedFormProofTagV2);
        bool hasComposedFormProofs = derivedFormProofsHaveSelection;
        if (!hasComposedFormProofs) hasComposedFormProofs = r.TryExpect(ComposedFormProofTag);
        _legacyComposedFormAuditHash = hasComposedFormProofs && !derivedFormProofsHaveSelection;
        if (hasComposedFormProofs)
        {
            int proofCount = r.I32();
            if (proofCount < 0 || proofCount > _mintLog.Count)
                throw new InvalidDataException("EmlSieve checkpoint carries an invalid derived-form proof count");
            for (int i = 0; i < proofCount; i++)
            {
                EmlPredictionID sourcePredictionID = new(r.I32());
                EmlPredictionID derivedPredictionID = new(r.I32());
                string program = r.Str();
                EmlCert certificate = ReadCert(r);
                EmlRung0Proof proof = EmlRung0Checkpoint.ReadProof(r);
                EmlRung0Audit audit = EmlRung0Checkpoint.ReadAudit(r, hasSelection: derivedFormProofsHaveSelection);
                if ((uint)sourcePredictionID.Value >= (uint)_mintLog.Count
                    || (uint)derivedPredictionID.Value >= (uint)_mintLog.Count
                    || proof.PredictionID != sourcePredictionID
                    || !proof.IsValidShape
                    || !string.Equals(proof.ConsequentRPN, program, StringComparison.Ordinal)
                    || !string.Equals(proof.SourceDigest, Digest(_mintLog[sourcePredictionID.Value].Line), StringComparison.Ordinal)
                    || !IsValidComposedAudit(in proof, in audit)
                    || audit.Status == EmlRung0AuditStatuses.Disagreed
                    || certificate.Grade != 'E'
                    || !EmlPrediction.TryParse(_mintLog[derivedPredictionID.Value].Line, out EmlPrediction derivedPrediction)
                    || derivedPrediction.Tilde
                    || !string.Equals(derivedPrediction.Lhs, program, StringComparison.Ordinal)
                    || !string.Equals(derivedPrediction.Rhs, proof.AntecedentRPN, StringComparison.Ordinal))
                    throw new InvalidDataException($"EmlSieve checkpoint derived-form proof {i} failed reconstruction");
                string proofKey = sourcePredictionID.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "\u0001" + proof.Digest.ToString("X16", System.Globalization.CultureInfo.InvariantCulture);
                if (!_derivedFormProofKeys.Add(proofKey))
                    throw new InvalidDataException($"EmlSieve checkpoint repeats derived-form proof {proofKey}");
                _derivedFormProofs.Add(new EmlComposedFormProof(
                    sourcePredictionID,
                    derivedPredictionID,
                    program,
                    certificate,
                    proof,
                    audit));
            }
        }
        EmlEvaluatorClockSnapshot closureClock = _evaluatorClock.Capture();
        ValidateObligationClosures(_legacyComposedFormAuditHash);
        _evaluatorClock.Restore(in closureClock, writesCheckpoint: true);
        _newSemanticDeltas.Clear();
        _newMints.Clear();
        int nn = r.I32();
        for (int i = 0; i < nn; i++) _newMints.Add(new EmlMint(r.Str(), r.Str(), ReadSig(r), (char)r.U8(), r.Bool()));
        bool clockCheckpointed = _evaluatorClock.TryLoad(r);
        EmlEvaluatorClockSnapshot savedClock = _evaluatorClock.Capture();
        RebuildCas();
        _evaluatorClock.Restore(in savedClock, clockCheckpointed);       // CAS reconstruction is derived checkpoint work, not run history
        RebuildExactRPNForms();
        RebuildObligationLookup();
        EmlEvaluatorClockSnapshot validationClock = _evaluatorClock.Capture();
        ValidateObligationClosures(_legacyComposedFormAuditHash);
        _evaluatorClock.Restore(in validationClock, writesCheckpoint: true);
        if (savedAnomalyHits != AnomalyHits)
            throw new InvalidDataException($"EmlSieve checkpoint obligation captures disagree with the saved aggregate ({AnomalyHits} != {savedAnomalyHits})");
        RebuildHeldCaptured();
        _checkpointMintCount = _mintLog.Count;
        _checkpointObligationCount = _obligations.Count;
        _checkpointResidualProofCount = _residualProofs.Count;
        _checkpointExactCompositionCount = _exactCompositionObligations.Count;
        _checkpointClosureCount = _obligationClosures.Count;
        _checkpointProcessProofCount = _processResidualProofs.Count;
        _checkpointComposedFormProofCount = _derivedFormProofs.Count;
        _checkpointDeliberationAdmissions = _deliberationJournal.Admissions.Count;
        _checkpointDeliberationPhases = _deliberationJournal.Phases.Count;
        _checkpointDeliberationSettlements = _deliberationJournal.Settlements.Count;
        _checkpointPredictionMintEvents.Clear();
        _checkpointPredictionCompositionEvents.Clear();
    }

    private void ValidateObligationClosures(bool legacyAuditHash = false)
    {
        for (int targetIndex = 0; targetIndex < _exactCompositionObligations.Count; targetIndex++)
        {
            EmlExactCompositionObligation target = _exactCompositionObligations[targetIndex];
            int targetSourceIndex = target.SourcePredictionID.Value;
            if (targetSourceIndex < 0 || targetSourceIndex >= _mintLog.Count
                || target.Supports.Count == 0
                || !target.Supports.SequenceEqual(target.Supports.OrderBy(static id => id.Value))
                || target.MintEventID is not TapeEventID
                || _mintLog[targetSourceIndex].Grade != 'E'
                || !EmlPrediction.TryParse(_mintLog[targetSourceIndex].Line, out EmlPrediction targetPrediction)
                || !targetPrediction.RhsRpn || !string.Equals(targetPrediction.Lhs, target.CarrierRPN, StringComparison.Ordinal)
                || !string.Equals(target.SourceDigest, Digest(_mintLog[targetSourceIndex].Line), StringComparison.Ordinal)
                // During checkpoint load this validator runs before CAS rebuild repopulates
                // certificates; the post-rebuild validation below enforces the certificate tie.
                || (_mintCerts.Count == _mintLog.Count && target.SourceCertificate != _mintCerts[target.SourcePredictionID.Value])
                || !string.Equals(target.Identity, ComputeExactCompositionIdentity(target.SourcePredictionID, target.SourceDigest, target.CarrierRPN, target.SourceCertificate, target.Supports, target.MintEventID), StringComparison.Ordinal))
                throw new InvalidDataException($"EML exact derivation target {targetIndex} is not source-bound");
        }
        for (int i = 0; i < _obligationClosures.Count; i++)
        {
            EmlObligationClosure closure = _obligationClosures[i];
            if (!closure.Closed) continue;
            if (closure.Status != EmlObligationClosureStatuses.Accepted || !string.Equals(closure.Reason, "accepted", StringComparison.Ordinal))
                throw new InvalidDataException($"EML closure {i} carries an invalid accepted outcome");
            if (closure.SourcePredictionID.Value < 0 || closure.SourcePredictionID.Value >= _mintLog.Count)
                throw new InvalidDataException($"EML closure {i} source claim is outside the mint journal");
            bool residualTarget = _obligations.Any(o => o.SourcePredictionID == closure.SourcePredictionID);
            bool exactTarget = _exactCompositionBySource.ContainsKey(closure.SourcePredictionID);
            if ((!residualTarget && !exactTarget)
                || !TryReadTargetIdentity(closure.SourcePredictionID, out string targetIdentity, out EmlObligationTargetSpecies targetSpecies)
                || !string.Equals(targetIdentity, closure.ObligationID, StringComparison.Ordinal)
                || closure.Species != targetSpecies)
                throw new InvalidDataException($"EML closure {i} does not bind to a registered obligation");
            if (closure.Kind == EmlObligationProofKinds.Rung0ComposedForm)
            {
                EmlRung0ComposedFormObligationEvidence evidence = closure.Rung0ComposedFormEvidence
                    ?? throw new InvalidDataException($"EML rung-0 closure {i} has no typed derived-form evidence");
                EmlRung0ComposedFormProof witness = FindRung0ComposedFormProof(evidence.ProofID, closure.SourcePredictionID)
                    ?? throw new InvalidDataException($"EML rung-0 closure {i} has no matching proof record");
                bool staleWitness = evidence.ObligationPredictionID != closure.SourcePredictionID
                    || !string.Equals(evidence.ObligationID, closure.ObligationID, StringComparison.Ordinal)
                    || evidence.ComposedPredictionID != witness.ComposedPredictionID
                    || !string.Equals(evidence.LhsRPN, witness.LhsRPN, StringComparison.Ordinal)
                    || !string.Equals(evidence.RhsRPN, witness.RhsRPN, StringComparison.Ordinal)
                    || !string.Equals(evidence.GuardPackageDigest, witness.GuardPackageDigest, StringComparison.Ordinal)
                    || !string.Equals(evidence.AuditID, witness.AuditID, StringComparison.Ordinal)
                    || (evidence.ProofSHA256.Length != 0
                        && !string.Equals(evidence.ProofSHA256, EmlRung0Checkpoint.ProofSHA256(witness.Proof), StringComparison.Ordinal))
                    || (evidence.AuditSHA256.Length != 0
                        && !string.Equals(evidence.AuditSHA256,
                            legacyAuditHash
                                ? EmlRung0Checkpoint.LegacyAuditSHA256(witness.Audit)
                                : EmlRung0Checkpoint.AuditSHA256(witness.Audit), StringComparison.Ordinal))
                    || !string.Equals(evidence.AdmissionID, witness.AdmissionID, StringComparison.Ordinal)
                    || !string.Equals(evidence.ClosureID, witness.ClosureID, StringComparison.Ordinal)
                    || !string.Equals(evidence.AdmissionPathCanonical, witness.AdmissionPath.GuardPackageCanonical, StringComparison.Ordinal)
                    || !string.Equals(evidence.AdmissionPathFingerprint, witness.AdmissionPath.GuardPackageFingerprint, StringComparison.Ordinal)
                    || witness.Proof.PredictionID != closure.SourcePredictionID
                    || witness.ComposedPredictionID == closure.SourcePredictionID
                    || witness.ComposedPredictionID.Value < 0 || witness.ComposedPredictionID.Value >= _mintLog.Count
                    || !string.Equals(witness.ObligationID, closure.ObligationID, StringComparison.Ordinal)
                    || !string.Equals(evidence.Comparator, "EmlGrader.GradeRpn", StringComparison.Ordinal)
                    || !witness.IsExactZeroAdmission
                    || evidence.Evaluator != witness.AdmissionEvaluation
                    || evidence.ComparatorEvaluation != witness.ComparatorEvaluation
                    || evidence.ComparatorEvaluation.Calls <= 0
                    || evidence.MainEvaluatorCalls != 0
                    || evidence.After.Grade != 'E'
                    || !string.Equals(evidence.CandidateDigest,
                        Digest(witness.LhsRPN + "|" + witness.RhsRPN + "|guard=" + witness.GuardPackageDigest), StringComparison.Ordinal)
                    || !string.Equals(evidence.AttachmentDigest,
                        AttachmentDigest(targetIdentity, EmlObligationProofKinds.Rung0ComposedForm, evidence.CandidateDigest), StringComparison.Ordinal)
                    || !string.Equals(closure.AttachmentID, ComputeAttachmentID(closure.ObligationID, EmlObligationProofKinds.Rung0ComposedForm, evidence.CandidateDigest), StringComparison.Ordinal)
                    || !string.Equals(closure.AttemptID, ComputeAttemptID(closure.ObligationID, EmlObligationProofKinds.Rung0ComposedForm, null, null, evidence.CandidateDigest), StringComparison.Ordinal);
                if (staleWitness)
                    throw new InvalidDataException($"EML rung-0 closure {i} has a stale structural admission witness");
                EmlMint derivedMint = _mintLog[witness.ComposedPredictionID.Value];
                if (!EmlPrediction.TryParse(derivedMint.Line, out EmlPrediction derivedPrediction)
                    || !string.Equals(derivedPrediction.Lhs, witness.RhsRPN, StringComparison.Ordinal)
                    || !string.Equals(derivedPrediction.Rhs, witness.LhsRPN, StringComparison.Ordinal)
                    || derivedMint.Grade != 'E'
                    || (_mintCerts.Count == _mintLog.Count && evidence.After != _mintCerts[witness.ComposedPredictionID.Value]))
                    throw new InvalidDataException($"EML rung-0 closure {i} derived claim is not bound to its admission");
            }
            else if (closure.Kind == EmlObligationProofKinds.FiniteRPN)
            {
                EmlFiniteObligationProofEvidence evidence = closure.FiniteEvidence
                    ?? throw new InvalidDataException($"EML finite closure {i} has no evidence");
                EmlFiniteObligationProofPolicy policy = closure.FinitePolicy
                    ?? throw new InvalidDataException($"EML finite closure {i} has no policy");
                if (policy.SignatureDigits != _sig || policy.WitnessVersion != 1
                    || !string.Equals(policy.VerifierRevision, "eml-finite-residual-v2", StringComparison.Ordinal)
                    || !string.Equals(evidence.CandidateDigest, evidence.AttachmentDigest, StringComparison.Ordinal)
                    || evidence.Evaluator.End < evidence.Evaluator.Start || evidence.WallTicks < 0)
                    throw new InvalidDataException($"EML finite closure {i} carries a stale policy or telemetry interval");
                EmlResidualProof? proof = null;
                for (int p = 0; p < _residualProofs.Count; p++)
                    if (_residualProofs[p].SourcePredictionID == closure.SourcePredictionID
                        && residualTarget && string.Equals(AttachmentDigest(targetIdentity, EmlObligationProofKinds.FiniteRPN, _residualProofs[p].Program), evidence.AttachmentDigest, StringComparison.Ordinal))
                    { proof = _residualProofs[p]; break; }
                if (proof is null || proof.Value.Certificate != evidence.After)
                    throw new InvalidDataException($"EML finite closure {i} has no matching proof attachment");
                EmlMint sourceMint = _mintLog[closure.SourcePredictionID.Value];
                if (!EmlPrediction.TryParse(sourceMint.Line, out EmlPrediction sourcePrediction)
                    || !_grader.TryGrade(in sourcePrediction, _claimReferences, out EmlVerdict sourceVerdict)
                    || evidence.Before != EmlCert.Of(in sourceVerdict, _sig))
                    throw new InvalidDataException($"EML finite closure {i} has a stale source certificate");
                EmlObligation obligation = _obligations.First(o => o.SourcePredictionID == closure.SourcePredictionID);
                EmlObligationResolution resolution = ResolveObligation(obligation);
                EmlResidualWitness witness = resolution.Corroboration;
                EmlResidualExpression expression = EmlResidualExpression.CreateFiniteRPN(proof.Value.Program);
                EmlResidualExpressionEvaluation expressionEvaluation = expression.Evaluate(_evaluatorClock, _grader);
                EmlHoleRepairOccurrenceCheck verification = EmlHoleSolver.VerifyExpression(
                    expression, in expressionEvaluation, in witness, _grader);
                if (!verification.Accepted)
                    throw new InvalidDataException($"EML finite closure {i} failed witness re-verification: {verification.Detail}");
            }
            else
            {
                EmlProcessObligationProofEvidence evidence = closure.ProcessEvidence
                    ?? throw new InvalidDataException($"EML process closure {i} has no evidence");
                EmlProcessResidualProof? matchedProof = null;
                for (int p = 0; p < _processResidualProofs.Count; p++)
                    if (_processResidualProofs[p].SourcePredictionID == closure.SourcePredictionID
                        && residualTarget && string.Equals(AttachmentDigest(targetIdentity, EmlObligationProofKinds.ProcessFunction, _processResidualProofs[p].Digest), evidence.AttachmentDigest, StringComparison.Ordinal)
                        && string.Equals(_processResidualProofs[p].Digest, evidence.CertificateDigest, StringComparison.Ordinal))
                    { matchedProof = _processResidualProofs[p]; break; }
                if (matchedProof is null) throw new InvalidDataException($"EML process closure {i} has no matching proof attachment");
                EmlProcessObligationProofPolicy policy = closure.ProcessPolicy
                    ?? throw new InvalidDataException($"EML process closure {i} has no policy");
                EmlProcessResidualProof processProof = matchedProof.Value;
                if (policy.SignatureDigits != _sig || policy.FuelPerProbe != processProof.Function.Fuel
                    || policy.ProbeCount != 3 || policy.FunctionVersion != processProof.Function.Version
                    || policy.CompositionVersion != (processProof.CompositionLaw is null ? 0 : 1)
                    || !string.Equals(policy.VerifierRevision, "eml-process-residual-v3", StringComparison.Ordinal)
                    || !string.Equals(evidence.CandidateDigest, evidence.CertificateDigest, StringComparison.Ordinal)
                    || evidence.Evaluator.End < evidence.Evaluator.Start || evidence.WallTicks < 0)
                    throw new InvalidDataException($"EML process closure {i} carries a stale policy or telemetry interval");
                if (evidence.After != processProof.Certificate)
                    throw new InvalidDataException($"EML process closure {i} has a stale terminal certificate");
                if (evidence.FuelTotal != checked(evidence.FuelPerProbe * policy.ProbeCount)
                    || processProof.ProcessFuel != evidence.FuelTotal)
                    throw new InvalidDataException($"EML process closure {i} fuel journal is not three probe units");
                EmlMint sourceMint = _mintLog[closure.SourcePredictionID.Value];
                if (!EmlPrediction.TryParse(sourceMint.Line, out EmlPrediction sourcePrediction)
                    || !_grader.TryGrade(in sourcePrediction, _claimReferences, out EmlVerdict sourceVerdict)
                    || evidence.Before != EmlCert.Of(in sourceVerdict, _sig))
                    throw new InvalidDataException($"EML process closure {i} has a stale source certificate");
            }
        }
    }

    private EmlRung0ComposedFormProof? FindRung0ComposedFormProof(string proofID, EmlPredictionID sourcePredictionID)
    {
        for (int i = 0; i < _derivedFormProofs.Count; i++)
        {
            EmlComposedFormProof stored = _derivedFormProofs[i];
            if (stored.SourcePredictionID == sourcePredictionID
                && string.Equals(stored.Proof.Digest.ToString("X16", System.Globalization.CultureInfo.InvariantCulture), proofID, StringComparison.Ordinal))
            {
                EmlEvaluatorClock comparatorClock = new();
                long comparatorStart = comparatorClock.ProgramPointEvaluations;
                EmlRung0Proof storedProof = stored.Proof;
                EmlRung0AdmissionPath admissionPath = EmlRung0AdmissionPath.Create(stored.SourcePredictionID, in storedProof);
                _ = admissionPath.Grade(new EmlGrader(comparatorClock));
                string guardPackageDigest = stored.Proof.Steps[^1].GuardWitness.Digest.ToString("X16", System.Globalization.CultureInfo.InvariantCulture);
                string candidateDigest = Digest(stored.Proof.AntecedentRPN + "|" + stored.Proof.ConsequentRPN + "|guard=" + guardPackageDigest);
                return new EmlRung0ComposedFormProof(
                    stored.SourcePredictionID,
                    TryReadTargetIdentity(stored.SourcePredictionID, out string storedIdentity, out _) ? storedIdentity : "unknown",
                    stored.ComposedPredictionID,
                    stored.Proof.AntecedentRPN,
                    stored.Proof.ConsequentRPN,
                    guardPackageDigest,
                    proofID,
                    proofID + ":audit",
                    proofID + ":admission:" + stored.ComposedPredictionID.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ComputeAttachmentID(storedIdentity, EmlObligationProofKinds.Rung0ComposedForm, candidateDigest),
                    "EmlGrader.GradeRpn",
                    EmlEvaluatorInterval.EmptyAt(0),
                    comparatorClock.MeasureFrom(comparatorStart),
                    stored.Proof,
                    stored.Audit);
            }
        }
        return null;
    }

    private EmlObligationResolution ResolveObligation(EmlObligation obligation)
        => ResolveObligation(obligation, _claimReferences);

    private EmlObligationResolution ResolveObligation(
        EmlObligation obligation,
        Dictionary<string, Func<Complex, Complex, Complex>> references)
    {
        int sourceIndex = obligation.SourcePredictionID.Value;
        if ((uint)sourceIndex >= (uint)_mintLog.Count)
            throw new InvalidDataException($"EmlSieve obligation addresses missing source claim {sourceIndex} of {_mintLog.Count}");
        EmlMint sourceMint = _mintLog[sourceIndex];
        if (!EmlPrediction.TryParse(sourceMint.Line, out EmlPrediction sourcePrediction)
            || !_obligationGrader.TryGrade(in sourcePrediction, references, out EmlVerdict verdict))
            throw new InvalidDataException($"EmlSieve obligation source claim {sourceIndex} is unresolvable: {sourceMint.Line}");
        if (verdict.Grade != 'A')
            throw new InvalidDataException($"EmlSieve obligation source claim {sourceIndex} is {verdict.Grade}, not asymptotic: {sourceMint.Line}");
        if (!_obligationGrader.TryDescribeResidual(in sourcePrediction, references, out EmlResidualWitness witness))
            throw new InvalidDataException($"EmlSieve obligation source claim {sourceIndex} has no finite residual function");
        Complex residual = witness.P3.Value;
        if (!double.IsFinite(residual.Real) || !double.IsFinite(residual.Imaginary) || Complex.Abs(residual) < 1e-12)
            throw new InvalidDataException($"EmlSieve obligation source claim {sourceIndex} has no registerable residual");
        EmlSig residualSignature = Eml.Signature(
            new EmlValue(witness.P1.Value, true),
            new EmlValue(witness.P2.Value, true),
            _sig);
        return new EmlObligationResolution(
            obligation.SourcePredictionID,
            residualSignature,
            EmlGrader.AnomalyLabel(residual),
            residual,
            witness,
            ClosureCount(obligation.SourcePredictionID),
            obligation.OpportunityEventIDs,
            obligation.MintEventID);
    }

    private void RebuildObligationLookup()
    {
        _obligationByResidual.Clear();
        _obligationBySource.Clear();
        for (int i = 0; i < _obligations.Count; i++)
        {
            EmlObligation obligation = _obligations[i];
            if (!_obligationBySource.TryAdd(obligation.SourcePredictionID, i))
                throw new InvalidDataException($"EmlSieve obligations repeat source claim {obligation.SourcePredictionID.Value}");
            EmlObligationResolution resolution = ResolveObligation(obligation, _claimReferences);
            if (!_obligationByResidual.TryAdd(resolution.ResidualSignature, i))
                throw new InvalidDataException($"EmlSieve obligations resolve to duplicate residual {resolution.Label}");
        }
    }

    // ── derived: the theorem CAS — re-graded from the mint log on Load (the grade-census law one level up:
    // recount, never trust a stored copy). The ladder is a pure function and the grader the same law, so the
    // rebuilt certificates are bit-identical to the live ones; the checkpoint format is UNTOUCHED (the magic
    // stays). The grade byte doubles as an integrity gate: a rebuilt grade disagreeing with the minted one means
    // the grading law drifted under a live journal — fail loud, never re-key silently.
    private void RebuildCas()
    {
        _cas.Clear(); _mintCerts.Clear(); _mintAdmissions.Clear(); TheoremClasses = 0; ExactClasses = 0;
        if (_mintLog.Count == 0) return;
        foreach (var m in _mintLog)
        {
            if (!EmlPrediction.TryParse(m.Line, out EmlPrediction c)
                || !_grader.TryGrade(in c, _claimReferences, out EmlVerdict v))
                throw new InvalidDataException($"EmlSieve checkpoint: mint line unresolvable in the CAS rebuild: {m.Line}");
            if (v.Grade != m.Grade)
                throw new InvalidDataException($"EmlSieve checkpoint: grade drift in the CAS rebuild ({m.Grade} minted, {v.Grade} rebuilt): {m.Line}");
            RegisterCert(EmlCert.Of(in v, _sig), c.Lhs, c.Rhs);
        }
    }

    private void RebuildHeldCaptured()
    {
        _heldCaptured.Clear();
        if (_isHeld is null) return;
        foreach (var m in _mintLog)
        {
            if (m.Grade != 'E' || !_targetBySig.TryGetValue(m.Sig, out var hits)) continue;
            foreach (int t in hits) if (_isHeld[t]) _heldCaptured.Add(t);
        }
    }

    /// MemStat census read — the sieve's resident masses (dedup set · mint log · canon · basin census · CAS reps ·
    /// the grader's ladder cache), counts + chars only. The census's map of which store grows on which axis.
    internal (long CanonChars, long MintedKeys, long MintedChars, long LogChars, long SigHits, long CasRepChars,
              long GraderKeys, long GraderChars) Mass()
    {
        long canonChars = 0; foreach (var p in _canon.Values) canonChars += p.Length;
        long mintedChars = 0; foreach (var s in _minted) mintedChars += s.Length;
        long logChars = 0; foreach (var m in _mintLog) logChars += m.Line.Length + m.Prog.Length;
        long repChars = 0; foreach (var c in _cas.Values) repChars += c.Rep.Length;
        var (gKeys, gChars) = _grader.CacheMass();
        return (canonChars, _minted.Count, mintedChars, logChars, _sigHits.Count, repChars, gKeys, gChars);
    }

    private static void WriteSig(CkptWriter w, EmlSig s) { w.I64(s.R1); w.I64(s.I1); w.I64(s.R2); w.I64(s.I2); }
    private static EmlSig ReadSig(CkptReader r) => new(r.I64(), r.I64(), r.I64(), r.I64());
    private static void WriteCert(CkptWriter w, EmlCert c) { w.U8((byte)c.Grade); WriteSig(w, c.Limit); w.I64(c.RateRe); w.I64(c.RateIm); }
    private static EmlCert ReadCert(CkptReader r) => new((char)r.U8(), ReadSig(r), r.I64(), r.I64());
    private static IEnumerable<EmlSig> SortedSigs(IEnumerable<EmlSig> keys)
        => keys.OrderBy(k => k.R1).ThenBy(k => k.I1).ThenBy(k => k.R2).ThenBy(k => k.I2);

    // The paper's Table (leaf_count) — the calculator primitives + the paper's shortest-RPN "Direct search" column.
    // Rows marked timed-out are the ">K" lower bounds whose exhaustive search never completed (past K ≤ 9).
    // Internal: the regrade ladder reads this as the label→reference chart for value-hit lines.
    internal static EmlTarget[] BuildTargets() =>
    [
        // ── constants (the reference ignores x, y) ──
        new("1",     EmlCats.Constant, 1,  false, (x, y) => Complex.One),
        new("0",     EmlCats.Constant, 7,  false, (x, y) => Complex.Zero),
        new("-1",    EmlCats.Constant, 15, false, (x, y) => -Complex.One),
        new("2",     EmlCats.Constant, 19, false, (x, y) => new Complex(2, 0)),
        new("-2",    EmlCats.Constant, 27, false, (x, y) => new Complex(-2, 0)),
        new("1/2",   EmlCats.Constant, 29, false, (x, y) => new Complex(0.5, 0)),
        new("-1/2",  EmlCats.Constant, 31, false, (x, y) => new Complex(-0.5, 0)),
        new("2/3",   EmlCats.Constant, 39, false, (x, y) => new Complex(2.0 / 3, 0)),
        new("-2/3",  EmlCats.Constant, 45, false, (x, y) => new Complex(-2.0 / 3, 0)),
        new("sqrt2", EmlCats.Constant, 47, true,  (x, y) => new Complex(Math.Sqrt(2), 0)),
        new("i",     EmlCats.Constant, 55, true,  (x, y) => Complex.ImaginaryOne),
        new("e",     EmlCats.Constant, 3,  false, (x, y) => new Complex(Math.E, 0)),
        new("pi",    EmlCats.Constant, 53, true,  (x, y) => new Complex(Math.PI, 0)),
        // ── functions (the reference reads x) ──
        new("x",     EmlCats.Function, 9,  false, (x, y) => x),
        new("exp",   EmlCats.Function, 3,  false, (x, y) => Complex.Exp(x)),
        new("ln",    EmlCats.Function, 7,  false, (x, y) => Complex.Log(x)),
        new("neg",   EmlCats.Function, 15, false, (x, y) => -x),
        new("inv",   EmlCats.Function, 15, false, (x, y) => Complex.One / x),
        new("x-1",   EmlCats.Function, 11, false, (x, y) => x - Complex.One),
        new("x+1",   EmlCats.Function, 19, false, (x, y) => x + Complex.One),
        new("x/2",   EmlCats.Function, 27, false, (x, y) => x / 2),
        new("2x",    EmlCats.Function, 19, false, (x, y) => 2 * x),
        new("sqrt",  EmlCats.Function, 43, false, (x, y) => Complex.Sqrt(x)),
        new("x^2",   EmlCats.Function, 17, false, (x, y) => x * x),
        // ── operators (the reference reads x and y) ──
        new("x-y",   EmlCats.Operator, 11, false, (x, y) => x - y),
        new("x+y",   EmlCats.Operator, 19, false, (x, y) => x + y),
        new("x*y",   EmlCats.Operator, 17, false, (x, y) => x * y),
        new("x/y",   EmlCats.Operator, 17, false, (x, y) => x / y),
        new("x^y",   EmlCats.Operator, 25, false, (x, y) => Complex.Pow(x, y)),
        new("log_xy",EmlCats.Operator, 29, false, (x, y) => Complex.Log(y) / Complex.Log(x)),
        new("avg",   EmlCats.Operator, 27, true,  (x, y) => (x + y) / 2),
        new("x^2+y^2",EmlCats.Operator,27, true,  (x, y) => x * x + y * y),
    ];
}

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
//  GENERATION — breadth-first enumeration (the null) + grammar-biased sampling (the spiral)
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

/// The two candidate sources the dream draws from. ENUMERATE walks every well-formed RPN program in breadth-first
/// (length, then lexical) order — the paper's own exhaustive search, and the kill-line's OFF null. SAMPLE assembles
/// programs from the current grammar's CHUNKS (recurring RPN substrings = discovered subroutines) under the stack
/// discipline — the ON arm, where a chunked subroutine re-zeros downstream depth so a few units reach a deep shell
/// (the spiral re-centering). Both are deterministic (enumeration by construction; sampling via the seeded LCG).
public static class EmlGen
{
    private static readonly char[] Terminals = [Eml.One, Eml.VarX, Eml.VarY];
    // The token strings, pre-interned — Sample runs ~batch·steps× per bench and appended `t.ToString()`/`Op.ToString()`
    // a fresh 1-char string every unit; these constants are byte-identical and allocate nothing.
    private static readonly string[] TerminalToks = [Eml.One.ToString(), Eml.VarX.ToString(), Eml.VarY.ToString()];
    private static readonly string OpTok = Eml.Op.ToString();

    /// The sampler's LCG step (Knuth MMIX) — ONE authority for every rng advance in generation (Sample's draws,
    /// EmlSampler's rail fork), so the streams stay bit-identical across refactors of who draws.
    internal static ulong Lcg(ulong s) => s * 6364136223846793005UL + 1442695040888963407UL;

    /// Every well-formed RPN-EML program of odd length in [startLen, maxLen], in breadth-first lexical order. A
    /// program is well-formed iff the stack never underflows and ends with exactly one value; valid lengths are odd
    /// (T leaves force T−1 operators, so length = 2T−1). The exhaustive null — its shells explode combinatorially
    /// (Catalan × 3^T), which is exactly why breadth-first search stalls at shallow K under a fixed budget.
    public static IEnumerable<string> Enumerate(int startLen, int maxLen)
    {
        for (int len = startLen | 1; len <= maxLen; len += 2)            // odd lengths only
            foreach (var prog in OfLength(len))
                yield return prog;
    }

    private static IEnumerable<string> OfLength(int len)
    {
        var buf = new char[len];
        return Build(buf, 0, 0, len);
    }

    // recursive well-formed-RPN generator — `h` is the stack height after `pos` tokens; a terminal raises it, the
    // operator (needs h ≥ 2) lowers it; the base case keeps only the height-1 completions.
    private static IEnumerable<string> Build(char[] buf, int pos, int h, int len)
    {
        if (pos == len) { if (h == 1) yield return new string(buf); yield break; }
        int rem = len - pos - 1;
        foreach (char t in Terminals)
            if (Reachable(h + 1, rem))
            { buf[pos] = t; foreach (var s in Build(buf, pos + 1, h + 1, len)) yield return s; }
        if (h >= 2 && Reachable(h - 1, rem))
        { buf[pos] = Eml.Op; foreach (var s in Build(buf, pos + 1, h - 1, len)) yield return s; }
    }

    // can a run of `rem` more ±1 steps starting at height `h` land on exactly 1 (parity + range prune)?
    private static bool Reachable(int h, int rem) => h - rem <= 1 && h + rem >= 1 && (((h + rem) & 1) == 1);

    /// A grammar CHUNK usable as a generation unit — a pure-RPN rule expansion, its net stack effect, and how many
    /// values it needs on the stack to apply without underflow. `Freq` (the rule's use count) is the sampling
    /// weight, so a frequently-reused subroutine (the learned `1E` = exp-of-top, the ln body, …) is drawn more often.
    public readonly record struct Chunk(string Toks, int Freq, int DeltaH, int MinReq);

    /// Extract the generation chunks from an induced grammar: every rule whose expansion is PURE RPN (over {E,1,x,y}
    /// — the `= LABEL` metadata bytes of a mint line are filtered out), profiled for its stack effect. The grammar
    /// induced over the minted identity corpus surfaces exactly the recurring subroutines; sampling from them is the
    /// grammar-bias that turns discovered structure into deeper reachable expressions.
    public static List<Chunk> PureChunks(RePairResult g)
    {
        var uses = Engine.RuleUses(g);
        var chunks = new List<Chunk>(g.Rules.Length);
        for (int i = 0; i < g.Rules.Length; i++)
        {
            var exp = Reconstruct.Expand(g.Rules, [new Symbol(Symbol.FirstNonterminal + (uint)i)]);
            if (exp.Length < 2 || exp.Length > Eml.MaxProgramLen) continue;
            bool pure = true;
            foreach (var by in exp) if (!Eml.IsToken((char)by)) { pure = false; break; }
            if (!pure) continue;
            var toks = new string(Array.ConvertAll(exp, b => (char)b));
            var (dh, mr) = StackProfile(toks);
            chunks.Add(new Chunk(toks, Math.Max(1, uses[i]), dh, mr));
        }
        return chunks;
    }

    // net stack effect of a chunk + the minimum starting height at which it never underflows.
    internal static (int DeltaH, int MinReq) StackProfile(string toks)
    {
        int h = 0, minReq = 0;
        foreach (char c in toks)
            if (c == Eml.Op) { minReq = Math.Max(minReq, 2 - h); h -= 1; }
            else h += 1;
        return (h, minReq);
    }

    internal static List<EmlClosedSpan> ClosedSpans(string program)
    {
        return EmlTree.TryParseRPN(program, out EmlTree? tree)
            ? tree!.GetClosedSpans()
            : new List<EmlClosedSpan>();
    }

    /// Sample one well-formed RPN program, grammar-biased by `chunks` under THE POLICY-MIX ERGODICITY FLOOR (the
    /// third law-book sibling: bias may CONCENTRATE but never EXCLUDE). The un-mixed sampler was a proven basin
    /// trap — every unit was ONE token or ONE chunk out of `unitsBase` ∈ [2, base] units, so any program outside
    /// the chunk-closure of the machine's own vocabulary was UNREACHABLE (not improbable): the sampler Goodharted
    /// K=119 deep into the exp/ln tower while the value frontier froze at 6/32 bench targets across every ruler.
    /// The floor here is the UNIFORM rail: with probability
    /// `eps` a program assembles from BARE TOKENS only (chunks excluded), its unit count drawn FLAT over
    /// [2, ceiling] — every shell ≤ the maxLen ruler carries positive ε-mass, so every well-formed program has
    /// positive probability. The BIASED rail is untouched ([2, base] units, chunk-weighted) — measured law, not
    /// taste: letting the deep tail ride the biased rail DEEPENED the basin instead of escaping it (600-step arm:
    /// A-grade absorption 2904→4665, false-exact 48.7%→80.6%, bench 8/32→6/32 — the tail handed the canal a
    /// longer drill). eps = 0 is the pure-bias arm, BYTE-IDENTICAL to the pre-mix sampler (no extra rng draws —
    /// the kill-line's control). Units are drawn INSIDE (one shared draw discipline — the two former call sites
    /// duplicated it). Weighting as before: chunk reuse-frequency × `chunkGain` against a flat token weight;
    /// CLOSE folds the stack to one value. The caller re-validates via Eml.Eval, so any bookkeeping slip is
    /// safely discarded, not trusted. This is the two ASSEMBLY rails only — the THIRD rail (the systematic
    /// ε-enumeration sweep, which needs a cursor) forks above this call in EmlSampler, the stateful policy home.
    public static string Sample(List<Chunk> chunks, int unitsBase, int maxLen, int chunkGain, double eps, ref ulong rng,
                                StringBuilder sb, List<(string Toks, int Weight, int DeltaH)> pool)
    {
        rng = Lcg(rng);
        int units = 2 + (int)((rng >> 33) % (ulong)Math.Max(1, unitsBase - 1));
        bool uniform = false;
        if (eps > 0)
        {
            rng = Lcg(rng);
            uniform = (rng >> 33) % 1_000_000 < (ulong)(eps * 1_000_000);   // the ε fork — this program speaks bare tokens
            if (uniform)
            {
                int ceil = (maxLen + 1) / 2;                             // the terminal ceiling — more units cannot fit the ruler
                rng = Lcg(rng);
                units = 2 + (int)((rng >> 33) % (ulong)Math.Max(1, ceil - 1));   // FLAT over [2, ceil] — the equal-mass shell floor
            }
        }

        sb.Clear();                                                      // caller-owned scratch (EmlSampler) — clear-don't-new; Sample is per-candidate hot
        int h = 0;
        for (int u = 0; u < units; u++)
        {
            pool.Clear();
            long total = 0;
            // the candidate pool for this unit — terminals (interned toks) + the bare op + stack-safe chunks, each
            // gated by the length budget. Inlined (no local closure) since Sample runs ~batch·steps× per bench.
            for (int ti = 0; ti < TerminalToks.Length; ti++)
                if (sb.Length + 1 <= maxLen) { pool.Add((TerminalToks[ti], 1, +1)); total += 1; }   // terminals always stack-safe (MinReq 0)
            if (h >= 2 && sb.Length + 1 <= maxLen) { pool.Add((OpTok, 1, -1)); total += 1; }          // the bare operator
            if (!uniform)
                foreach (var c in chunks)
                    if (h >= c.MinReq && sb.Length + c.Toks.Length <= maxLen) { pool.Add((c.Toks, c.Freq * chunkGain, c.DeltaH)); total += c.Freq * chunkGain; }
            if (total == 0) break;                                       // nothing fits the length budget — go close
            rng = Lcg(rng);
            long pick = (long)((rng >> 11) % (ulong)total);
            foreach (var (toks, w, dh) in pool) { pick -= w; if (pick < 0) { sb.Append(toks); h += dh; break; } }
        }
        while (h > 1) { sb.Append(Eml.Op); h -= 1; }                     // close: fold the stack to one value
        if (h == 0) sb.Append(Eml.One);                                  // nothing assembled — emit the base case
        return sb.ToString();
    }
}

/// THE THREE-RAIL CANDIDATE SOURCE — the policy-MIX completed with systematic COVERAGE ('s measure
/// fix). The uniform ε-rail restored SUPPORT (nothing excluded) but spreads its mass Catalan(T−1)·3^T thin per
/// shell — a lottery ticket per deep program — which is why the named census stayed FROZEN under it (bench 6-8/32,
/// the subtraction-face x−y/x−1 at K=11 never drawn) while the pure-enumeration OFF arm, walking shells IN ORDER,
/// hits them a few thousand candidates past the seed shells. This class is that walk woven in as an ε-rail.
/// Rails, outermost fork first:
///   ENUMERATION (mass epsEnum)          the breadth-first shell-sweep CONTINUATION (EmlGen.Enumerate past the
///                                       seed shells, up to the maxLen ruler) — cursored, resumable across steps
///                                       and checkpoints, so the ε-mass CONCENTRATES: every shell gets swept in
///                                       order, with a deadline, not a lottery ticket. Deterministic and
///                                       seed-independent — only WHEN the cursor advances is seed-dependent.
///   UNIFORM (mass (1−epsEnum)·eps)      bare-token assembly, units flat to the ruler ceiling — unordered support
///                                       at every shell (EmlGen.Sample's ε fork).
///   BIAS (the rest)                     chunk-weighted assembly — the depth rail (EmlGen.Sample's main body).
/// epsEnum=0 adds NO rng draws, so epsEnum=0 ∧ eps=0 is byte-identical to the pure-bias sampler (the kill-line's
/// control arm). "Bias may concentrate but never exclude" thus completes: the bias concentrates depth, the
/// uniform floor guarantees support, the sweep GUARANTEES coverage.
internal readonly record struct EmlSamplerCheckpointDelta(ulong Rng, int RailTaken, bool RailDone);

public sealed class EmlSampler
{
    private readonly int _units, _gain, _railStart;
    private int _maxLen;                                 // THE K-RULER — live under the lift organ (RulerLift); immutable when disarmed
    private readonly double _eps, _epsEnum;
    private ulong _rng;
    private IEnumerator<string>? _rail;                  // the systematic continuation (live iff epsEnum > 0 and not dry)
    private int _railTaken;                              // successful MoveNexts — the checkpoint cursor (Load replays it)
    private int _railShell;                              // last program length the rail issued — the sweep's shell read
    private bool _railDone;                              // the ruler swept dry — the ε-mass reverts to assembly
    // Sample's per-call scratch, owned here (the ONE Sample caller) — Next fires ~batch·steps× per bench, so the
    // string-build buffer + candidate pool are reused (clear-don't-new); neither survives past a single Sample call.
    private readonly StringBuilder _sampleSb = new();
    private readonly List<(string Toks, int Weight, int DeltaH)> _samplePool = new();

    /// `seedK` anchors the rail past the shared seed shells (it continues at seedK+2, exactly like the OFF arm's
    /// enumeration); the rail's cap is the `maxLen` RULER itself, not the OFF arm's budget-frame cap — the sweep
    /// is the guarantee that every shell ≤ ruler is eventually crossed.
    public EmlSampler(int units, int maxLen, int gain, double eps, double epsEnum, int seedK, ulong seed)
    {
        _units = units; _maxLen = maxLen; _gain = gain; _eps = eps; _epsEnum = epsEnum;
        _railStart = seedK + 2;
        _rng = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        if (_epsEnum > 0) _rail = EmlGen.Enumerate(_railStart, maxLen).GetEnumerator();
    }

    /// The rail's live cursor (systematic candidates issued · the shell it is sweeping · dry) — the observatory's
    /// coverage read.
    public (int Taken, int Shell, bool Done) RailReads => (_railTaken, _railShell, _railDone);

    internal EmlSamplerCheckpointDelta CaptureCheckpointDelta()
        => new(_rng, _railTaken, _railDone);

    internal void LoadCheckpointDelta(in EmlSamplerCheckpointDelta delta)
    {
        if (delta.RailTaken < 0) throw new InvalidDataException("EML sampler rail cursor is negative");
        _rng = delta.Rng;
        _railTaken = delta.RailTaken;
        _railDone = delta.RailDone;
        _rail = null;
        _railShell = 0;
        if (_epsEnum <= 0 || _railDone) return;
        _rail = EmlGen.Enumerate(_railStart, _maxLen).GetEnumerator();
        for (int i = 0; i < _railTaken; i++)
            if (!_rail.MoveNext()) throw new InvalidDataException("EML sampler rail cursor exceeds enumeration");
        if (_railTaken > 0) _railShell = _rail.Current.Length;
    }

    internal static void WriteCheckpointDelta(CkptWriter writer, in EmlSamplerCheckpointDelta delta)
    {
        writer.U8(1);
        writer.U64(delta.Rng);
        writer.I32(delta.RailTaken);
        writer.Bool(delta.RailDone);
    }

    internal static EmlSamplerCheckpointDelta ReadCheckpointDelta(CkptReader reader)
    {
        if (reader.U8() != 1) throw new InvalidDataException("unknown EML sampler checkpoint delta version");
        ulong rng = reader.U64();
        int railTaken = reader.I32();
        if (railTaken < 0) throw new InvalidDataException("EML sampler rail cursor is negative");
        return new(rng, railTaken, reader.Bool());
    }

    /// The live K-ruler — the sampled-program length cap generation is currently boxed by.
    public int MaxLen => _maxLen;

    /// THE K-RULER LIFT: raise the length cap IN PLACE. The
    /// ruler lives entirely in generation — the sieve, grader, and CAS are ruler-free — so a lift re-opens the
    /// frontier without re-keying one vested certificate (the health invariant's census-closure, by construction).
    /// Lifting only APPENDS shells to the breadth-first walk, so the rail cursor stays valid: rebuild the
    /// enumerator at the wider cap and replay the taken prefix (the Load path's own law); a rail that swept the
    /// OLD ruler dry RE-OPENS — the sweep resumes at the first fresh shell, which is exactly the frontier the
    /// lift exists to re-open.
    public void LiftMaxLen(int maxLen)
    {
        if (maxLen <= _maxLen) return;
        _maxLen = maxLen;
        if (_epsEnum <= 0) return;
        _railDone = false;
        _rail = EmlGen.Enumerate(_railStart, _maxLen).GetEnumerator();
        for (int i = 0; i < _railTaken; i++) _rail.MoveNext();
        if (_railTaken > 0) _railShell = _rail.Current.Length;
    }

    /// Resume-time ruler adoption — assignment ONLY, no rail re-open (Load right after rebuilds the rail with the
    /// checkpointed cursor+dryness at this cap, so a rail that was dry at save STAYS dry — the rng stream must not
    /// gain a fork draw the saved run never made). ReplayCalc.LoadState calls this before _sampler.Load.
    internal void AdoptRuler(int maxLen) => _maxLen = Math.Max(_maxLen, maxLen);

    /// Draw one candidate program under the three-rail policy.
    public string Next(List<EmlGen.Chunk> chunks)
    {
        if (_epsEnum > 0 && !_railDone)
        {
            _rng = EmlGen.Lcg(_rng);
            if ((_rng >> 33) % 1_000_000 < (ulong)(_epsEnum * 1_000_000))   // the enumeration fork — this draw is the sweep's
            {
                if (_rail!.MoveNext())
                {
                    _railTaken++;
                    _railShell = _rail.Current.Length;
                    return _rail.Current;
                }
                _railDone = true; _rail = null;          // dry — this and every later ε-hit falls through to assembly
            }
        }
        return EmlGen.Sample(chunks, _units, _maxLen, _gain, _eps, ref _rng, _sampleSb, _samplePool);
    }

    /// CHECKPOINT — the LCG + the rail cursor. The walk is deterministic, so Load rebuilds the enumerator and
    /// replays the position (the OFF-arm continuation's own pattern); `_railShell` is derived (the cursor's last
    /// program), never stored.
    public void Save(CkptWriter w) { w.U64(_rng); w.I32(_railTaken); w.Bool(_railDone); }

    public void Load(CkptReader r)
    {
        _rng = r.U64(); _railTaken = r.I32(); _railDone = r.Bool();
        _rail = null; _railShell = 0;
        if (_epsEnum <= 0 || _railDone) return;
        _rail = EmlGen.Enumerate(_railStart, _maxLen).GetEnumerator();
        for (int i = 0; i < _railTaken; i++) _rail.MoveNext();
        if (_railTaken > 0) _railShell = _rail.Current.Length;
    }
}
