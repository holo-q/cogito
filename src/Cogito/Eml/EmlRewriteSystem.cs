namespace Cogito;

using System.Text;

internal readonly record struct EmlRewriteReduction(
    string RPN,
    int Steps,
    bool FuelExhausted);

internal readonly record struct EmlRewriteSampledJoinReceipt(
    string LeftRPN,
    string RightRPN,
    string LeftReducedRPN,
    string RightReducedRPN,
    int Steps,
    bool Joined,
    bool FuelExhausted);

internal readonly record struct EmlRewriteSystemCensus(
    int ProbeWitnessRules,
    int SampledTerms,
    int SampledLocalPeaks,
    int JoinedSampledPeaks,
    int UnjoinedSampledPeaks,
    int FuelExhaustions,
    int GuardedRules,
    int UnguardedRules);

/// Reduces probe-witness equations under a well-founded (length, ordinal) rank. The resulting
/// joins are sampled ground evidence, not canonical forms, schema derivations, or confluence proofs.
internal sealed class EmlRewriteSystem
{
    private const int DefaultFuel = 16;
    private readonly List<EmlVerifiedLaw> _laws = new();
    private readonly List<RewriteRule> _rules = new();
    private readonly HashSet<string> _visited = new(StringComparer.Ordinal);
    private readonly List<EmlLawRewrite> _normalizationRewrites = new();
    private readonly HashSet<string> _seeds = new(StringComparer.Ordinal);
    private readonly List<string> _orderedAntecedents = new();
    private readonly List<EmlLawRewrite> _measurementRewrites = new();
    private readonly List<string> _consequences = new();
    private readonly HashSet<string> _distinctConsequences = new(StringComparer.Ordinal);
    private readonly EmlLawStore _store;
    private const int SearchRevision = 2;
    private int _searchBudget = DefaultFuel;
    private ulong _derivationDigest;

    public EmlRewriteSystem(EmlLawStore store)
    {
        _store = store;
        _searchBudget = store.RewriteSearchBudget;
        _derivationDigest = store.CompositionDigest;
        store.AppendVerifiedLaws(_laws);
        for (int i = 0; i < _laws.Count; i++)
        {
            EmlVerifiedLaw law = _laws[i];
            if (EmlOneHoleLaw.TryParse(law.Law.Template, out EmlOneHoleLaw parsed))
                _rules.Add(new RewriteRule(parsed, law.Certificate, law.Proof));
        }
    }

    public EmlRewriteReduction Reduce(string rpn, int fuel = DefaultFuel)
    {
        if (fuel < 0) throw new ArgumentOutOfRangeException(nameof(fuel));
        string current = rpn;
        _visited.Clear();
        _visited.Add(current);
        int steps = 0;
        while (steps < fuel)
        {
            _normalizationRewrites.Clear();
            AppendRewrites(current, _normalizationRewrites);
            EmlLawRewrite? selected = null;
            for (int i = 0; i < _normalizationRewrites.Count; i++)
            {
                EmlLawRewrite rewrite = _normalizationRewrites[i];
                if (rewrite.IsRelationNull
                    || !ReducesRank(rewrite.AntecedentRpn, rewrite.ConsequentRpn)
                    || _visited.Contains(rewrite.ConsequentRpn)) continue;
                if (selected is null || CompareRewrites(rewrite, selected.Value) < 0) selected = rewrite;
            }
            if (selected is null) return new EmlRewriteReduction(current, steps, false);
            current = selected.Value.ConsequentRpn;
            _visited.Add(current);
            steps++;
        }

        _normalizationRewrites.Clear();
        AppendRewrites(current, _normalizationRewrites);
        bool exhausted = false;
        for (int i = 0; i < _normalizationRewrites.Count; i++)
        {
            EmlLawRewrite rewrite = _normalizationRewrites[i];
            if (!rewrite.IsRelationNull && ReducesRank(rewrite.AntecedentRpn, rewrite.ConsequentRpn))
            {
                exhausted = true;
                break;
            }
        }
        return new EmlRewriteReduction(current, steps, exhausted);
    }

