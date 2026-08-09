namespace Cogito;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ronmamon;

/// The three arms of the R20 causal triad.  The names are deliberately roles,
/// not generic left/right positions: the report must remain readable after the
/// run directories have been archived.
public enum WorldNoveltyArmKinds : byte
{
    EpochLive,
    StationaryControl,
    EpochOrderNull,
}

/// The only biological shapes the causal triad may emit.  MechanismInvalid and
/// BankedNull are result species, not excuses to collapse a missing opportunity
/// into an honest no-effect result.
public enum WorldNoveltyResultSpecies : byte
{
    StructuredNovelty,
    ReorderingEffect,
    NoEffect,
    BankedNull,
    MechanismInvalid,
    UnexpectedContrast,
}

public enum WorldNoveltyContrastPatterns : byte
{
    None,
    EpochOnly,
    StationaryOnly,
    OrderNullOnly,
    EpochAndStationary,
    EpochAndOrderNull,
    StationaryAndOrderNull,
    AllArms,
}

public enum WorldNoveltyAssayStatuses : byte { Exact, Invalid }
public enum WorldNoveltyPowerStatuses : byte { Powered, Unpowered }
public enum WorldNoveltyVerdictStatuses : byte { PASS, FAIL, BANKED_NULL, INVALID }

/// The preregistered comparison contract.  Boundary and opportunity window
/// are measured in the same step domain as the arm receipts; no post-hoc window
/// may be selected by the adjudicator.
public readonly record struct WorldNoveltyBoundaryWindow(
    int BoundaryStep,
    int BoundaryPrefix,
    int WindowSteps,
    int MinimumPaidComparisons)
{
    public void Validate()
    {
        if (BoundaryStep < 0 || BoundaryPrefix <= 0 || WindowSteps <= 0 || MinimumPaidComparisons <= 0)
            throw new InvalidDataException("R20 world-novelty preregistration is malformed");
    }
}

public sealed class WorldNoveltyAdjudicationConfig
{
    public WorldNoveltyAdjudicationConfig(
        IReadOnlyList<WorldNoveltyBoundaryWindow> boundaries,
        double equalityTolerance,
        double minimumDirectionalEffect,
        int minimumArmDivergences,
        string contrastDigest)
    {
        Boundaries = boundaries?.ToArray() ?? throw new ArgumentNullException(nameof(boundaries));
        EqualityTolerance = equalityTolerance;
        MinimumDirectionalEffect = minimumDirectionalEffect;
        MinimumArmDivergences = minimumArmDivergences;
        ContrastDigest = contrastDigest ?? throw new ArgumentNullException(nameof(contrastDigest));
    }

    public static WorldNoveltyAdjudicationConfig FromRegistration(WorldNoveltyRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        return new(registration.OpportunityFloor.BoundarySteps.Select((step, index)
            => new WorldNoveltyBoundaryWindow(step, registration.OpportunityFloor.BoundaryPrefixes[index], registration.OpportunityFloor.WindowK,
                registration.OpportunityFloor.MinimumPaidComparisonsPerBoundary)).ToArray(),
            registration.Contrast.EqualityTolerance, registration.Contrast.MinimumDirectionalEffect,
            registration.Contrast.MinimumArmDivergences, registration.Contrast.Digest);
    }
    public IReadOnlyList<WorldNoveltyBoundaryWindow> Boundaries { get; }
    public double EqualityTolerance { get; }
    public double MinimumDirectionalEffect { get; }
    public int MinimumArmDivergences { get; }
    public string ContrastDigest { get; }
    public IReadOnlyList<int> BoundarySteps => Boundaries.Select(static boundary => boundary.BoundaryStep).ToArray();

    public void Validate()
    {
        if (Boundaries.Count == 0 || double.IsNaN(EqualityTolerance) || double.IsInfinity(EqualityTolerance) || EqualityTolerance < 0
            || double.IsNaN(MinimumDirectionalEffect) || double.IsInfinity(MinimumDirectionalEffect) || MinimumDirectionalEffect <= 0
            || MinimumArmDivergences <= 0
            || !WorldNoveltyArmResult.IsDigest(ContrastDigest))
            throw new InvalidDataException("R20 world-novelty preregistration is malformed");
        for (int i = 0; i < Boundaries.Count; i++)
        {
            WorldNoveltyBoundaryWindow boundary = Boundaries[i];
            boundary.Validate();
            if (i > 0 && (boundary.BoundaryStep <= Boundaries[i - 1].BoundaryStep || boundary.BoundaryPrefix <= Boundaries[i - 1].BoundaryPrefix))
                throw new InvalidDataException("R20 world-novelty boundaries are not strictly ordered");
        }
    }
}

/// One arm's typed evidence.  OfferedFuel is the preregistered plan and is the
/// only fuel vector used for mechanism equalization. ActualFuel and RefundFuel
/// remain evidence: adaptive execution is allowed to settle them differently.
public sealed class WorldNoveltyArmResult
{
    public WorldNoveltyArmResult(
        WorldNoveltyArmKinds kind,
        string runID,
        ulong seed,
        string worldSHA256,
        string scheduleSHA256,
        string configSHA256,
        string schemaSHA256,
        string offeredFuelVectorSHA256,
        int horizon,
        IReadOnlyList<int> boundarySteps,
        IReadOnlyList<int> fundedComparisonsByBoundary,
        IReadOnlyList<int> candidateDivergencesByBoundary,
        EmlDeliberationCounts offeredFuel,
        EmlDeliberationCounts actualFuel = default,
        EmlDeliberationCounts refundFuel = default,
        IReadOnlyList<int>? transitionPrefixes = null,
        int readRows = 0,
        int canonicalStateRows = 0,
        int candidatePresent = 0,
        IReadOnlyList<int>? boundaryPrefixes = null)
    {
        Kind = kind;
        RunID = runID ?? throw new ArgumentNullException(nameof(runID));
        Seed = seed;
        WorldSHA256 = worldSHA256 ?? throw new ArgumentNullException(nameof(worldSHA256));
        ScheduleSHA256 = scheduleSHA256 ?? throw new ArgumentNullException(nameof(scheduleSHA256));
        ConfigSHA256 = configSHA256 ?? throw new ArgumentNullException(nameof(configSHA256));
        SchemaSHA256 = schemaSHA256 ?? throw new ArgumentNullException(nameof(schemaSHA256));
        OfferedFuelVectorSHA256 = offeredFuelVectorSHA256 ?? throw new ArgumentNullException(nameof(offeredFuelVectorSHA256));
        Horizon = horizon;
        BoundarySteps = boundarySteps?.ToArray() ?? throw new ArgumentNullException(nameof(boundarySteps));
        PaidComparisonsByBoundary = fundedComparisonsByBoundary?.ToArray() ?? throw new ArgumentNullException(nameof(fundedComparisonsByBoundary));
        CandidateDivergencesByBoundary = candidateDivergencesByBoundary?.ToArray() ?? throw new ArgumentNullException(nameof(candidateDivergencesByBoundary));
        OfferedFuel = offeredFuel;
        ActualFuel = actualFuel;
        RefundFuel = refundFuel;
        TransitionPrefixes = transitionPrefixes?.ToArray() ?? Array.Empty<int>();
        ReadRows = readRows;
        CanonicalStateRows = canonicalStateRows;
        CandidatePresent = candidatePresent;
        BoundaryPrefixes = boundaryPrefixes?.ToArray() ?? Array.Empty<int>();
    }

