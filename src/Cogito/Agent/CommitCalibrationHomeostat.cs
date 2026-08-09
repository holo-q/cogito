namespace Cogito;

// ── COMMITCALIBRATIONHOMEOSTAT ──  the solve stream's commit boundary is regulated, never authored.
// Target: calibration error -> 0, where calibration error is accuracy-at-commit minus confidence-at-commit.
internal enum NoveltyBands
{
    Novel = 0,
    Edge = 1,
    Familiar = 2,
}

internal readonly record struct CommitCalibrationRead(NoveltyBands Band, string Label, double Coverage, double Floor);

internal enum CommitCalibrationActions : byte { Continue, Commit }

internal enum CommitCalibrationMetricIDs : ushort
{
    Confidence = 800,
    Coverage,
    Floor,
    Band,
    Commits,
    CalibrationError,
    SuccessAtCommit,
    AbstentionRate,
    Correct = 820,
    Committed,
    AbsoluteCalibrationError,
    TerminalActions,
}

internal readonly record struct CommitCalibrationChoice(
    CommitCalibrationRead Read,
    CortexPolicyDecision PolicyDecision,
    CommitCalibrationActions Action,
    double Confidence,
    int Look);

internal sealed class CommitCalibrationHomeostat
{
    public static CortexPolicyID PolicyID { get; } = CortexPolicyID.Parse("loc.commit-calibration");
    public static CortexPolicySchema PolicySchema { get; } = new(
        PolicyID,
        featureCount: 8,
        actionCount: 2,
        outcomeCount: 4,
        authorityCeiling: CortexPolicyModes.Autonomic,
        admission: CortexPolicyAdmissionKinds.Verified);

    public const double CalibrationResolutionTarget = 0.05; // one commit may move accuracy by at most 5 percentage points
    public static int SampleFloor => (int)Math.Ceiling(1.0 / CalibrationResolutionTarget);

    private readonly BandCell[] _bands =
    [
        new(NoveltyBands.Novel),
        new(NoveltyBands.Edge),
        new(NoveltyBands.Familiar),
    ];
    private readonly List<double> _coverageDistribution = new();
    private readonly List<(double Confidence, bool Correct, int Actions)> _samples = new();
    private NoveltyBands _lastBand = NoveltyBands.Edge;

    public int Commits { get { int n = 0; foreach (var b in _bands) n += b.Commits; return n; } }
    public int Correct { get { int n = 0; foreach (var b in _bands) n += b.Correct; return n; } }
    public int Abstains { get { int n = 0; foreach (var b in _bands) n += b.Abstains; return n; } }
    public int Total => Commits + Abstains;
    public double CalibrationError => SuccessAtCommit - ConfidenceAtCommit;
    public double ConfidenceFloor => ConservativeFloor();

    public CommitCalibrationRead Read(double coverage)
    {
        coverage = CleanCoverage(coverage);
        var band = BandForCoverage(coverage);
        return new CommitCalibrationRead(band, LabelOf(band), coverage, FloorFor(band));
    }

    public CommitCalibrationChoice Choose(Cortex cortex, double confidence, double coverage, int look)
    {
        CommitCalibrationRead read = Read(coverage);
        CommitCalibrationActions launchpad = confidence >= read.Floor
            ? CommitCalibrationActions.Commit
            : CommitCalibrationActions.Continue;
        Span<MetricSample> features = stackalloc MetricSample[8]
        {
            new(new MetricID((ushort)CommitCalibrationMetricIDs.Confidence), NumericValue.FromF64(confidence)),
            new(new MetricID((ushort)CommitCalibrationMetricIDs.Coverage), NumericValue.FromF64(read.Coverage)),
            new(new MetricID((ushort)CommitCalibrationMetricIDs.Floor), NumericValue.FromF64(read.Floor)),
            new(new MetricID((ushort)CommitCalibrationMetricIDs.Band), NumericValue.FromI64((int)read.Band)),
            new(new MetricID((ushort)CommitCalibrationMetricIDs.Commits), NumericValue.FromI64(Commits)),
            new(new MetricID((ushort)CommitCalibrationMetricIDs.CalibrationError), NumericValue.FromF64(CalibrationError)),
            new(new MetricID((ushort)CommitCalibrationMetricIDs.SuccessAtCommit), NumericValue.FromF64(SuccessAtCommit)),
            new(new MetricID((ushort)CommitCalibrationMetricIDs.AbstentionRate), NumericValue.FromF64(AbstentionRate)),
        };
        CortexPolicyDecision decision = cortex.ChoosePolicyAction(PolicyID, (int)launchpad, features);
        _lastBand = read.Band;
        return new CommitCalibrationChoice(read, decision, (CommitCalibrationActions)decision.Action, confidence, look);
    }

