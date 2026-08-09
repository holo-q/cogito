namespace Cogito;

using System.Numerics;
using System.Text;

/// A structural address within an EML tree. Paths survive RPN rendering and reparsing; replacing a subtree preserves
/// every path outside the replaced branch. L selects the first EML operand and R selects the second.
public readonly struct EmlPath : IEquatable<EmlPath>
{
    private readonly string? _steps;

    public EmlPath(string steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        for (int i = 0; i < steps.Length; i++)
            if (steps[i] is not 'L' and not 'R')
                throw new ArgumentException("EML paths contain only L and R steps", nameof(steps));
        _steps = steps;
    }

    public static EmlPath Root => default;
    public string Steps => _steps ?? string.Empty;
    public int Depth => Steps.Length;

    public EmlPath AppendLeft() => new(Steps + 'L');
    public EmlPath AppendRight() => new(Steps + 'R');
    public bool Equals(EmlPath other) => string.Equals(Steps, other.Steps, StringComparison.Ordinal);
    public override bool Equals(object? value) => value is EmlPath path && Equals(path);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Steps);
    public override string ToString() => Depth == 0 ? "." : Steps;
    public static bool operator ==(EmlPath left, EmlPath right) => left.Equals(right);
    public static bool operator !=(EmlPath left, EmlPath right) => !left.Equals(right);
}

/// A closed RPN substring and the structural path of the subtree it encodes. Start/Length remain the migration face
/// for mutation code that still edits strings; Path is the stable domain address.
public readonly record struct EmlClosedSpan(int Start, int Length, EmlPath Path = default);

public enum EmlAbsorptions
{
    None,
    ExponentialTerm,
    LogarithmTerm,
}

public readonly record struct EmlAbsorption(EmlAbsorptions Term, double MinorMajorRatio, bool Bitwise)
{
    public static readonly EmlAbsorption None = new(EmlAbsorptions.None, 1.0, false);
}

/// Principal-branch facts for one node value. ExponentialTurn records the 2*pi*i turn discarded by Log(Exp(z)); an
/// inverse solver can therefore distinguish a principal inverse from a numerically matching non-principal one.
public readonly record struct EmlPrincipalBranch(
    Complex PrincipalLog,
    Complex PrincipalLogOfExponential,
    long ExponentialTurn,
    bool LogDefined,
    bool OnNegativeRealCut,
    bool EnclosureCrossesNegativeRealCut,
    bool ExpAfterLogRoundTrips,
    bool LogAfterExpRoundTrips);

public readonly record struct EmlProbePoint(string Name, Complex X, Complex Y);

public readonly record struct EmlProbeEvaluation(
    EmlValue Plain,
    EmlRect Enclosure,
    bool Valid,
    EmlAbsorption Absorption,
    EmlPrincipalBranch PrincipalBranch)
{
    public static readonly EmlProbeEvaluation Invalid = new(
        EmlValue.Invalid, EmlRect.Blown, false, EmlAbsorption.None, default);
}

public readonly record struct EmlNodeEvaluation(
    EmlTree.Node Node,
    EmlProbeEvaluation P1,
    EmlProbeEvaluation P2,
    EmlProbeEvaluation P3);

public sealed class EmlTreeEvaluation
{
    private readonly Dictionary<EmlPath, EmlNodeEvaluation> _nodes;

    internal EmlTreeEvaluation(EmlTree tree, Dictionary<EmlPath, EmlNodeEvaluation> nodes, bool authoritative, Complex x, Complex y)
    {
        Tree = tree;
        _nodes = nodes;
        IsAuthoritative = authoritative;
        X = x;
        Y = y;
    }

    public EmlTree Tree { get; }
    public bool IsAuthoritative { get; }
    public Complex X { get; }
    public Complex Y { get; }
    public IReadOnlyDictionary<EmlPath, EmlNodeEvaluation> Nodes => _nodes;

    public EmlNodeEvaluation GetNode(EmlPath path)
        => _nodes.TryGetValue(path, out EmlNodeEvaluation evaluation)
            ? evaluation
            : throw new ArgumentOutOfRangeException(nameof(path), path, "path does not address an EML node");