    public WorldNoveltyArmKinds Kind { get; }
    public string RunID { get; }
    public ulong Seed { get; }
    public string WorldSHA256 { get; }
    public string ScheduleSHA256 { get; }
    public string ConfigSHA256 { get; }
    public string SchemaSHA256 { get; }
    public string OfferedFuelVectorSHA256 { get; }
    public int Horizon { get; }
    public IReadOnlyList<int> BoundarySteps { get; }
    public IReadOnlyList<int> PaidComparisonsByBoundary { get; }
    public IReadOnlyList<int> CandidateDivergencesByBoundary { get; }
    public double PostBoundaryEffect => TotalPaidComparisons == 0 ? 0d : (double)CandidateDivergences / TotalPaidComparisons;
    public EmlDeliberationCounts OfferedFuel { get; }
    public EmlDeliberationCounts ActualFuel { get; }
    public EmlDeliberationCounts RefundFuel { get; }
    public IReadOnlyList<int> TransitionPrefixes { get; }
    public int ReadRows { get; }
    public int CanonicalStateRows { get; }
    public int CandidatePresent { get; }
    public int CandidateDivergences => CandidateDivergencesByBoundary.Sum();
    public IReadOnlyList<int> BoundaryPrefixes { get; }

    // The plan and observation vectors remain separate: only OfferedFuel is
    // used for equalization, while actual/refund vectors are retained as evidence.
    public EmlDeliberationCounts PlannedFuel => OfferedFuel;
    public double PostBoundaryMetric => PostBoundaryEffect;
    public int TotalPaidComparisons => PaidComparisonsByBoundary.Sum();

    internal void Validate(WorldNoveltyAdjudicationConfig config)
    {
        if (!Enum.IsDefined(Kind) || string.IsNullOrWhiteSpace(RunID)
            || !IsDigest(WorldSHA256) || !IsDigest(ScheduleSHA256) || !IsDigest(ConfigSHA256) || !IsDigest(SchemaSHA256)
            || !IsDigest(OfferedFuelVectorSHA256)
            || Horizon <= 0
            || BoundarySteps.Count != config.Boundaries.Count
            || !IsMonotone(BoundarySteps)
            || ReadRows < 0 || CanonicalStateRows < 0 || CandidatePresent < 0 || CandidateDivergences < 0
            || CandidateDivergences > CandidatePresent
            || !IsMonotone(TransitionPrefixes))
            throw new InvalidDataException($"R20 {Kind} arm evidence is malformed");
        if (PaidComparisonsByBoundary.Count != config.Boundaries.Count
            || PaidComparisonsByBoundary.Any(static value => value < 0))
            throw new InvalidDataException($"R20 {Kind} arm does not carry one liveness count per preregistered boundary");
        if (CandidateDivergencesByBoundary.Count != config.Boundaries.Count
            || CandidateDivergencesByBoundary.Any(static value => value < 0))
            throw new InvalidDataException($"R20 {Kind} arm does not carry one divergence count per preregistered boundary");
        if (BoundaryPrefixes.Count != config.Boundaries.Count)
            throw new InvalidDataException($"R20 {Kind} arm does not carry one boundary prefix per preregistered boundary");
        if (BoundaryPrefixes.Any(static value => value < 0))
            throw new InvalidDataException($"R20 {Kind} arm carries a negative boundary prefix");
        for (int i = 0; i < BoundaryPrefixes.Count; i++)
            if (!TransitionPrefixes.Contains(BoundaryPrefixes[i]))
                throw new InvalidDataException($"R20 {Kind} arm omitted admitted boundary prefix {BoundaryPrefixes[i]}");
        for (int i = 0; i < config.Boundaries.Count; i++)
            if (CandidateDivergencesByBoundary[i] > PaidComparisonsByBoundary[i])
                throw new InvalidDataException($"R20 {Kind} arm divergence count exceeds funded comparisons at boundary {i}");
        for (int i = 0; i < config.Boundaries.Count; i++)
            if (i > 0 && config.Boundaries[i].BoundaryStep <= config.Boundaries[i - 1].BoundaryStep)
                throw new InvalidDataException("R20 boundary contract is not ordered");
        OfferedFuel.ValidateNonnegative("R20 offered fuel");
        ActualFuel.ValidateNonnegative("R20 actual fuel");
        RefundFuel.ValidateNonnegative("R20 refund fuel");
        for (int i = 0; i < config.Boundaries.Count; i++)
            if (BoundarySteps[i] != config.Boundaries[i].BoundaryStep || BoundaryPrefixes[i] != config.Boundaries[i].BoundaryPrefix
                || BoundarySteps[i] < 0 || BoundarySteps[i] + config.Boundaries[i].WindowSteps > Horizon)
                throw new InvalidDataException($"R20 {Kind} arm boundary {i} is outside the preregistered window");
    }

    private static bool IsMonotone(IReadOnlyList<int> values)
    {
        for (int i = 0; i < values.Count; i++)
            if (values[i] < 0 || (i > 0 && values[i] <= values[i - 1])) return false;
        return true;
    }

    internal static bool IsDigest(string? value)
        => value is not null && value.Length == 64 && value.All(Uri.IsHexDigit) && value == value.ToLowerInvariant();
}