    /// Exact C2 seam: a future bounded search may call this without admitting an unguarded rule.
    /// C1 deliberately exposes only the one-step witness; no evaluator bypass or BFS is performed here.
    public bool TryDeriveGuardedOneStep(string antecedentRPN, string consequentRPN, int budget,
        out EmlCompositionSearch derivation)
    {
        if (budget < 1) throw new ArgumentOutOfRangeException(nameof(budget));
        _searchBudget = budget;
        _normalizationRewrites.Clear();
        AppendRewrites(antecedentRPN, _normalizationRewrites);
        for (int i = 0; i < _normalizationRewrites.Count; i++)
        {
            EmlLawRewrite rewrite = _normalizationRewrites[i];
            if (!rewrite.IsRung0Eligible
                || rewrite.IsRelationNull
                || !string.Equals(rewrite.ConsequentRpn, consequentRPN, StringComparison.Ordinal)
                || !ReducesRank(rewrite.AntecedentRpn, rewrite.ConsequentRpn)) continue;
            EmlCompositionStep step = new(
                rewrite.RuleID,
                rewrite.Orientation,
                rewrite.MatchedPath,
                rewrite.SubstitutionRpn,
                rewrite.AntecedentRpn,
                rewrite.ConsequentRpn,
                rewrite.GuardWitness,
                rewrite.AntecedentSize,
                rewrite.ConsequentSize,
                rewrite.RulePattern,
                rewrite.BasisLawDigest,
                rewrite.DomainGuardDigest);
            _derivationDigest = EmlCompositionDigest.Calculate(SearchRevision, budget, [step]);
            derivation = new EmlCompositionSearch(SearchRevision, budget, [step], _derivationDigest);
            _store.RecordComposition(in derivation);
            return true;
        }
        derivation = new EmlCompositionSearch(SearchRevision, budget, Array.Empty<EmlCompositionStep>(), _derivationDigest);
        return false;
    }

    public bool TryDeriveGuardedOneStepAt(
        string antecedentRPN,
        string consequentRPN,
        int budget,
        EmlTreeEvaluation enclosureCarrier,
        out EmlCompositionSearch derivation)
    {
        if (budget < 1) throw new ArgumentOutOfRangeException(nameof(budget));
        _searchBudget = budget;
        _normalizationRewrites.Clear();
        AppendRewrites(antecedentRPN, _normalizationRewrites, null, enclosureCarrier);
        for (int i = 0; i < _normalizationRewrites.Count; i++)
        {
            EmlLawRewrite rewrite = _normalizationRewrites[i];
            if (!rewrite.IsRung0Eligible || rewrite.IsRelationNull
                || !string.Equals(rewrite.ConsequentRpn, consequentRPN, StringComparison.Ordinal)
                || !ReducesRank(rewrite.AntecedentRpn, rewrite.ConsequentRpn)) continue;
            EmlCompositionStep step = new(
                rewrite.RuleID, rewrite.Orientation, rewrite.MatchedPath,
                rewrite.SubstitutionRpn, rewrite.AntecedentRpn, rewrite.ConsequentRpn,
                rewrite.GuardWitness, rewrite.AntecedentSize, rewrite.ConsequentSize,
                rewrite.RulePattern, rewrite.BasisLawDigest, rewrite.DomainGuardDigest);
            _derivationDigest = EmlCompositionDigest.Calculate(SearchRevision, budget, [step]);
            derivation = new EmlCompositionSearch(SearchRevision, budget, [step], _derivationDigest);
            _store.RecordComposition(in derivation);
            return true;
        }
        derivation = new EmlCompositionSearch(SearchRevision, budget, Array.Empty<EmlCompositionStep>(), _derivationDigest);
        return false;
    }

