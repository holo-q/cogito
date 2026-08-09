namespace Cogito;

using System.Numerics;
using System.Text;
using Cogito.Induct;

internal static class EmlMeetInMiddle
{
    private const int MaxIndexBytes = 16 * 1024 * 1024;
    private const int MaxCandidates = 32;
    private static readonly Complex P1X = new(EmlSieve.Gamma, 0);
    private static readonly Complex P1Y = new(EmlSieve.Glaisher, 0);
    private static readonly Complex P2X = new(EmlSieve.Catalan, 0);
    private static readonly Complex P2Y = new(EmlSieve.Apery, 0);

    private readonly record struct SearchCandidate(string Program, string Target, int K);
    private readonly record struct ArmResult(string Arm, long Calls, int Offers, int Targets, int ExactClasses,
        int TheoremClasses, int CandidateCount, long InverseTransforms, long HashProbes, long JoinHits);

    public static int Run(ulong seed, long evaluatorBudget, int signatureDigits, int maxK)
    {
        if (evaluatorBudget < 70_000) throw new ArgumentOutOfRangeException(nameof(evaluatorBudget), "MITM requires at least 70,000 evaluator calls");
        if (maxK is < 1 or > 11 || (maxK & 1) == 0) throw new ArgumentOutOfRangeException(nameof(maxK), "MITM K must be odd and no greater than 11");

        string directory = Cogito.Run.New("eml-mitm").Dir;
        Dictionary<EmlSig, string> index = new();
        List<string> indexedPrograms = new();
        long indexCalls = BuildIndex(index, indexedPrograms, signatureDigits, maxK, out int indexBytes);
        if (indexBytes > MaxIndexBytes) throw new InvalidOperationException($"MITM index exceeded {MaxIndexBytes:N0}B ({indexBytes:N0}B)");

        bool positiveControl = VerifyPositiveControl(index, signatureDigits);
        ArmResult mitm = RunMitm(index, indexedPrograms, evaluatorBudget, indexCalls, signatureDigits,
            out List<SearchCandidate> candidates);
        ArmResult enumeration = RunEnumeration(evaluatorBudget, signatureDigits);
        ArmResult sampling = RunSampling(seed, evaluatorBudget, signatureDigits, indexedPrograms);
        bool beatsNulls = mitm.Targets > enumeration.Targets && mitm.Targets > sampling.Targets;

        WriteReport(directory, index.Count, indexBytes, indexCalls, positiveControl, beatsNulls,
            candidates, in mitm, in enumeration, in sampling);
        Console.WriteLine($"  EML MITM → {Path.GetRelativePath(Environment.CurrentDirectory, Path.Combine(directory, "mitm.tsv"))}");
        Console.WriteLine($"  index {index.Count:N0} signatures · {indexBytes:N0}B · {indexCalls:N0} calls · positive control {(positiveControl ? "PASS" : "FAIL")}");
        Console.WriteLine($"  targets MITM {mitm.Targets} · enum {enumeration.Targets} · sample {sampling.Targets} · verdict {(beatsNulls ? "GRADUATE" : "HOLD")}");
        return positiveControl ? 0 : 1;
    }

    private static long BuildIndex(Dictionary<EmlSig, string> index, List<string> programs,
        int signatureDigits, int maxK, out int bytes)
    {
        long calls = 0;
        bytes = 0;
        foreach (string program in EmlGen.Enumerate(1, maxK))
        {
            EmlValue p1 = Eml.Eval(program, P1X, P1Y);
            EmlValue p2 = Eml.Eval(program, P2X, P2Y);
            calls += 2;
            if (!p1.Finite || !p2.Finite) continue;
            EmlSig signature = Eml.Signature(p1, p2, signatureDigits);
            if (index.TryGetValue(signature, out string? incumbent))
            {
                if (EmlSieve.CompareCertRepresentatives(program, incumbent) < 0) index[signature] = program;
                continue;
            }
            index.Add(signature, program);
            programs.Add(program);
            bytes = checked(bytes + 64 + program.Length * sizeof(char));
            if (bytes > MaxIndexBytes) return calls;
        }
        programs.Sort(static (left, right) => EmlSieve.CompareCertRepresentatives(left, right));
        return calls;
    }

