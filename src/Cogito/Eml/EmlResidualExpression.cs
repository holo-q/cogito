namespace Cogito;

using System.Globalization;
using System.Numerics;
using System.Text;

public enum EmlResidualExpressionKinds
{
    FiniteRPN,
    ProcessFunction,
    EGate,
}

public readonly struct EmlResidualExpressionEvaluation
{
    public EmlResidualExpressionEvaluation(EmlLadder p1, EmlLadder p2, EmlLadder p3, long processFuelConsumed)
    {
        P1 = p1;
        P2 = p2;
        P3 = p3;
        ProcessFuelConsumed = processFuelConsumed;
    }

    public EmlLadder P1 { get; }
    public EmlLadder P2 { get; }
    public EmlLadder P3 { get; }
    public long ProcessFuelConsumed { get; }
}

public sealed class EmlResidualExpression
{
    private readonly EmlTree? _finiteTree;
    private readonly EmlProcessFunction _processFunction;
    private readonly EmlResidualExpression? _left;
    private readonly EmlResidualExpression? _right;

    private EmlResidualExpression(EmlTree finiteTree)
    {
        Kind = EmlResidualExpressionKinds.FiniteRPN;
        _finiteTree = finiteTree;
        StructuralCost = finiteTree.RenderRPN().Length;
    }

    private EmlResidualExpression(in EmlProcessFunction processFunction)
    {
        Kind = EmlResidualExpressionKinds.ProcessFunction;
        _processFunction = processFunction;
        StructuralCost = 1;
        BearsProcess = true;
    }

    private EmlResidualExpression(EmlResidualExpression left, EmlResidualExpression right)
    {
        Kind = EmlResidualExpressionKinds.EGate;
        _left = left;
        _right = right;
        StructuralCost = checked(1 + left.StructuralCost + right.StructuralCost);
        BearsProcess = left.BearsProcess || right.BearsProcess;
    }

    public EmlResidualExpressionKinds Kind { get; }
    public int StructuralCost { get; }
    public bool BearsProcess { get; }

    public static EmlResidualExpression CreateFiniteRPN(string rpn)
    {
        EmlTree tree = EmlTree.ParseRPN(rpn);
        return new EmlResidualExpression(tree);
    }

    public static EmlResidualExpression CreateProcessFunction(in EmlProcessFunction processFunction)
        => new EmlResidualExpression(in processFunction);

    public static EmlResidualExpression CreateEGate(EmlResidualExpression left, EmlResidualExpression right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return new EmlResidualExpression(left, right);
    }

    public static EmlResidualExpression ParseCanonical(string canonical)
    {
        if (!TryParseCanonical(canonical, out EmlResidualExpression? expression))
            throw new FormatException("invalid EML residual expression canonical form");
        return expression!;
    }

    public static bool TryParseCanonical(string? canonical, out EmlResidualExpression? expression)
    {
        try
        {
            expression = ParseCanonicalNode(canonical ?? throw new ArgumentNullException(nameof(canonical)));
            return true;
        }
        catch (Exception error) when (error is ArgumentException or FormatException or InvalidDataException)
        {
            expression = null;
            return false;
        }
    }

    private static EmlResidualExpression ParseCanonicalNode(string canonical)
    {
        if (canonical.StartsWith("rpn[", StringComparison.Ordinal) && canonical.EndsWith(']'))
            return CreateFiniteRPN(canonical[4..^1]);
        if (canonical.StartsWith("process[", StringComparison.Ordinal) && canonical.EndsWith(']'))
            return CreateProcessFunction(ParseProcessCanonical(canonical[8..^1]));
        if (canonical.StartsWith("E(", StringComparison.Ordinal) && canonical.EndsWith(')'))
        {
            string body = canonical[2..^1];
            int comma = FindTopLevelComma(body);
            if (comma < 1 || comma >= body.Length - 1)
                throw new FormatException("EML residual gate requires two canonical children");
            return CreateEGate(
                ParseCanonicalNode(body[..comma]),
                ParseCanonicalNode(body[(comma + 1)..]));
        }
        throw new FormatException("unknown EML residual expression canonical form");
    }