/// Economics evidence is arm-local.  The same opportunity identity may occur in
/// each equalized arm, but a report must never erase the arm/run that carried it.
public readonly record struct WorldNoveltyArmAdmissionEconomicsSummary(
    WorldNoveltyArmKinds Arm,
    string RunID,
    int Opportunities,
    int EligibleCandidates,
    int PricedCandidates,
    int AdmittedCandidates,
    int RefusedCandidates,
    long LiteralCostMbits,
    long MaterializedCostMbits,
    long MarginalSavingsMbits,
    string IdentitySetDigest,
    string EvidenceDigest)
{
    internal static WorldNoveltyArmAdmissionEconomicsSummary From(WorldNoveltyAdmissionEconomicsEvidence evidence)
    {
        evidence.Validate();
        IReadOnlyList<EmlPatternGrammarAdmissionEconomicsReceipt> receipts = evidence.Receipts;
        long literal = 0, materialized = 0, savings = 0;
        int eligible = 0, priced = 0, admitted = 0, refused = 0;
        List<string> identities = new(receipts.Count);
        List<string> digests = new(receipts.Count);
        foreach (EmlPatternGrammarAdmissionEconomicsReceipt receipt in receipts)
        {
            identities.Add(receipt.IdentityKey);
            digests.Add(receipt.Digest);
            if (!receipt.Eligible) continue;
            eligible++;
            if (receipt.MdlPriced) priced++;
            if (receipt.MaterializationAdmitted) admitted++;
            if (receipt.IsRefusal) refused++;
            literal = checked(literal + receipt.Price.LiteralCostMbits);
            materialized = checked(materialized + receipt.Price.MaterializedCostMbits);
            savings = checked(savings + receipt.Price.MarginalSavingsMbits);
        }
        WorldNoveltyArmAdmissionEconomicsSummary summary = new(evidence.Arm, evidence.RunID, receipts.Count,
            eligible, priced, admitted, refused, literal, materialized, savings,
            Digest(string.Join('|', identities.OrderBy(static value => value, StringComparer.Ordinal))),
            Digest(string.Join('|', digests.OrderBy(static value => value, StringComparer.Ordinal))));
        summary.Validate();
        return summary;
    }

    public void Validate()
    {
        if (!Enum.IsDefined(Arm) || string.IsNullOrWhiteSpace(RunID) || Opportunities <= 0
            || EligibleCandidates < 0 || PricedCandidates < 0 || AdmittedCandidates < 0 || RefusedCandidates < 0
            || EligibleCandidates > Opportunities || PricedCandidates > EligibleCandidates
            || AdmittedCandidates + RefusedCandidates > PricedCandidates
            || !WorldNoveltyArmResult.IsDigest(IdentitySetDigest) || !WorldNoveltyArmResult.IsDigest(EvidenceDigest)
            || MarginalSavingsMbits != checked(LiteralCostMbits - MaterializedCostMbits))
            throw new InvalidDataException("R20 arm promotion economics summary is malformed");
    }

    private static string Digest(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

/// Corpus-wide promotion economics, carried beside the world contrast without
/// collapsing equalized-arm twins into one unbound receipt list.
public sealed class WorldNoveltyAdmissionEconomicsSummary
{
    private WorldNoveltyAdmissionEconomicsSummary(
        IReadOnlyList<WorldNoveltyArmAdmissionEconomicsSummary> arms,
        int opportunities, int eligibleCandidates, int pricedCandidates, int admittedCandidates, int refusedCandidates,
        long literalCostMbits, long materializedCostMbits, long marginalSavingsMbits, string evidenceDigest)
    {
        Arms = arms;
        Opportunities = opportunities;
        EligibleCandidates = eligibleCandidates;
        PricedCandidates = pricedCandidates;
        AdmittedCandidates = admittedCandidates;
        RefusedCandidates = refusedCandidates;
        LiteralCostMbits = literalCostMbits;
        MaterializedCostMbits = materializedCostMbits;
        MarginalSavingsMbits = marginalSavingsMbits;
        EvidenceDigest = evidenceDigest;
    }

    public IReadOnlyList<WorldNoveltyArmAdmissionEconomicsSummary> Arms { get; }
    public int Opportunities { get; }
    public int EligibleCandidates { get; }
    public int PricedCandidates { get; }
    public int AdmittedCandidates { get; }
    public int RefusedCandidates { get; }
    public long LiteralCostMbits { get; }
    public long MaterializedCostMbits { get; }
    public long MarginalSavingsMbits { get; }
    public string EvidenceDigest { get; }

    internal static WorldNoveltyAdmissionEconomicsSummary From(
        IReadOnlyList<WorldNoveltyAdmissionEconomicsEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.Count != 3) throw new InvalidDataException("R20 promotion economics must carry all three arm receipts");
        List<WorldNoveltyArmAdmissionEconomicsSummary> arms = evidence.Select(WorldNoveltyArmAdmissionEconomicsSummary.From).ToList();
        if (arms.Select(static arm => arm.Arm).Distinct().Count() != arms.Count
            || arms.Select(static arm => arm.RunID).Distinct(StringComparer.Ordinal).Count() != arms.Count)
            throw new InvalidDataException("R20 promotion economics repeats an arm or run identity");
        WorldNoveltyAdmissionEconomicsSummary summary = new(arms,
            arms.Sum(static arm => arm.Opportunities), arms.Sum(static arm => arm.EligibleCandidates),
            arms.Sum(static arm => arm.PricedCandidates), arms.Sum(static arm => arm.AdmittedCandidates),
            arms.Sum(static arm => arm.RefusedCandidates), arms.Sum(static arm => arm.LiteralCostMbits),
            arms.Sum(static arm => arm.MaterializedCostMbits), arms.Sum(static arm => arm.MarginalSavingsMbits),
            Digest(string.Join('|', arms.OrderBy(static arm => arm.Arm).Select(static arm =>
                string.Join(':', arm.Arm, arm.RunID, arm.IdentitySetDigest, arm.EvidenceDigest)))));
        summary.Validate();
        return summary;
    }

    internal static WorldNoveltyAdmissionEconomicsSummary FromEncoded(
        IReadOnlyList<WorldNoveltyArmAdmissionEconomicsSummary> arms,
        int opportunities, int eligibleCandidates, int pricedCandidates, int admittedCandidates, int refusedCandidates,
        long literalCostMbits, long materializedCostMbits, long marginalSavingsMbits, string evidenceDigest)
    {
        ArgumentNullException.ThrowIfNull(arms);
        WorldNoveltyAdmissionEconomicsSummary summary = new(arms.ToArray(), opportunities, eligibleCandidates,
            pricedCandidates, admittedCandidates, refusedCandidates, literalCostMbits, materializedCostMbits,
            marginalSavingsMbits, evidenceDigest);
        summary.Validate();
        return summary;
    }

    public void Validate()
    {
        if (Arms.Count != 3 || Arms.Select(static arm => arm.Arm).Distinct().Count() != 3
            || Arms.Select(static arm => arm.RunID).Distinct(StringComparer.Ordinal).Count() != 3
            || Opportunities != Arms.Sum(static arm => arm.Opportunities)
            || EligibleCandidates != Arms.Sum(static arm => arm.EligibleCandidates)
            || PricedCandidates != Arms.Sum(static arm => arm.PricedCandidates)
            || AdmittedCandidates != Arms.Sum(static arm => arm.AdmittedCandidates)
            || RefusedCandidates != Arms.Sum(static arm => arm.RefusedCandidates)
            || LiteralCostMbits != Arms.Sum(static arm => arm.LiteralCostMbits)
            || MaterializedCostMbits != Arms.Sum(static arm => arm.MaterializedCostMbits)
            || MarginalSavingsMbits != checked(LiteralCostMbits - MaterializedCostMbits)
            || !WorldNoveltyArmResult.IsDigest(EvidenceDigest))
            throw new InvalidDataException("R20 promotion economics summary is malformed");
        foreach (WorldNoveltyArmAdmissionEconomicsSummary arm in Arms) arm.Validate();
        string expected = Digest(string.Join('|', Arms.OrderBy(static arm => arm.Arm).Select(static arm =>
            string.Join(':', arm.Arm, arm.RunID, arm.IdentitySetDigest, arm.EvidenceDigest))));
        if (!string.Equals(expected, EvidenceDigest, StringComparison.Ordinal))
            throw new InvalidDataException("R20 promotion economics arm digest changed");
    }

    private static string Digest(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public readonly record struct WorldNoveltyVerdict(
    WorldNoveltyResultSpecies Species,
    WorldNoveltyAssayStatuses Assay,
    WorldNoveltyPowerStatuses Power,
    WorldNoveltyVerdictStatuses Status,
    WorldNoveltyContrastPatterns ArmEffectPattern,
    string Detail,
    string EvidenceDigest)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Species) || !Enum.IsDefined(Assay) || !Enum.IsDefined(Power) || !Enum.IsDefined(Status)
            || !Enum.IsDefined(ArmEffectPattern)
            || string.IsNullOrWhiteSpace(Detail) || !WorldNoveltyArmResult.IsDigest(EvidenceDigest))
            throw new InvalidDataException("R20 world-novelty verdict is malformed");
        WorldNoveltyVerdictStatuses expected = Species switch
        {
            WorldNoveltyResultSpecies.StructuredNovelty or WorldNoveltyResultSpecies.ReorderingEffect => WorldNoveltyVerdictStatuses.PASS,
            WorldNoveltyResultSpecies.NoEffect or WorldNoveltyResultSpecies.UnexpectedContrast => WorldNoveltyVerdictStatuses.FAIL,
            WorldNoveltyResultSpecies.BankedNull => WorldNoveltyVerdictStatuses.BANKED_NULL,
            _ => WorldNoveltyVerdictStatuses.INVALID,
        };
        if (Status != expected) throw new InvalidDataException("R20 world-novelty verdict status disagrees with species");
        if (Species == WorldNoveltyResultSpecies.MechanismInvalid && Assay != WorldNoveltyAssayStatuses.Invalid)
            throw new InvalidDataException("R20 mechanism-invalid result is not assay-invalid");
    }
}

