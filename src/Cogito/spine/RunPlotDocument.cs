using System.Globalization;
using System.Text;

using ScottPlot;

namespace Cogito;

internal sealed class RunPlotDocument
{
    private const int BUCKET_CAPACITY = 642;
    private const int DEFAULT_PANEL_LIMIT = 18;
    private const int PANEL_WIDTH = 800;
    private const int PANEL_HEIGHT = 300;

    private static readonly string[] PreferredColumns =
    [
        // Compute receipts are derived from the typed conservation records. Keep the total,
        // every named phase, and the explicit residual visible in the compute dashboard.
        "step_wall_ms", "prelude_ms", "induce_ms", "harvest_ms", "generate_ms", "read_ms",
        "model_ms", "orchestration_ms", "input_ms", "action_ms", "sleep_ms", "report_ms", "verifier_ms", "policy_boundary_ms", "residual_ms",
        // Anytime-curve diagnostics are deliberately first: the derived plot must keep its frontier,
        // dominator/kill markers, and real wall panel visible even when the dashboard caps panel count.
        "frontier_total", "dominator_marker", "dominated", "kill_marker", "wall_ms",
        "cortex.loom.rules", "cortex.loom.mdl_saved",
        "cortex.homeostat.authority", "cortex.homeostat.shadow_agreement",
        "cortex.homeostat.takeover_executions", "cortex.homeostat.paid_takeovers",
        "eml.evaluator.calls", "eml.census.certs", "eml.frontier.k", "cvz", "meanz", "dream_frac",
        "eml.frontier.residual", "eml.futility.attempts", "eml.execution.affirm_skips",
        "cortex.loom.publish_lag_bytes", "eml.futility.suppressions", "eml.execution.admitted", "eml.hypothesis.cap_skips",
        "cortex.loom.symbols", "eml.targets.train_hit", "eml.census.exact", "eml.census.theorem",
        "coverage", "maxspan", "kz", "criticality", "momentum",
        "vest_rate", "vest_peer", "vest_n0", "dreams_n0", "dreams_peer", "rules", "compressed",
        "residual", "reward", "accuracy", "success_at_commit", "calibration_error",
        "novelty_coverage", "abstention_rate", "churn", "births", "real", "dream",
        "vested", "vest_total"
    ];

    private static readonly string[] IdentityColumns =
    [
        "step", "idx", "index", "id", "prefix_step", "round", "pass", "epoch", "time", "timestamp", "seed"
    ];

    private static readonly string[] Colors =
    [
        "#22d3ee", "#fbbf24", "#f472b6", "#4ade80", "#a78bfa", "#fb7185",
        "#60a5fa", "#a3e635", "#fb923c", "#2dd4bf", "#e879f9", "#facc15"
    ];

    private readonly object _gate = new();
    private readonly string _curvePath;
    private readonly string _outputPath;
    private string[] _columnNames = [];
    private double[][] _minimums = [];
    private double[][] _maximums = [];
    private double[] _xMinimums = [];
    private double[] _xMaximums = [];
    private double[] _latest = [];
    private long[] _numericCounts = [];
    private bool[] _numeric = [];
    private int _xColumn = -1;
    private int _scoredColumn = -1;
    private int _bucketCount;
    private long _bucketSpan = 1;
    private long _bucketRows;
    private long _rowCount;
    private long _physicalRowCount;

    private RunPlotDocument(string runDirectory, string curveFile)
    {
        string fullRunDirectory = Path.GetFullPath(runDirectory);
        string curveName = Path.GetFileName(curveFile);
        string outputName = string.Equals(curveName, "curve.tsv", StringComparison.OrdinalIgnoreCase)
            ? "plots.png"
            : Path.GetFileNameWithoutExtension(curveName) + ".plots.png";
        _curvePath = Path.GetFullPath(Path.Combine(fullRunDirectory, curveFile));
        _outputPath = Path.Combine(fullRunDirectory, outputName);
    }

    public static RunPlotDocument Load(string runDirectory, string curveFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(curveFile);

        RunPlotDocument document = new(runDirectory, curveFile);
        if (File.Exists(document._curvePath)) document.LoadCompleteLines();
        return document;
    }

