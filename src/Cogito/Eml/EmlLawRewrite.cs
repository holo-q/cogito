namespace Cogito;

internal enum EmlLawOrientations
{
    LeftToRight,
    RightToLeft,
}

internal readonly record struct EmlLawRewrite(
    string AntecedentRpn,
    string SubstitutionRpn,
    string ConsequentRpn,
    EmlLawBehaviorCertificate LawCertificate,
    EmlLawProof LawProof,
    EmlLawOrientations Orientation,
    string MatchedTermRpn,
    EmlPath MatchedPath,
    bool IsRelationNull = false,
    EmlRuleID RuleID = default,
    EmlGuardWitness GuardWitness = default,
    bool IsRung0Eligible = false,
    string RulePattern = "",
    ulong BasisLawDigest = 0,
    ulong DomainGuardDigest = 0,
    EmlRuleID RelationNullSourceID = default,
    EmlRuleID RelationNullDonorID = default,
    ulong RelationNullSalt = 0)
{
    public int AntecedentSize => AntecedentRpn.Length;
    public int SubstitutionSize => SubstitutionRpn.Length;
    public int ConsequentSize => ConsequentRpn.Length;
    public bool HasGuard => IsRung0Eligible;

    public EmlLawRewrite CreateRelationNull(in EmlLawRewrite consequentDonor, ulong salt, EmlGrader grader)
        => TryCreateRelationNull(this, consequentDonor, salt, grader, out EmlLawRewrite relationNull)
            ? relationNull
            : throw new ArgumentException(
                "relation-null donor must be a distinct relation with identical antecedent and consequent sizes",
                nameof(consequentDonor));

    public static bool TryCreateRelationNull(
        in EmlLawRewrite relation,
        in EmlLawRewrite consequentDonor,
        ulong salt,
        EmlGrader grader,
        out EmlLawRewrite relationNull)
    {
        ArgumentNullException.ThrowIfNull(grader);
        EmlRuleID sourceID = EmlRuleID.CreateRewriteInstance(in relation);
        EmlRuleID donorID = EmlRuleID.CreateRewriteInstance(in consequentDonor);
        if (relation.IsRelationNull
            || consequentDonor.IsRelationNull
            || relation.RuleID.IsEmpty
            || consequentDonor.RuleID.IsEmpty
            || sourceID == donorID
            || salt == 0
            || !EmlRewriteSystem.ReducesRank(relation.AntecedentRpn, relation.ConsequentRpn)
            || relation.AntecedentSize != consequentDonor.AntecedentSize
            || relation.ConsequentSize != consequentDonor.ConsequentSize
            || string.Equals(relation.ConsequentRpn, consequentDonor.ConsequentRpn, StringComparison.Ordinal))
        {
            relationNull = default;
            return false;
        }

        if (grader.GradeRpn(relation.ConsequentRpn, consequentDonor.ConsequentRpn).Grade == 'E'
            || grader.GradeRpn(relation.AntecedentRpn, consequentDonor.ConsequentRpn).Grade == 'E')
        {
            relationNull = default;
            return false;
        }

        relationNull = relation with
        {
            ConsequentRpn = consequentDonor.ConsequentRpn,
            IsRelationNull = true,
            RuleID = EmlRuleID.CreateRelationNull(sourceID, donorID, salt),
            GuardWitness = default,
            IsRung0Eligible = false,
            RulePattern = "",
            BasisLawDigest = 0,
            DomainGuardDigest = 0,
            RelationNullSourceID = sourceID,
            RelationNullDonorID = donorID,
            RelationNullSalt = salt,
        };
        return true;
    }
}

