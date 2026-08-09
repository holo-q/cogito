namespace Cogito;

using System.Text;
using Cogito.Induct;

internal readonly record struct EmlLaw(string Template, int CertificateClasses, int Fillers, double MdlGain,
    string OccurrenceCheckFiller, string OccurrenceCheckPrediction);

internal readonly record struct EmlLawPrediction(
    EmlCert Cert,
    string LeftRpn,
    string RightRpn,
    EmlPredictionID? SourcePredictionID = null);

internal sealed record EmlLawCandidate(EmlLaw Law, List<EmlLawPrediction> Support);

internal readonly record struct EmlLawFunnel(
    int InputPredictions, int CandidatePredictions, int CandidateClasses, int CandidateForms, long PairSpace, int NeighborEdges,
    int PairInputs, int Templates, int Groups, int SupportedGroups, int MdlGroups, int OccurrenceCheckSamples,
    int VerifiedGroups);

internal readonly record struct EmlLawLane(string Name, int CandidateCap, int Neighbors, bool MutualNeighbors);

internal sealed record EmlLawLaneResult(EmlLawLane Lane, EmlLawFunnel Funnel, List<EmlLaw> Laws);

/// Anti-unifies exact RPN identities at tree grain. Accepted laws pay their two-part description
/// cost and still hold for a filler absent from the inducing certificate classes.
internal static class EmlAntiUnify
{
    private const int CandidateCap = 128;
    private const int Neighbors = 3;
    private const double MdlFloor = 1.0;

    public static List<EmlLaw> Discover(EmlSieve sieve, RePairResult grammar, ulong seed)
    {
        List<PredictionTree> claims = CollectPredictions(sieve);
        EmlLawLane shipping = new("shipping", CandidateCap, Neighbors, true);
        return DiscoverPredictions(claims, grammar, seed, shipping, out _, null);
    }

    public static List<EmlLawCandidate> DiscoverCandidates(EmlSieve sieve, RePairResult grammar, ulong seed)
    {
        List<PredictionTree> claims = CollectPredictions(sieve);
        return DiscoverCandidates(claims, grammar, seed, out _);
    }

    public static List<EmlLawCandidate> DiscoverCandidates(EmlSieve sieve, RePairResult grammar, ulong seed,
        out EmlLawFunnel funnel)
    {
        List<PredictionTree> claims = CollectPredictions(sieve);
        return DiscoverCandidates(claims, grammar, seed, out funnel);
    }

    public static List<EmlLawCandidate> DiscoverCandidates(IReadOnlyList<EmlLawPrediction> input,
        RePairResult grammar, ulong seed)
        => DiscoverCandidates(BuildPredictions(input), grammar, seed, out _);

    private static List<EmlLawCandidate> DiscoverCandidates(List<PredictionTree> claims,
        RePairResult grammar, ulong seed, out EmlLawFunnel funnel)
    {
        EmlLawLane shipping = new("shipping", CandidateCap, Neighbors, true);
        List<EmlLawCandidate> candidates = new();
        DiscoverPredictions(claims, grammar, seed, shipping, out funnel, candidates);
        return candidates;
    }

    public static List<EmlLawLaneResult> Probe(IReadOnlyList<EmlLawPrediction> input, RePairResult grammar, ulong seed)
    {
        List<PredictionTree> claims = BuildPredictions(input);
        EmlLawLane[] lanes =
        [
            new EmlLawLane("shipping", CandidateCap, Neighbors, true),
            new EmlLawLane("uncapped_mutual_top3", int.MaxValue, Neighbors, true),
            new EmlLawLane("uncapped_all_pairs", int.MaxValue, 0, false),
        ];
        List<EmlLawLaneResult> results = new(lanes.Length);
        for (int i = 0; i < lanes.Length; i++)
        {
            List<EmlLaw> laws = DiscoverPredictions(claims, grammar, seed, lanes[i], out EmlLawFunnel funnel, null);
            results.Add(new EmlLawLaneResult(lanes[i], funnel, laws));
        }
        return results;
    }

