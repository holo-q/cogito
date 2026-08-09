namespace Cogito;

using System.Globalization;
using System.Numerics;
using System.Text;

internal enum EmlDomainGuardKinds
{
    Finite,
    ExpArgumentRange,
    LogArgumentNonZero,
    PrincipalCut,
    LogExpImaginaryRange,
    ExpLogArgumentNonZero,
}

internal readonly record struct EmlFiniteDomain(EmlPath Path);
internal readonly record struct EmlExpArgumentRange(EmlPath Path, double Maximum);
internal readonly record struct EmlLogArgumentNonZero(EmlPath Path);
internal readonly record struct EmlPrincipalCut(EmlPath Path);
internal readonly record struct EmlLogExpImaginaryRange(EmlPath Path, double LowerExclusive, double UpperInclusive);
internal readonly record struct EmlExpLogArgumentNonZero(EmlPath Path);

internal enum EmlGuardSides
{
    Antecedent,
    Consequent,
}

/// One authoritative node fact from the concrete carrier.  Parameter-erasure is
/// a nested identity: a single root enclosure cannot prove the branch conditions
/// of its inner log arguments, so every required path rides the witness.
internal readonly record struct EmlGuardNodeFact(
    EmlGuardSides Side,
    EmlPath Path,
    EmlEnclosureWitness Enclosure,
    EmlBranchWitness Branch)
{
    public string Canonical()
        => string.Concat(Side, "|", Path.Steps, "|", Enclosure.Canonical(), "|", Branch.Canonical());
}

/// One typed domain atom. The six typed constructors are deliberately explicit: a rewrite cannot
/// smuggle a branch condition through an unstructured string.
internal readonly record struct EmlDomainAtom(
    EmlDomainGuardKinds Kind,
    EmlPath Path,
    double Lower,
    double Upper,
    EmlGuardSides Side = EmlGuardSides.Antecedent)
{
    public static EmlDomainAtom For(in EmlFiniteDomain guard) => new(EmlDomainGuardKinds.Finite, guard.Path, 0, 0);
    public static EmlDomainAtom For(in EmlExpArgumentRange guard) => new(EmlDomainGuardKinds.ExpArgumentRange, guard.Path, double.MinValue, guard.Maximum);
    public static EmlDomainAtom For(in EmlLogArgumentNonZero guard) => new(EmlDomainGuardKinds.LogArgumentNonZero, guard.Path, 0, 0);
    public static EmlDomainAtom For(in EmlPrincipalCut guard) => new(EmlDomainGuardKinds.PrincipalCut, guard.Path, 0, 0);
    public static EmlDomainAtom For(in EmlLogExpImaginaryRange guard) => new(EmlDomainGuardKinds.LogExpImaginaryRange, guard.Path, guard.LowerExclusive, guard.UpperInclusive);
    public static EmlDomainAtom For(in EmlExpLogArgumentNonZero guard) => new(EmlDomainGuardKinds.ExpLogArgumentNonZero, guard.Path, 0, 0);

    public string Canonical()
        => string.Concat(
            Kind.ToString(), "|", Side, "|", Path.Steps, "|",
            Lower.ToString("R", CultureInfo.InvariantCulture), "|",
            Upper.ToString("R", CultureInfo.InvariantCulture));
}

internal readonly record struct EmlEnclosureWitness(
    double RealLower,
    double RealUpper,
    double ImaginaryLower,
    double ImaginaryUpper)
{
    public bool IsFinite
        => double.IsFinite(RealLower) && double.IsFinite(RealUpper)
            && double.IsFinite(ImaginaryLower) && double.IsFinite(ImaginaryUpper)
            && RealLower <= RealUpper && ImaginaryLower <= ImaginaryUpper;

    public bool ExcludesZero => IsFinite && (RealLower > 0 || RealUpper < 0 || ImaginaryLower > 0 || ImaginaryUpper < 0);
    public bool IsWithin(double lowerExclusive, double upperInclusive)
        => IsFinite && ImaginaryLower > lowerExclusive && ImaginaryUpper <= upperInclusive;

    public bool CrossesPrincipalCut
        => IsFinite && RealLower < 0 && ImaginaryLower <= 0 && ImaginaryUpper >= 0;

    public static EmlEnclosureWitness From(in EmlRect rect)
        => new(rect.Re.Lo, rect.Re.Hi, rect.Im.Lo, rect.Im.Hi);

    /// A concrete probe remains a finite instance witness even when interval propagation gives up.
    /// The point enclosure records that evaluated instance; it does not claim a neighborhood
    /// bound that the blown interval could not establish.
    public static EmlEnclosureWitness FromConcreteProbe(in EmlProbeEvaluation probe)
    {
        if (!probe.Valid || !probe.Plain.Finite) return default;
        return probe.Enclosure.IsBlown
            ? From(EmlRect.Point(probe.Plain.Value))
            : From(probe.Enclosure);
    }

    public string Canonical()
        => string.Join(",", RealLower.ToString("R", CultureInfo.InvariantCulture),
            RealUpper.ToString("R", CultureInfo.InvariantCulture),
            ImaginaryLower.ToString("R", CultureInfo.InvariantCulture),
            ImaginaryUpper.ToString("R", CultureInfo.InvariantCulture));
}