    public EmlRung0Result Derive(
        in EmlRewritePredictionCarrier carrier,
        string antecedentRPN,
        string consequentRPN,
        in EmlRung0Budget budget,
        EmlDeliberationLease? deliberationLease = null)
    {
        budget.Validate();
        deliberationLease?.ReserveLawRewriteTreeNodes(1);
        if (!EmlRung0Digest.IsCanonicalRPN(antecedentRPN))
            throw new ArgumentException("rung-0 antecedent is not one canonical closed EML program", nameof(antecedentRPN));
        deliberationLease?.ReserveLawRewriteTreeNodes(1);
        if (!EmlRung0Digest.IsCanonicalRPN(consequentRPN))
            throw new ArgumentException("rung-0 consequent is not one canonical closed EML program", nameof(consequentRPN));
        if (string.Equals(antecedentRPN, consequentRPN, StringComparison.Ordinal))
            return new EmlRung0Result(EmlRung0Statuses.NoCandidate, null, default);

        deliberationLease?.ReserveLawRewriteTreeNodes(1);
        EmlRewriteState initial = carrier.CreateState(antecedentRPN);
        List<SearchNode> frontier = [new SearchNode(initial, Array.Empty<EmlCompositionStep>(), 0)];
        HashSet<EmlRewriteStateKey> visited = new()
        {
            new EmlRewriteStateKey(initial.RPN, initial.GuardContextDigest),
        };
        EmlRewriteEdgeBudget edgeBudget = new(budget.MaxApplications);
        int expandedStates = 0;
        int guardRejections = 0;
        bool targetGuardRejected = false;
        bool depthTruncated = false;

        while (frontier.Count > 0)
        {
            frontier.Sort(CompareSearchNodes);
            List<SearchNode> next = new();
            for (int frontierIndex = 0; frontierIndex < frontier.Count; frontierIndex++)
            {
                SearchNode node = frontier[frontierIndex];
                if (node.Depth >= budget.MaxDepth)
                {
                    depthTruncated = true;
                    continue;
                }

                deliberationLease?.ReserveLawRewriteTreeNodes(1);
                expandedStates++;
                _normalizationRewrites.Clear();
                AppendRewritesForSearch(node.State, _normalizationRewrites, deliberationLease, edgeBudget);
                _normalizationRewrites.Sort(CompareRung0Rewrites);
                for (int rewriteIndex = 0; rewriteIndex < _normalizationRewrites.Count; rewriteIndex++)
                {
                    EmlLawRewrite rewrite = _normalizationRewrites[rewriteIndex];
                    if (!ReducesRank(rewrite.AntecedentRpn, rewrite.ConsequentRpn)) continue;
                    if (rewrite.IsRelationNull
                        || !rewrite.IsRung0Eligible
                        || _store.IsRung0RuleQuarantined(rewrite.RuleID))
                    {
                        guardRejections++;
                        targetGuardRejected |= string.Equals(
                            rewrite.ConsequentRpn, consequentRPN, StringComparison.Ordinal);
                        continue;
                    }

                    EmlCompositionStep step = new(
                        rewrite.RuleID,
                        rewrite.Orientation,
                        rewrite.MatchedPath,
                        rewrite.SubstitutionRpn,
                        rewrite.AntecedentRpn,
                        rewrite.ConsequentRpn,
                        rewrite.GuardWitness,
                        rewrite.AntecedentSize,
                        rewrite.ConsequentSize,
                        rewrite.RulePattern,
                        rewrite.BasisLawDigest,
                        rewrite.DomainGuardDigest);
                    deliberationLease?.ReserveLawRewriteTreeNodes(1);
                    EmlRewriteState state = carrier.CreateState(rewrite.ConsequentRpn);
                    EmlRewriteStateKey key = new(state.RPN, state.GuardContextDigest);
                    if (visited.Contains(key)) continue;
                    if (visited.Count >= budget.MaxStates)
                        return Finish(EmlRung0Statuses.Exhausted, null);
                    visited.Add(key);

                    EmlCompositionStep[] steps = new EmlCompositionStep[node.Steps.Count + 1];
                    for (int i = 0; i < node.Steps.Count; i++) steps[i] = node.Steps[i];
                    steps[^1] = step;
                    if (string.Equals(state.RPN, consequentRPN, StringComparison.Ordinal))
                    {
                        EmlRung0Work work = MeasureWork();
                        EmlRung0Proof proof = new(
                            carrier.PredictionID,
                            carrier.SourceDigest,
                            antecedentRPN,
                            consequentRPN,
                            SearchRevision,
                            budget,
                            steps,
                            work,
                            0);
                        proof = proof with { Digest = EmlRung0Digest.Calculate(in proof) };
                        _store.RecordRung0Proof(in proof);
                        return new EmlRung0Result(EmlRung0Statuses.Composed, proof, work);
                    }

                    if (node.Depth + 1 >= budget.MaxDepth) depthTruncated = true;
                    else next.Add(new SearchNode(state, steps, node.Depth + 1));
                }
                if (edgeBudget.Exhausted)
                    return Finish(EmlRung0Statuses.Exhausted, null);
            }
            frontier = next;
        }

        return Finish(
            targetGuardRejected ? EmlRung0Statuses.GuardRejected
                : depthTruncated ? EmlRung0Statuses.Exhausted
                : guardRejections > 0 ? EmlRung0Statuses.GuardRejected
                : EmlRung0Statuses.NoCandidate,
            null);

        EmlRung0Work MeasureWork()
            => new(expandedStates, visited.Count, edgeBudget.Applications, guardRejections);

        EmlRung0Result Finish(EmlRung0Statuses status, EmlRung0Proof? proof)
            => new(status, proof, MeasureWork());
    }

