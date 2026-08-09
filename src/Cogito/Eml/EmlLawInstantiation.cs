namespace Cogito;

internal readonly record struct EmlLawInstantiation(
    string Filler,
    string LeftRpn,
    string RightRpn)
{
    public static bool TryCreate(string template, string filler, out EmlLawInstantiation instantiation)
    {
        if (!EmlOneHoleLaw.TryParse(template, out EmlOneHoleLaw law)
            || !EmlTree.TryParseRPN(filler, out EmlTree? substitution))
        {
            instantiation = default;
            return false;
        }

        string left = law.InstantiateLeft(substitution!).RenderRPN();
        string right = law.InstantiateRight(substitution!).RenderRPN();
        if (left.Length > Eml.MaxProgramLen || right.Length > Eml.MaxProgramLen)
        {
            instantiation = default;
            return false;
        }

        instantiation = new EmlLawInstantiation(filler, left, right);
        return true;
    }
}

internal readonly record struct EmlLawCandidateInstantiation(
    EmlObligationResolution Obligation,
    EmlLawRewrite Rewrite,
    EmlRewritePredictionCarrier? PredictionCarrier = null,
    EmlObligationTarget? Target = null)
{
    public EmlObligationTarget Address
        => Target is EmlObligationTarget target ? target : EmlObligationTarget.Residual(Obligation.SourcePredictionID);
    public EmlLawInstantiation Instantiation => new(
        Rewrite.SubstitutionRpn,
        Rewrite.AntecedentRpn,
        Rewrite.ConsequentRpn);

    public EmlLawBehaviorCertificate SupportCertificate => Rewrite.LawCertificate;
    public EmlLawProof SupportProof => Rewrite.LawProof;

}