    public void ObserveLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        lock (_gate)
        {
            if (_columnNames.Length == 0)
            {
                ParseHeader(line.TrimEnd('\r'));
                return;
            }
            ParseRow(line.AsSpan().TrimEnd('\r'));
        }
    }

    public void Render()
    {
        PlotSnapshot snapshot;
        lock (_gate)
        {
            snapshot = CaptureSnapshot();
        }

        ScottPlot.Multiplot dashboard = BuildDashboard(snapshot);
        int columns = snapshot.Series.Length <= 1 ? 1 : 2;
        int rows = Math.Max(1, (snapshot.Series.Length + columns - 1) / columns);
        string temporaryPath = _outputPath + ".tmp." + Environment.ProcessId.ToString(CultureInfo.InvariantCulture)
            + "." + Guid.NewGuid().ToString("N") + ".png";
        try
        {
            dashboard.SavePng(temporaryPath, PANEL_WIDTH * columns, PANEL_HEIGHT * rows);
            File.Move(temporaryPath, _outputPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private void LoadCompleteLines()
    {
        using FileStream stream = new(_curvePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length == 0) return;
        stream.Seek(-1, SeekOrigin.End);
        bool endsWithNewline = stream.ReadByte() == '\n';
        stream.Seek(0, SeekOrigin.Begin);

        using StreamReader reader = new(stream, Encoding.UTF8, true, 16_384, false);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (reader.EndOfStream && !endsWithNewline) break;
            ObserveLine(line);
        }
    }

    private void ParseHeader(string line)
    {
        string[] names = line.Split('\t');
        if (names.Length == 0) return;
        _columnNames = names;
        _minimums = new double[names.Length][];
        _maximums = new double[names.Length][];
        _xMinimums = new double[BUCKET_CAPACITY];
        _xMaximums = new double[BUCKET_CAPACITY];
        Array.Fill(_xMinimums, double.NaN);
        Array.Fill(_xMaximums, double.NaN);
        _latest = new double[names.Length];
        _numericCounts = new long[names.Length];
        _numeric = new bool[names.Length];
        Array.Fill(_latest, double.NaN);
        for (int column = 0; column < names.Length; column++)
        {
            _minimums[column] = new double[BUCKET_CAPACITY];
            _maximums[column] = new double[BUCKET_CAPACITY];
            Array.Fill(_minimums[column], double.NaN);
            Array.Fill(_maximums[column], double.NaN);
            if (_xColumn < 0 && IsIdentityColumn(names[column])) _xColumn = column;
            if (_scoredColumn < 0 && string.Equals(names[column], "scored", StringComparison.OrdinalIgnoreCase)) _scoredColumn = column;
        }
    }

    private void ParseRow(ReadOnlySpan<char> line)
    {
        _physicalRowCount++;
        if (_scoredColumn >= 0 && TryReadNumericField(line, _scoredColumn, out double scored) && scored == 0)
            return;
        if (_bucketCount == BUCKET_CAPACITY) CollapseBuckets();
        int fieldStart = 0;
        for (int column = 0; column < _columnNames.Length; column++)
        {
            int relativeTab = line[fieldStart..].IndexOf('\t');
            int fieldEnd = relativeTab < 0 ? line.Length : fieldStart + relativeTab;
            ReadOnlySpan<char> field = line[fieldStart..fieldEnd];
            if (double.TryParse(field, NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture, out double value) && double.IsFinite(value))
            {
                _numeric[column] = true;
                _numericCounts[column]++;
                _latest[column] = value;
                double minimum = _minimums[column][_bucketCount];
                double maximum = _maximums[column][_bucketCount];
                _minimums[column][_bucketCount] = double.IsFinite(minimum) ? Math.Min(minimum, value) : value;
                _maximums[column][_bucketCount] = double.IsFinite(maximum) ? Math.Max(maximum, value) : value;
                if (column == _xColumn)
                {
                    double xMinimum = _xMinimums[_bucketCount];
                    double xMaximum = _xMaximums[_bucketCount];
                    _xMinimums[_bucketCount] = double.IsFinite(xMinimum) ? Math.Min(xMinimum, value) : value;
                    _xMaximums[_bucketCount] = double.IsFinite(xMaximum) ? Math.Max(xMaximum, value) : value;
                }
            }
            fieldStart = relativeTab < 0 ? line.Length : fieldEnd + 1;
        }

        _rowCount++;
        _bucketRows++;
        if (_bucketRows < _bucketSpan) return;
        _bucketCount++;
        _bucketRows = 0;
    }

    private void CollapseBuckets()
    {
        int collapsedCount = BUCKET_CAPACITY / 2;
        for (int column = 0; column < _columnNames.Length; column++)
        {
            for (int bucket = 0; bucket < collapsedCount; bucket++)
            {
                int left = bucket * 2;
                int right = left + 1;
                _minimums[column][bucket] = FindFiniteMinimum(_minimums[column][left], _minimums[column][right]);
                _maximums[column][bucket] = FindFiniteMaximum(_maximums[column][left], _maximums[column][right]);
            }
            for (int bucket = collapsedCount; bucket < BUCKET_CAPACITY; bucket++)
            {
                _minimums[column][bucket] = double.NaN;
                _maximums[column][bucket] = double.NaN;
            }
        }
        for (int bucket = 0; bucket < collapsedCount; bucket++)
        {
            int left = bucket * 2;
            int right = left + 1;
            _xMinimums[bucket] = FindFiniteMinimum(_xMinimums[left], _xMinimums[right]);
            _xMaximums[bucket] = FindFiniteMaximum(_xMaximums[left], _xMaximums[right]);
        }
        for (int bucket = collapsedCount; bucket < BUCKET_CAPACITY; bucket++)
        {
            _xMinimums[bucket] = double.NaN;
            _xMaximums[bucket] = double.NaN;
        }
        _bucketCount = collapsedCount;
        _bucketSpan *= 2;
    }

    private PlotSnapshot CaptureSnapshot()
    {
        int visibleBucketCount = _bucketCount + (_bucketRows > 0 ? 1 : 0);
        List<int> selectedColumns = SelectColumns();
        PlotSeries[] series = new PlotSeries[selectedColumns.Count];
        for (int index = 0; index < selectedColumns.Count; index++)
        {
            int column = selectedColumns[index];
            double[] x = new double[visibleBucketCount];
            double[] low = new double[visibleBucketCount];
            double[] center = new double[visibleBucketCount];
            double[] high = new double[visibleBucketCount];
            for (int bucket = 0; bucket < visibleBucketCount; bucket++)
            {
                double xMinimum = _xMinimums[bucket];
                double xMaximum = _xMaximums[bucket];
                x[bucket] = double.IsFinite(xMinimum) && double.IsFinite(xMaximum)
                    ? (xMinimum + xMaximum) * 0.5
                    : Math.Min(_rowCount, (bucket + 0.5) * _bucketSpan);
                low[bucket] = _minimums[column][bucket];
                high[bucket] = _maximums[column][bucket];
                center[bucket] = double.IsFinite(low[bucket]) && double.IsFinite(high[bucket])
                    ? (low[bucket] + high[bucket]) * 0.5
                    : double.NaN;
            }
            series[index] = new PlotSeries(_columnNames[column], x, low, center, high, _latest[column]);
        }
        return new PlotSnapshot(Path.GetFileName(_curvePath), _physicalRowCount, _rowCount, _scoredColumn >= 0, _bucketSpan, series);
    }

    internal RunPlotReceipt CaptureReceipt()
    {
        lock (_gate)
        {
            List<RunPlotSeriesReceipt> series = [];
            for (int column = 0; column < _columnNames.Length; column++)
            {
                if (!_numeric[column]) continue;
                double minimum = double.NaN;
                double maximum = double.NaN;
                for (int bucket = 0; bucket < _bucketCount + (_bucketRows > 0 ? 1 : 0); bucket++)
                {
                    minimum = FindFiniteMinimum(minimum, _minimums[column][bucket]);
                    maximum = FindFiniteMaximum(maximum, _maximums[column][bucket]);
                }
                series.Add(new(_columnNames[column], _numericCounts[column], minimum, maximum));
            }
            return new(_physicalRowCount, _rowCount, _scoredColumn >= 0, series.ToArray());
        }
    }

    private List<int> SelectColumns()
    {
        List<int> selected = new(DEFAULT_PANEL_LIMIT);
        for (int preferred = 0; preferred < PreferredColumns.Length && selected.Count < DEFAULT_PANEL_LIMIT; preferred++)
        {
            for (int column = 0; column < _columnNames.Length; column++)
            {
                if (!_numeric[column] || selected.Contains(column)) continue;
                if (!string.Equals(_columnNames[column], PreferredColumns[preferred], StringComparison.OrdinalIgnoreCase)) continue;
                selected.Add(column);
                break;
            }
        }
        for (int column = 0; column < _columnNames.Length && selected.Count < DEFAULT_PANEL_LIMIT; column++)
        {
            if (!_numeric[column] || selected.Contains(column) || IsIdentityColumn(_columnNames[column])) continue;
            selected.Add(column);
        }
        return selected;
    }

    private static ScottPlot.Multiplot BuildDashboard(PlotSnapshot snapshot)
    {
        ScottPlot.Multiplot dashboard = new();
        int panelCount = Math.Max(1, snapshot.Series.Length);
        dashboard.AddPlots(panelCount);
        int columns = panelCount <= 1 ? 1 : 2;
        int rows = (panelCount + columns - 1) / columns;
        dashboard.Layout = new ScottPlot.MultiplotLayouts.Grid(rows, columns);

        if (snapshot.Series.Length == 0)
        {
            ScottPlot.Plot emptyPlot = dashboard.GetPlot(0);
            StylePlot(emptyPlot);
            emptyPlot.Title(snapshot.SourceName + " · no numeric signals");
            return dashboard;
        }

        for (int index = 0; index < snapshot.Series.Length; index++)
        {
            PlotSeries series = snapshot.Series[index];
            ScottPlot.Plot plot = dashboard.GetPlot(index);
            StylePlot(plot);
            ScottPlot.Color color = ScottPlot.Color.FromHex(Colors[index % Colors.Length]);
            ScottPlot.Plottables.Scatter low = plot.Add.ScatterLine(series.X, series.Low);
            ScottPlot.Plottables.Scatter high = plot.Add.ScatterLine(series.X, series.High);
            ScottPlot.Plottables.Scatter center = plot.Add.ScatterLine(series.X, series.Center);
            low.Color = color.WithAlpha(.35);
            high.Color = color.WithAlpha(.35);
            low.LineWidth = 1;
            high.LineWidth = 1;
            center.Color = color;
            center.LineWidth = 2;
            plot.Title(series.Name + " · latest " + series.Latest.ToString("0.######", CultureInfo.InvariantCulture));
            string rowLabel = snapshot.HasScoredColumn
                ? "physical " + snapshot.PhysicalRowCount.ToString("N0", CultureInfo.InvariantCulture) + " · scored " + snapshot.ScoredRowCount.ToString("N0", CultureInfo.InvariantCulture)
                : snapshot.ScoredRowCount.ToString("N0", CultureInfo.InvariantCulture) + " rows";
            plot.XLabel(rowLabel + " · "
                + snapshot.BucketSpan.ToString("N0", CultureInfo.InvariantCulture) + "/bucket");
        }
        return dashboard;
    }

    private static void StylePlot(ScottPlot.Plot plot)
    {
        plot.FigureBackground.Color = ScottPlot.Color.FromHex("#111113");
        plot.DataBackground.Color = ScottPlot.Color.FromHex("#09090b");
        plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#27272a");
        plot.Axes.Color(ScottPlot.Color.FromHex("#d4d4d8"));
    }

    private static bool IsIdentityColumn(string name)
    {
        for (int index = 0; index < IdentityColumns.Length; index++)
            if (string.Equals(name, IdentityColumns[index], StringComparison.OrdinalIgnoreCase)) return true;
        return name.EndsWith("_id", StringComparison.OrdinalIgnoreCase);
    }

    private static double FindFiniteMinimum(double left, double right)
    {
        if (!double.IsFinite(left)) return right;
        if (!double.IsFinite(right)) return left;
        return Math.Min(left, right);
    }

    private static double FindFiniteMaximum(double left, double right)
    {
        if (!double.IsFinite(left)) return right;
        if (!double.IsFinite(right)) return left;
        return Math.Max(left, right);
    }

    private static bool TryReadNumericField(ReadOnlySpan<char> line, int targetColumn, out double value)
    {
        int fieldStart = 0;
        for (int column = 0; column <= targetColumn; column++)
        {
            int relativeTab = line[fieldStart..].IndexOf('\t');
            int fieldEnd = relativeTab < 0 ? line.Length : fieldStart + relativeTab;
            if (column == targetColumn)
            {
                ReadOnlySpan<char> field = line[fieldStart..fieldEnd];
                return double.TryParse(field, NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
            }
            fieldStart = relativeTab < 0 ? line.Length : fieldEnd + 1;
        }
        value = double.NaN;
        return false;
    }

    private readonly record struct PlotSnapshot(string SourceName, long PhysicalRowCount, long ScoredRowCount, bool HasScoredColumn, long BucketSpan, PlotSeries[] Series);
    private readonly record struct PlotSeries(string Name, double[] X, double[] Low, double[] Center, double[] High, double Latest);
}

internal readonly record struct RunPlotSeriesReceipt(string Name, long NumericRows, double Minimum, double Maximum);
internal readonly record struct RunPlotReceipt(long PhysicalRows, long ScoredRows, bool HasScoredColumn, RunPlotSeriesReceipt[] Series);