    private static List<EmlLaw> DiscoverPredictions(IReadOnlyList<PredictionTree> input, RePairResult grammar, ulong seed,
                                                EmlLawLane lane, out EmlLawFunnel funnel,
                                                List<EmlLawCandidate>? candidates)
    {
        List<PredictionTree> claims = SelectCandidatePredictions(input, lane.CandidateCap,
            out int candidateClasses, out int candidateForms);
        long pairSpace = (long)claims.Count * (claims.Count - 1) / 2;
        if (claims.Count < 2)
        {
            funnel = new EmlLawFunnel(input.Count, claims.Count, candidateClasses, candidateForms,
                pairSpace, 0, 0, 0, 0, 0, 0, 0, 0);
            return new List<EmlLaw>();
        }

        HashSet<int>[] nearest = lane.MutualNeighbors ? BuildNeighborInputs(claims, lane.Neighbors) : [];
        int neighborEdges = 0;
        for (int i = 0; i < nearest.Length; i++) neighborEdges += nearest[i].Count;
        Dictionary<string, LawGroup> groups = new(StringComparer.Ordinal);
        int pairInputs = 0;
        int templates = 0;
        for (int left = 0; left < claims.Count; left++)
        {
            for (int right = left + 1; right < claims.Count; right++)
            {
                if (lane.MutualNeighbors && (!nearest[left].Contains(right) || !nearest[right].Contains(left))) continue;
                pairInputs++;
                if (!TryTemplate(claims[left], claims[right], out LawInstance instance)) continue;
                templates++;
                if (!groups.TryGetValue(instance.Template, out LawGroup? group))
                {
                    group = new LawGroup(instance.Template, instance.LeftTemplate, instance.RightTemplate);
                    groups.Add(instance.Template, group);
                }
                group.Add(claims[left], instance.LeftFiller);
                group.Add(claims[right], instance.RightFiller);
            }
        }

        List<EmlGen.Chunk> chunks = EmlGen.PureChunks(grammar);
        List<EmlLaw> laws = new();
        int supportedGroups = 0;
        int mdlGroups = 0;
        int verificationSamples = 0;
        foreach (LawGroup group in groups.Values)
        {
            if (group.Instances.Count < 2 || group.Fillers.Count < 2) continue;
            supportedGroups++;
            double gain = CalculateMdlGain(group);
            if (gain <= MdlFloor) continue;
            mdlGroups++;
            if (!VerifyFresh(group, chunks, seed ^ CalculateHash64(group.Template), out string filler, out string claim,
                             out int samples))
            {
                verificationSamples += samples;
                continue;
            }
            verificationSamples += samples;
            EmlLaw law = new(group.Template, group.Instances.Count, group.Fillers.Count, gain, filler, claim);
            laws.Add(law);
            if (candidates is not null)
            {
                List<EmlLawPrediction> support = new(group.Instances.Count);
                foreach (PredictionTree instance in group.Instances.Values)
                    support.Add(new EmlLawPrediction(instance.Cert, instance.LeftRpn, instance.RightRpn, instance.SourcePredictionID));
                candidates.Add(new EmlLawCandidate(law, support));
            }
        }
        laws.Sort(static (left, right) =>
        {
            int byGain = right.MdlGain.CompareTo(left.MdlGain);
            return byGain != 0 ? byGain : string.CompareOrdinal(left.Template, right.Template);
        });
        funnel = new EmlLawFunnel(input.Count, claims.Count, candidateClasses, candidateForms, pairSpace,
                                  neighborEdges, pairInputs, templates, groups.Count, supportedGroups, mdlGroups,
                                  verificationSamples, laws.Count);
        return laws;
    }

    public static string Report(IReadOnlyList<EmlLaw> laws)
    {
        StringBuilder report = new("template\tcertificate_classes\tfillers\tmdl_gain\tverification_filler\tverification_claim\n");
        for (int i = 0; i < laws.Count; i++)
        {
            EmlLaw law = laws[i];
            report.Append(law.Template).Append('\t').Append(law.CertificateClasses).Append('\t').Append(law.Fillers).Append('\t')
                  .Append(law.MdlGain.ToString("F2")).Append('\t').Append(law.OccurrenceCheckFiller).Append('\t')
                  .Append(law.OccurrenceCheckPrediction).AppendLine();
        }
        return report.ToString();
    }

    public static string ReportFunnel(in EmlLawFunnel funnel)
    {
        StringBuilder report = new("stage\tcount\n");
        AppendFunnelStage(report, "input_claims", funnel.InputPredictions);
        AppendFunnelStage(report, "candidate_claims", funnel.CandidatePredictions);
        AppendFunnelStage(report, "candidate_classes", funnel.CandidateClasses);
        AppendFunnelStage(report, "candidate_forms", funnel.CandidateForms);
        AppendFunnelStage(report, "pair_space", funnel.PairSpace);
        AppendFunnelStage(report, "neighbor_edges", funnel.NeighborEdges);
        AppendFunnelStage(report, "pair_inputs", funnel.PairInputs);
        AppendFunnelStage(report, "templates", funnel.Templates);
        AppendFunnelStage(report, "template_groups", funnel.Groups);
        AppendFunnelStage(report, "supported_groups", funnel.SupportedGroups);
        AppendFunnelStage(report, "mdl_groups", funnel.MdlGroups);
        AppendFunnelStage(report, "verification_samples", funnel.OccurrenceCheckSamples);
        AppendFunnelStage(report, "fresh_verified", funnel.VerifiedGroups);
        return report.ToString();
    }