    public bool TryGetNode(EmlPath path, out EmlNodeEvaluation evaluation) => _nodes.TryGetValue(path, out evaluation);
}

/// The domain-owned EML syntax tree. Parsing, RPN rendering, subtree spans, stable addressing, replacement, and
/// annotated evaluation all derive from this one structure.
public sealed class EmlTree
{
    public const char Hole = '?';

    public sealed record Node
    {
        public Node(char token, Node? left = null, Node? right = null)
        {
            bool gate = token == Eml.Op;
            bool leaf = token is Eml.One or Eml.VarX or Eml.VarY or Hole;
            if (!gate && !leaf) throw new ArgumentOutOfRangeException(nameof(token), token, "unknown EML tree token");
            if (gate && (left is null || right is null) || !gate && (left is not null || right is not null))
                throw new ArgumentException("an EML gate has two children and an EML leaf has none");
            Token = token;
            Left = left;
            Right = right;
        }

        public char Token { get; }
        public Node? Left { get; }
        public Node? Right { get; }
        public bool IsGate => Token == Eml.Op;
        public bool IsHole => Token == Hole;
    }

    public static readonly EmlProbePoint P1 = new(
        "P1", new Complex(EmlSieve.Gamma, 0), new Complex(EmlSieve.Glaisher, 0));
    public static readonly EmlProbePoint P2 = new(
        "P2", new Complex(EmlSieve.Catalan, 0), new Complex(EmlSieve.Apery, 0));
    public static readonly EmlProbePoint P3 = new(
        "P3", new Complex(1.0 / EmlGrader.FeigenbaumDelta, 0), new Complex(1.0 / EmlGrader.FeigenbaumAlpha, 0));

    public EmlTree(Node root) => Root = root ?? throw new ArgumentNullException(nameof(root));

    public Node Root { get; }

    public static bool TryParseRPN(string rpn, out EmlTree? tree, bool allowHoles = false)
    {
        ArgumentNullException.ThrowIfNull(rpn);
        if (rpn.Length == 0 || rpn.Length > Eml.MaxProgramLen)
        {
            tree = null;
            return false;
        }

        Stack<Node> stack = new();
        for (int i = 0; i < rpn.Length; i++)
        {
            char token = rpn[i];
            if (token == Eml.Op)
            {
                if (stack.Count < 2)
                {
                    tree = null;
                    return false;
                }
                Node right = stack.Pop();
                Node left = stack.Pop();
                stack.Push(new Node(Eml.Op, left, right));
            }
            else if (token is Eml.One or Eml.VarX or Eml.VarY || allowHoles && token == Hole)
                stack.Push(new Node(token));
            else
            {
                tree = null;
                return false;
            }
        }

        if (stack.Count != 1)
        {
            tree = null;
            return false;
        }
        tree = new EmlTree(stack.Pop());
        return true;
    }

    public static EmlTree ParseRPN(string rpn, bool allowHoles = false)
        => TryParseRPN(rpn, out EmlTree? tree, allowHoles)
            ? tree!
            : throw new FormatException("text is not one closed EML RPN tree");

    public string RenderRPN()
    {
        StringBuilder builder = new();
        AppendRPN(Root, builder);
        return builder.ToString();
    }

    public Node GetNode(EmlPath path)
        => TryGetNode(path, out Node? node)
            ? node!
            : throw new ArgumentOutOfRangeException(nameof(path), path, "path does not address an EML node");

    public bool TryGetNode(EmlPath path, out Node? node)
    {
        node = Root;
        string steps = path.Steps;
        for (int i = 0; i < steps.Length; i++)
        {
            if (node is null || !node.IsGate)
            {
                node = null;
                return false;
            }
            node = steps[i] == 'L' ? node.Left : node.Right;
        }
        return node is not null;
    }

    public EmlTree ReplaceSubtree(EmlPath path, EmlTree replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        return new EmlTree(ReplaceNode(Root, path.Steps, 0, replacement.Root));
    }