internal readonly record struct EmlOneHoleLaw(EmlTree Left, EmlTree Right)
{
    public static bool TryParse(string template, out EmlOneHoleLaw law)
    {
        int separator = template.IndexOf(" = ", StringComparison.Ordinal);
        if (separator <= 0
            || separator != template.LastIndexOf(" = ", StringComparison.Ordinal)
            || !EmlTree.TryParseRPN(template[..separator], out EmlTree? left, allowHoles: true)
            || !EmlTree.TryParseRPN(template[(separator + 3)..], out EmlTree? right, allowHoles: true)
            || !ContainsHole(left!.Root)
            || !ContainsHole(right!.Root))
        {
            law = default;
            return false;
        }

        law = new EmlOneHoleLaw(left, right);
        return true;
    }

    public EmlTree InstantiateLeft(EmlTree substitution) => Instantiate(Left, substitution);
    public EmlTree InstantiateRight(EmlTree substitution) => Instantiate(Right, substitution);

    public EmlTree InstantiateMatch(EmlLawOrientations orientation, EmlTree substitution)
        => orientation == EmlLawOrientations.LeftToRight
            ? InstantiateLeft(substitution)
            : InstantiateRight(substitution);

    public EmlTree InstantiateReplacement(EmlLawOrientations orientation, EmlTree substitution)
        => orientation == EmlLawOrientations.LeftToRight
            ? InstantiateRight(substitution)
            : InstantiateLeft(substitution);

    public void AppendRewrites(
        EmlTree antecedent,
        EmlLawBehaviorCertificate certificate,
        EmlLawProof proof,
        List<EmlLawRewrite> rewrites,
        EmlDeliberationLease? deliberationLease = null,
        EmlTreeEvaluation? enclosureCarrier = null,
        EmlRewriteEdgeBudget? edgeBudget = null)
    {
        deliberationLease?.ReserveLawRewriteTreeNodes(1);
        string antecedentRpn = antecedent.RenderRPN();
        AppendRewritesAt(antecedent, antecedent.Root, EmlPath.Root, antecedentRpn,
            Left.Root, Right, EmlLawOrientations.LeftToRight, certificate, proof, rewrites,
            deliberationLease, enclosureCarrier, edgeBudget);
        if (edgeBudget?.Exhausted == true) return;
        AppendRewritesAt(antecedent, antecedent.Root, EmlPath.Root, antecedentRpn,
            Right.Root, Left, EmlLawOrientations.RightToLeft, certificate, proof, rewrites,
            deliberationLease, enclosureCarrier, edgeBudget);
    }

    private static void AppendRewritesAt(
        EmlTree antecedent,
        EmlTree.Node term,
        EmlPath path,
        string antecedentRpn,
        EmlTree.Node pattern,
        EmlTree consequentPattern,
        EmlLawOrientations orientation,
        EmlLawBehaviorCertificate certificate,
        EmlLawProof proof,
        List<EmlLawRewrite> rewrites,
        EmlDeliberationLease? deliberationLease = null,
        EmlTreeEvaluation? enclosureCarrier = null,
        EmlRewriteEdgeBudget? edgeBudget = null)
    {
        if (edgeBudget?.Exhausted == true) return;
        deliberationLease?.ReserveLawRewriteTreeNodes(1);
        EmlTree.Node? substitution = null;
        if (TryMatch(pattern, term, ref substitution) && substitution is not null)
        {
            if (edgeBudget is null) deliberationLease?.ReserveLawRewriteApplication();
            else if (!edgeBudget.TryReserve(deliberationLease)) return;
            EmlTree substitutionTree = new(substitution);
            EmlTree matchedTerm = new(term);
            EmlTree consequentTerm = Instantiate(consequentPattern, substitutionTree);
            EmlTree consequent = antecedent.ReplaceSubtree(path, consequentTerm);
            string consequentRpn = consequent.RenderRPN();
            if (consequentRpn.Length <= Eml.MaxProgramLen)
            {
                EmlDomainGuardSet guards = proof.DomainGuards?.BindToPath(path) ?? EmlDomainGuardSet.Empty;
                EmlGuardWitness guardWitness = default;
                if (enclosureCarrier is not null && enclosureCarrier.IsAuthoritative
                    && string.Equals(enclosureCarrier.Tree.RenderRPN(), antecedentRpn, StringComparison.Ordinal)
                    && enclosureCarrier.TryGetNode(path, out EmlNodeEvaluation node))
                {
                    EmlProbeEvaluation probe = node.P1;
                    EmlEnclosureWitness enclosure = EmlEnclosureWitness.FromConcreteProbe(probe);
                    EmlBranchWitness branch = new(
                        probe.PrincipalBranch.LogDefined,
                        probe.PrincipalBranch.EnclosureCrossesNegativeRealCut,
                        probe.PrincipalBranch.ExpAfterLogRoundTrips,
                        probe.PrincipalBranch.LogAfterExpRoundTrips,
                        probe.PrincipalBranch.ExponentialTurn);
                    guardWitness = EmlGuardWitness.Create(
                        path, matchedTerm.RenderRPN(), substitutionTree.RenderRPN(), antecedentRpn, consequentRpn,
                        in enclosure, in branch, CreateNodeFacts(antecedent, consequent, enclosureCarrier));
                }
                string rulePattern = new EmlTree(pattern).RenderRPN() + " = " + consequentPattern.RenderRPN();
                EmlRuleID ruleID = EmlRuleID.Create(
                    rulePattern,
                    orientation, proof.OccurrenceDigest, guards.Digest);
                bool guarded = proof.IsRung0Eligible
                    && enclosureCarrier is not null
                    && enclosureCarrier.IsAuthoritative
                    && guardWitness.IsInstanceBound
                    && guardWitness.HasValidDigest
                    && guards.TryValidate(in guardWitness);
                rewrites.Add(new EmlLawRewrite(
                    antecedentRpn,
                    substitutionTree.RenderRPN(),
                    consequentRpn,
                    certificate,
                    proof,
                    orientation,
                    matchedTerm.RenderRPN(),
                    path,
                    false,
                    ruleID,
                    guardWitness,
                    guarded,
                    rulePattern,
                    proof.OccurrenceDigest,
                    guards.Digest));
            }
        }

        if (!term.IsGate) return;
        AppendRewritesAt(antecedent, term.Left!, path.AppendLeft(), antecedentRpn,
            pattern, consequentPattern, orientation, certificate, proof, rewrites,
            deliberationLease, enclosureCarrier, edgeBudget);
        if (edgeBudget?.Exhausted == true) return;
        AppendRewritesAt(antecedent, term.Right!, path.AppendRight(), antecedentRpn,
            pattern, consequentPattern, orientation, certificate, proof, rewrites,
            deliberationLease, enclosureCarrier, edgeBudget);
    }

    private static IReadOnlyList<EmlGuardNodeFact> CreateNodeFacts(
        EmlTree antecedent,
        EmlTree consequent,
        EmlTreeEvaluation carrier)
    {
        List<EmlGuardNodeFact> facts = new(carrier.Nodes.Count * 2);
        AppendNodeFacts(facts, EmlGuardSides.Antecedent, carrier);
        EmlTreeEvaluation consequentEvaluation = consequent.EvaluateAt(carrier.X, carrier.Y);
        AppendNodeFacts(facts, EmlGuardSides.Consequent, consequentEvaluation);
        facts.Sort(static (left, right) =>
        {
            int side = left.Side.CompareTo(right.Side);
            return side != 0 ? side : string.CompareOrdinal(left.Path.Steps, right.Path.Steps);
        });
        return facts;
    }

    private static void AppendNodeFacts(List<EmlGuardNodeFact> facts, EmlGuardSides side, EmlTreeEvaluation evaluation)
    {
        foreach ((EmlPath nodePath, EmlNodeEvaluation node) in evaluation.Nodes)
        {
            EmlProbeEvaluation probe = node.P1;
            if (!probe.Valid || !probe.Plain.Finite) continue;
            facts.Add(new EmlGuardNodeFact(
                side,
                nodePath,
                EmlEnclosureWitness.FromConcreteProbe(probe),
                new EmlBranchWitness(
                    probe.PrincipalBranch.LogDefined,
                    probe.PrincipalBranch.EnclosureCrossesNegativeRealCut,
                    probe.PrincipalBranch.ExpAfterLogRoundTrips,
                    probe.PrincipalBranch.LogAfterExpRoundTrips,
                    probe.PrincipalBranch.ExponentialTurn)));
        }
    }

    private static bool TryMatch(EmlTree.Node pattern, EmlTree.Node term, ref EmlTree.Node? substitution)
    {
        if (pattern.IsHole)
        {
            if (substitution is null)
            {
                substitution = term;
                return true;
            }
            return NodesEqual(substitution, term);
        }
        if (pattern.Token != term.Token || pattern.IsGate != term.IsGate) return false;
        return !pattern.IsGate
            || TryMatch(pattern.Left!, term.Left!, ref substitution)
            && TryMatch(pattern.Right!, term.Right!, ref substitution);
    }

    private static bool NodesEqual(EmlTree.Node left, EmlTree.Node right)
    {
        if (left.Token != right.Token || left.IsGate != right.IsGate) return false;
        return !left.IsGate
            || NodesEqual(left.Left!, right.Left!) && NodesEqual(left.Right!, right.Right!);
    }

    private static EmlTree Instantiate(EmlTree pattern, EmlTree substitution)
        => new(InstantiateNode(pattern.Root, substitution.Root));

    private static EmlTree.Node InstantiateNode(EmlTree.Node pattern, EmlTree.Node substitution)
    {
        if (pattern.IsHole) return substitution;
        return pattern.IsGate
            ? new EmlTree.Node(Eml.Op,
                InstantiateNode(pattern.Left!, substitution),
                InstantiateNode(pattern.Right!, substitution))
            : pattern;
    }

    private static bool ContainsHole(EmlTree.Node node)
        => node.IsHole || node.IsGate && (ContainsHole(node.Left!) || ContainsHole(node.Right!));
}