/// The standalone R20 report.  It has no ClosureCertificate or R19 report
/// dependency: certification of the older two-arm report remains untouched.
public sealed class WorldNoveltyReport
{
    internal WorldNoveltyReport(
        WorldNoveltyAdjudicationConfig config,
        WorldNoveltyArmResult epochLive,
        WorldNoveltyArmResult stationaryControl,
        WorldNoveltyArmResult epochOrderNull,
        WorldNoveltyVerdict verdict,
        WorldNoveltyAdmissionEconomicsSummary admissionEconomics,
        string digest)
    {
        Config = config;
        EpochLive = epochLive;
        StationaryControl = stationaryControl;
        EpochOrderNull = epochOrderNull;
        Verdict = verdict;
        AdmissionEconomics = admissionEconomics;
        Digest = digest;
    }

    public WorldNoveltyAdjudicationConfig Config { get; }
    public WorldNoveltyArmResult EpochLive { get; }
    public WorldNoveltyArmResult StationaryControl { get; }
    public WorldNoveltyArmResult EpochOrderNull { get; }
    public WorldNoveltyVerdict Verdict { get; }
    public WorldNoveltyAdmissionEconomicsSummary AdmissionEconomics { get; }
    public IReadOnlyList<WorldNoveltyArmAdmissionEconomicsSummary> AdmissionEconomicsByArm => AdmissionEconomics.Arms;
    public string Digest { get; }
    public WorldNoveltyResultSpecies Species => Verdict.Species;
    public WorldNoveltyContrastPatterns ContrastPattern => Verdict.ArmEffectPattern;
    public string ReportSpecies => WorldNoveltyRegistration.ReportSpecies;
    public string TitleClause => WorldNoveltyRegistration.TitleClause;
    public WorldNoveltyArmResult Live => EpochLive;
    public WorldNoveltyArmResult Control => StationaryControl;
    public WorldNoveltyArmResult OrderNull => EpochOrderNull;
    public WorldNoveltyVerdict Result => Verdict;

    internal static WorldNoveltyReport Create(
        WorldNoveltyAdjudicationConfig config,
        WorldNoveltyArmResult epochLive,
        WorldNoveltyArmResult stationaryControl,
        WorldNoveltyArmResult epochOrderNull,
        WorldNoveltyVerdict verdict,
        WorldNoveltyAdmissionEconomicsSummary admissionEconomics)
    {
        config.Validate();
        admissionEconomics.Validate();
        verdict.Validate();
        WorldNoveltyReport report = new(config, epochLive, stationaryControl, epochOrderNull, verdict, admissionEconomics, "");
        string digest = ComputeDigest(report.EncodeDocument(""));
        return new(config, epochLive, stationaryControl, epochOrderNull, verdict, admissionEconomics, digest);
    }

    public byte[] Encode()
    {
        Validate();
        byte[] first = EncodeDocument(Digest);
        byte[] second = EncodeDocument(Digest);
        if (!first.AsSpan().SequenceEqual(second)) throw new InvalidDataException("R20 world-novelty RON encoding is nondeterministic");
        return first;
    }