internal readonly record struct EmlBranchWitness(
    bool LogDefined,
    bool EnclosureCrossesNegativeRealCut,
    bool ExpAfterLogRoundTrips,
    bool LogAfterExpRoundTrips,
    long ExponentialTurn)
{
    public string Canonical()
        => string.Concat(LogDefined ? '1' : '0', EnclosureCrossesNegativeRealCut ? '1' : '0',
            ExpAfterLogRoundTrips ? '1' : '0', LogAfterExpRoundTrips ? '1' : '0', ":", ExponentialTurn);
}

/// A witness is tied to one concrete matched instance, not to the fixed numeric probe points.
internal readonly record struct EmlGuardWitness(
    string MatchedTermRpn,
    string SubstitutionRpn,
    EmlEnclosureWitness Enclosure,
    EmlBranchWitness Branch,
    ulong Digest,
    EmlPath MatchedPath = default,
    string AntecedentRpn = "",
    string ConsequentRpn = "",
    IReadOnlyList<EmlGuardNodeFact>? NodeFacts = null)
{
    private const string DigestScheme = "guard-witness-concrete-instance-v2";

    public bool IsInstanceBound => !string.IsNullOrEmpty(MatchedTermRpn) && !string.IsNullOrEmpty(SubstitutionRpn) && Digest != 0;
    public bool HasValidDigest
    {
        get
        {
        if (!IsInstanceBound) return Digest == 0;
            try { return Create(MatchedPath, MatchedTermRpn, SubstitutionRpn, AntecedentRpn, ConsequentRpn, Enclosure, Branch, NodeFacts).Digest == Digest; }
            catch (ArgumentException) { return false; }
        }
    }

    public static EmlGuardWitness Create(
        string matchedTermRpn,
        string substitutionRpn,
        in EmlEnclosureWitness enclosure,
        in EmlBranchWitness branch)
        => Create(EmlPath.Root, matchedTermRpn, substitutionRpn, matchedTermRpn, matchedTermRpn, in enclosure, in branch, null);

    public static EmlGuardWitness Create(
        EmlPath matchedPath,
        string matchedTermRpn,
        string substitutionRpn,
        string antecedentRpn,
        string consequentRpn,
        in EmlEnclosureWitness enclosure,
        in EmlBranchWitness branch,
        IReadOnlyList<EmlGuardNodeFact>? nodeFacts = null)
    {
        if (string.IsNullOrEmpty(matchedTermRpn) || string.IsNullOrEmpty(substitutionRpn)
            || string.IsNullOrEmpty(antecedentRpn) || string.IsNullOrEmpty(consequentRpn) || !enclosure.IsFinite)
            throw new ArgumentException("guard witnesses must identify one finite matched instance");
        ulong digest = 14695981039346656037UL;
        HashText(ref digest, DigestScheme);
        HashText(ref digest, matchedTermRpn);
        HashText(ref digest, substitutionRpn);
        HashText(ref digest, matchedPath.Steps);
        HashText(ref digest, antecedentRpn);
        HashText(ref digest, consequentRpn);
        HashText(ref digest, enclosure.Canonical());
        HashText(ref digest, branch.Canonical());
        if (nodeFacts is { Count: > 0 })
        {
            for (int i = 0; i < nodeFacts.Count; i++) HashText(ref digest, nodeFacts[i].Canonical());
        }
        return new EmlGuardWitness(matchedTermRpn, substitutionRpn, enclosure, branch, digest, matchedPath, antecedentRpn, consequentRpn, nodeFacts);
    }

    public bool Matches(string matchedTermRpn, string substitutionRpn)
        => IsInstanceBound
            && string.Equals(MatchedTermRpn, matchedTermRpn, StringComparison.Ordinal)
            && string.Equals(SubstitutionRpn, substitutionRpn, StringComparison.Ordinal);

    public bool Matches(EmlPath path, string matchedTermRpn, string substitutionRpn, string antecedentRpn, string consequentRpn)
        => Matches(matchedTermRpn, substitutionRpn)
            && MatchedPath == path
            && string.Equals(AntecedentRpn, antecedentRpn, StringComparison.Ordinal)
            && string.Equals(ConsequentRpn, consequentRpn, StringComparison.Ordinal);

    public string Canonical()
    {
        StringBuilder text = new();
        text.Append(MatchedTermRpn).Append('|').Append(SubstitutionRpn).Append('|').Append(MatchedPath.Steps)
            .Append('|').Append(AntecedentRpn).Append('|').Append(ConsequentRpn).Append('|')
            .Append(Enclosure.Canonical()).Append('|').Append(Branch.Canonical());
        if (NodeFacts is { Count: > 0 })
            for (int i = 0; i < NodeFacts.Count; i++) text.Append('|').Append(NodeFacts[i].Canonical());
        return text.Append('|').Append(Digest.ToString("X16", CultureInfo.InvariantCulture)).ToString();
    }

    private static void HashText(ref ulong hash, string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            hash ^= text[i];
            hash *= 1099511628211UL;
        }
        hash ^= 0xFF;
        hash *= 1099511628211UL;
    }
}

