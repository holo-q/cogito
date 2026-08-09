namespace Cogito;

using System.Globalization;

public static partial class AgentSolve
{
    [Flags]
    public enum LocAnchorClasses
    {
        Unanchored = 0,
        TracebackFrame = 1,
        QuotedIdentifier = 2,
        ErrorString = 4,
    }

    public enum LocTargetDepths
    {
        Missing,
        Shallow,
        Middle,
        Deep,
    }

    public readonly record struct LocThermometryRequest(
        string DataDirectory,
        ulong Seed,
        int Limit = 0);

    public readonly record struct LocAnchorCensusRow(
        string Instance,
        LocAnchorClasses Anchors,
        LocTargetDepths TargetDepth);

    public readonly record struct LocAnchorCensus(
        int Total,
        int Bindable,
        List<LocAnchorCensusRow> Instances)
    {
        public double BindableFraction => Total == 0 ? 0.0 : (double)Bindable / Total;
    }

    public readonly record struct LocThermometryMetrics(
        LocAnchorClasses Anchors,
        int Total,
        int Commits,
        int CorrectCommits,
        int Solved,
        double SuccessAtCommit,
        double ActionsToCommit,
        double CalibrationError,
        double ReportedCalibrationError,
        double AbstentionRate,
        int DeepTotal,
        int DeepCorrect)
    {
        public double DeepSuccess => DeepTotal == 0 ? 0.0 : (double)DeepCorrect / DeepTotal;
    }

    public readonly record struct LocThermometryResult(
        int ExitCode,
        string RunDirectory,
        LocAnchorCensus Census,
        LocThermometryMetrics Overall,
        List<LocThermometryMetrics> ByAnchorClass);

    public static LocAnchorCensus CensusAnchors(string dataDirectory, int limit = 0)
    {
        List<string> directories = CollectThermometryDirectories(dataDirectory, limit);
        List<LocAnchorCensusRow> rows = new(directories.Count);
        int bindable = 0;
        for (int i = 0; i < directories.Count; i++)
        {
            string directory = directories[i];
            string query = File.ReadAllText(Path.Combine(directory, "query.txt"));
            LocAnchorClasses anchors = ClassifyAnchors(query);
            if (anchors != LocAnchorClasses.Unanchored) bindable++;
            rows.Add(new LocAnchorCensusRow(
                Path.GetFileName(directory),
                anchors,
                ClassifyThermometryDepth(directory)));
        }
        return new LocAnchorCensus(rows.Count, bindable, rows);
    }

    public static LocThermometryResult RunThermometry(in LocThermometryRequest request)
    {
        List<string> directories = CollectThermometryDirectories(request.DataDirectory, request.Limit);
        LocAnchorCensus census = CensusAnchors(request.DataDirectory, request.Limit);
        Dictionary<string, LocAnchorCensusRow> censusByInstance = new(StringComparer.Ordinal);
        for (int i = 0; i < census.Instances.Count; i++)
            censusByInstance.Add(census.Instances[i].Instance, census.Instances[i]);

        // This is deliberately a frozen mount of the existing full LOC Cortex. The thermometry surface exposes
        // corpus, seed, and sample size, but no bench-directed solver or policy knobs.
        SolveOpts options = new(
            Looks: 8,
            LooksCap: 8,
            Len: 80,
            Sweeps: 2,
            Seed: request.Seed,
            Limit: request.Limit,
            Pretrain: true,
            MeshHomeo: false,
            SiteBudget: 48,
            ConfidenceTrace: false,
            ExplainRank: false,
            MeshFloor: 0.05,
            MeshGain: 0.30,
            MixSpans: 0,
            Passes: 1,
            Interleave: true,
            CheckpointEvery: 25,
            AnswerLeakFree: true,
            ShuffleBindings: false,
            Heldout: 0,
            Revisited: 0);
        directories = Interleave(directories);
        LocCurriculum curriculum = new(directories, options);
        CortexConfig config = BuildSolveConfig(
            curriculum,
            options,
            ComputeSolveStepBudget(directories.Count, options),
            "loc-thermometry");
        Cortex cortex = new(config);
        int exitCode = cortex.Run();
        string runDirectory = cortex.CurrentRun.Dir;
        return ReadThermometryResult(exitCode, runDirectory, census, censusByInstance);
    }