    public static WorldNoveltyReport Decode(ReadOnlySpan<byte> bytes)
    {
        WorldNoveltyReportRON document = RonSerializer.Deserialize<WorldNoveltyReportRON>(bytes);
        if (document.SchemaVersion != 2 || !string.Equals(document.Species, "WorldNoveltyReport", StringComparison.Ordinal)
            || !string.Equals(document.TitleClause, WorldNoveltyRegistration.TitleClause, StringComparison.Ordinal))
            throw new InvalidDataException("unsupported R20 world-novelty report schema");
        WorldNoveltyAdjudicationConfig config = new(document.Boundaries.Select(static boundary =>
            new WorldNoveltyBoundaryWindow(boundary.BoundaryStep, boundary.BoundaryPrefix, boundary.WindowSteps, boundary.MinimumPaidComparisons)).ToArray(),
            document.EqualityTolerance, document.MinimumDirectionalEffect, document.MinimumArmDivergences, document.ContrastDigest);
        WorldNoveltyArmResult live = DecodeArm(document.Live);
        WorldNoveltyArmResult control = DecodeArm(document.Control);
        WorldNoveltyArmResult orderNull = DecodeArm(document.OrderNull);
        WorldNoveltyVerdict verdict = new(ParseEnum<WorldNoveltyResultSpecies>(document.VerdictSpecies),
            ParseEnum<WorldNoveltyAssayStatuses>(document.VerdictAssay), ParseEnum<WorldNoveltyPowerStatuses>(document.VerdictPower),
            ParseEnum<WorldNoveltyVerdictStatuses>(document.VerdictStatus), ParseEnum<WorldNoveltyContrastPatterns>(document.VerdictPattern),
            document.VerdictDetail, document.VerdictEvidenceDigest);
        List<WorldNoveltyArmAdmissionEconomicsSummary> economicsArms = document.EconomicsArms.Select(static arm =>
            new WorldNoveltyArmAdmissionEconomicsSummary(ParseEnum<WorldNoveltyArmKinds>(arm.Arm), arm.RunID,
                arm.Opportunities, arm.EligibleCandidates, arm.PricedCandidates, arm.AdmittedCandidates, arm.RefusedCandidates,
                arm.LiteralCostMbits, arm.MaterializedCostMbits, arm.MarginalSavingsMbits, arm.IdentitySetDigest, arm.EvidenceDigest)).ToList();
        WorldNoveltyAdmissionEconomicsSummary economics = WorldNoveltyAdmissionEconomicsSummary.FromEncoded(
            economicsArms, document.EconomicsOpportunities, document.EconomicsEligible, document.EconomicsPriced,
            document.EconomicsAdmitted, document.EconomicsRefused, document.EconomicsLiteral, document.EconomicsMaterialized,
            document.EconomicsSavings, document.EconomicsDigest);
        WorldNoveltyReport report = new(config, live, control, orderNull, verdict, economics, document.Digest);
        report.Validate();
        if (!string.Equals(report.Digest, ComputeDigest(report.EncodeDocument("")), StringComparison.Ordinal))
            throw new InvalidDataException("R20 world-novelty report digest mismatch");
        if (!report.Encode().AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("R20 world-novelty report RON drift");
        return report;
    }

    public void Validate()
    {
        Config.Validate();
        EpochLive.Validate(Config); StationaryControl.Validate(Config); EpochOrderNull.Validate(Config);
        AdmissionEconomics.Validate(); ValidateAdmissionEconomicsArmBinding(); Verdict.Validate();
        if (EpochLive.Kind != WorldNoveltyArmKinds.EpochLive || StationaryControl.Kind != WorldNoveltyArmKinds.StationaryControl
            || EpochOrderNull.Kind != WorldNoveltyArmKinds.EpochOrderNull)
            throw new InvalidDataException("R20 world-novelty report arm roles are not the causal triad");
        if (!WorldNoveltyArmResult.IsDigest(Digest)) throw new InvalidDataException("R20 world-novelty report digest is malformed");
    }

    private void ValidateAdmissionEconomicsArmBinding()
    {
        foreach (WorldNoveltyArmAdmissionEconomicsSummary economics in AdmissionEconomics.Arms)
        {
            WorldNoveltyArmResult arm = economics.Arm switch
            {
                WorldNoveltyArmKinds.EpochLive => EpochLive,
                WorldNoveltyArmKinds.StationaryControl => StationaryControl,
                WorldNoveltyArmKinds.EpochOrderNull => EpochOrderNull,
                _ => throw new InvalidDataException("R20 promotion economics names an unknown arm"),
            };
            if (!string.Equals(economics.RunID, arm.RunID, StringComparison.Ordinal))
                throw new InvalidDataException($"R20 promotion economics run identity is not bound to {economics.Arm}");
        }
    }

    private byte[] EncodeDocument(string digest)
    {
        WorldNoveltyReportRON document = new()
        {
            SchemaVersion = 2, Species = "WorldNoveltyReport", TitleClause = WorldNoveltyRegistration.TitleClause,
            Boundaries = Config.Boundaries.Select(static boundary => new WorldNoveltyRONBoundary
            {
                BoundaryStep = boundary.BoundaryStep, WindowSteps = boundary.WindowSteps,
                BoundaryPrefix = boundary.BoundaryPrefix,
                MinimumPaidComparisons = boundary.MinimumPaidComparisons,
            }).ToList(),
            EqualityTolerance = Config.EqualityTolerance,
            MinimumDirectionalEffect = Config.MinimumDirectionalEffect,
            MinimumArmDivergences = Config.MinimumArmDivergences,
            ContrastDigest = Config.ContrastDigest,
            Live = EncodeArm(EpochLive), Control = EncodeArm(StationaryControl), OrderNull = EncodeArm(EpochOrderNull),
            VerdictSpecies = Verdict.Species.ToString(), VerdictAssay = Verdict.Assay.ToString(), VerdictPower = Verdict.Power.ToString(),
            VerdictStatus = Verdict.Status.ToString(), VerdictPattern = EncodeContrastPattern(Verdict.ArmEffectPattern), VerdictDetail = Verdict.Detail,
            VerdictEvidenceDigest = Verdict.EvidenceDigest,
            EconomicsOpportunities = AdmissionEconomics.Opportunities, EconomicsEligible = AdmissionEconomics.EligibleCandidates,
            EconomicsPriced = AdmissionEconomics.PricedCandidates, EconomicsAdmitted = AdmissionEconomics.AdmittedCandidates,
            EconomicsRefused = AdmissionEconomics.RefusedCandidates, EconomicsLiteral = AdmissionEconomics.LiteralCostMbits,
            EconomicsMaterialized = AdmissionEconomics.MaterializedCostMbits, EconomicsSavings = AdmissionEconomics.MarginalSavingsMbits,
            EconomicsArms = AdmissionEconomics.Arms.Select(static arm => new WorldNoveltyRONEconomicsArm
            {
                Arm = arm.Arm.ToString(), RunID = arm.RunID, Opportunities = arm.Opportunities,
                EligibleCandidates = arm.EligibleCandidates, PricedCandidates = arm.PricedCandidates,
                AdmittedCandidates = arm.AdmittedCandidates, RefusedCandidates = arm.RefusedCandidates,
                LiteralCostMbits = arm.LiteralCostMbits, MaterializedCostMbits = arm.MaterializedCostMbits,
                MarginalSavingsMbits = arm.MarginalSavingsMbits, IdentitySetDigest = arm.IdentitySetDigest,
                EvidenceDigest = arm.EvidenceDigest,
            }).ToList(),
            EconomicsDigest = AdmissionEconomics.EvidenceDigest, Digest = digest,
        };
        return RonSerializer.SerializeToUtf8(in document);
    }

    private static WorldNoveltyReportRONArm EncodeArm(WorldNoveltyArmResult arm)
        => new()
        {
            Kind = arm.Kind.ToString(), RunID = arm.RunID, Seed = arm.Seed, WorldSHA256 = arm.WorldSHA256, ScheduleSHA256 = arm.ScheduleSHA256,
            ConfigSHA256 = arm.ConfigSHA256, SchemaSHA256 = arm.SchemaSHA256, OfferedFuelVectorSHA256 = arm.OfferedFuelVectorSHA256,
            Horizon = arm.Horizon, BoundarySteps = arm.BoundarySteps.ToList(),
            PaidComparisonsByBoundary = arm.PaidComparisonsByBoundary.ToList(), CandidateDivergencesByBoundary = arm.CandidateDivergencesByBoundary.ToList(),
            OfferedFuel = EncodeFuel(arm.OfferedFuel), ActualFuel = EncodeFuel(arm.ActualFuel), RefundFuel = EncodeFuel(arm.RefundFuel),
            TransitionPrefixes = arm.TransitionPrefixes.ToList(), ReadRows = arm.ReadRows, CanonicalStateRows = arm.CanonicalStateRows,
            CandidatePresent = arm.CandidatePresent,
            BoundaryPrefixes = arm.BoundaryPrefixes.ToList(),
        };

    private static WorldNoveltyArmResult DecodeArm(WorldNoveltyReportRONArm arm)
        => new(ParseEnum<WorldNoveltyArmKinds>(arm.Kind), arm.RunID, arm.Seed, arm.WorldSHA256, arm.ScheduleSHA256,
            arm.ConfigSHA256, arm.SchemaSHA256, arm.OfferedFuelVectorSHA256, arm.Horizon,
            arm.BoundarySteps, arm.PaidComparisonsByBoundary, arm.CandidateDivergencesByBoundary,
            DecodeFuel(arm.OfferedFuel), DecodeFuel(arm.ActualFuel), DecodeFuel(arm.RefundFuel), arm.TransitionPrefixes,
            arm.ReadRows, arm.CanonicalStateRows, arm.CandidatePresent,
            arm.BoundaryPrefixes);

    private static WorldNoveltyRONFuel EncodeFuel(in EmlDeliberationCounts fuel)
        => new() { CandidateEvaluations = fuel.CandidateEvaluations, LogicalProgramPoints = fuel.LogicalProgramPoints,
            ExecutedProgramPoints = fuel.ExecutedProgramPoints, InverseTransforms = fuel.InverseTransforms, HashProbes = fuel.HashProbes,
            JoinAttempts = fuel.JoinAttempts, JoinHits = fuel.JoinHits, ProcessTerms = fuel.ProcessTerms,
            VerifierProgramPoints = fuel.VerifierProgramPoints, CandidateSupplyItems = fuel.CandidateSupplyItems,
            LawRewriteApplications = fuel.LawRewriteApplications, LawRewriteTreeNodes = fuel.LawRewriteTreeNodes };

    private static EmlDeliberationCounts DecodeFuel(WorldNoveltyRONFuel fuel)
        => new(fuel.CandidateEvaluations, fuel.LogicalProgramPoints, fuel.ExecutedProgramPoints, fuel.InverseTransforms,
            fuel.HashProbes, fuel.JoinAttempts, fuel.JoinHits, fuel.ProcessTerms, fuel.VerifierProgramPoints,
            fuel.CandidateSupplyItems, fuel.LawRewriteApplications, fuel.LawRewriteTreeNodes);

    private static T ParseEnum<T>(string value) where T : struct, Enum
        => Enum.TryParse<T>(value, out T parsed) && Enum.IsDefined(parsed) ? parsed : throw new InvalidDataException($"invalid R20 enum {value}");

    private static string EncodeContrastPattern(WorldNoveltyContrastPatterns pattern)
        => pattern switch
        {
            WorldNoveltyContrastPatterns.None => nameof(WorldNoveltyContrastPatterns.None),
            WorldNoveltyContrastPatterns.EpochOnly => nameof(WorldNoveltyContrastPatterns.EpochOnly),
            WorldNoveltyContrastPatterns.StationaryOnly => nameof(WorldNoveltyContrastPatterns.StationaryOnly),
            WorldNoveltyContrastPatterns.OrderNullOnly => nameof(WorldNoveltyContrastPatterns.OrderNullOnly),
            WorldNoveltyContrastPatterns.EpochAndStationary => nameof(WorldNoveltyContrastPatterns.EpochAndStationary),
            WorldNoveltyContrastPatterns.EpochAndOrderNull => nameof(WorldNoveltyContrastPatterns.EpochAndOrderNull),
            WorldNoveltyContrastPatterns.StationaryAndOrderNull => nameof(WorldNoveltyContrastPatterns.StationaryAndOrderNull),
            WorldNoveltyContrastPatterns.AllArms => nameof(WorldNoveltyContrastPatterns.AllArms),
            _ => throw new InvalidDataException("invalid R20 contrast pattern"),
        };

    private static string ComputeDigest(ReadOnlySpan<byte> bytes)
        => Convert.ToHexStringLower(SHA256.HashData(bytes));
}

/// Read-only causal adjudication for the R20 triad.  It never opens or mutates
/// run directories and it never calls the R19 report reader/certifier.
public static class WorldNoveltyAdjudicator
{
    public static WorldNoveltyReport Adjudicate(
        WorldNoveltyRegistration registration,
        WorldNoveltyArmResult epochLive,
        WorldNoveltyArmResult stationaryControl,
        WorldNoveltyArmResult epochOrderNull,
        IReadOnlyList<WorldNoveltyAdmissionEconomicsEvidence> admissionEconomics)
    {
        ArgumentNullException.ThrowIfNull(registration);
        registration.Validate();
        WorldNoveltyAdjudicationConfig config = WorldNoveltyAdjudicationConfig.FromRegistration(registration);
        WorldNoveltyAdmissionEconomicsSummary economics = WorldNoveltyAdmissionEconomicsSummary.From(admissionEconomics);
        try
        {
            ArgumentNullException.ThrowIfNull(epochLive);
            ArgumentNullException.ThrowIfNull(stationaryControl);
            ArgumentNullException.ThrowIfNull(epochOrderNull);
            ValidateRegistrationArms(registration, epochLive, stationaryControl, epochOrderNull);
            ValidateTriad(epochLive, stationaryControl, epochOrderNull, config);
            ValidateAdmissionEconomicsArms(economics, epochLive, stationaryControl, epochOrderNull);
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or OverflowException)
        {
            WorldNoveltyArmResult invalidLive = NormalizeInvalidArm(epochLive, WorldNoveltyArmKinds.EpochLive, registration, config);
            WorldNoveltyArmResult invalidControl = NormalizeInvalidArm(stationaryControl, WorldNoveltyArmKinds.StationaryControl, registration, config);
            WorldNoveltyArmResult invalidOrderNull = NormalizeInvalidArm(epochOrderNull, WorldNoveltyArmKinds.EpochOrderNull, registration, config);
            WorldNoveltyVerdict invalid = CreateVerdict(WorldNoveltyResultSpecies.MechanismInvalid, WorldNoveltyAssayStatuses.Invalid,
                WorldNoveltyPowerStatuses.Unpowered, error.Message, invalidLive, invalidControl, invalidOrderNull, economics,
                WorldNoveltyContrastPatterns.None);
            return WorldNoveltyReport.Create(config, invalidLive, invalidControl, invalidOrderNull, invalid, economics);
        }

        bool powered = HasOpportunityFloor(epochLive, config)
            && HasOpportunityFloor(stationaryControl, config)
            && HasOpportunityFloor(epochOrderNull, config);
        if (!powered)
        {
            WorldNoveltyVerdict banked = CreateVerdict(WorldNoveltyResultSpecies.BankedNull, WorldNoveltyAssayStatuses.Exact,
                WorldNoveltyPowerStatuses.Unpowered, "the preregistered post-boundary opportunity-liveness floor was not met",
                epochLive, stationaryControl, epochOrderNull, economics, ComputePattern(epochLive, stationaryControl, epochOrderNull, config));
            return WorldNoveltyReport.Create(config, epochLive, stationaryControl, epochOrderNull, banked, economics);
        }

        double live = epochLive.PostBoundaryEffect, stationary = stationaryControl.PostBoundaryEffect, orderNull = epochOrderNull.PostBoundaryEffect;
        WorldNoveltyContrastPatterns pattern = ComputePattern(epochLive, stationaryControl, epochOrderNull, config);
        bool structured = pattern == WorldNoveltyContrastPatterns.EpochOnly
            && live > stationary + config.MinimumDirectionalEffect
            && live > orderNull + config.MinimumDirectionalEffect
            && Approximately(stationary, orderNull, config.EqualityTolerance);
        bool reordered = pattern == WorldNoveltyContrastPatterns.EpochAndOrderNull
            && Approximately(live, orderNull, config.EqualityTolerance)
            && orderNull > stationary + config.MinimumDirectionalEffect;
        WorldNoveltyResultSpecies species = structured ? WorldNoveltyResultSpecies.StructuredNovelty
            : reordered ? WorldNoveltyResultSpecies.ReorderingEffect
            : pattern == WorldNoveltyContrastPatterns.None ? WorldNoveltyResultSpecies.NoEffect : WorldNoveltyResultSpecies.UnexpectedContrast;
        string detail = species switch
        {
            WorldNoveltyResultSpecies.StructuredNovelty => "epoch live exceeds both stationary control and epoch-order null after the epoch boundary; controls are equalized",
            WorldNoveltyResultSpecies.ReorderingEffect => "epoch live tracks the epoch-order null above stationary control; the effect is attributable to order",
            WorldNoveltyResultSpecies.UnexpectedContrast => "a powered arm contrast was observed outside the preregistered structured/reordering patterns; retain it as learner-side behavior",
            _ => "the preregistered post-boundary directional contrast was not reproduced",
        };
        WorldNoveltyVerdict verdict = CreateVerdict(species, WorldNoveltyAssayStatuses.Exact, WorldNoveltyPowerStatuses.Powered,
            detail, epochLive, stationaryControl, epochOrderNull, economics, pattern);
        return WorldNoveltyReport.Create(config, epochLive, stationaryControl, epochOrderNull, verdict, economics);
    }

