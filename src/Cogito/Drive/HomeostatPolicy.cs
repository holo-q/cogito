namespace Cogito;

using System.Text;

public enum HomeostatPolicyConditions : byte
{
    Relax,
    Quiet,
    Collapsing,
    Sealed,
    Hot,
    Surprised,
    Heavy,
    Stalled,
    Speculative,
}

public enum HomeostatScalarMoves : byte { Hold, Down, Up, Relax }
public enum HomeostatBreachMoves : byte { Hold, Clear, Grant }
public enum HomeostatForceGeneralizeMoves : byte { Hold, Disable, Enable }

public readonly struct HomeostatPolicyContext(
    HomeostatPolicyConditions condition,
    bool previousConsolidationPhaseWasted,
    bool growthAboveMintParity)
{
    public HomeostatPolicyConditions Condition { get; } = condition;
    public bool PreviousConsolidationPhaseWasted { get; } = previousConsolidationPhaseWasted;
    public bool GrowthAboveMintParity { get; } = growthAboveMintParity;

    public static HomeostatPolicyContext From(
        HomeoConditions? condition,
        bool previousConsolidationPhaseWasted,
        bool growthAboveMintParity)
        => new(MapCondition(condition), previousConsolidationPhaseWasted, growthAboveMintParity);

    public string RenderToken()
        => $"c:{RenderCondition(Condition)},w:{(PreviousConsolidationPhaseWasted ? '1' : '0')},g:{(GrowthAboveMintParity ? '1' : '0')}";

    public static HomeostatPolicyContext ParseToken(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        string[] fields = token.Split(',');
        if (fields.Length != 3 || !fields[0].StartsWith("c:", StringComparison.Ordinal)
            || !fields[1].StartsWith("w:", StringComparison.Ordinal)
            || !fields[2].StartsWith("g:", StringComparison.Ordinal))
            throw new FormatException($"invalid Homeostat policy context token '{token}'");
        return new HomeostatPolicyContext(
            ParseCondition(fields[0][2..]),
            ParseBit(fields[1][2..], token),
            ParseBit(fields[2][2..], token));
    }

    internal static string RenderCondition(HomeostatPolicyConditions condition) => condition switch
    {
        HomeostatPolicyConditions.Relax => "rx",
        HomeostatPolicyConditions.Quiet => "qu",
        HomeostatPolicyConditions.Collapsing => "co",
        HomeostatPolicyConditions.Sealed => "se",
        HomeostatPolicyConditions.Hot => "ho",
        HomeostatPolicyConditions.Surprised => "su",
        HomeostatPolicyConditions.Heavy => "he",
        HomeostatPolicyConditions.Stalled => "st",
        HomeostatPolicyConditions.Speculative => "sp",
        _ => throw new ArgumentOutOfRangeException(nameof(condition)),
    };

    internal static HomeostatPolicyConditions ParseCondition(string token) => token switch
    {
        "rx" => HomeostatPolicyConditions.Relax,
        "qu" => HomeostatPolicyConditions.Quiet,
        "co" => HomeostatPolicyConditions.Collapsing,
        "se" => HomeostatPolicyConditions.Sealed,
        "ho" => HomeostatPolicyConditions.Hot,
        "su" => HomeostatPolicyConditions.Surprised,
        "he" => HomeostatPolicyConditions.Heavy,
        "st" => HomeostatPolicyConditions.Stalled,
        "sp" => HomeostatPolicyConditions.Speculative,
        _ => throw new FormatException($"invalid Homeostat policy condition '{token}'"),
    };

    private static HomeostatPolicyConditions MapCondition(HomeoConditions? condition) => condition switch
    {
        null => HomeostatPolicyConditions.Relax,
        HomeoConditions.Quiet => HomeostatPolicyConditions.Quiet,
        HomeoConditions.Collapsing => HomeostatPolicyConditions.Collapsing,
        HomeoConditions.Sealed => HomeostatPolicyConditions.Sealed,
        HomeoConditions.Hot => HomeostatPolicyConditions.Hot,
        HomeoConditions.Surprised => HomeostatPolicyConditions.Surprised,
        HomeoConditions.Heavy => HomeostatPolicyConditions.Heavy,
        HomeoConditions.Stalled => HomeostatPolicyConditions.Stalled,
        HomeoConditions.Speculative => HomeostatPolicyConditions.Speculative,
        _ => throw new ArgumentOutOfRangeException(nameof(condition)),
    };

    private static bool ParseBit(string value, string token) => value switch
    {
        "0" => false,
        "1" => true,
        _ => throw new FormatException($"invalid Homeostat policy context bit in '{token}'"),
    };
}