    private static List<string> CollectThermometryDirectories(string dataDirectory, int limit)
    {
        if (!Directory.Exists(dataDirectory))
            throw new DirectoryNotFoundException($"LOC thermometry corpus not found: {dataDirectory}");
        if (limit < 0) throw new ArgumentOutOfRangeException(nameof(limit), limit, "thermometry limit cannot be negative");
        List<string> directories = Directory.GetDirectories(dataDirectory)
            .Where(static directory =>
                File.Exists(Path.Combine(directory, "query.txt"))
                && File.Exists(Path.Combine(directory, "sites.jsonl"))
                && File.Exists(Path.Combine(directory, "gold.json")))
            .OrderBy(static directory => Path.GetFileName(directory), StringComparer.Ordinal)
            .ToList();
        if (limit > 0 && directories.Count > limit) directories.RemoveRange(limit, directories.Count - limit);
        if (directories.Count == 0)
            throw new InvalidDataException($"LOC thermometry corpus has no complete instances: {dataDirectory}");
        return directories;
    }

    private static LocAnchorClasses ClassifyAnchors(string query)
    {
        LocAnchorClasses anchors = LocAnchorClasses.Unanchored;
        if (ContainsTracebackFrame(query)) anchors |= LocAnchorClasses.TracebackFrame;
        if (ContainsQuotedIdentifier(query)) anchors |= LocAnchorClasses.QuotedIdentifier;
        if (ContainsErrorString(query)) anchors |= LocAnchorClasses.ErrorString;
        return anchors;
    }

    private static bool ContainsTracebackFrame(string query)
        => query.Contains("Traceback (most recent call last)", StringComparison.Ordinal)
           || (query.Contains("File \"", StringComparison.Ordinal)
               && query.Contains(", line ", StringComparison.Ordinal));

    private static bool ContainsQuotedIdentifier(string query)
    {
        for (int start = 0; start < query.Length; start++)
        {
            char quote = query[start];
            if (quote is not ('\'' or '"' or '`')) continue;
            int end = query.IndexOf(quote, start + 1);
            if (end < 0) return false;
            if (IsIdentifier(query.AsSpan(start + 1, end - start - 1))) return true;
            start = end;
        }
        return false;
    }