    private static void ValidateTriad(WorldNoveltyArmResult live, WorldNoveltyArmResult control, WorldNoveltyArmResult orderNull,
        WorldNoveltyAdjudicationConfig config)
    {
        live.Validate(config); control.Validate(config); orderNull.Validate(config);
        if (live.Kind != WorldNoveltyArmKinds.EpochLive || control.Kind != WorldNoveltyArmKinds.StationaryControl
            || orderNull.Kind != WorldNoveltyArmKinds.EpochOrderNull)
            throw new InvalidDataException("R20 causal triad arm roles are incorrect");
        if (live.WorldSHA256 != control.WorldSHA256 || live.WorldSHA256 != orderNull.WorldSHA256)
            throw new InvalidDataException("R20 causal triad world SHA-256 differs across arms");
        if (live.ConfigSHA256 != control.ConfigSHA256 || live.ConfigSHA256 != orderNull.ConfigSHA256
            || live.SchemaSHA256 != control.SchemaSHA256 || live.SchemaSHA256 != orderNull.SchemaSHA256)
            throw new InvalidDataException("R20 causal triad policy config or schema differs across arms");
        if (live.Horizon != control.Horizon || live.Horizon != orderNull.Horizon)
            throw new InvalidDataException("R20 causal triad horizons are not equal");
        if (live.Seed != control.Seed || live.Seed != orderNull.Seed)
            throw new InvalidDataException("R20 causal triad seeds are not equal");
        if (live.OfferedFuelVectorSHA256 != control.OfferedFuelVectorSHA256
            || live.OfferedFuelVectorSHA256 != orderNull.OfferedFuelVectorSHA256)
            throw new InvalidDataException("R20 causal triad offered fuel vector digests are not equal");
        if (live.OfferedFuel != control.OfferedFuel || live.OfferedFuel != orderNull.OfferedFuel)
            throw new InvalidDataException("R20 causal triad offered fuel plans are not exactly equal");
        if (live.ScheduleSHA256 == control.ScheduleSHA256 || live.ScheduleSHA256 == orderNull.ScheduleSHA256
            || control.ScheduleSHA256 == orderNull.ScheduleSHA256)
            throw new InvalidDataException("R20 causal triad schedule authorities are not distinct");
    }