    private static ArmResult RunMitm(Dictionary<EmlSig, string> index, List<string> leftPrograms,
        long budget, long indexCalls, int signatureDigits, out List<SearchCandidate> candidates)
    {
        EmlSieve sieve = new(signatureDigits);
        candidates = new List<SearchCandidate>();
        HashSet<string> seen = new(StringComparer.Ordinal);
        long calls = indexCalls;
        long inverse = 0;
        long probes = 0;
        long hits = 0;
        List<EmlTarget> targets = new();
        EmlTarget[] allTargets = EmlSieve.BuildTargets();
        for (int i = 0; i < allTargets.Length; i++)
            if (allTargets[i].PaperK > 7 && allTargets[i].Label != "x") targets.Add(allTargets[i]);
        int probesPerTarget = Math.Max(1, (int)((budget - indexCalls - MaxCandidates * 2) / (targets.Count * 2L)));
        for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
        {
            EmlTarget target = targets[targetIndex];
            int targetProbes = Math.Min(probesPerTarget, leftPrograms.Count);
            for (int leftIndex = 0; leftIndex < targetProbes; leftIndex++)
            {
                if (calls + 2 > budget - MaxCandidates * 2) break;
                string left = leftPrograms[leftIndex];
                EmlValue a1 = Eml.Eval(left, P1X, P1Y);
                EmlValue a2 = Eml.Eval(left, P2X, P2Y);
                calls += 2;
                inverse += 2;
                if (!a1.Finite || !a2.Finite) continue;
                Complex b1 = Complex.Exp(Complex.Exp(a1.Value) - target.Ref(P1X, P1Y));
                Complex b2 = Complex.Exp(Complex.Exp(a2.Value) - target.Ref(P2X, P2Y));
                if (!IsPrincipalInverse(b1, Complex.Exp(a1.Value) - target.Ref(P1X, P1Y))
                    || !IsPrincipalInverse(b2, Complex.Exp(a2.Value) - target.Ref(P2X, P2Y))) continue;
                EmlSig wanted = Eml.Signature(new EmlValue(b1, true), new EmlValue(b2, true), signatureDigits);
                probes++;
                if (!index.TryGetValue(wanted, out string? right)) continue;
                hits++;
                string program = left + right + Eml.Op;
                if (program.Length > Eml.MaxProgramLen || !seen.Add(program)) continue;
                candidates.Add(new SearchCandidate(program, target.Label, program.Length));
            }
        }
        candidates.Sort(static (left, right) => left.K != right.K ? left.K.CompareTo(right.K) : string.CompareOrdinal(left.Program, right.Program));
        if (candidates.Count > MaxCandidates) candidates.RemoveRange(MaxCandidates, candidates.Count - MaxCandidates);
        int offers = 0;
        for (int i = 0; i < candidates.Count && calls + 2 <= budget; i++)
        {
            sieve.Offer(candidates[i].Program);
            calls += 2;
            offers++;
        }
        return CaptureResult("mitm", sieve, calls, offers, candidates.Count, inverse, probes, hits);
    }

    private static bool VerifyPositiveControl(Dictionary<EmlSig, string> index, int signatureDigits)
    {
        EmlValue a1 = Eml.Eval("1", P1X, P1Y);
        EmlValue a2 = Eml.Eval("1", P2X, P2Y);
        Complex e = new(Math.E, 0);
        Complex b1 = Complex.Exp(Complex.Exp(a1.Value) - e);
        Complex b2 = Complex.Exp(Complex.Exp(a2.Value) - e);
        EmlSig wanted = Eml.Signature(new EmlValue(b1, true), new EmlValue(b2, true), signatureDigits);
        return index.TryGetValue(wanted, out string? right) && "1" + right + Eml.Op == "11E";
    }