internal sealed class EmlDomainGuardSet
{
    private readonly List<EmlDomainAtom> _atoms;

    private EmlDomainGuardSet(List<EmlDomainAtom> atoms, ulong digest)
    {
        _atoms = atoms;
        Digest = digest;
    }

    public static EmlDomainGuardSet Empty { get; } = Create([]);
    public IReadOnlyList<EmlDomainAtom> Atoms => _atoms;
    public ulong Digest { get; }
    public bool IsGuarded => _atoms.Count != 0 && Digest != 0;

    public static EmlDomainGuardSet Create(IEnumerable<EmlDomainAtom> atoms)
    {
        List<EmlDomainAtom> ordered = new(atoms);
        ordered.Sort(static (left, right) => string.CompareOrdinal(left.Canonical(), right.Canonical()));
        for (int i = 1; i < ordered.Count; i++)
            if (ordered[i] == ordered[i - 1]) throw new InvalidDataException("duplicate EML domain guard atom");
        ulong digest = 14695981039346656037UL;
        for (int i = 0; i < ordered.Count; i++) HashText(ref digest, ordered[i].Canonical());
        return new EmlDomainGuardSet(ordered, digest);
    }

    public static EmlDomainGuardSet ForLogExp(in EmlPath path)
        => Create([
            EmlDomainAtom.For(new EmlFiniteDomain(path)),
            EmlDomainAtom.For(new EmlExpArgumentRange(path, Eml.ExpReMax)),
            EmlDomainAtom.For(new EmlLogArgumentNonZero(path)),
            EmlDomainAtom.For(new EmlPrincipalCut(path)),
            EmlDomainAtom.For(new EmlLogExpImaginaryRange(path, -Math.PI, Math.PI)),
        ]);

    public static EmlDomainGuardSet ForExpLog(in EmlPath path)
        => Create([
            EmlDomainAtom.For(new EmlFiniteDomain(path)),
            EmlDomainAtom.For(new EmlLogArgumentNonZero(path)),
            EmlDomainAtom.For(new EmlPrincipalCut(path)),
            EmlDomainAtom.For(new EmlExpLogArgumentNonZero(path)),
        ]);

    public static EmlDomainGuardSet ForExpLog(EmlTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (tree.RenderRPN() != "11?E1EE1E") return Empty;
        List<EmlDomainAtom> atoms = new();
        EmlPath[] finite = [new(""), new("L"), new("LR"), new("LRL"), new("LRLR"), new("LRLL"), new("LRR"), new("LL"), new("R")];
        for (int i = 0; i < finite.Length; i++) atoms.Add(EmlDomainAtom.For(new EmlFiniteDomain(finite[i])));
        EmlPath[] expArguments = [new("L"), new("LRL"), new("LRLL"), new("LL")];
        for (int i = 0; i < expArguments.Length; i++) atoms.Add(EmlDomainAtom.For(new EmlExpArgumentRange(expArguments[i], Eml.ExpReMax)));
        EmlPath[] logArguments = [new("R"), new("LR"), new("LRR"), new("LRLR")];
        for (int i = 0; i < logArguments.Length; i++)
        {
            atoms.Add(EmlDomainAtom.For(new EmlLogArgumentNonZero(logArguments[i])));
            atoms.Add(EmlDomainAtom.For(new EmlPrincipalCut(logArguments[i])));
        }
        atoms.Add(EmlDomainAtom.For(new EmlLogExpImaginaryRange(new EmlPath("LRL"), -Math.PI, Math.PI)));
        return Create(atoms);
    }

