namespace Cogito;

using System.Globalization;
using System.Text;

public sealed class CortexReadoutConfig
{
    /// Null means fresh default selection; an empty array is an explicit empty
    /// readout and must remain empty when a checkpoint rebuilds the runtime.
    public string[]? Curve { get; init; }

    internal static string[] CreateDefaultCurve(bool includesEml, bool includesEmlActions)
    {
        string[] core =
        [
            "cortex.loom.rules", "cortex.loom.symbols", "cortex.loom.mdl_saved", "cortex.loom.publish_lag_bytes",
            "cortex.tape.resident", "cortex.tape.shed", "cortex.tape.execution", "cortex.tape.born_evidence",
            "cortex.tape.unreflected_dreams",
            "cortex.homeostat.authority", "cortex.homeostat.cached_contexts", "cortex.homeostat.shadow_agreement",
            "cortex.homeostat.takeover_executions", "cortex.homeostat.paid_takeovers", "cortex.homeostat.repromotions",
        ];
        if (!includesEml) return core;
        string[] eml =
            [
                .. core,
                "eml.evaluator.calls", "eml.targets.train_hit", "eml.census.exact",
                "eml.census.theorem", "eml.census.certs", "eml.frontier.k",
            ];
        return includesEmlActions
            ?
            [
                .. eml,
                "eml.frontier.residual",
                "eml.futility.attempts", "eml.futility.suppressions", "eml.futility.suppressed_calls",
                "eml.execution.admitted", "eml.execution.affirm_skips",
                "eml.hypothesis.cap_skips",
            ]
            : eml;
    }
}

public sealed class CogitoWorkspace
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly List<string> _keys = new();

    public IReadOnlyList<string> Keys => _keys;

    public void Define(params string[] keys)
    {
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("workspace key cannot be blank", nameof(keys));
            if (_values.ContainsKey(key)) continue;
            _values[key] = "";
            _keys.Add(key);
        }
    }

    public void Post(string key, int value) => Post(key, value.ToString(CultureInfo.InvariantCulture));
    public void Post(string key, long value) => Post(key, value.ToString(CultureInfo.InvariantCulture));
    public void Post(string key, double value) => Post(key, double.IsNaN(value) ? "nan" : value.ToString("G17", CultureInfo.InvariantCulture));
    public void Post(string key, bool value) => Post(key, value ? "1" : "0");

    public void Post(string key, string value)
    {
        if (!_values.ContainsKey(key)) Define(key);
        _values[key] = value;
    }

    public void RequireKey(string key)
    {
        if (!_values.ContainsKey(key)) throw new ArgumentException($"workspace selector '{key}' is not defined", nameof(key));
    }

    public bool TryReadDouble(string key, out double value)
    {
        value = 0;
        return _values.TryGetValue(key, out string? text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && !double.IsNaN(value);
    }

    public CogitoReadout Select(string? selectors) => Select(string.IsNullOrWhiteSpace(selectors) ? null : [selectors]);

    public CogitoReadout Select(IEnumerable<string>? selectors)
    {
        var selected = new List<string>();
        foreach (var selector in ExpandSelectors(selectors))
        {
            if (selector.EndsWith('*'))
            {
                string prefix = selector[..^1];
                int before = selected.Count;
                foreach (var key in _keys)
                    if (key.StartsWith(prefix, StringComparison.Ordinal) && !selected.Contains(key, StringComparer.Ordinal))
                        selected.Add(key);
                if (selected.Count == before)
                    throw new ArgumentException($"workspace selector '{selector}' matched no keys");
            }
            else
            {
                if (!_values.ContainsKey(selector))
                    throw new ArgumentException($"workspace selector '{selector}' is not defined");
                if (!selected.Contains(selector, StringComparer.Ordinal)) selected.Add(selector);
            }
        }
        return new CogitoReadout(this, selected.ToArray());
    }

    internal string RowSuffix(IReadOnlyList<string> keys)
    {
        if (keys.Count == 0) return "";
        var sb = new StringBuilder();
        foreach (var key in keys)
        {
            sb.Append('\t');
            if (_values.TryGetValue(key, out var value)) sb.Append(value);
        }
        return sb.ToString();
    }

    private static IEnumerable<string> ExpandSelectors(IEnumerable<string>? selectors)
    {
        if (selectors is null) yield break;
        foreach (var raw in selectors)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            foreach (var part in raw.Split(','))
            {
                var selector = part.Trim();
                if (selector.Length > 0) yield return selector;
            }
        }
    }
}

public sealed class CogitoReadout(CogitoWorkspace workspace, string[] keys)
{
    public string HeaderSuffix => keys.Length == 0 ? "" : "\t" + string.Join('\t', keys);
    public string RowSuffix() => workspace.RowSuffix(keys);
}