    private static void ValidateAdmissionEconomicsArms(
        WorldNoveltyAdmissionEconomicsSummary economics,
        WorldNoveltyArmResult live,
        WorldNoveltyArmResult control,
        WorldNoveltyArmResult orderNull)
    {
        WorldNoveltyArmAdmissionEconomicsSummary liveEconomics = FindEconomics(economics, WorldNoveltyArmKinds.EpochLive);
        WorldNoveltyArmAdmissionEconomicsSummary controlEconomics = FindEconomics(economics, WorldNoveltyArmKinds.StationaryControl);
        WorldNoveltyArmAdmissionEconomicsSummary orderNullEconomics = FindEconomics(economics, WorldNoveltyArmKinds.EpochOrderNull);
        if (!string.Equals(liveEconomics.RunID, live.RunID, StringComparison.Ordinal)
            || !string.Equals(controlEconomics.RunID, control.RunID, StringComparison.Ordinal)
            || !string.Equals(orderNullEconomics.RunID, orderNull.RunID, StringComparison.Ordinal))
            throw new InvalidDataException("R20 promotion economics run identity is not bound to its arm evidence");
    }

    private static WorldNoveltyArmAdmissionEconomicsSummary FindEconomics(
        WorldNoveltyAdmissionEconomicsSummary economics, WorldNoveltyArmKinds arm)
    {
        foreach (WorldNoveltyArmAdmissionEconomicsSummary candidate in economics.Arms)
            if (candidate.Arm == arm) return candidate;
        throw new InvalidDataException($"R20 promotion economics omitted {arm} arm evidence");
    }

    private static void ValidateRegistrationArms(WorldNoveltyRegistration registration,
        WorldNoveltyArmResult live, WorldNoveltyArmResult control, WorldNoveltyArmResult orderNull)
    {
        if (live.WorldSHA256 != registration.WorldSHA256 || control.WorldSHA256 != registration.WorldSHA256
            || orderNull.WorldSHA256 != registration.WorldSHA256)
            throw new InvalidDataException("R20 arm world differs from registration");
        if (live.ScheduleSHA256 != registration.EpochSchedule.ScheduleSHA256
            || control.ScheduleSHA256 != registration.StationarySchedule.ScheduleSHA256
            || orderNull.ScheduleSHA256 != registration.EpochOrderNullSchedule.ScheduleSHA256)
            throw new InvalidDataException("R20 arm schedule differs from registration");
        if (live.ConfigSHA256 != registration.EpochConfig.ConfigSHA256
            || control.ConfigSHA256 != registration.StationaryConfig.ConfigSHA256
            || orderNull.ConfigSHA256 != registration.OrderNullConfig.ConfigSHA256
            || live.SchemaSHA256 != registration.SchemaSHA256
            || control.SchemaSHA256 != registration.SchemaSHA256
            || orderNull.SchemaSHA256 != registration.SchemaSHA256)
            throw new InvalidDataException("R20 arm policy/schema authority differs from registration");
        if (live.Seed != registration.Seed || control.Seed != registration.Seed || orderNull.Seed != registration.Seed
            || live.Horizon != registration.Horizon || control.Horizon != registration.Horizon || orderNull.Horizon != registration.Horizon)
            throw new InvalidDataException("R20 arm seed or horizon differs from registration");
        if (live.OfferedFuelVectorSHA256 != registration.OfferedFuelVectorSHA256
            || control.OfferedFuelVectorSHA256 != registration.OfferedFuelVectorSHA256
            || orderNull.OfferedFuelVectorSHA256 != registration.OfferedFuelVectorSHA256)
            throw new InvalidDataException("R20 arm offered fuel vector differs from registration");
        IReadOnlyList<int> registeredBoundarySteps = registration.OpportunityFloor.BoundarySteps;
        IReadOnlyList<int> registeredBoundaryPrefixes = registration.OpportunityFloor.BoundaryPrefixes;
        if (!live.BoundarySteps.SequenceEqual(registeredBoundarySteps)
            || !control.BoundarySteps.SequenceEqual(registeredBoundarySteps)
            || !orderNull.BoundarySteps.SequenceEqual(registeredBoundarySteps)
            || !live.BoundaryPrefixes.SequenceEqual(registeredBoundaryPrefixes)
            || !control.BoundaryPrefixes.SequenceEqual(registeredBoundaryPrefixes)
            || !orderNull.BoundaryPrefixes.SequenceEqual(registeredBoundaryPrefixes))
            throw new InvalidDataException("R20 arm opportunity floor differs from registration");
    }

    private static WorldNoveltyVerdict CreateVerdict(WorldNoveltyResultSpecies species, WorldNoveltyAssayStatuses assay,
        WorldNoveltyPowerStatuses power, string detail, WorldNoveltyArmResult live, WorldNoveltyArmResult control,
        WorldNoveltyArmResult orderNull, WorldNoveltyAdmissionEconomicsSummary economics, WorldNoveltyContrastPatterns pattern)
    {
        string material = string.Join('|', species, assay, power, detail, live.WorldSHA256, live.ScheduleSHA256, live.ConfigSHA256, live.SchemaSHA256,
            control.ScheduleSHA256, orderNull.ScheduleSHA256, live.PostBoundaryEffect.ToString("G17", CultureInfo.InvariantCulture),
            control.PostBoundaryEffect.ToString("G17", CultureInfo.InvariantCulture), orderNull.PostBoundaryEffect.ToString("G17", CultureInfo.InvariantCulture),
            economics.EvidenceDigest);
        string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material + "|" + pattern)));
        WorldNoveltyVerdictStatuses status = species switch
        {
            WorldNoveltyResultSpecies.StructuredNovelty or WorldNoveltyResultSpecies.ReorderingEffect => WorldNoveltyVerdictStatuses.PASS,
            WorldNoveltyResultSpecies.NoEffect or WorldNoveltyResultSpecies.UnexpectedContrast => WorldNoveltyVerdictStatuses.FAIL,
            WorldNoveltyResultSpecies.BankedNull => WorldNoveltyVerdictStatuses.BANKED_NULL,
            _ => WorldNoveltyVerdictStatuses.INVALID,
        };
        return new(species, assay, power, status, pattern, detail, digest);
    }

    private static WorldNoveltyContrastPatterns ComputePattern(
        WorldNoveltyArmResult live,
        WorldNoveltyArmResult control,
        WorldNoveltyArmResult orderNull,
        WorldNoveltyAdjudicationConfig config)
    {
        bool epoch = live.CandidateDivergences >= config.MinimumArmDivergences;
        bool stationary = control.CandidateDivergences >= config.MinimumArmDivergences;
        bool orderNullArm = orderNull.CandidateDivergences >= config.MinimumArmDivergences;
        return (epoch, stationary, orderNullArm) switch
        {
            (false, false, false) => WorldNoveltyContrastPatterns.None,
            (true, false, false) => WorldNoveltyContrastPatterns.EpochOnly,
            (false, true, false) => WorldNoveltyContrastPatterns.StationaryOnly,
            (false, false, true) => WorldNoveltyContrastPatterns.OrderNullOnly,
            (true, true, false) => WorldNoveltyContrastPatterns.EpochAndStationary,
            (true, false, true) => WorldNoveltyContrastPatterns.EpochAndOrderNull,
            (false, true, true) => WorldNoveltyContrastPatterns.StationaryAndOrderNull,
            _ => WorldNoveltyContrastPatterns.AllArms,
        };
    }

    private static WorldNoveltyArmResult NormalizeInvalidArm(WorldNoveltyArmResult? source, WorldNoveltyArmKinds kind,
        WorldNoveltyRegistration registration, WorldNoveltyAdjudicationConfig config)
    {
        string runID = source?.RunID is { Length: > 0 } ? source.RunID : $"invalid-{kind}";
        string schedule = kind switch
        {
            WorldNoveltyArmKinds.EpochLive => registration.EpochSchedule.ScheduleSHA256,
            WorldNoveltyArmKinds.StationaryControl => registration.StationarySchedule.ScheduleSHA256,
            _ => registration.EpochOrderNullSchedule.ScheduleSHA256,
        };
        string configDigest = kind switch
        {
            WorldNoveltyArmKinds.EpochLive => registration.EpochConfig.ConfigSHA256,
            WorldNoveltyArmKinds.StationaryControl => registration.StationaryConfig.ConfigSHA256,
            _ => registration.OrderNullConfig.ConfigSHA256,
        };
        return new(kind, runID, registration.Seed, registration.WorldSHA256, schedule, configDigest, registration.SchemaSHA256,
            registration.OfferedFuelVectorSHA256, registration.Horizon, config.BoundarySteps,
            new int[config.Boundaries.Count], new int[config.Boundaries.Count], default, default, default,
            config.Boundaries.Select(static boundary => boundary.BoundaryPrefix).ToArray(), 0, 0, 0,
            config.Boundaries.Select(static boundary => boundary.BoundaryPrefix).ToArray());
    }

    private static bool Approximately(double left, double right, double tolerance)
        => Math.Abs(left - right) <= tolerance * Math.Max(1d, Math.Max(Math.Abs(left), Math.Abs(right)));

    private static bool HasOpportunityFloor(WorldNoveltyArmResult arm, WorldNoveltyAdjudicationConfig config)
    {
        if (arm.PaidComparisonsByBoundary.Count != config.Boundaries.Count) return false;
        for (int i = 0; i < config.Boundaries.Count; i++)
            if (arm.PaidComparisonsByBoundary[i] < config.Boundaries[i].MinimumPaidComparisons) return false;
        return true;
    }
}