public readonly record struct HomeostatPolicyProgram(
    HomeostatScalarMoves Sleep,
    HomeostatScalarMoves Mix,
    HomeostatScalarMoves Intake,
    HomeostatScalarMoves Budget,
    HomeostatBreachMoves Breach,
    HomeostatForceGeneralizeMoves ForceGeneralize)
{
    public HomeoActuation Execute(
        in HomeoActuation current,
        in HomeoActuation rest,
        double gain,
        int currentBreachAmplitude)
    {
        if (!double.IsFinite(gain) || gain < 0 || gain > 1) throw new ArgumentOutOfRangeException(nameof(gain));
        if (currentBreachAmplitude < 0) throw new ArgumentOutOfRangeException(nameof(currentBreachAmplitude));
        Validate(this);
        return new HomeoActuation(
            MoveDouble(Sleep, current.SleepFrac, rest.SleepFrac, gain, 1.0 / 32, 1.0 / 4),
            MoveInt(Mix, current.MixEvery, rest.MixEvery, gain, rest.MixEvery / 4, rest.MixEvery),
            MoveInt(Intake, current.IntakeBatch, rest.IntakeBatch, gain, rest.IntakeBatch, rest.IntakeBatch * 4),
            MoveLong(Budget, current.BudgetBits, rest.BudgetBits, gain, rest.BudgetBits / 2, rest.BudgetBits * 2),
            Breach switch
            {
                HomeostatBreachMoves.Hold => current.BreachQuota,
                HomeostatBreachMoves.Clear => 0,
                HomeostatBreachMoves.Grant => currentBreachAmplitude,
                _ => throw new ArgumentOutOfRangeException(nameof(Breach)),
            },
            ForceGeneralize switch
            {
                HomeostatForceGeneralizeMoves.Hold => current.ForceGeneralize,
                HomeostatForceGeneralizeMoves.Disable => false,
                HomeostatForceGeneralizeMoves.Enable => true,
                _ => throw new ArgumentOutOfRangeException(nameof(ForceGeneralize)),
            });
    }

    public string RenderToken()
        => $"sl:{RenderScalar(Sleep)},mx:{RenderScalar(Mix)},in:{RenderScalar(Intake)},bb:{RenderScalar(Budget)},br:{RenderBreach(Breach)},fg:{RenderForce(ForceGeneralize)}";

    public static HomeostatPolicyProgram ParseToken(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        string[] fields = token.Split(',');
        string[] labels = ["sl:", "mx:", "in:", "bb:", "br:", "fg:"];
        if (fields.Length != labels.Length) throw new FormatException($"invalid Homeostat policy program token '{token}'");
        for (int i = 0; i < labels.Length; i++)
            if (!fields[i].StartsWith(labels[i], StringComparison.Ordinal))
                throw new FormatException($"invalid Homeostat policy program token '{token}'");
        HomeostatPolicyProgram program = new(
            ParseScalar(fields[0][3..]), ParseScalar(fields[1][3..]), ParseScalar(fields[2][3..]),
            ParseScalar(fields[3][3..]), ParseBreach(fields[4][3..]), ParseForce(fields[5][3..]));
        Validate(program);
        return program;
    }

    internal static void Validate(in HomeostatPolicyProgram program)
    {
        if (!Enum.IsDefined(program.Sleep) || !Enum.IsDefined(program.Mix) || !Enum.IsDefined(program.Intake)
            || !Enum.IsDefined(program.Budget) || !Enum.IsDefined(program.Breach) || !Enum.IsDefined(program.ForceGeneralize))
            throw new ArgumentException("a Homeostat policy program contains an invalid move");
    }

    private static double MoveDouble(HomeostatScalarMoves move, double current, double rest, double gain, double low, double high)
        => move switch
        {
            HomeostatScalarMoves.Hold => current,
            HomeostatScalarMoves.Down => Math.Max(low, current * 0.85),
            HomeostatScalarMoves.Up => Math.Min(high, current * 1.15),
            HomeostatScalarMoves.Relax => current + gain * (rest - current),
            _ => throw new ArgumentOutOfRangeException(nameof(move)),
        };

    private static int MoveInt(HomeostatScalarMoves move, int current, int rest, double gain, int low, int high)
        => move switch
        {
            HomeostatScalarMoves.Hold => current,
            HomeostatScalarMoves.Down => Math.Max(low, (int)(current * 0.85)),
            HomeostatScalarMoves.Up => Math.Min(high, (int)Math.Ceiling(current * 1.15)),
            HomeostatScalarMoves.Relax => (int)Math.Round(current + gain * (rest - current)),
            _ => throw new ArgumentOutOfRangeException(nameof(move)),
        };

    private static long MoveLong(HomeostatScalarMoves move, long current, long rest, double gain, long low, long high)
        => move switch
        {
            HomeostatScalarMoves.Hold => current,
            HomeostatScalarMoves.Down => Math.Max(low, (long)(current * 0.85)),
            HomeostatScalarMoves.Up => Math.Min(high, (long)Math.Ceiling(current * 1.15)),
            HomeostatScalarMoves.Relax => (long)Math.Round(current + gain * (rest - current)),
            _ => throw new ArgumentOutOfRangeException(nameof(move)),
        };

    private static string RenderScalar(HomeostatScalarMoves move) => move switch
    {
        HomeostatScalarMoves.Hold => "h", HomeostatScalarMoves.Down => "d",
        HomeostatScalarMoves.Up => "u", HomeostatScalarMoves.Relax => "r",
        _ => throw new ArgumentOutOfRangeException(nameof(move)),
    };

    private static HomeostatScalarMoves ParseScalar(string token) => token switch
    {
        "h" => HomeostatScalarMoves.Hold, "d" => HomeostatScalarMoves.Down,
        "u" => HomeostatScalarMoves.Up, "r" => HomeostatScalarMoves.Relax,
        _ => throw new FormatException($"invalid Homeostat scalar move '{token}'"),
    };

    private static string RenderBreach(HomeostatBreachMoves move) => move switch
    {
        HomeostatBreachMoves.Hold => "h", HomeostatBreachMoves.Clear => "c", HomeostatBreachMoves.Grant => "g",
        _ => throw new ArgumentOutOfRangeException(nameof(move)),
    };

    private static HomeostatBreachMoves ParseBreach(string token) => token switch
    {
        "h" => HomeostatBreachMoves.Hold, "c" => HomeostatBreachMoves.Clear, "g" => HomeostatBreachMoves.Grant,
        _ => throw new FormatException($"invalid Homeostat breach move '{token}'"),
    };

    private static string RenderForce(HomeostatForceGeneralizeMoves move) => move switch
    {
        HomeostatForceGeneralizeMoves.Hold => "h", HomeostatForceGeneralizeMoves.Disable => "d",
        HomeostatForceGeneralizeMoves.Enable => "e",
        _ => throw new ArgumentOutOfRangeException(nameof(move)),
    };

    private static HomeostatForceGeneralizeMoves ParseForce(string token) => token switch
    {
        "h" => HomeostatForceGeneralizeMoves.Hold, "d" => HomeostatForceGeneralizeMoves.Disable,
        "e" => HomeostatForceGeneralizeMoves.Enable,
        _ => throw new FormatException($"invalid Homeostat force-generalize move '{token}'"),
    };
}

public readonly record struct HomeostatPolicyInput(
    HomeostatPolicyContext Context,
    Interocept Senses,
    HomeoActuation Actuation);
