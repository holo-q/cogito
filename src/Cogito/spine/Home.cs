namespace Cogito;

using System.Text;

// Home — a scalar probe's comfort zone (homeostasis). It remembers a baseline (Mu) and a natural WIDTH (the
// mean absolute deviation — the degrees of freedom it tolerates). A value INSIDE home is free variation and
// mints nothing. A value OUTSIDE home (beyond k natural widths) MINTS an excursion, and that value becomes
// the NEW home (re-center). The stream of excursions is the system's own dynamics, sparsely encoded; feeding
// it back into the grammar is how cogito learns the grammar of its OWN learning — the genesis idea applied to
// performance/surprise, not just rule mints. (The richest home would be the meta-grammar's own prediction,
// minting on the residual; this EWMA/MAD width is the breathing-but-not-yet-predictive v1.)
public sealed class Home
{
    double _mu, _mad;
    int _seen;
    readonly double _k, _alpha;
    public Home(double k = 2.0, double alpha = 0.25) { _k = k; _alpha = alpha; }

    /// Observe a value. Returns the signed excursion (−1 / +1) if it left home (then re-centers), else 0.
    public int Observe(double v)
    {
        if (_seen++ == 0) { _mu = v; _mad = 0.05 * Math.Abs(v) + 1e-6; return 0; }   // seed a relative noise floor
        double dev = v - _mu;
        if (Math.Abs(dev) > _k * _mad)                         // beyond k natural widths → excursion (a surprise)
        {
            int dir = Math.Sign(dev);
            _mu = v;                                            // the new value becomes the new home (re-center)
            return dir;                                         // mad UNTOUCHED — it tracks the noise floor, not the signal
        }
        _mu += _alpha * dev;                                   // inside home — natural drift of the baseline
        _mad += _alpha * (Math.Abs(dev) - _mad);               // the tolerated width breathes with in-home noise only
        return 0;
    }

    public double Mu => _mu;

    // checkpoint — the comfort zone IS these three numbers (k/alpha are ctor constants, rebuilt).
    public void Save(CkptWriter w) { w.F64(_mu); w.F64(_mad); w.I32(_seen); }
    public void Load(CkptReader r) { _mu = r.F64(); _mad = r.F64(); _seen = r.I32(); }
}

// HomeWatch — one Home per named probe; turns a probe snapshot into an excursion TOKEN (the probes that left
// home this step, with direction), or "" when all quiet. The token stream IS cogito's dynamics, minted sparsely
// — the corpus of its own learning, ready to be induced into a grammar of itself.
public sealed class HomeWatch
{
    readonly (char Code, Home H)[] _probes;
    readonly StringBuilder _excursionScratch = new();
    public HomeWatch(string codes)
    {
        _probes = new (char Code, Home H)[codes.Length];
        for (int i = 0; i < codes.Length; i++) _probes[i] = (codes[i], new Home());
    }

    public string Observe(ReadOnlySpan<double> values)
    {
        _excursionScratch.Clear();
        for (int i = 0; i < _probes.Length; i++)
        {
            double v = values[i];
            if (double.IsNaN(v) || double.IsInfinity(v)) continue;    // a not-yet-defined probe (CvZ before ≥2 scales) never folds — no NaN poison, no spurious excursion
            int e = _probes[i].H.Observe(v);
            if (e != 0) _excursionScratch.Append(_probes[i].Code).Append(e > 0 ? '+' : '-');
        }
        return _excursionScratch.ToString();
    }

    // checkpoint — per-probe homes in code order (the codes string is a ctor constant, identical across runs).
    public void Save(CkptWriter w) { foreach (var (_, h) in _probes) h.Save(w); }
    public void Load(CkptReader r) { foreach (var (_, h) in _probes) h.Load(r); }
}