    public static void Resolve(Cortex cortex, in CommitCalibrationChoice choice, bool committed, bool correct, int terminalActions, bool invariantClean)
    {
        double calibrationError = committed ? Math.Abs((correct ? 1.0 : 0.0) - choice.Confidence) : 0;
        Span<MetricSample> outcomes = stackalloc MetricSample[4]
        {
            new(new MetricID((ushort)CommitCalibrationMetricIDs.Correct), NumericValue.FromI64(correct ? 1 : 0)),
            new(new MetricID((ushort)CommitCalibrationMetricIDs.Committed), NumericValue.FromI64(committed ? 1 : 0)),
            new(new MetricID((ushort)CommitCalibrationMetricIDs.AbsoluteCalibrationError), NumericValue.FromF64(calibrationError)),
            new(new MetricID((ushort)CommitCalibrationMetricIDs.TerminalActions), NumericValue.FromI64(terminalActions)),
        };
        CortexPolicyDecision decision = choice.PolicyDecision;
        cortex.ResolvePolicyOutcome(in decision, outcomes, invariantClean,
            conservedCost: Math.Max(0, terminalActions - choice.Look));
    }

    public void ObserveCommit(double confidence, bool correct, int actions, double coverage)
    {
        confidence = Math.Clamp(confidence, 0.0, 1.0);
        coverage = CleanCoverage(coverage);
        var band = BandForCoverage(coverage);
        _lastBand = band;
        _coverageDistribution.Add(coverage);
        _bands[(int)band].ObserveCommit(confidence, correct, actions);
        _samples.Add((confidence, correct, actions));
    }

    public void ObserveAbstain(int actions, double coverage)
    {
        coverage = CleanCoverage(coverage);
        var band = BandForCoverage(coverage);
        _lastBand = band;
        _coverageDistribution.Add(coverage);
        _bands[(int)band].ObserveAbstain(actions);
    }

    public double SuccessAtCommit => Commits > 0 ? (double)Correct / Commits : 0.0;
    public double ConfidenceAtCommit
    {
        get
        {
            int commits = 0;
            double confidence = 0;
            foreach (var b in _bands) { commits += b.Commits; confidence += b.ConfidenceSum; }
            return commits > 0 ? confidence / commits : 0.0;
        }
    }
    public double AbstentionRate => Total > 0 ? (double)Abstains / Total : 0.0;
    public double ActionsToCommit
    {
        get
        {
            int commits = 0, actions = 0;
            foreach (var b in _bands) { commits += b.Commits; actions += b.ActionsToCommitSum; }
            return commits > 0 ? (double)actions / commits : 0.0;
        }
    }

    public string CurveVerdict()
    {
        if (_samples.Count < 4) return "calibration-curve warming (need >=4 commits)";
        int mid = _samples.Count / 2;
        double early = CalibrationAbsError(_samples, 0, mid);
        double late = CalibrationAbsError(_samples, mid, _samples.Count - mid);
        return $"calibration-curve |err| early={early:F3} late={late:F3} => {(late < early ? "TIGHTENING" : late == early ? "flat" : "DIVERGING")}";
    }

    public string Line()
    {
        double abstainActions = Abstains > 0 ? (double)AbstainActionsSum() / Abstains : 0.0;
        return $"  COMMIT-GATE · novelty-conditioned setpoint calibration error -> 0 · success@commit {Correct}/{Commits} ({100.0 * SuccessAtCommit:F1}%)"
             + $" · sample-floor {Commits}/{SampleFloor}"
             + $" · confidence@commit {ConfidenceAtCommit:F3} · calibration-error {CalibrationError:+0.000;-0.000;0.000}"
             + $" · abstention {Abstains}/{Total} ({100.0 * AbstentionRate:F1}%)"
             + $" · actions-to-commit {ActionsToCommit:F2}"
             + (Abstains > 0 ? $" · abstain-actions {abstainActions:F2}" : "")
             + $" · active-band {LabelOf(_lastBand)} floor {FloorFor(_lastBand):F3} · bands {FloorSummary()} · {CurveVerdict()}";
    }

    public void Save(CkptWriter w)
    {
        w.I32((int)_lastBand);
        w.I32(_coverageDistribution.Count);
        foreach (double c in _coverageDistribution) w.F64(c);
        w.I32(_bands.Length);
        foreach (var b in _bands) b.Save(w);
        w.I32(_samples.Count);
        foreach (var s in _samples) { w.F64(s.Confidence); w.Bool(s.Correct); w.I32(s.Actions); }
    }