    public EmlRung0NullExecution Derive(
        in EmlRewritePredictionCarrier carrier,
        string antecedentRPN,
        in EmlLawRewrite relationNull,
        in EmlRung0Budget budget,
        EmlDeliberationLease? deliberationLease = null)
    {
        budget.Validate();
        if (!EmlRung0Digest.IsCanonicalRPN(antecedentRPN))
            throw new ArgumentException("relation-null antecedent is not canonical", nameof(antecedentRPN));
        if (!relationNull.IsRelationNull
            || relationNull.IsRung0Eligible
            || relationNull.RuleID.IsEmpty
            || relationNull.RelationNullSourceID.IsEmpty
            || relationNull.RelationNullDonorID.IsEmpty
            || relationNull.RelationNullSalt == 0
            || relationNull.RuleID != EmlRuleID.CreateRelationNull(
                relationNull.RelationNullSourceID,
                relationNull.RelationNullDonorID,
                relationNull.RelationNullSalt)
            || !string.Equals(antecedentRPN, relationNull.AntecedentRpn, StringComparison.Ordinal)
            || !EmlRung0Digest.IsCanonicalRPN(relationNull.ConsequentRpn)
            || !ReducesRank(relationNull.AntecedentRpn, relationNull.ConsequentRpn))
            return new EmlRung0NullExecution(antecedentRPN, antecedentRPN, budget, default, relationNull.RuleID, 0);

        deliberationLease?.ReserveLawRewriteTreeNodes(1);
        EmlRewriteState initial = carrier.CreateState(antecedentRPN);
        deliberationLease?.ReserveLawRewriteTreeNodes(1);
        EmlRewriteEdgeBudget edgeBudget = new(budget.MaxApplications);
        if (!edgeBudget.TryReserve(deliberationLease))
            return new EmlRung0NullExecution(antecedentRPN, antecedentRPN, budget,
                new EmlRung0Work(1, 1, 0, 0), relationNull.RuleID, 0);
        deliberationLease?.ReserveLawRewriteTreeNodes(1);
        EmlRewriteState terminal = carrier.CreateState(relationNull.ConsequentRpn);
        EmlRung0Work work = new(1, 2, edgeBudget.Applications, 0);
        return new EmlRung0NullExecution(initial.RPN, terminal.RPN, budget, work, relationNull.RuleID, 0);
    }

    public EmlCompositionSearch GetCompositionSearch()
        => new(SearchRevision, _searchBudget, Array.Empty<EmlCompositionStep>(), _derivationDigest);

    public EmlRewriteSampledJoinReceipt MeasureSampledJoin(string leftRPN, string rightRPN, int fuel = DefaultFuel)
    {
        EmlRewriteReduction left = Reduce(leftRPN, fuel);
        EmlRewriteReduction right = Reduce(rightRPN, fuel);
        return new EmlRewriteSampledJoinReceipt(
            leftRPN,
            rightRPN,
            left.RPN,
            right.RPN,
            checked(left.Steps + right.Steps),
            string.Equals(left.RPN, right.RPN, StringComparison.Ordinal),
            left.FuelExhausted || right.FuelExhausted);
    }