    private static EmlProcessFunction ParseProcessCanonical(string payload)
    {
        string[] fields = payload.Split(':', StringSplitOptions.None);
        if (fields.Length != 5 || !fields[1].StartsWith('v')
            || !fields[2].StartsWith("num=", StringComparison.Ordinal)
            || !fields[3].StartsWith("den=", StringComparison.Ordinal)
            || !fields[4].StartsWith("fuel=", StringComparison.Ordinal))
            throw new FormatException("invalid EML process canonical payload");
        EmlProcessFunctionAlgorithms algorithm = fields[0] switch
        {
            "negative-log-series" => EmlProcessFunctionAlgorithms.NegativeLogSeries,
            "log-ratio-series" => EmlProcessFunctionAlgorithms.LogRatioSeries,
            "exp-series" => EmlProcessFunctionAlgorithms.ExponentialSeries,
            _ => throw new FormatException("unknown EML process canonical algorithm"),
        };
        if (!int.TryParse(fields[1][1..], NumberStyles.None, CultureInfo.InvariantCulture, out int version)
            || !long.TryParse(fields[4][5..], NumberStyles.Integer, CultureInfo.InvariantCulture, out long fuel))
            throw new FormatException("invalid EML process canonical numeric field");
        EmlProcessFunction function = new(
            algorithm,
            version,
            fields[2][4..],
            fields[3][4..],
            fuel);
        EmlProcessFunctions.ValidateDescriptor(in function);
        return function;
    }

    private static int FindTopLevelComma(string value)
    {
        int depth = 0;
        for (int i = 0; i < value.Length; i++)
        {
            switch (value[i])
            {
                case '[' or '(': depth++; break;
                case ']' or ')': depth--; break;
                case ',' when depth == 0: return i;
            }
        }
        return -1;
    }

    public string RenderCanonical()
    {
        StringBuilder builder = new StringBuilder();
        AppendCanonical(builder);
        return builder.ToString();
    }

    public bool TryRenderRPN(out string rpn)
    {
        StringBuilder builder = new StringBuilder(StructuralCost);
        if (!TryAppendRPN(builder))
        {
            rpn = string.Empty;
            return false;
        }
        rpn = builder.ToString();
        return true;
    }

    public bool TryGetProcessFunction(out EmlProcessFunction processFunction)
    {
        if (Kind == EmlResidualExpressionKinds.ProcessFunction)
        {
            processFunction = _processFunction;
            return true;
        }
        processFunction = default;
        return false;
    }

    public EmlResidualExpressionEvaluation Evaluate(EmlEvaluatorClock clock, EmlGrader? grader = null, EmlDeliberationLease? deliberationLease = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (grader is not null && TryRenderRPN(out string finiteRPN))
            return grader.EvaluateFinite(finiteRPN);
        return Kind switch
        {
            EmlResidualExpressionKinds.FiniteRPN => EvaluateFinite(clock),
            EmlResidualExpressionKinds.ProcessFunction => EvaluateProcess(deliberationLease),
            EmlResidualExpressionKinds.EGate => EvaluateGate(clock, grader, deliberationLease),
            _ => throw new InvalidOperationException($"unknown EML residual expression kind {(int)Kind}"),
        };
    }

    private void AppendCanonical(StringBuilder builder)
    {
        switch (Kind)
        {
            case EmlResidualExpressionKinds.FiniteRPN:
                builder.Append("rpn[").Append(_finiteTree!.RenderRPN()).Append(']');
                return;
            case EmlResidualExpressionKinds.ProcessFunction:
                builder.Append("process[");
                AppendProcessFunction(builder, in _processFunction);
                builder.Append(']');
                return;
            case EmlResidualExpressionKinds.EGate:
                builder.Append("E(");
                _left!.AppendCanonical(builder);
                builder.Append(',');
                _right!.AppendCanonical(builder);
                builder.Append(')');
                return;
            default:
                throw new InvalidOperationException($"unknown EML residual expression kind {(int)Kind}");
        }
    }

    private static void AppendProcessFunction(StringBuilder builder, in EmlProcessFunction processFunction)
    {
        string algorithm = processFunction.Algorithm switch
        {
            EmlProcessFunctionAlgorithms.NegativeLogSeries => "negative-log-series",
            EmlProcessFunctionAlgorithms.LogRatioSeries => "log-ratio-series",
            EmlProcessFunctionAlgorithms.ExponentialSeries => "exp-series",
            _ => throw new InvalidOperationException($"unknown EML process-function algorithm {(int)processFunction.Algorithm}"),
        };
        builder.Append(algorithm)
            .Append(":v").Append(processFunction.Version.ToString(CultureInfo.InvariantCulture))
            .Append(":num=").Append(processFunction.NumeratorRPN)
            .Append(":den=").Append(processFunction.DenominatorRPN)
            .Append(":fuel=").Append(processFunction.Fuel.ToString(CultureInfo.InvariantCulture));
    }