    public List<EmlClosedSpan> GetClosedSpans()
    {
        List<EmlClosedSpan> spans = new();
        MeasureRPN(Root, EmlPath.Root, 0, spans);
        spans.Sort(static (left, right) =>
        {
            int start = left.Start.CompareTo(right.Start);
            return start != 0 ? start : left.Length.CompareTo(right.Length);
        });
        return spans;
    }

    public EmlTreeEvaluation EvaluateProbes()
    {
        Dictionary<EmlPath, EmlNodeEvaluation> nodes = new();
        EvaluateNode(Root, EmlPath.Root, nodes, P1, P2, P3);
        return new EmlTreeEvaluation(this, nodes, false, P1.X, P1.Y);
    }

    /// Evaluates one concrete instance through the same node walk used by the probe evaluator.
    /// The instance rides P1 so guard construction can consume the authoritative matched tree,
    /// while P2/P3 remain available for callers that inspect a complete evaluation.
    public EmlTreeEvaluation EvaluateAt(Complex x, Complex y)
    {
        Dictionary<EmlPath, EmlNodeEvaluation> nodes = new();
        EvaluateNode(Root, EmlPath.Root, nodes, new EmlProbePoint("instance", x, y), P2, P3);
        return new EmlTreeEvaluation(this, nodes, true, x, y);
    }

    private static void AppendRPN(Node node, StringBuilder builder)
    {
        if (node.IsGate)
        {
            AppendRPN(node.Left!, builder);
            AppendRPN(node.Right!, builder);
        }
        builder.Append(node.Token);
    }

    private static Node ReplaceNode(Node node, string steps, int depth, Node replacement)
    {
        if (depth == steps.Length) return replacement;
        if (!node.IsGate)
            throw new ArgumentOutOfRangeException(nameof(steps), steps, "path descends through an EML leaf");
        return steps[depth] == 'L'
            ? new Node(Eml.Op, ReplaceNode(node.Left!, steps, depth + 1, replacement), node.Right)
            : new Node(Eml.Op, node.Left, ReplaceNode(node.Right!, steps, depth + 1, replacement));
    }

    private static int MeasureRPN(Node node, EmlPath path, int start, List<EmlClosedSpan> spans)
    {
        int length = 1;
        if (node.IsGate)
        {
            int leftLength = MeasureRPN(node.Left!, path.AppendLeft(), start, spans);
            int rightLength = MeasureRPN(node.Right!, path.AppendRight(), start + leftLength, spans);
            length = leftLength + rightLength + 1;
        }
        spans.Add(new EmlClosedSpan(start, length, path));
        return length;
    }

    private static EmlNodeEvaluation EvaluateNode(
        Node node,
        EmlPath path,
        Dictionary<EmlPath, EmlNodeEvaluation> nodes,
        EmlProbePoint p1,
        EmlProbePoint p2,
        EmlProbePoint p3)
    {
        EmlNodeEvaluation evaluation;
        if (!node.IsGate)
        {
            evaluation = new EmlNodeEvaluation(
                node,
                EvaluateLeaf(node.Token, p1),
                EvaluateLeaf(node.Token, p2),
                EvaluateLeaf(node.Token, p3));
        }
        else
        {
            EmlNodeEvaluation left = EvaluateNode(node.Left!, path.AppendLeft(), nodes, p1, p2, p3);
            EmlNodeEvaluation right = EvaluateNode(node.Right!, path.AppendRight(), nodes, p1, p2, p3);
            evaluation = new EmlNodeEvaluation(
                node,
                EvaluateGate(left.P1, right.P1),
                EvaluateGate(left.P2, right.P2),
                EvaluateGate(left.P3, right.P3));
        }
        nodes.Add(path, evaluation);
        return evaluation;
    }