    private static ArmResult RunEnumeration(long budget, int signatureDigits)
    {
        EmlSieve sieve = new(signatureDigits);
        long calls = 0;
        int offers = 0;
        foreach (string program in EmlGen.Enumerate(1, 17))
        {
            if (calls + 2 > budget) break;
            sieve.Offer(program);
            calls += 2;
            offers++;
        }
        return CaptureResult("fresh-enum", sieve, calls, offers, 0, 0, 0, 0);
    }

    private static ArmResult RunSampling(ulong seed, long budget, int signatureDigits, List<string> indexedPrograms)
    {
        StringBuilder corpus = new();
        for (int i = 0; i < indexedPrograms.Count; i++) corpus.Append(indexedPrograms[i]).Append('\n');
        RePairResult grammar = Engine.Induce(Encoding.ASCII.GetBytes(corpus.ToString())).Result;
        List<EmlGen.Chunk> chunks = EmlGen.PureChunks(grammar);
        EmlSieve sieve = new(signatureDigits);
        StringBuilder builder = new();
        List<(string Toks, int Weight, int DeltaH)> pool = new();
        ulong rng = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        long calls = 0;
        int offers = 0;
        while (calls + 2 <= budget)
        {
            string program = EmlGen.Sample(chunks, 6, 40, 4, 0.125, ref rng, builder, pool);
            sieve.Offer(program);
            calls += 2;
            offers++;
        }
        return CaptureResult("fresh-bias", sieve, calls, offers, 0, 0, 0, 0);
    }

    private static ArmResult CaptureResult(string arm, EmlSieve sieve, long calls, int offers,
        int candidates, long inverse, long probes, long hits)
        => new(arm, calls, offers, sieve.TargetsHit(), sieve.ExactClasses, sieve.TheoremClasses,
            candidates, inverse, probes, hits);

    private static bool IsPrincipalInverse(Complex value, Complex expected)
    {
        if (!double.IsFinite(value.Real) || !double.IsFinite(value.Imaginary) || value == Complex.Zero) return false;
        Complex observed = Complex.Log(value);
        double scale = Math.Max(1, expected.Magnitude);
        return (observed - expected).Magnitude <= 1e-10 * scale;
    }

    private static void WriteReport(string directory, int indexCount, int indexBytes, long indexCalls,
        bool positiveControl, bool beatsNulls, List<SearchCandidate> candidates,
        in ArmResult mitm, in ArmResult enumeration, in ArmResult sampling)
    {
        StringBuilder report = new("kind\tname\tcalls\toffers\ttargets\texact_classes\ttheorem_classes\tcandidates\tinverse_transforms\thash_probes\tjoin_hits\n");
        AppendArm(report, in mitm);
        AppendArm(report, in enumeration);
        AppendArm(report, in sampling);
        report.Append("index\tforward\t").Append(indexCalls).Append("\t\t\t\t\t").Append(indexCount).Append("\t\t").Append(indexBytes).AppendLine();
        report.Append("control\t11E\t").Append(positiveControl ? 1 : 0).AppendLine();
        report.Append("verdict\tbeats_both_nulls\t").Append(beatsNulls ? 1 : 0).AppendLine();
        File.WriteAllText(Path.Combine(directory, "mitm.tsv"), report.ToString());
        StringBuilder proposals = new("target\tK\tprogram\n");
        for (int i = 0; i < candidates.Count; i++)
            proposals.Append(candidates[i].Target).Append('\t').Append(candidates[i].K).Append('\t').Append(candidates[i].Program).AppendLine();
        File.WriteAllText(Path.Combine(directory, "mitm_candidates.tsv"), proposals.ToString());
    }

    private static void AppendArm(StringBuilder report, in ArmResult arm)
        => report.Append("arm\t").Append(arm.Arm).Append('\t').Append(arm.Calls).Append('\t')
            .Append(arm.Offers).Append('\t').Append(arm.Targets).Append('\t').Append(arm.ExactClasses).Append('\t')
            .Append(arm.TheoremClasses).Append('\t').Append(arm.CandidateCount).Append('\t')
            .Append(arm.InverseTransforms).Append('\t').Append(arm.HashProbes).Append('\t').Append(arm.JoinHits).AppendLine();
}
