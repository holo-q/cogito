namespace Cogito;

internal enum RecursionMarathonClassifications : byte
{
    Incomplete,
    Compounding,
    Plateau,
    Regressing,
    Inconclusive,
    Stable,
    Unstable
}

internal readonly record struct RecursionMetricObservation(
    string Name,
    double Value,
    double BaselineLow,
    double BaselineHigh);

internal sealed class RecursionMarathonWindow
{
    public required int Index { get; init; }
    public required long CompletedUnits { get; init; }
    public required long WallTicks { get; init; }
    public required long CanonicalDeltas { get; init; }
    public required long LawClasses { get; init; }
    public required long ProofAttachments { get; init; }
    public required long FrontierHighWater { get; init; }
    public required long ProcedureReuses { get; init; }
    public required List<RecursionMetricObservation> Stability { get; init; }
}

internal sealed class RecursionLaneClassification
{
    public required RecursionMarathonLanes Lane { get; init; }
    public required RecursionMarathonClassifications Classification { get; init; }
    public required double TheilSenSlope { get; init; }
    public required double BootstrapLow { get; init; }
    public required double BootstrapHigh { get; init; }
    public required double FirstThirdRate { get; init; }
    public required double FinalThirdRate { get; init; }
    public required int StableMetricWindows { get; init; }
    public required int TotalMetricWindows { get; init; }
    public required List<string> Breaches { get; init; }
}

internal static class RecursionMarathonClassifier
{
    public static RecursionLaneClassification ClassifyEML(List<RecursionMarathonWindow> windows, ulong seed)
    {
        ValidateWindows(windows);
        if (windows.Count != RecursionMarathonDefaults.ClassificationWindows)
            return CreateIncomplete(RecursionMarathonLanes.EMLProcedure);

        double[] rates = new double[windows.Count];
        for (int i = 0; i < windows.Count; i++)
        {
            if (windows[i].CompletedUnits <= 0) return CreateIncomplete(RecursionMarathonLanes.EMLProcedure);
            rates[i] = windows[i].CanonicalDeltas * 1_000_000.0 / windows[i].CompletedUnits;
        }

        double slope = ComputeTheilSen(rates);
        (double low, double high) = ComputeBootstrapSlopeInterval(rates, seed);
        double firstThird = (rates[0] + rates[1]) / 2.0;
        double finalThird = (rates[4] + rates[5]) / 2.0;
        RecursionMarathonClassifications classification;
        if (slope > 0 && low > 0 && finalThird > firstThird)
            classification = RecursionMarathonClassifications.Compounding;
        else if (high < 0)
            classification = RecursionMarathonClassifications.Regressing;
        else if (finalThird <= firstThird && windows[5].FrontierHighWater <= windows[1].FrontierHighWater)
            classification = RecursionMarathonClassifications.Plateau;
        else
            classification = RecursionMarathonClassifications.Inconclusive;

        return new RecursionLaneClassification
        {
            Lane = RecursionMarathonLanes.EMLProcedure,
            Classification = classification,
            TheilSenSlope = slope,
            BootstrapLow = low,
            BootstrapHigh = high,
            FirstThirdRate = firstThird,
            FinalThirdRate = finalThird,
            StableMetricWindows = 0,
            TotalMetricWindows = 0,
            Breaches = []
        };
    }

    public static RecursionLaneClassification ClassifyCampfire(List<RecursionMarathonWindow> windows)
    {
        ValidateWindows(windows);
        if (windows.Count != RecursionMarathonDefaults.ClassificationWindows)
            return CreateIncomplete(RecursionMarathonLanes.Campfire);

        int stable = 0;
        int total = 0;
        List<string> breaches = new();
        foreach (RecursionMarathonWindow window in windows)
        {
            foreach (RecursionMetricObservation metric in window.Stability)
            {
                total++;
                if (!double.IsNaN(metric.Value) && metric.Value >= metric.BaselineLow && metric.Value <= metric.BaselineHigh)
                    stable++;
                else
                    breaches.Add($"window={window.Index} metric={metric.Name} value={metric.Value:G17} baseline=[{metric.BaselineLow:G17},{metric.BaselineHigh:G17}]");
            }
        }
        bool holds = total > 0 && stable * 100L >= total * 95L;
        return new RecursionLaneClassification
        {
            Lane = RecursionMarathonLanes.Campfire,
            Classification = holds ? RecursionMarathonClassifications.Stable : RecursionMarathonClassifications.Unstable,
            TheilSenSlope = double.NaN,
            BootstrapLow = double.NaN,
            BootstrapHigh = double.NaN,
            FirstThirdRate = double.NaN,
            FinalThirdRate = double.NaN,
            StableMetricWindows = stable,
            TotalMetricWindows = total,
            Breaches = breaches
        };
    }

    private static void ValidateWindows(List<RecursionMarathonWindow> windows)
    {
        int previous = -1;
        foreach (RecursionMarathonWindow window in windows)
        {
            if (window.Index <= previous) throw new InvalidDataException("marathon windows must be strictly ordered");
            if (window.CompletedUnits < 0 || window.WallTicks <= 0) throw new InvalidDataException("marathon windows require non-negative units and positive wall time");
            previous = window.Index;
        }
    }

    private static RecursionLaneClassification CreateIncomplete(RecursionMarathonLanes lane)
        => new()
        {
            Lane = lane,
            Classification = RecursionMarathonClassifications.Incomplete,
            TheilSenSlope = double.NaN,
            BootstrapLow = double.NaN,
            BootstrapHigh = double.NaN,
            FirstThirdRate = double.NaN,
            FinalThirdRate = double.NaN,
            StableMetricWindows = 0,
            TotalMetricWindows = 0,
            Breaches = ["six complete windows were not available"]
        };

    private static double ComputeTheilSen(double[] values)
    {
        int count = values.Length * (values.Length - 1) / 2;
        double[] slopes = new double[count];
        int cursor = 0;
        for (int i = 0; i < values.Length; i++)
            for (int j = i + 1; j < values.Length; j++)
                slopes[cursor++] = (values[j] - values[i]) / (j - i);
        Array.Sort(slopes);
        return ReadMedian(slopes);
    }

    private static (double Low, double High) ComputeBootstrapSlopeInterval(double[] values, ulong seed)
    {
        double[] slopes = new double[RecursionMarathonDefaults.BootstrapDraws];
        ulong state = seed ^ 0xA0761D6478BD642FUL;
        double[] sample = new double[values.Length];
        for (int draw = 0; draw < slopes.Length; draw++)
        {
            int cursor = 0;
            while (cursor < sample.Length)
            {
                state = NextRandom(state);
                int start = (int)(state % (ulong)(values.Length - 1));
                sample[cursor++] = values[start];
                if (cursor < sample.Length) sample[cursor++] = values[start + 1];
            }
            slopes[draw] = ComputeTheilSen(sample);
        }
        Array.Sort(slopes);
        int lowIndex = (int)Math.Floor(0.025 * (slopes.Length - 1));
        int highIndex = (int)Math.Ceiling(0.975 * (slopes.Length - 1));
        return (slopes[lowIndex], slopes[highIndex]);
    }

    private static ulong NextRandom(ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        ulong mixed = state;
        mixed = (mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL;
        mixed = (mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL;
        return mixed ^ (mixed >> 31);
    }

    private static double ReadMedian(double[] values)
    {
        if (values.Length == 0) return double.NaN;
        int middle = values.Length / 2;
        return values.Length % 2 == 0 ? (values[middle - 1] + values[middle]) / 2.0 : values[middle];
    }
}