    private static bool IsIdentifier(ReadOnlySpan<char> value)
    {
        if (value.Length == 0) return false;
        bool hasNameCharacter = false;
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            if (char.IsLetter(character) || character == '_') hasNameCharacter = true;
            else if (!char.IsDigit(character) && character is not ('.' or ':' or '-')) return false;
        }
        return hasNameCharacter;
    }

    private static bool ContainsErrorString(string query)
    {
        ReadOnlySpan<char> remaining = query.AsSpan();
        while (!remaining.IsEmpty)
        {
            int newline = remaining.IndexOf('\n');
            ReadOnlySpan<char> line = newline < 0 ? remaining : remaining[..newline];
            if (line.Contains("error:", StringComparison.OrdinalIgnoreCase)
                || line.Contains("exception:", StringComparison.OrdinalIgnoreCase)
                || line.Contains("failed:", StringComparison.OrdinalIgnoreCase)) return true;
            if (newline < 0) break;
            remaining = remaining[(newline + 1)..];
        }
        return false;
    }

    private static LocTargetDepths ClassifyThermometryDepth(string instanceDirectory)
    {
        List<Tool.SiteRow> sites = Tool.LoadSites(Path.Combine(instanceDirectory, "sites.jsonl"));
        if (sites.Count == 0) return LocTargetDepths.Missing;
        string gold = LoadGoldFile(Path.Combine(instanceDirectory, "gold.json"));
        int index = sites.FindIndex(site => string.Equals(site.Path, gold, StringComparison.Ordinal));
        if (index < 0) return LocTargetDepths.Missing;
        int third = Math.Max(1, sites.Count / 3);
        if (index < third) return LocTargetDepths.Shallow;
        return index < 2 * third ? LocTargetDepths.Middle : LocTargetDepths.Deep;
    }

    private static LocThermometryResult ReadThermometryResult(
        int exitCode,
        string runDirectory,
        LocAnchorCensus census,
        Dictionary<string, LocAnchorCensusRow> censusByInstance)
    {
        string curvePath = Path.Combine(runDirectory, "loc_curve.tsv");
        if (!File.Exists(curvePath)) throw new InvalidDataException($"LOC thermometry run did not emit {curvePath}");
        Dictionary<LocAnchorClasses, ThermometryAccumulator> accumulators = new();
        ThermometryAccumulator overall = new(LocAnchorClasses.Unanchored);
        int lineNumber = 0;
        foreach (string line in File.ReadLines(curvePath))
        {
            lineNumber++;
            if (lineNumber == 1) continue;
            if (line.Length == 0) continue;
            string[] columns = line.Split('\t');
            if (columns.Length != 16)
                throw new InvalidDataException($"LOC thermometry curve row {lineNumber} has {columns.Length} columns, expected 16");
            string instance = columns[1];
            if (!censusByInstance.TryGetValue(instance, out LocAnchorCensusRow censusRow))
                throw new InvalidDataException($"LOC thermometry curve names uncensused instance {instance}");
            bool committed = ParseCurveBool(columns[3], lineNumber, "committed");
            bool correct = ParseCurveBool(columns[4], lineNumber, "correct");
            int actions = ParseCurveInt(columns[7], lineNumber, "actions");
            double confidence = ParseCurveDouble(columns[8], lineNumber, "confidence");
            double reportedCalibration = ParseCurveDouble(columns[9], lineNumber, "calibration_error");
            if (!accumulators.TryGetValue(censusRow.Anchors, out ThermometryAccumulator? accumulator))
            {
                accumulator = new ThermometryAccumulator(censusRow.Anchors);
                accumulators.Add(censusRow.Anchors, accumulator);
            }
            accumulator.Add(committed, correct, actions, confidence, reportedCalibration, censusRow.TargetDepth);
            overall.Add(committed, correct, actions, confidence, reportedCalibration, censusRow.TargetDepth);
        }

        List<LocThermometryMetrics> splits = new(accumulators.Count);
        foreach (KeyValuePair<LocAnchorClasses, ThermometryAccumulator> row in accumulators.OrderBy(static row => (int)row.Key))
            splits.Add(row.Value.Finish());
        return new LocThermometryResult(exitCode, runDirectory, census, overall.Finish(), splits);
    }

    private static bool ParseCurveBool(string value, int line, string column)
    {
        int parsed = ParseCurveInt(value, line, column);
        if (parsed is 0 or 1) return parsed == 1;
        throw new InvalidDataException($"LOC thermometry curve row {line} has invalid {column} value {value}");
    }

    private static int ParseCurveInt(string value, int line, string column)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) return parsed;
        throw new InvalidDataException($"LOC thermometry curve row {line} has invalid {column} value {value}");
    }

    private static double ParseCurveDouble(string value, int line, string column)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            && double.IsFinite(parsed)) return parsed;
        throw new InvalidDataException($"LOC thermometry curve row {line} has invalid {column} value {value}");
    }

    private sealed class ThermometryAccumulator
    {
        private readonly LocAnchorClasses _anchors;
        private int _total;
        private int _commits;
        private int _correctCommits;
        private int _solved;
        private int _actions;
        private double _calibration;
        private double _reportedCalibration;
        private int _deepTotal;
        private int _deepCorrect;

        public ThermometryAccumulator(LocAnchorClasses anchors) => _anchors = anchors;

        public void Add(bool committed, bool correct, int actions, double confidence,
            double reportedCalibration, LocTargetDepths depth)
        {
            _total++;
            if (correct) _solved++;
            _reportedCalibration += reportedCalibration;
            if (committed)
            {
                _commits++;
                _actions += actions;
                if (correct) _correctCommits++;
                _calibration += Math.Abs(confidence - (correct ? 1.0 : 0.0));
            }
            if (depth != LocTargetDepths.Deep) return;
            _deepTotal++;
            if (correct) _deepCorrect++;
        }

        public LocThermometryMetrics Finish()
            => new(
                _anchors,
                _total,
                _commits,
                _correctCommits,
                _solved,
                _commits == 0 ? 0.0 : (double)_correctCommits / _commits,
                _commits == 0 ? 0.0 : (double)_actions / _commits,
                _commits == 0 ? 0.0 : _calibration / _commits,
                _total == 0 ? 0.0 : _reportedCalibration / _total,
                _total == 0 ? 0.0 : (double)(_total - _commits) / _total,
                _deepTotal,
                _deepCorrect);
    }
}