    private static EmlProbeEvaluation EvaluateLeaf(char token, EmlProbePoint point)
    {
        if (token == Hole) return EmlProbeEvaluation.Invalid;
        Complex value = token switch
        {
            Eml.One => Complex.One,
            Eml.VarX => point.X,
            Eml.VarY => point.Y,
            _ => default,
        };
        EmlRect enclosure = EmlRect.Point(value);
        return new EmlProbeEvaluation(
            new EmlValue(value, true), enclosure, true, EmlAbsorption.None, DescribePrincipalBranch(value, enclosure));
    }

    private static EmlProbeEvaluation EvaluateGate(EmlProbeEvaluation left, EmlProbeEvaluation right)
    {
        if (!left.Valid || !right.Valid || left.Plain.Value.Real > Eml.ExpReMax)
            return EmlProbeEvaluation.Invalid;

        Complex exponential = Complex.Exp(left.Plain.Value);
        Complex logarithm = Complex.Log(right.Plain.Value);
        Complex value = exponential - logarithm;
        if (!IsFinite(value)) return EmlProbeEvaluation.Invalid;

        double exponentialMagnitude = Complex.Abs(exponential);
        double logarithmMagnitude = Complex.Abs(logarithm);
        double ratio = 1.0;
        if (exponentialMagnitude > 0 && logarithmMagnitude > 0 && exponentialMagnitude != logarithmMagnitude)
            ratio = Math.Min(exponentialMagnitude, logarithmMagnitude) / Math.Max(exponentialMagnitude, logarithmMagnitude);

        EmlAbsorption absorption = EmlAbsorption.None;
        if (logarithmMagnitude > 0 && value == exponential)
            absorption = new EmlAbsorption(EmlAbsorptions.LogarithmTerm, 0, true);
        else if (value == -logarithm)
            absorption = new EmlAbsorption(EmlAbsorptions.ExponentialTerm, 0, true);
        else if (ratio < 1.0)
            absorption = new EmlAbsorption(EmlAbsorptions.None, ratio, false);

        EmlRect enclosure = EmlRect.Sub(EmlRect.Exp(left.Enclosure), EmlRect.Log(right.Enclosure));
        return new EmlProbeEvaluation(
            new EmlValue(value, true), enclosure, true, absorption, DescribePrincipalBranch(value, enclosure));
    }

    private static EmlPrincipalBranch DescribePrincipalBranch(Complex value, EmlRect enclosure)
    {
        bool finite = IsFinite(value);
        bool logDefined = finite && value != Complex.Zero;
        Complex principalLog = logDefined ? Complex.Log(value) : default;
        Complex exponential = finite && value.Real <= Eml.ExpReMax ? Complex.Exp(value) : new Complex(double.NaN, double.NaN);
        bool exponentialFinite = IsFinite(exponential) && exponential != Complex.Zero;
        Complex principalLogOfExponential = exponentialFinite ? Complex.Log(exponential) : default;
        bool expAfterLog = logDefined && NearlyEqual(Complex.Exp(principalLog), value);
        bool logAfterExp = exponentialFinite && NearlyEqual(principalLogOfExponential, value);
        long turn = 0;
        if (exponentialFinite)
        {
            double turns = (value.Imaginary - principalLogOfExponential.Imaginary) / (2.0 * Math.PI);
            if (double.IsFinite(turns) && turns >= long.MinValue && turns <= long.MaxValue)
                turn = (long)Math.Round(turns);
        }
        bool onCut = logDefined && value.Real < 0 && value.Imaginary == 0;
        bool crossesCut = !enclosure.IsBlown
            && enclosure.Re.Lo < 0
            && enclosure.Im.Lo < 0
            && enclosure.Im.Hi >= 0;
        return new EmlPrincipalBranch(
            principalLog, principalLogOfExponential, turn, logDefined, onCut, crossesCut, expAfterLog, logAfterExp);
    }

    private static bool NearlyEqual(Complex left, Complex right)
    {
        double scale = Math.Max(1.0, right.Magnitude);
        return (left - right).Magnitude <= 1e-12 * scale;
    }

    private static bool IsFinite(Complex value)
        => double.IsFinite(value.Real) && double.IsFinite(value.Imaginary);
}
