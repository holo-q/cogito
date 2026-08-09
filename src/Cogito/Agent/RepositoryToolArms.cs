namespace Cogito;

/// G3 — the ClosureCertificate in native form. The divergence assay is re-hosted on the crawler, and the
/// intervention it needs is not a redesigned world: it is a valve on the ONE place the organism
/// touches reality, the tool result. Same repository, same fuel, same grammar, same everything —
/// only what comes back through the mouth changes.
///
///   ToolsLive      the world answers truthfully. The assay's live arm.
///   ToolsBlocked   the world answers nothing. A look is still funded, still costs fuel, still lands
///                  as custody — and returns empty. This kills the LOOK, not the organism, which is
///                  what separates "the answer came from the world" from "the answer came from the
///                  prior": whatever survives here was never evidence.
///   ToolsShuffled  the world answers, incoherently — each look receives the PREVIOUS look's result.
///                  The mouth is open and the bytes are real repository bytes, but their binding to
///                  the question is deranged. This is the sharper null of the two: an organism that
///                  scores the same under a derangement was never routing on what it found.
///
/// A cyclic shift is a genuine derangement of the call sequence and needs no privileged knowledge of
/// the future — the first look, having no predecessor to inherit, receives nothing. The Vow holds:
/// the mediation is a pure function of the arm and the order the looks actually happened in.
public enum RepositoryToolArms : byte
{
    ToolsLive,
    ToolsBlocked,
    ToolsShuffled,
}

/// The arm's spelling on the command line. Kebab-case because that is how the assay names its arms
/// in every report and receipt it writes; an unknown spelling is refused rather than defaulted, so a
/// typo can never silently run the live arm and be banked as a null.
internal static class RepositoryToolArmNames
{
    internal static RepositoryToolArms Parse(string? name) => (name ?? "").Trim().ToLowerInvariant() switch
    {
        "" or "tools-live" => RepositoryToolArms.ToolsLive,
        "tools-blocked" => RepositoryToolArms.ToolsBlocked,
        "tools-shuffled" => RepositoryToolArms.ToolsShuffled,
        _ => throw new InvalidDataException($"unknown tool arm '{name}' — expected tools-live, tools-blocked, or tools-shuffled"),
    };

    internal static string Render(RepositoryToolArms arm) => arm switch
    {
        RepositoryToolArms.ToolsLive => "tools-live",
        RepositoryToolArms.ToolsBlocked => "tools-blocked",
        RepositoryToolArms.ToolsShuffled => "tools-shuffled",
        _ => throw new InvalidDataException($"unknown tool arm {arm}"),
    };
}

/// The valve itself. One instance per run, mutated only by the tool seam, so the derangement's
/// state is the run's own look order and nothing else.
internal sealed class RepositoryToolMediation(RepositoryToolArms arm)
{
    private Tool.Observation _previous = Tool.Observation.Empty;
    private int _looks;

    internal RepositoryToolArms Arm => arm;

    /// How many looks were mediated, and how many came back empty because the arm withheld them.
    /// The report needs this: an arm that spent no fuel proves nothing, and an arm whose looks all
    /// came back empty must be visible as such rather than read as an organism that chose not to
    /// look.
    internal int Looks => _looks;
    internal int Withheld { get; private set; }

    internal Tool.Observation Mediate(in Tool.Observation observed)
    {
        _looks++;
        switch (arm)
        {
            case RepositoryToolArms.ToolsLive:
                _previous = observed;
                return observed;
            case RepositoryToolArms.ToolsBlocked:
                Withheld++;
                return Tool.Observation.Empty;
            case RepositoryToolArms.ToolsShuffled:
                Tool.Observation inherited = _previous;
                _previous = observed;
                if (inherited.Text.Length == 0) Withheld++;      // the first look has no predecessor to inherit
                return inherited;
            default:
                throw new InvalidDataException($"repository tool arm {arm} is not a mediation");
        }
    }
}
