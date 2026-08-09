namespace Cogito;

internal sealed class EmlRematchFixture
{
    private const int BaseEnumerationK = 7;
    private const int MaximumBindings = 512;

    private EmlRematchFixture(
        EmlSieve sieve,
        byte[] admissionImage,
        List<EmlHoleCandidate> bindings,
        List<EmlObligationResolution> obligations)
    {
        Sieve = sieve;
        AdmissionImage = admissionImage;
        Bindings = bindings;
        Obligations = obligations;
    }

    public EmlSieve Sieve { get; }
    public byte[] AdmissionImage { get; }
    public List<EmlHoleCandidate> Bindings { get; }
    public List<EmlObligationResolution> Obligations { get; }

    public static EmlRematchFixture Create(int signatureDigits)
    {
        EmlSieve sieve = CreateFiniteSieve(signatureDigits);
        byte[] admissionImage = sieve.CaptureAdmissionState();
        List<EmlHoleCandidate> bindings = CreateBindings(sieve);
        List<EmlObligationResolution> obligations = ResolveObligations(sieve);
        return new EmlRematchFixture(sieve, admissionImage, bindings, obligations);
    }

    internal static EmlRematchFixture CaptureBound(EmlSieve sieve)
    {
        ArgumentNullException.ThrowIfNull(sieve);
        return new EmlRematchFixture(
            sieve,
            sieve.CaptureAdmissionState(),
            CreateBindings(sieve),
            ResolveObligations(sieve));
    }

    public static EmlSieve CloneSieve(int signatureDigits, byte[] image)
    {
        EmlSieve clone = new(signatureDigits);
        using MemoryStream stream = new(image, writable: false);
        using CkptReader reader = new(stream);
        clone.Load(reader);
        return clone;
    }

    private static EmlSieve CreateFiniteSieve(int signatureDigits)
    {
        EmlSieve sieve = new(signatureDigits);
        foreach (string program in EmlGen.Enumerate(1, BaseEnumerationK)) sieve.Offer(program);
        sieve.Offer("x");
        sieve.Offer("11xE1EE1E", new EmlOfferContext([new TapeEventID(7001)]));
        sieve.Offer("y");
        sieve.Offer("11yE1EE1E", new EmlOfferContext([new TapeEventID(7002)]));
        sieve.DrainNewMints();
        return sieve;
    }

    internal static List<EmlHoleCandidate> CreateBindings(EmlSieve sieve)
    {
        List<string> programs = new();
        sieve.AppendCanonicalPrograms(programs, MaximumBindings);
        List<EmlHoleCandidate> candidates = new(programs.Count);
        for (int i = 0; i < programs.Count; i++)
            candidates.Add(new EmlHoleCandidate(programs[i], "canon", programs[i].Length));
        candidates.Sort(static (left, right) =>
        {
            int byCost = left.Cost.CompareTo(right.Cost);
            return byCost != 0 ? byCost : string.CompareOrdinal(left.Program, right.Program);
        });
        return candidates;
    }

    internal static List<EmlObligationResolution> ResolveObligations(EmlSieve sieve)
    {
        List<EmlObligationResolution> resolutions = new(sieve.Obligations.Count);
        for (int i = 0; i < sieve.Obligations.Count; i++)
            resolutions.Add(sieve.ResolveObligation(sieve.Obligations[i].SourcePredictionID));
        resolutions.Sort(static (left, right) => left.SourcePredictionID.Value.CompareTo(right.SourcePredictionID.Value));
        return resolutions;
    }
}