    private static void AppendFunnelStage(StringBuilder report, string stage, long count)
        => report.Append(stage).Append('\t').Append(count).AppendLine();

    private static List<PredictionTree> SelectCandidatePredictions(IReadOnlyList<PredictionTree> input, int candidateCap,
        out int candidateClasses, out int candidateForms)
    {
        if (input.Count <= candidateCap)
        {
            CountCandidateStrata(input, out candidateClasses, out candidateForms);
            return new List<PredictionTree>(input);
        }

        Dictionary<EmlCert, CertificateForms> formsByCertificate = new();
        List<CertificateForms> certificates = new();
        for (int ordinal = 0; ordinal < input.Count; ordinal++)
        {
            PredictionTree claim = input[ordinal];
            if (!formsByCertificate.TryGetValue(claim.Cert, out CertificateForms? certificate))
            {
                certificate = new CertificateForms(claim.Cert);
                formsByCertificate.Add(claim.Cert, certificate);
                certificates.Add(certificate);
            }
            certificate.TryAddForm(ordinal, claim);
        }
        certificates.Sort(static (left, right) =>
        {
            int byForms = left.Forms.Count.CompareTo(right.Forms.Count);
            return byForms != 0 ? byForms : string.CompareOrdinal(left.Ordinal, right.Ordinal);
        });

        List<PredictionTree> selected = new(candidateCap);
        HashSet<int> selectedOrdinals = new();
        for (int round = 0; selected.Count < candidateCap; round++)
        {
            bool admitted = false;
            for (int certificateIndex = 0;
                 certificateIndex < certificates.Count && selected.Count < candidateCap;
                 certificateIndex++)
            {
                CertificateForms certificate = certificates[certificateIndex];
                if (round >= certificate.Forms.Count) continue;
                PredictionForm form = certificate.Forms[round];
                selected.Add(form.Prediction);
                selectedOrdinals.Add(form.Ordinal);
                admitted = true;
            }
            if (!admitted) break;
        }

        for (int ordinal = 0; ordinal < input.Count && selected.Count < candidateCap; ordinal++)
        {
            if (selectedOrdinals.Contains(ordinal)) continue;
            selected.Add(input[ordinal]);
        }
        CountCandidateStrata(selected, out candidateClasses, out candidateForms);
        return selected;
    }

    private static void CountCandidateStrata(IReadOnlyList<PredictionTree> claims,
        out int candidateClasses, out int candidateForms)
    {
        HashSet<EmlCert> certificates = new();
        HashSet<(EmlCert Certificate, StructuralFingerprint Fingerprint)> forms = new();
        for (int i = 0; i < claims.Count; i++)
        {
            PredictionTree claim = claims[i];
            certificates.Add(claim.Cert);
            forms.Add((claim.Cert, claim.Fingerprint));
        }
        candidateClasses = certificates.Count;
        candidateForms = forms.Count;
    }

    private static List<PredictionTree> CollectPredictions(EmlSieve sieve)
    {
        // The sieve grows the parsed trees at mint time, in mint order — the same input sequence the
        // historical full mint-log re-walk produced, so the sort's tie resolution is unchanged.
        List<PredictionTree> claims = new(sieve.LawPredictionTrees.Count);
        claims.AddRange(sieve.LawPredictionTrees);
        SortPredictions(claims);
        return claims;
    }

    private static List<PredictionTree> BuildPredictions(IReadOnlyList<EmlLawPrediction> input)
    {
        List<PredictionTree> claims = new(input.Count);
        for (int i = 0; i < input.Count; i++)
        {
            EmlLawPrediction claim = input[i];
            if (CreatePredictionTree(claim.Cert, claim.LeftRpn, claim.RightRpn, claim.SourcePredictionID) is { } tree)
                claims.Add(tree);
        }
        SortPredictions(claims);
        return claims;
    }

