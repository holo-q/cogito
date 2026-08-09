namespace Cogito;

using Cogito.Induct;

// ── THE GENERATION STRATEGIES ──  the IGenerator seam + the proven default (MetabolicWalk) and its baseline arms
// (MarkovWalk, McmcWalk). The other two members of the zoo live with their machinery — CouplingWalk (Couplings.cs,
// the MEANING generator) and NodeBirthWalk (NodeBirth.cs, the DEPTH organ) — and EnergyPolicy (Energy.cs) is the
// unified field that supersedes the zoo behind the same seam. Every strategy is deterministic given (grammar, seed)
// and is offered the Metabolism: baselines ignore it, the metabolic default consults+updates it.

/// A generation strategy — turns a grammar into a block of bytes. Deterministic given (grammar, seed). The
/// Metabolism is offered to every strategy; the metabolic default consults+updates it (the proven organ made
/// concrete), the baselines ignore it.
public interface IGenerator
{
    string Name { get; }
    byte[] Generate(RePairResult grammar, int count, ulong seed, Metabolism metabolism);
}

/// The PROVEN default: cogito's own order-2→order-1 Markov walk (identical bag construction to Engine.GenerateFrom)
/// with the curiosity-metabolism reweighting every pick. This is where the novelty-decay actually bites — the walk
/// is pulled AWAY from the over-emitted chunk in proportion to how hard it has been firing, so a run opens instead
/// of basining.
public sealed class MetabolicWalk : IGenerator
{
    /// Shared instance — a stateless strategy (the successor bags are method-local scratch); one serves every dispatch.
    public static readonly MetabolicWalk Instance = new();
    private readonly Engine.MarkovModel _model = new();
    public string Name => "metabolic";

    public byte[] Generate(RePairResult r, int count, ulong seed, Metabolism metab)
    {
        return _model.GenerateMetabolic(in r, count, seed, metab);
    }
}

/// Baseline — the plain forward Markov walk (Engine.GenerateFrom), metabolism-free. The control the metabolic
/// default is measured against (the fixed-farmer arm — threads ~19 lines but COLLAPSES).
public sealed class MarkovWalk : IGenerator
{
    /// Shared instance — a stateless strategy (all generation state is method-local); one serves every dispatch.
    public static readonly MarkovWalk Instance = new();
    private readonly Engine.MarkovModel _model = new();
    public string Name => "markov";
    public byte[] Generate(RePairResult r, int count, ulong seed, Metabolism _)
        => _model.GenerateFrom(in r, count, seed, 1);
}

/// Baseline — Gibbs/MCMC bidirectional walk (Engine.GenerateMCMC): more globally coherent, still metabolism-free.
public sealed class McmcWalk : IGenerator
{
    /// Shared instance — a stateless strategy (all generation state is method-local); one serves every dispatch.
    public static readonly McmcWalk Instance = new();
    private readonly Engine.MarkovModel _model = new();
    public string Name => "mcmc";
    public byte[] Generate(RePairResult r, int count, ulong seed, Metabolism _)
        => _model.GenerateMCMC(in r, count, 4, seed);
}
