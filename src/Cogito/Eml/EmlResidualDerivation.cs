namespace Cogito;

public enum EmlResidualCompositionLaws
{
    SharedExponentialArgument,
    ExponentialTail,
}

public readonly record struct EmlResidualComposition(
    EmlPredictionID SourcePredictionID,
    EmlResidualCompositionLaws Law,
    string NumeratorRPN,
    string DenominatorRPN,
    EmlProcessFunction Process)
{
    public string Receipt
        => Law == EmlResidualCompositionLaws.ExponentialTail
            ? $"{Law}:exp-series({NumeratorRPN})"
            : $"{Law}:log({NumeratorRPN}/{DenominatorRPN})";
}

/// Extracts residual processes from EML syntax before numeric search. Equal first operands cancel
/// the exponential exactly; the remaining logarithm difference is evaluated as one certified
/// log-ratio series, avoiding the subtraction that erased the source claim's correction.
public static class EmlResidualDeriver
{
    public static bool TryDeriveSharedExponentialArgument(
        EmlPredictionID sourcePredictionID,
        in EmlPrediction claim,
        long processFuel,
        out EmlResidualComposition derivation,
        EmlDeliberationLease? deliberationLease = null)
    {
        if (!claim.RhsRpn
            || !EmlTree.TryParseRPN(claim.Lhs, out EmlTree? left)
            || !EmlTree.TryParseRPN(claim.Rhs, out EmlTree? right)
            || !left!.Root.IsGate
            || !right!.Root.IsGate
            || left.Root.Left != right.Root.Left)
        {
            derivation = default;
            return false;
        }

        string numeratorRPN = new EmlTree(right.Root.Right!).RenderRPN();
        string denominatorRPN = new EmlTree(left.Root.Right!).RenderRPN();
        EmlProcessFunction process = EmlProcessFunctions.CreateLogRatio(
            numeratorRPN,
            denominatorRPN,
            processFuel);
        try
        {
            EmlProcessFunctionCertificate certificate = EmlProcessFunctions.Certify(in process, deliberationLease);
            if (!EmlProcessFunctionChecker.Check(in certificate, deliberationLease).Accepted)
            {
                derivation = default;
                return false;
            }
        }
        catch (Exception error) when (error is ArithmeticException or ArgumentException or InvalidDataException)
        {
            derivation = default;
            return false;
        }

        derivation = new EmlResidualComposition(
            sourcePredictionID,
            EmlResidualCompositionLaws.SharedExponentialArgument,
            numeratorRPN,
            denominatorRPN,
            process);
        return true;
    }

    /// Finds the source-typed residual `eml(u,eml(1,1)) ~ u`, whose value is
    /// the exponential tail `exp(u)-1-u`. The entire lhs/rhs census must contain
    /// exactly one local tail, and it must be the lhs root paired directly with
    /// rhs == argument. A nested extra match is ambiguous rather than evidence
    /// for a combined species; B2 may compose independently verified leaves.
    public static bool TryDeriveExponentialTail(
        EmlPredictionID sourcePredictionID,
        in EmlPrediction claim,
        long processFuel,
        out EmlResidualComposition derivation,
        EmlDeliberationLease? deliberationLease = null)
    {
        if (!claim.RhsRpn
            || !EmlTree.TryParseRPN(claim.Lhs, out EmlTree? left)
            || !EmlTree.TryParseRPN(claim.Rhs, out EmlTree? right)
            || !TryFindExponentialTail(left!.Root, right!.Root, out EmlTree.Node? argument))
        {
            derivation = default;
            return false;
        }

        string argumentRPN = new EmlTree(argument!).RenderRPN();
        EmlProcessFunction process = EmlProcessFunctions.CreateExpSeries(argumentRPN, processFuel);
        try
        {
            EmlProcessFunctionCertificate certificate = EmlProcessFunctions.Certify(in process, deliberationLease);
            if (!EmlProcessFunctionChecker.Check(in certificate, deliberationLease).Accepted)
            {
                derivation = default;
                return false;
            }
        }
        catch (Exception error) when (error is ArithmeticException or ArgumentException or InvalidDataException)
        {
            derivation = default;
            return false;
        }

        derivation = new EmlResidualComposition(
            sourcePredictionID,
            EmlResidualCompositionLaws.ExponentialTail,
            argumentRPN,
            Eml.One.ToString(),
            process);
        return true;
    }

    private static bool TryFindExponentialTail(
        EmlTree.Node left,
        EmlTree.Node right,
        out EmlTree.Node? argument)
    {
        argument = null;
        List<EmlTree.Node> matches = [];
        CensusLocalTailMatches(left, matches);
        CensusLocalTailMatches(right, matches);
        if (matches.Count != 1 || !ReferenceEquals(matches[0], left)
            || !left.IsGate
            || left.Right is not { IsGate: true, Left.Token: Eml.One, Right.Token: Eml.One }
            || left.Left is null
            || !string.Equals(new EmlTree(right).RenderRPN(), new EmlTree(left.Left).RenderRPN(), StringComparison.Ordinal))
            return false;
        argument = left.Left;
        return true;
    }

    private static void CensusLocalTailMatches(EmlTree.Node node, List<EmlTree.Node> matches)
    {
        if (node.IsGate
            && node.Right is { IsGate: true, Left.Token: Eml.One, Right.Token: Eml.One })
            matches.Add(node);
        if (!node.IsGate) return;
        CensusLocalTailMatches(node.Left!, matches);
        CensusLocalTailMatches(node.Right!, matches);
    }
}