    public static EmlDomainGuardSet ForParameterErasure(EmlTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        // E(p,E(E(p,z),1)) = log(z) is guarded at the exact cancellation sites.
        // The fixed paths are part of the typed law, not a textual convention:
        // p=L/RLL, z=RLR, w=RL, and the outer log(exp(w)) is the R node.
        string shape = tree.RenderRPN();
        if (shape is not "xx?E1EE" and not "11?E1EE")
            return Empty;
        List<EmlDomainAtom> atoms = new();
        EmlPath[] finite = [new(""), new("L"), new("R"), new("RL"), new("RLL"), new("RLR"), new("RR")];
        EmlPath[] expArguments = [new("L"), new("RLL"), new("RL")];
        EmlPath[] logArguments = [new("RLR"), new("R"), new("RR")];
        foreach (EmlGuardSides side in Enum.GetValues<EmlGuardSides>())
        {
            for (int i = 0; i < finite.Length; i++) atoms.Add(new EmlDomainAtom(EmlDomainGuardKinds.Finite, finite[i], 0, 0, side));
            for (int i = 0; i < expArguments.Length; i++) atoms.Add(new EmlDomainAtom(EmlDomainGuardKinds.ExpArgumentRange, expArguments[i], double.MinValue, Eml.ExpReMax, side));
            for (int i = 0; i < logArguments.Length; i++)
            {
                atoms.Add(new EmlDomainAtom(EmlDomainGuardKinds.LogArgumentNonZero, logArguments[i], 0, 0, side));
                atoms.Add(new EmlDomainAtom(EmlDomainGuardKinds.PrincipalCut, logArguments[i], 0, 0, side));
            }
            atoms.Add(new EmlDomainAtom(EmlDomainGuardKinds.LogExpImaginaryRange, new EmlPath("RL"), -Math.PI, Math.PI, side));
        }
        return Create(atoms);
    }

    public bool TryValidate(in EmlGuardWitness witness)
    {
        if (!IsGuarded || !witness.IsInstanceBound || !witness.Enclosure.IsFinite || !witness.HasValidDigest) return false;
        for (int i = 0; i < _atoms.Count; i++)
        {
            EmlDomainAtom atom = _atoms[i];
            EmlGuardNodeFact fact = default;
            bool hasFact = false;
            if (witness.NodeFacts is { Count: > 0 })
            {
                for (int j = 0; j < witness.NodeFacts.Count; j++)
                    if (witness.NodeFacts[j].Side == atom.Side && witness.NodeFacts[j].Path == atom.Path) { fact = witness.NodeFacts[j]; hasFact = true; break; }
            }
            else if (atom.Path == witness.MatchedPath)
            {
                fact = new EmlGuardNodeFact(atom.Side, witness.MatchedPath, witness.Enclosure, witness.Branch);
                hasFact = true;
            }
            if (!hasFact) return false;
            bool valid = atom.Kind switch
            {
                EmlDomainGuardKinds.Finite => fact.Enclosure.IsFinite,
                EmlDomainGuardKinds.ExpArgumentRange => fact.Enclosure.RealUpper <= atom.Upper,
                EmlDomainGuardKinds.LogArgumentNonZero => fact.Enclosure.ExcludesZero,
                EmlDomainGuardKinds.PrincipalCut => !fact.Enclosure.CrossesPrincipalCut
                    && !fact.Branch.EnclosureCrossesNegativeRealCut,
                EmlDomainGuardKinds.LogExpImaginaryRange => fact.Enclosure.IsWithin(atom.Lower, atom.Upper)
                    && fact.Branch.LogAfterExpRoundTrips && fact.Branch.ExponentialTurn == 0,
                EmlDomainGuardKinds.ExpLogArgumentNonZero => fact.Enclosure.ExcludesZero
                    && fact.Branch.LogDefined && fact.Branch.ExpAfterLogRoundTrips,
                _ => false,
            };
            if (!valid) return false;
        }
        return true;
    }