    public void Load(CkptReader r)
    {
        _lastBand = (NoveltyBands)r.I32();
        _coverageDistribution.Clear();
        int nc = r.I32();
        for (int i = 0; i < nc; i++) _coverageDistribution.Add(r.F64());
        int nb = r.I32();
        if (nb != _bands.Length) throw new InvalidDataException($"commit calibration band count drifted ({nb} != {_bands.Length})");
        for (int i = 0; i < nb; i++) _bands[i].Load(r);
        _samples.Clear();
        int n = r.I32();
        for (int i = 0; i < n; i++) _samples.Add((r.F64(), r.Bool(), r.I32()));
    }

    public string FloorSummary()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var b in _bands)
        {
            if (sb.Length > 0) sb.Append("; ");
            sb.Append(LabelOf(b.Band)).Append(' ')
              .Append(b.Commits).Append('/').Append(SampleFloor)
              .Append(" floor ").Append(FloorFor(b.Band).ToString("F3"))
              .Append(" s@c ").Append((100.0 * b.SuccessAtCommit).ToString("F1")).Append('%');
        }
        return sb.ToString();
    }

    public static string LabelOf(NoveltyBands band)
        => band switch
        {
            NoveltyBands.Novel => "q0-novel",
            NoveltyBands.Edge => "q1-edge",
            _ => "q2-familiar",
        };

    private NoveltyBands BandForCoverage(double coverage)
    {
        if (_coverageDistribution.Count == 0) return NoveltyBands.Edge;
        int lower = 0;
        foreach (double seen in _coverageDistribution)
            if (seen < coverage) lower++;
        double rank = (lower + 0.5) / (_coverageDistribution.Count + 1.0);
        int idx = Math.Clamp((int)Math.Floor(rank * _bands.Length), 0, _bands.Length - 1);
        return (NoveltyBands)idx;
    }

    private double FloorFor(NoveltyBands band)
    {
        var cell = _bands[(int)band];
        if (!double.IsNaN(cell.Floor)) return cell.Floor;
        return Total == 0 ? 0.0 : 1.0 - CalibrationResolutionTarget;
    }

    private double ConservativeFloor()
    {
        if (Total == 0) return 0.0;
        double floor = 1.0 - CalibrationResolutionTarget;
        foreach (var b in _bands)
            if (!double.IsNaN(b.Floor) && b.Floor > floor)
                floor = b.Floor;
        return Math.Clamp(floor, 0.0, 1.0);
    }

    private static double CleanCoverage(double coverage)
        => double.IsNaN(coverage) || double.IsInfinity(coverage) ? 0.0 : Math.Clamp(coverage, 0.0, 1.0);

    private int AbstainActionsSum()
    {
        int actions = 0;
        foreach (var b in _bands) actions += b.AbstainActionsSum;
        return actions;
    }

    private static double CalibrationAbsError(List<(double Confidence, bool Correct, int Actions)> samples, int start, int count)
    {
        if (count <= 0) return 0.0;
        int correct = 0; double conf = 0;
        for (int i = start; i < start + count; i++)
        {
            if (samples[i].Correct) correct++;
            conf += samples[i].Confidence;
        }
        return Math.Abs((double)correct / count - conf / count);
    }

    private sealed class BandCell
    {
        public readonly NoveltyBands Band;
        public double Floor = double.NaN;
        public int Commits, Correct, Abstains, ActionsToCommitSum, AbstainActionsSum;
        public double ConfidenceSum, LastError;

        public BandCell(NoveltyBands band) => Band = band;

        public double SuccessAtCommit => Commits > 0 ? (double)Correct / Commits : 0.0;
        public double ConfidenceAtCommit => Commits > 0 ? ConfidenceSum / Commits : 0.0;

        public void ObserveCommit(double confidence, bool correct, int actions)
        {
            if (double.IsNaN(Floor)) Floor = confidence;
            Commits++;
            if (correct) Correct++;
            ConfidenceSum += confidence;
            ActionsToCommitSum += actions;

            LastError = SuccessAtCommit - ConfidenceAtCommit;
            double gain = 1.0 / Math.Sqrt(Commits);
            Floor = Math.Clamp(Floor - LastError * gain, 0.0, 1.0);
        }

        public void ObserveAbstain(int actions)
        {
            Abstains++;
            AbstainActionsSum += actions;
        }

        public void Save(CkptWriter w)
        {
            w.I32((int)Band); w.F64(Floor); w.I32(Commits); w.I32(Correct); w.I32(Abstains);
            w.I32(ActionsToCommitSum); w.I32(AbstainActionsSum); w.F64(ConfidenceSum); w.F64(LastError);
        }

        public void Load(CkptReader r)
        {
            var band = (NoveltyBands)r.I32();
            if (band != Band) throw new InvalidDataException($"commit calibration band order drifted ({band} != {Band})");
            Floor = r.F64(); Commits = r.I32(); Correct = r.I32(); Abstains = r.I32();
            ActionsToCommitSum = r.I32(); AbstainActionsSum = r.I32(); ConfidenceSum = r.F64(); LastError = r.F64();
        }
    }
}