[RonObject]
internal partial class WorldNoveltyReportRON
{
    public int SchemaVersion;
    public string Species = "";
    public string TitleClause = "";
    public List<WorldNoveltyRONBoundary> Boundaries = new();
    public double EqualityTolerance;
    public double MinimumDirectionalEffect;
    public int MinimumArmDivergences;
    public string ContrastDigest = "";
    public WorldNoveltyReportRONArm Live = new();
    public WorldNoveltyReportRONArm Control = new();
    public WorldNoveltyReportRONArm OrderNull = new();
    public string VerdictSpecies = "";
    public string VerdictAssay = "";
    public string VerdictPower = "";
    public string VerdictStatus = "";
    public string VerdictPattern = "";
    public string VerdictDetail = "";
    public string VerdictEvidenceDigest = "";
    public int EconomicsOpportunities;
    public int EconomicsEligible;
    public int EconomicsPriced;
    public int EconomicsAdmitted;
    public int EconomicsRefused;
    public long EconomicsLiteral;
    public long EconomicsMaterialized;
    public long EconomicsSavings;
    public string EconomicsDigest = "";
    public List<WorldNoveltyRONEconomicsArm> EconomicsArms = new();
    public string Digest = "";
}

[RonObject]
internal partial class WorldNoveltyRONEconomicsArm
{
    public string Arm = "";
    public string RunID = "";
    public int Opportunities;
    public int EligibleCandidates;
    public int PricedCandidates;
    public int AdmittedCandidates;
    public int RefusedCandidates;
    public long LiteralCostMbits;
    public long MaterializedCostMbits;
    public long MarginalSavingsMbits;
    public string IdentitySetDigest = "";
    public string EvidenceDigest = "";
}

[RonObject]
internal partial class WorldNoveltyReportRONArm
{
    public string Kind = "";
    public string RunID = "";
    public ulong Seed;
    public string WorldSHA256 = "";
    public string ScheduleSHA256 = "";
    public string ConfigSHA256 = "";
    public string SchemaSHA256 = "";
    public string OfferedFuelVectorSHA256 = "";
    public int Horizon;
    public List<int> BoundarySteps = new();
    public List<int> PaidComparisonsByBoundary = new();
    public List<int> CandidateDivergencesByBoundary = new();
    public WorldNoveltyRONFuel OfferedFuel = new();
    public WorldNoveltyRONFuel ActualFuel = new();
    public WorldNoveltyRONFuel RefundFuel = new();
    public List<int> TransitionPrefixes = new();
    public int ReadRows;
    public int CanonicalStateRows;
    public int CandidatePresent;
    public List<int> BoundaryPrefixes = new();
}

[RonObject]
internal partial class WorldNoveltyRONBoundary
{
    public int BoundaryStep;
    public int BoundaryPrefix;
    public int WindowSteps;
    public int MinimumPaidComparisons;
}

[RonObject]
internal partial class WorldNoveltyRONFuel
{
    public long CandidateEvaluations;
    public long LogicalProgramPoints;
    public long ExecutedProgramPoints;
    public long InverseTransforms;
    public long HashProbes;
    public long JoinAttempts;
    public long JoinHits;
    public long ProcessTerms;
    public long VerifierProgramPoints;
    public long CandidateSupplyItems;
    public long LawRewriteApplications;
    public long LawRewriteTreeNodes;
}

/// Focused report-surface fixture: arm/run custody is distinct from the
/// opportunity identity itself, and malformed encoded summaries are refused.
internal static class WorldNoveltyAdmissionEconomicsFixture
{
    internal static bool Verify(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        const string identity = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string evidence = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        WorldNoveltyArmAdmissionEconomicsSummary live = CreateArm(WorldNoveltyArmKinds.EpochLive, "run-live", identity, evidence);
        WorldNoveltyArmAdmissionEconomicsSummary control = CreateArm(WorldNoveltyArmKinds.StationaryControl, "run-control", identity, evidence);
        WorldNoveltyArmAdmissionEconomicsSummary orderNull = CreateArm(WorldNoveltyArmKinds.EpochOrderNull, "run-null", identity, evidence);
        WorldNoveltyArmAdmissionEconomicsSummary[] arms = [live, control, orderNull];
        string digest = Digest(string.Join('|', arms.OrderBy(static arm => arm.Arm).Select(static arm =>
            string.Join(':', arm.Arm, arm.RunID, arm.IdentitySetDigest, arm.EvidenceDigest))));
        WorldNoveltyAdmissionEconomicsSummary exact = WorldNoveltyAdmissionEconomicsSummary.FromEncoded(
            arms, 3, 3, 3, 3, 0, 3, 0, 3, digest);
        bool exactAccepted = exact.Arms.Count == 3 && exact.Opportunities == 3;
        bool duplicateArmRejected = Rejects(() => WorldNoveltyAdmissionEconomicsSummary.FromEncoded(
            [live, control, live], 3, 3, 3, 3, 0, 3, 0, 3, digest));
        bool countMutationRejected = Rejects(() => WorldNoveltyAdmissionEconomicsSummary.FromEncoded(
            arms, 4, 3, 3, 3, 0, 3, 0, 3, digest));
        output.WriteLine($"  R20 economics report custody · arm/run={(exactAccepted ? "bound" : "BROKEN")} · duplicate-arm={(duplicateArmRejected ? "rejected" : "ACCEPTED")} · count-mutation={(countMutationRejected ? "rejected" : "ACCEPTED")} · {(exactAccepted && duplicateArmRejected && countMutationRejected ? "PASS" : "FAIL")}");
        return exactAccepted && duplicateArmRejected && countMutationRejected;
    }

    private static WorldNoveltyArmAdmissionEconomicsSummary CreateArm(
        WorldNoveltyArmKinds arm, string runID, string identitySetDigest, string evidenceDigest)
        => new(arm, runID, 1, 1, 1, 1, 0, 3, 0, 3, identitySetDigest, evidenceDigest);

    private static bool Rejects(Action action)
    {
        try { action(); return false; }
        catch (InvalidDataException) { return true; }
    }

    private static string Digest(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