    public bool HasDirectReduction(string antecedentRPN, string consequentRPN)
    {
        _normalizationRewrites.Clear();
        AppendRewrites(antecedentRPN, _normalizationRewrites);
        for (int i = 0; i < _normalizationRewrites.Count; i++)
        {
            EmlLawRewrite rewrite = _normalizationRewrites[i];
            if (!rewrite.IsRelationNull
                && ReducesRank(rewrite.AntecedentRpn, rewrite.ConsequentRpn)
                && string.Equals(rewrite.ConsequentRpn, consequentRPN, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    public bool HasDirectReductionAt(string antecedentRPN, string consequentRPN, EmlTreeEvaluation enclosureCarrier)
    {
        _normalizationRewrites.Clear();
        AppendRewrites(antecedentRPN, _normalizationRewrites, null, enclosureCarrier);
        for (int i = 0; i < _normalizationRewrites.Count; i++)
        {
            EmlLawRewrite rewrite = _normalizationRewrites[i];
            if (!rewrite.IsRelationNull && rewrite.IsRung0Eligible
                && ReducesRank(rewrite.AntecedentRpn, rewrite.ConsequentRpn)
                && string.Equals(rewrite.ConsequentRpn, consequentRPN, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    public void AppendRewritesForEvaluation(
        string antecedentRPN,
        List<EmlLawRewrite> rewrites,
        EmlTreeEvaluation enclosureCarrier,
        EmlDeliberationLease? deliberationLease = null)
        => AppendRewrites(antecedentRPN, rewrites, deliberationLease, enclosureCarrier);

    public void AppendRewrites(
        IReadOnlyCollection<string> knownAntecedents,
        List<EmlLawRewrite> rewrites,
        EmlDeliberationLease? deliberationLease = null)
    {
        _orderedAntecedents.Clear();
        _orderedAntecedents.AddRange(knownAntecedents);
        _orderedAntecedents.Sort(StringComparer.Ordinal);
        string? previous = null;
        for (int i = 0; i < _orderedAntecedents.Count; i++)
        {
            string antecedentRPN = _orderedAntecedents[i];
            if (string.Equals(previous, antecedentRPN, StringComparison.Ordinal)) continue;
            previous = antecedentRPN;
            AppendRewrites(antecedentRPN, rewrites, deliberationLease);
        }
    }

    public EmlRewriteSystemCensus Measure()
    {
        _seeds.Clear();
        for (int i = 0; i < _laws.Count; i++)
        {
            EmlVerifiedLaw law = _laws[i];
            string[] fillers = ["1", "x", "y", law.Proof.AbsentFiller];
            for (int fillerIndex = 0; fillerIndex < fillers.Length; fillerIndex++)
            {
                if (!EmlLawInstantiation.TryCreate(law.Law.Template, fillers[fillerIndex], out EmlLawInstantiation instance))
                    continue;
                _seeds.Add(instance.LeftRpn);
                _seeds.Add(instance.RightRpn);
            }
        }

        int sampledLocalPeaks = 0;
        int joinedSampledPeaks = 0;
        int unjoinedSampledPeaks = 0;
        int fuelExhaustions = 0;
        int guardedRules = 0;
        int unguardedRules = 0;
        for (int i = 0; i < _rules.Count; i++)
        {
            if (_rules[i].Proof.IsGuarded) guardedRules++;
            else unguardedRules++;
        }
        foreach (string seed in _seeds)
        {
            _measurementRewrites.Clear();
            AppendRewrites(seed, _measurementRewrites);
            _consequences.Clear();
            _distinctConsequences.Clear();
            for (int i = 0; i < _measurementRewrites.Count; i++)
            {
                EmlLawRewrite rewrite = _measurementRewrites[i];
                if (rewrite.IsRelationNull
                    || !ReducesRank(rewrite.AntecedentRpn, rewrite.ConsequentRpn)
                    || !_distinctConsequences.Add(rewrite.ConsequentRpn)) continue;
                _consequences.Add(rewrite.ConsequentRpn);
            }
            _consequences.Sort(StringComparer.Ordinal);
            for (int left = 0; left < _consequences.Count; left++)
            for (int right = left + 1; right < _consequences.Count; right++)
            {
                sampledLocalPeaks++;
                EmlRewriteSampledJoinReceipt receipt =
                    MeasureSampledJoin(_consequences[left], _consequences[right]);
                if (receipt.FuelExhausted)
                {
                    fuelExhaustions++;
                    continue;
                }
                if (receipt.Joined) joinedSampledPeaks++;
                else unjoinedSampledPeaks++;
            }
        }

        return new EmlRewriteSystemCensus(
            _laws.Count,
            _seeds.Count,
            sampledLocalPeaks,
            joinedSampledPeaks,
            unjoinedSampledPeaks,
            fuelExhaustions,
            guardedRules,
            unguardedRules);
    }

    public string Report()
    {
        EmlRewriteSystemCensus census = Measure();
        StringBuilder report = new("metric\tvalue\n");
        report.Append("probe_witness_rules\t").Append(census.ProbeWitnessRules).AppendLine()
            .Append("execution_authority\tproposal-only").AppendLine()
            .Append("admission_authority\tsemantic-cas-only").AppendLine()
            .Append("global_domain\tinstance-guarded-only").AppendLine()
            .Append("guarded_rules\t").Append(census.GuardedRules).AppendLine()
            .Append("unguarded_rules\t").Append(census.UnguardedRules).AppendLine()
            .Append("derivation_revision\t").Append(SearchRevision).AppendLine()
            .Append("derivation_budget\t").Append(_searchBudget).AppendLine()
            .Append("derivation_digest\t").Append(_derivationDigest.ToString("X16")).AppendLine()
            .Append("sampled_terms\t").Append(census.SampledTerms).AppendLine()
            .Append("sampled_local_peaks\t").Append(census.SampledLocalPeaks).AppendLine()
            .Append("sampled_join_settled\t").Append(census.JoinedSampledPeaks).AppendLine()
            .Append("sampled_join_unjoined\t").Append(census.UnjoinedSampledPeaks).AppendLine()
            .Append("sampled_join_fuel_exhaustions\t").Append(census.FuelExhaustions).AppendLine();
        return report.ToString();
    }

    internal static bool ReducesRank(string antecedent, string consequent)
        => consequent.Length < antecedent.Length
            || consequent.Length == antecedent.Length
            && string.CompareOrdinal(consequent, antecedent) < 0;

    private static int CompareRewrites(EmlLawRewrite left, EmlLawRewrite right)
    {
        int byLength = left.ConsequentSize.CompareTo(right.ConsequentSize);
        if (byLength != 0) return byLength;
        int byConsequent = string.CompareOrdinal(left.ConsequentRpn, right.ConsequentRpn);
        if (byConsequent != 0) return byConsequent;
        int byPath = string.CompareOrdinal(left.MatchedPath.Steps, right.MatchedPath.Steps);
        return byPath != 0 ? byPath : left.Orientation.CompareTo(right.Orientation);
    }

    private static int CompareRung0Rewrites(EmlLawRewrite left, EmlLawRewrite right)
    {
        int order = CompareRewrites(left, right);
        if (order != 0) return order;
        order = string.CompareOrdinal(left.RuleID.Value, right.RuleID.Value);
        if (order != 0) return order;
        order = string.CompareOrdinal(left.SubstitutionRpn, right.SubstitutionRpn);
        if (order != 0) return order;
        order = string.CompareOrdinal(left.MatchedTermRpn, right.MatchedTermRpn);
        return order != 0 ? order : left.GuardWitness.Digest.CompareTo(right.GuardWitness.Digest);
    }

    private static int CompareSearchNodes(SearchNode left, SearchNode right)
    {
        int order = left.Depth.CompareTo(right.Depth);
        if (order != 0) return order;
        order = string.CompareOrdinal(left.State.RPN, right.State.RPN);
        return order != 0 ? order : left.State.GuardContextDigest.CompareTo(right.State.GuardContextDigest);
    }

    private void AppendRewrites(
        string antecedentRPN,
        List<EmlLawRewrite> rewrites,
        EmlDeliberationLease? deliberationLease = null,
        EmlTreeEvaluation? enclosureCarrier = null)
    {
        if (!EmlTree.TryParseRPN(antecedentRPN, out EmlTree? antecedent)) return;
        EmlTreeEvaluation effectiveCarrier = enclosureCarrier
            ?? antecedent!.EvaluateProbes();
        for (int i = 0; i < _rules.Count; i++)
        {
            RewriteRule rule = _rules[i];
            rule.Pattern.AppendRewrites(antecedent!, rule.Certificate, rule.Proof, rewrites, deliberationLease, effectiveCarrier);
        }
    }

    private void AppendRewritesForSearch(
        in EmlRewriteState state,
        List<EmlLawRewrite> rewrites,
        EmlDeliberationLease? deliberationLease,
        EmlRewriteEdgeBudget edgeBudget)
    {
        EmlTree antecedent = state.Evaluation.Tree;
        if (!state.Evaluation.IsAuthoritative
            || !string.Equals(antecedent.RenderRPN(), state.RPN, StringComparison.Ordinal))
            throw new InvalidDataException("rung-0 state lost its authoritative claim carrier");
        for (int i = 0; i < _rules.Count; i++)
        {
            if (edgeBudget.Exhausted) return;
            RewriteRule rule = _rules[i];
            rule.Pattern.AppendRewrites(
                antecedent,
                rule.Certificate,
                rule.Proof,
                rewrites,
                deliberationLease,
                state.Evaluation,
                edgeBudget);
        }
    }

    private readonly record struct RewriteRule(
        EmlOneHoleLaw Pattern,
        EmlLawBehaviorCertificate Certificate,
        EmlLawProof Proof);

    private readonly record struct SearchNode(
        EmlRewriteState State,
        IReadOnlyList<EmlCompositionStep> Steps,
        int Depth);

}