    private static void SortPredictions(List<PredictionTree> claims)
        => claims.Sort(static (left, right) =>
        {
            int bySize = (left.LeftRpn.Length + left.RightRpn.Length).CompareTo(right.LeftRpn.Length + right.RightRpn.Length);
            return bySize != 0 ? bySize : left.Cert.HashKey().CompareTo(right.Cert.HashKey());
        });

    private static HashSet<int>[] BuildNeighborInputs(IReadOnlyList<PredictionTree> claims, int neighbors)
    {
        HashSet<int>[] nearest = new HashSet<int>[claims.Count];
        for (int i = 0; i < claims.Count; i++)
        {
            List<(int Index, int Distance)> ranked = new();
            for (int j = 0; j < claims.Count; j++)
            {
                if (i == j) continue;
                int distance = TreeDistance(claims[i].Left, claims[j].Left) + TreeDistance(claims[i].Right, claims[j].Right);
                ranked.Add((j, distance));
            }
            ranked.Sort(static (left, right) =>
            {
                int byDistance = left.Distance.CompareTo(right.Distance);
                return byDistance != 0 ? byDistance : left.Index.CompareTo(right.Index);
            });
            nearest[i] = new HashSet<int>();
            for (int n = 0; n < Math.Min(neighbors, ranked.Count); n++) nearest[i].Add(ranked[n].Index);
        }
        return nearest;
    }

    private static int TreeDistance(EmlTree.Node left, EmlTree.Node right)
    {
        if (left.Token != right.Token) return CountNodes(left) + CountNodes(right);
        if (!left.IsGate) return 0;
        return TreeDistance(left.Left!, right.Left!) + TreeDistance(left.Right!, right.Right!);
    }

    private static int CountNodes(EmlTree.Node node)
        => node.IsGate ? 1 + CountNodes(node.Left!) + CountNodes(node.Right!) : 1;

    private static bool TryTemplate(PredictionTree left, PredictionTree right, out LawInstance instance)
    {
        HoleBinding binding = new();
        EmlTree.Node? leftTemplate = Unify(left.Left, right.Left, binding);
        EmlTree.Node? rightTemplate = leftTemplate is null ? null : Unify(left.Right, right.Right, binding);
        if (leftTemplate is null || rightTemplate is null || !binding.Bound || binding.Occurrences < 2)
        {
            instance = default;
            return false;
        }
        string template = EmlRender.ToRpn(leftTemplate) + " = " + EmlRender.ToRpn(rightTemplate);
        instance = new LawInstance(template, leftTemplate, rightTemplate,
            EmlRender.ToRpn(binding.LeftFiller!), EmlRender.ToRpn(binding.RightFiller!));
        return true;
    }

    private static EmlTree.Node? Unify(EmlTree.Node left, EmlTree.Node right, HoleBinding binding)
    {
        if (left == right) return left;
        if (left.Token == right.Token && left.IsGate)
        {
            EmlTree.Node? first = Unify(left.Left!, right.Left!, binding);
            if (first is null) return null;
            EmlTree.Node? second = Unify(left.Right!, right.Right!, binding);
            return second is null ? null : new EmlTree.Node(Eml.Op, first, second);
        }
        if (!binding.Bound)
        {
            binding.Bound = true;
            binding.LeftFiller = left;
            binding.RightFiller = right;
        }
        else if (binding.LeftFiller != left || binding.RightFiller != right) return null;
        binding.Occurrences++;
        return new EmlTree.Node(EmlTree.Hole);
    }

    private static double CalculateMdlGain(LawGroup group)
    {
        double raw = 0;
        foreach (PredictionTree claim in group.Instances.Values) raw += 2.0 * (claim.LeftRpn.Length + claim.RightRpn.Length);
        int templateTokens = group.Template.Count(static token => token is '1' or 'x' or 'y' or 'E' or '?');
        double model = 2.0 * templateTokens + 8.0;
        foreach (string filler in group.Fillers) model += 2.0 * filler.Length;
        model += group.Instances.Count * Math.Ceiling(Math.Log2(group.Fillers.Count + 1));
        return raw - model;
    }