    public EmlDomainGuardSet BindToPath(EmlPath path)
    {
        List<EmlDomainAtom> bound = new(_atoms.Count);
        for (int i = 0; i < _atoms.Count; i++)
        {
            EmlDomainAtom atom = _atoms[i];
            bound.Add(atom with { Path = Append(path, atom.Path) });
        }
        return Create(bound);
    }

    private static EmlPath Append(EmlPath prefix, EmlPath suffix)
        => new(prefix.Steps + suffix.Steps);

    public string Canonical()
    {
        StringBuilder builder = new();
        for (int i = 0; i < _atoms.Count; i++)
        {
            if (i != 0) builder.Append(';');
            builder.Append(_atoms[i].Canonical());
        }
        return builder.ToString();
    }

    private static void HashText(ref ulong hash, string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            hash ^= text[i];
            hash *= 1099511628211UL;
        }
        hash ^= 0xFF;
        hash *= 1099511628211UL;
    }
}

internal readonly record struct EmlRuleID(string Value)
{
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    public static EmlRuleID Create(string pattern, EmlLawOrientations orientation, ulong basisLawDigest, ulong domainGuardDigest)
        => new(string.Concat(pattern, "|", orientation, "|", basisLawDigest.ToString("X16", CultureInfo.InvariantCulture), "|", domainGuardDigest.ToString("X16", CultureInfo.InvariantCulture)));

    public static EmlRuleID CreateRewriteInstance(in EmlLawRewrite rewrite)
        => new(string.Concat(
            rewrite.RuleID.Value, "|", rewrite.AntecedentRpn, "|",
            rewrite.SubstitutionRpn, "|", rewrite.ConsequentRpn));

    public static EmlRuleID CreateRelationNull(EmlRuleID sourceID, EmlRuleID donorID, ulong salt)
        => new(string.Concat(
            "relation-null|", sourceID.Value, "|", donorID.Value, "|",
            salt.ToString("X16", CultureInfo.InvariantCulture)));
}

internal readonly record struct EmlCompositionStep(
    EmlRuleID RuleID,
    EmlLawOrientations Orientation,
    EmlPath Path,
    string SubstitutionRpn,
    string AntecedentRpn,
    string ConsequentRpn,
    EmlGuardWitness GuardWitness,
    int RankBefore,
    int RankAfter,
    string RulePattern = "",
    ulong BasisLawDigest = 0,
    ulong DomainGuardDigest = 0);

internal readonly record struct EmlCompositionSearch(
    int Revision,
    int Budget,
    IReadOnlyList<EmlCompositionStep> Steps,
    ulong Digest);

internal static class EmlCompositionDigest
{
    public static ulong Calculate(int revision, int budget, IReadOnlyList<EmlCompositionStep> steps)
    {
        if (revision < 1 || budget < 1) return 0;
        ulong hash = 14695981039346656037UL;
        HashText(ref hash, revision.ToString(CultureInfo.InvariantCulture));
        HashText(ref hash, budget.ToString(CultureInfo.InvariantCulture));
        for (int i = 0; i < steps.Count; i++)
        {
            EmlCompositionStep step = steps[i];
            HashText(ref hash, step.RuleID.Value);
            HashText(ref hash, step.Orientation.ToString());
            HashText(ref hash, step.Path.Steps);
            HashText(ref hash, step.SubstitutionRpn);
            HashText(ref hash, step.AntecedentRpn);
            HashText(ref hash, step.ConsequentRpn);
            HashText(ref hash, step.GuardWitness.Canonical());
            HashText(ref hash, step.RankBefore.ToString(CultureInfo.InvariantCulture));
            HashText(ref hash, step.RankAfter.ToString(CultureInfo.InvariantCulture));
            HashText(ref hash, step.RulePattern);
            HashText(ref hash, step.BasisLawDigest.ToString("X16", CultureInfo.InvariantCulture));
            HashText(ref hash, step.DomainGuardDigest.ToString("X16", CultureInfo.InvariantCulture));
        }
        return steps.Count == 0 ? 0 : hash;
    }

    private static void HashText(ref ulong hash, string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            hash ^= text[i];
            hash *= 1099511628211UL;
        }
        hash ^= 0xFF;
        hash *= 1099511628211UL;
    }
}