    private bool TryAppendRPN(StringBuilder builder)
    {
        switch (Kind)
        {
            case EmlResidualExpressionKinds.FiniteRPN:
                builder.Append(_finiteTree!.RenderRPN());
                return true;
            case EmlResidualExpressionKinds.ProcessFunction:
                return false;
            case EmlResidualExpressionKinds.EGate:
                int length = builder.Length;
                if (!_left!.TryAppendRPN(builder) || !_right!.TryAppendRPN(builder))
                {
                    builder.Length = length;
                    return false;
                }
                builder.Append(Eml.Op);
                return true;
            default:
                throw new InvalidOperationException($"unknown EML residual expression kind {(int)Kind}");
        }
    }

    private EmlResidualExpressionEvaluation EvaluateFinite(EmlEvaluatorClock clock)
    {
        string rpn = _finiteTree!.RenderRPN();
        return new EmlGrader(clock).EvaluateFinite(rpn);
    }

    private EmlResidualExpressionEvaluation EvaluateProcess(EmlDeliberationLease? deliberationLease)
    {
        EmlProcessFunctionCertificate certificate = EmlProcessFunctions.Certify(in _processFunction, deliberationLease);
        EmlProcessFunctionCheck check = EmlProcessFunctionChecker.Check(in certificate, deliberationLease);
        if (!check.Accepted)
            throw new InvalidDataException($"invalid EML process-function certificate: {check.Detail}");

        long fuel = checked(certificate.P1.FuelSpent + certificate.P2.FuelSpent + certificate.P3.FuelSpent);
        EmlProcessFunctionProbeCertificate p1 = certificate.P1;
        EmlProcessFunctionProbeCertificate p2 = certificate.P2;
        EmlProcessFunctionProbeCertificate p3 = certificate.P3;
        return new EmlResidualExpressionEvaluation(
            CreateProcessLadder(in p1),
            CreateProcessLadder(in p2),
            CreateProcessLadder(in p3),
            fuel);
    }

    private static EmlLadder CreateProcessLadder(in EmlProcessFunctionProbeCertificate probe)
    {
        bool finite = IsFinite(probe.Value);
        return finite
            ? new EmlLadder(new EmlValue(probe.Value, true), probe.Enclosure, 1.0, 0)
            : EmlLadder.Invalid;
    }

    private EmlResidualExpressionEvaluation EvaluateGate(EmlEvaluatorClock clock, EmlGrader? grader, EmlDeliberationLease? deliberationLease)
    {
        EmlResidualExpressionEvaluation left = _left!.Evaluate(clock, grader, deliberationLease);
        EmlResidualExpressionEvaluation right = _right!.Evaluate(clock, grader, deliberationLease);
        long fuel = checked(left.ProcessFuelConsumed + right.ProcessFuelConsumed);
        return new EmlResidualExpressionEvaluation(
            EvaluateGateProbe(left.P1, right.P1),
            EvaluateGateProbe(left.P2, right.P2),
            EvaluateGateProbe(left.P3, right.P3),
            fuel);
    }

    private static EmlLadder EvaluateGateProbe(EmlLadder left, EmlLadder right)
    {
        if (!left.Plain.Finite || !right.Plain.Finite || left.Plain.Value.Real > Eml.ExpReMax)
            return EmlLadder.Invalid;

        Complex exponential = Complex.Exp(left.Plain.Value);
        Complex logarithm = Complex.Log(right.Plain.Value);
        Complex value = exponential - logarithm;
        if (!IsFinite(value)) return EmlLadder.Invalid;

        double exponentialMagnitude = Complex.Abs(exponential);
        double logarithmMagnitude = Complex.Abs(logarithm);
        double minRatio = Math.Min(left.MinRatio, right.MinRatio);
        int subEpsOps = checked(left.SubEpsOps + right.SubEpsOps);
        if (exponentialMagnitude > 0.0 && logarithmMagnitude > 0.0 && exponentialMagnitude != logarithmMagnitude)
        {
            double ratio = Math.Min(exponentialMagnitude, logarithmMagnitude) /
                Math.Max(exponentialMagnitude, logarithmMagnitude);
            minRatio = Math.Min(minRatio, ratio);
        }
        if (logarithmMagnitude > 0.0 && value == exponential || value == -logarithm)
        {
            minRatio = 0.0;
            subEpsOps = checked(subEpsOps + 1);
        }

        EmlRect enclosure = EmlRect.Sub(EmlRect.Exp(left.Rect), EmlRect.Log(right.Rect));
        return new EmlLadder(new EmlValue(value, true), enclosure, minRatio, subEpsOps);
    }

    private static bool IsFinite(Complex value)
        => double.IsFinite(value.Real) && double.IsFinite(value.Imaginary);
}