    private static bool VerifyFresh(LawGroup group, List<EmlGen.Chunk> chunks, ulong seed, out string filler,
                                    out string claim, out int samples)
    {
        ulong rng = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        StringBuilder builder = new();
        List<(string Toks, int Weight, int DeltaH)> pool = new();
        EmlGrader grader = new();
        samples = 0;
        for (int attempt = 0; attempt < 24; attempt++)
        {
            samples++;
            string candidate = EmlGen.Sample(chunks, 6, 24, 4, 0.25, ref rng, builder, pool);
            if (group.Fillers.Contains(candidate)) continue;
            EmlTree.Node? fillerNode = EmlRender.Parse(candidate);
            if (fillerNode is null) continue;
            string lhs = EmlRender.ToRpn(Fill(group.LeftTemplate, fillerNode));
            string rhs = EmlRender.ToRpn(Fill(group.RightTemplate, fillerNode));
            if (lhs.Length > Eml.MaxProgramLen || rhs.Length > Eml.MaxProgramLen) continue;
            if (grader.GradeRpn(lhs, rhs).Grade != 'E') continue;
            filler = candidate;
            claim = lhs + " = " + rhs;
            return true;
        }
        filler = "";
        claim = "";
        return false;
    }

    private static EmlTree.Node Fill(EmlTree.Node template, EmlTree.Node filler)
    {
        if (template.IsHole) return filler;
        if (!template.IsGate) return template;
        return new EmlTree.Node(Eml.Op, Fill(template.Left!, filler), Fill(template.Right!, filler));
    }

    private static ulong CalculateHash64(string text)
    {
        ulong hash = 1469598103934665603UL;
        for (int i = 0; i < text.Length; i++)
        {
            hash ^= text[i];
            hash *= 1099511628211UL;
        }
        return hash;
    }

    private static ulong CalculateTreeHash(EmlTree.Node node)
    {
        ulong hash = 1469598103934665603UL;
        AppendTreeHash(node, ref hash);
        return hash;
    }

    private static void AppendTreeHash(EmlTree.Node node, ref ulong hash)
    {
        hash ^= node.Token;
        hash *= 1099511628211UL;
        if (!node.IsGate) return;
        AppendTreeHash(node.Left!, ref hash);
        AppendTreeHash(node.Right!, ref hash);
    }

    internal readonly record struct StructuralFingerprint(ulong Left, ulong Right);

    internal sealed record PredictionTree(EmlCert Cert, string LeftRpn, string RightRpn, EmlPredictionID? SourcePredictionID, EmlTree.Node Left,
        EmlTree.Node Right, StructuralFingerprint Fingerprint);

    /// Parse one exact RhsRpn claim into its standing discovery tree — the sieve calls this once at mint time
    /// (EmlSieve.IndexMint) so DiscoverCandidates reads a standing list instead of re-walking the mint log.
    internal static PredictionTree? CreatePredictionTree(EmlCert cert, string leftRpn, string rightRpn, EmlPredictionID? sourcePredictionID)
    {
        EmlTree.Node? left = EmlRender.Parse(leftRpn);
        EmlTree.Node? right = EmlRender.Parse(rightRpn);
        if (left is null || right is null) return null;
        StructuralFingerprint fingerprint = new(CalculateTreeHash(left), CalculateTreeHash(right));
        return new PredictionTree(cert, leftRpn, rightRpn, sourcePredictionID, left, right, fingerprint);
    }

    private readonly record struct PredictionForm(int Ordinal, PredictionTree Prediction);

    private sealed class CertificateForms(EmlCert certificate)
    {
        private readonly HashSet<StructuralFingerprint> _fingerprints = new();

        public string Ordinal { get; } = certificate.Hex();
        public List<PredictionForm> Forms { get; } = new();

        public bool TryAddForm(int ordinal, PredictionTree claim)
        {
            if (!_fingerprints.Add(claim.Fingerprint)) return false;
            Forms.Add(new PredictionForm(ordinal, claim));
            return true;
        }
    }

    private sealed class HoleBinding
    {
        public bool Bound;
        public int Occurrences;
        public EmlTree.Node? LeftFiller;
        public EmlTree.Node? RightFiller;
    }

    private readonly record struct LawInstance(string Template, EmlTree.Node LeftTemplate, EmlTree.Node RightTemplate,
        string LeftFiller, string RightFiller);

    private sealed class LawGroup(string template, EmlTree.Node leftTemplate, EmlTree.Node rightTemplate)
    {
        public string Template => template;
        public EmlTree.Node LeftTemplate => leftTemplate;
        public EmlTree.Node RightTemplate => rightTemplate;
        public Dictionary<string, PredictionTree> Instances { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Fillers { get; } = new(StringComparer.Ordinal);

        public void Add(PredictionTree claim, string filler)
        {
            Instances.TryAdd(claim.Cert.Hex(), claim);
            Fillers.Add(filler);
        }
    }
}
