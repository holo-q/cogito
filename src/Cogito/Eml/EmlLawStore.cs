namespace Cogito;

using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using Cogito.Grammar;
using Cogito.Induct;
using Ronmamon;

internal readonly record struct EmlLawBehaviorCertificate(EmlSig AtOne, EmlSig AtX, EmlSig AtY);

/// A durable boundary for evaluation-local EML accounting.  The cursor is
/// captured after the unscored handshake step has settled, so receipt totals
/// can be re-summed from the persisted settlement journal without admitting
/// step zero into the scored window.
internal readonly record struct EmlDeepRematchFuelCursor(
    int SettlementCount,
    long EvaluatorCalls,
    EmlDeliberationCounts Planned,
    EmlDeliberationCounts Actual,
    EmlDeliberationCounts Refund,
    string Digest,
    string PointID = "",
    string PointDigest = "",
    string SettlementDigest = "")
{
    internal long EvaluatorHighWater => EvaluatorCalls;

    internal EmlDeepRematchFuelCursor Validate()
    {
        if (SettlementCount < 0 || EvaluatorCalls < 0 || string.IsNullOrWhiteSpace(Digest)
            || Digest.Length != 64 || string.IsNullOrWhiteSpace(PointID) || PointDigest.Length != 64
            || SettlementDigest.Length != 64)
            throw new InvalidDataException("deep-rematch EML fuel cursor is malformed");
        Planned.ValidateNonnegative("deep-rematch EML cursor planned fuel");
        Actual.ValidateNonnegative("deep-rematch EML cursor actual fuel");
        Refund.ValidateNonnegative("deep-rematch EML cursor refund fuel");
        EmlDeliberationCounts planned = Planned;
        EmlDeliberationCounts actual = Actual;
        EmlDeliberationCounts refund = Refund;
        if (refund != EmlDeliberationCounts.Subtract(in planned, in actual)
            || !string.Equals(Digest, ComputeDigest(SettlementCount, EvaluatorCalls, in planned, in actual, in refund, PointID, PointDigest, SettlementDigest), StringComparison.Ordinal))
            throw new InvalidDataException("deep-rematch EML fuel cursor does not close");
        return this;
    }

    internal static string ComputeDigest(int settlementCount, long evaluatorCalls,
        in EmlDeliberationCounts planned, in EmlDeliberationCounts actual, in EmlDeliberationCounts refund,
        string pointID = "", string pointDigest = "", string settlementDigest = "")
    {
        string material = string.Join('|', settlementCount, evaluatorCalls, planned, actual, refund, pointID, pointDigest, settlementDigest);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}

[RonObject]
internal partial class EmlDeepRematchFuelCursorDocument
{
    public int schemaVersion = 1;
    public int settlementCount;
    public long evaluatorCalls;
    public long plannedCandidateEvaluations;
    public long plannedLogicalProgramPoints;
    public long plannedExecutedProgramPoints;
    public long plannedInverseTransforms;
    public long plannedHashProbes;
    public long plannedJoinAttempts;
    public long plannedJoinHits;
    public long plannedProcessTerms;
    public long plannedVerifierProgramPoints;
    public long plannedCandidateSupplyItems;
    public long plannedLawRewriteApplications;
    public long plannedLawRewriteTreeNodes;
    public long actualCandidateEvaluations;
    public long actualLogicalProgramPoints;
    public long actualExecutedProgramPoints;
    public long actualInverseTransforms;
    public long actualHashProbes;
    public long actualJoinAttempts;
    public long actualJoinHits;
    public long actualProcessTerms;
    public long actualVerifierProgramPoints;
    public long actualCandidateSupplyItems;
    public long actualLawRewriteApplications;
    public long actualLawRewriteTreeNodes;
    public long refundCandidateEvaluations;
    public long refundLogicalProgramPoints;
    public long refundExecutedProgramPoints;
    public long refundInverseTransforms;
    public long refundHashProbes;
    public long refundJoinAttempts;
    public long refundJoinHits;
    public long refundProcessTerms;
    public long refundVerifierProgramPoints;
    public long refundCandidateSupplyItems;
    public long refundLawRewriteApplications;
    public long refundLawRewriteTreeNodes;
    public string digest = "";
    public string pointID = "";
    public string pointDigest = "";
    public string settlementDigest = "";

    internal static EmlDeepRematchFuelCursorDocument FromCursor(in EmlDeepRematchFuelCursor cursor)
    {
        cursor.Validate();
        return new()
        {
            settlementCount = cursor.SettlementCount,
            evaluatorCalls = cursor.EvaluatorCalls,
            plannedCandidateEvaluations = cursor.Planned.CandidateEvaluations,
            plannedLogicalProgramPoints = cursor.Planned.LogicalProgramPoints,
            plannedExecutedProgramPoints = cursor.Planned.ExecutedProgramPoints,
            plannedInverseTransforms = cursor.Planned.InverseTransforms,
            plannedHashProbes = cursor.Planned.HashProbes,
            plannedJoinAttempts = cursor.Planned.JoinAttempts,
            plannedJoinHits = cursor.Planned.JoinHits,
            plannedProcessTerms = cursor.Planned.ProcessTerms,
            plannedVerifierProgramPoints = cursor.Planned.VerifierProgramPoints,
            plannedCandidateSupplyItems = cursor.Planned.CandidateSupplyItems,
            plannedLawRewriteApplications = cursor.Planned.LawRewriteApplications,
            plannedLawRewriteTreeNodes = cursor.Planned.LawRewriteTreeNodes,
            actualCandidateEvaluations = cursor.Actual.CandidateEvaluations,
            actualLogicalProgramPoints = cursor.Actual.LogicalProgramPoints,
            actualExecutedProgramPoints = cursor.Actual.ExecutedProgramPoints,
            actualInverseTransforms = cursor.Actual.InverseTransforms,
            actualHashProbes = cursor.Actual.HashProbes,
            actualJoinAttempts = cursor.Actual.JoinAttempts,
            actualJoinHits = cursor.Actual.JoinHits,
            actualProcessTerms = cursor.Actual.ProcessTerms,
            actualVerifierProgramPoints = cursor.Actual.VerifierProgramPoints,
            actualCandidateSupplyItems = cursor.Actual.CandidateSupplyItems,
            actualLawRewriteApplications = cursor.Actual.LawRewriteApplications,
            actualLawRewriteTreeNodes = cursor.Actual.LawRewriteTreeNodes,
            refundCandidateEvaluations = cursor.Refund.CandidateEvaluations,
            refundLogicalProgramPoints = cursor.Refund.LogicalProgramPoints,
            refundExecutedProgramPoints = cursor.Refund.ExecutedProgramPoints,
            refundInverseTransforms = cursor.Refund.InverseTransforms,
            refundHashProbes = cursor.Refund.HashProbes,
            refundJoinAttempts = cursor.Refund.JoinAttempts,
            refundJoinHits = cursor.Refund.JoinHits,
            refundProcessTerms = cursor.Refund.ProcessTerms,
            refundVerifierProgramPoints = cursor.Refund.VerifierProgramPoints,
            refundCandidateSupplyItems = cursor.Refund.CandidateSupplyItems,
            refundLawRewriteApplications = cursor.Refund.LawRewriteApplications,
            refundLawRewriteTreeNodes = cursor.Refund.LawRewriteTreeNodes,
            digest = cursor.Digest,
            pointID = cursor.PointID,
            pointDigest = cursor.PointDigest,
            settlementDigest = cursor.SettlementDigest,
        };
    }

    internal EmlDeepRematchFuelCursor ToCursor()
    {
        if (schemaVersion != 1) throw new InvalidDataException("unsupported deep-rematch EML cursor sidecar schema");
        EmlDeliberationCounts planned = new(plannedCandidateEvaluations, plannedLogicalProgramPoints, plannedExecutedProgramPoints, plannedInverseTransforms, plannedHashProbes, plannedJoinAttempts, plannedJoinHits, plannedProcessTerms, plannedVerifierProgramPoints, plannedCandidateSupplyItems, plannedLawRewriteApplications, plannedLawRewriteTreeNodes);
        EmlDeliberationCounts actual = new(actualCandidateEvaluations, actualLogicalProgramPoints, actualExecutedProgramPoints, actualInverseTransforms, actualHashProbes, actualJoinAttempts, actualJoinHits, actualProcessTerms, actualVerifierProgramPoints, actualCandidateSupplyItems, actualLawRewriteApplications, actualLawRewriteTreeNodes);
        EmlDeliberationCounts refund = new(refundCandidateEvaluations, refundLogicalProgramPoints, refundExecutedProgramPoints, refundInverseTransforms, refundHashProbes, refundJoinAttempts, refundJoinHits, refundProcessTerms, refundVerifierProgramPoints, refundCandidateSupplyItems, refundLawRewriteApplications, refundLawRewriteTreeNodes);
        return new EmlDeepRematchFuelCursor(settlementCount, evaluatorCalls, planned, actual, refund, digest, pointID, pointDigest, settlementDigest).Validate();
    }
}

internal readonly record struct EmlLawExactEvidence(
    char Grade,
    bool Q12Home,
    bool Q12Regime,
    string EnclosureColumns)
{
    public bool IsExact => Grade == 'E';

    internal static EmlLawExactEvidence FromVerdict(in EmlVerdict verdict)
        => new(verdict.Grade, verdict.Q12Home, verdict.Q12P3, verdict.EnclCols);
}

internal enum EmlSourcePredictionAdmissionSpecies : byte
{
    MintPacket = 1,
    LawExecutionPacket = 2,
    Rung0CompositionPacket = 3,
}

internal readonly record struct EmlSourcePredictionAdmission(
    EmlSourcePredictionAdmissionSpecies Species,
    TapeEventID EventID)
{
    internal bool IsValid =>
        EventID.Value >= 0
        && Species is (EmlSourcePredictionAdmissionSpecies.MintPacket
            or EmlSourcePredictionAdmissionSpecies.LawExecutionPacket
            or EmlSourcePredictionAdmissionSpecies.Rung0CompositionPacket);
}

/// A verified law candidate can be a member of an existing semantic class without
/// becoming that class's representative.  This receipt keeps the world support of
/// that member alive: SemanticCAS membership is not itself an admission path for
/// frontier generation.  The receipt is append-only and is consumed exactly once.
internal sealed class EmlVerifiedLawSupportReceipt
{
    internal readonly record struct SupportPrediction(int SourcePredictionID, string Certificate, string LeftRpn, string RightRpn);
    internal const int MaxWorldOpportunityEvents = 1024;

    internal EmlVerifiedLawSupportReceipt(
        string candidateAdmissionID,
        EmlVerifiedLaw candidate,
        string supportSetDigest,
        IReadOnlyList<SupportPrediction> candidateSupport,
        EmlLawBehaviorCertificate certificate,
        string canonicalAuthorityID,
        IReadOnlyList<int> sourcePredictionIDs,
        IReadOnlyList<string> sourcePredictionDigests,
        IReadOnlyList<string> sourcePredictionMintLineDigests,
        IReadOnlyList<IReadOnlyList<TapeEventID>> sourcePredictionOpportunityEvents,
        IReadOnlyList<EmlSourcePredictionAdmission?> sourcePredictionAdmissions,
        IReadOnlyList<TapeEventID> worldOpportunityEventIDs,
        int captureStep,
        int captureIndex,
        bool firstCapture,
        bool representativeChanged,
        string digest,
        bool consumed,
        TapeEventID? executionEventID = null,
        TapeEventID? supportEventID = null,
        IReadOnlyList<int>? generatedPredictionIDs = null)
    {
        CandidateAdmissionID = candidateAdmissionID;
        Candidate = candidate;
        SupportSetDigest = supportSetDigest;
        CandidateSupport = candidateSupport.ToArray();
        Certificate = certificate;
        CanonicalAuthorityID = canonicalAuthorityID;
        SourcePredictionIDs = sourcePredictionIDs.ToArray();
        SourcePredictionDigests = sourcePredictionDigests.ToArray();
        SourcePredictionMintLineDigests = sourcePredictionMintLineDigests.ToArray();
        SourcePredictionOpportunityEvents = sourcePredictionOpportunityEvents.Select(static events => events.ToArray()).ToArray();
        SourcePredictionAdmissions = sourcePredictionAdmissions.ToArray();
        WorldOpportunityEventIDs = worldOpportunityEventIDs.ToArray();
        CaptureStep = captureStep;
        CaptureIndex = captureIndex;
        FirstCapture = firstCapture;
        RepresentativeChanged = representativeChanged;
        Digest = digest;
        Consumed = consumed;
        ExecutionEventID = executionEventID;
        SupportEventID = supportEventID;
        GeneratedPredictionIDs = generatedPredictionIDs?.ToArray() ?? Array.Empty<int>();
        Validate();
    }

    internal EmlVerifiedLawSupportReceipt(
        string candidateAdmissionID,
        EmlVerifiedLaw candidate,
        string supportSetDigest,
        IReadOnlyList<SupportPrediction> candidateSupport,
        EmlLawBehaviorCertificate certificate,
        string canonicalAuthorityID,
        IReadOnlyList<int> sourcePredictionIDs,
        IReadOnlyList<string> sourcePredictionDigests,
        IReadOnlyList<string> sourcePredictionMintLineDigests,
        IReadOnlyList<IReadOnlyList<TapeEventID>> sourcePredictionOpportunityEvents,
        IReadOnlyList<TapeEventID?> sourcePredictionMintEvents,
        IReadOnlyList<TapeEventID> worldOpportunityEventIDs,
        int captureStep,
        int captureIndex,
        bool firstCapture,
        bool representativeChanged,
        string digest,
        bool consumed,
        TapeEventID? executionEventID = null,
        TapeEventID? supportEventID = null,
        IReadOnlyList<int>? generatedPredictionIDs = null)
        : this(candidateAdmissionID, candidate, supportSetDigest, candidateSupport, certificate, canonicalAuthorityID,
            sourcePredictionIDs, sourcePredictionDigests, sourcePredictionMintLineDigests, sourcePredictionOpportunityEvents,
            sourcePredictionMintEvents.Select(static eventID => eventID is TapeEventID id
                ? new EmlSourcePredictionAdmission(EmlSourcePredictionAdmissionSpecies.MintPacket, id)
                : (EmlSourcePredictionAdmission?)null).ToArray(), worldOpportunityEventIDs, captureStep, captureIndex,
            firstCapture, representativeChanged, digest, consumed, executionEventID, supportEventID, generatedPredictionIDs)
    {
    }

    internal string CandidateAdmissionID { get; }
    internal EmlVerifiedLaw Candidate { get; }
    internal string SupportSetDigest { get; }
    internal IReadOnlyList<SupportPrediction> CandidateSupport { get; }
    // CandidateSupport is fixed at construction, so its package digest (a StringBuilder+SHA256
    // over the whole support set) is a pure function of immutable data — computed once, not per GET.
    private string? _candidatePackageDigest;
    internal string CandidatePackageDigest => _candidatePackageDigest ??= ComputeCandidatePackageDigest(CandidateSupport);
    internal EmlLawBehaviorCertificate Certificate { get; }
    internal string CanonicalAuthorityID { get; }
    internal IReadOnlyList<int> SourcePredictionIDs { get; }
    internal IReadOnlyList<string> SourcePredictionDigests { get; }
    internal IReadOnlyList<string> SourcePredictionMintLineDigests { get; }
    internal IReadOnlyList<IReadOnlyList<TapeEventID>> SourcePredictionOpportunityEvents { get; }
    internal IReadOnlyList<EmlSourcePredictionAdmission?> SourcePredictionAdmissions { get; }
    // Fixture compatibility: old assay gates inspect only event identity. The
    // receipt's authoritative field is SourcePredictionAdmissions, which preserves
    // the admission species alongside that identity.
    private IReadOnlyList<TapeEventID?>? _sourcePredictionMintEvents;
    internal IReadOnlyList<TapeEventID?> SourcePredictionMintEvents
        => _sourcePredictionMintEvents ??= SourcePredictionAdmissions.Select(static admission => admission?.EventID).ToArray();
    internal IReadOnlyList<TapeEventID> WorldOpportunityEventIDs { get; }
    internal int CaptureStep { get; }
    internal int CaptureIndex { get; }
    internal bool FirstCapture { get; }
    internal bool RepresentativeChanged { get; }
    internal string Digest { get; }
    internal bool Consumed { get; private set; }
    internal TapeEventID? ExecutionEventID { get; private set; }
    internal TapeEventID? SupportEventID { get; private set; }
    internal IReadOnlyList<int> GeneratedPredictionIDs { get; private set; }

    internal bool HasWorldOpportunity => WorldOpportunityEventIDs.Count > 0;

    internal static string ComputeCandidatePackageDigest(IReadOnlyList<SupportPrediction> candidateSupport)
    {
        StringBuilder material = new();
        for (int i = 0; i < candidateSupport.Count; i++)
            material.Append(candidateSupport[i].SourcePredictionID).Append(':')
                .Append(candidateSupport[i].Certificate).Append(':')
                .Append(candidateSupport[i].LeftRpn).Append(':')
                .Append(candidateSupport[i].RightRpn).Append('|');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString())));
    }

    internal void MarkConsumed()
    {
        if (Consumed) throw new InvalidOperationException("verified-law support receipt was consumed twice");
        if (ExecutionEventID is not TapeEventID || GeneratedPredictionIDs.Count == 0)
            throw new InvalidOperationException("verified-law support cannot be consumed before execution custody is bound");
        Consumed = true;
    }

    internal void BindExecution(TapeEventID executionEventID, IReadOnlyList<int> generatedPredictionIDs)
    {
        if (executionEventID.Value < 0 || generatedPredictionIDs.Count == 0)
            throw new ArgumentOutOfRangeException(nameof(executionEventID));
        for (int i = 0; i < generatedPredictionIDs.Count; i++)
            if (generatedPredictionIDs[i] < 0 || (i > 0 && generatedPredictionIDs[i] <= generatedPredictionIDs[i - 1]))
                throw new InvalidDataException("verified-law generated claim IDs are not sorted and unique");
        if (ExecutionEventID is TapeEventID existing && existing != executionEventID)
            throw new InvalidOperationException("verified-law support execution is already bound");
        if (GeneratedPredictionIDs.Count > 0 && !GeneratedPredictionIDs.SequenceEqual(generatedPredictionIDs))
            throw new InvalidOperationException("verified-law generated claim custody is already bound");
        ExecutionEventID = executionEventID;
        GeneratedPredictionIDs = generatedPredictionIDs.ToArray();
    }

    internal void BindSupportPacket(TapeEventID supportEventID)
    {
        if (SupportEventID is TapeEventID existing && existing != supportEventID)
            throw new InvalidOperationException("verified-law support packet is already bound");
        SupportEventID = supportEventID;
    }

    internal void RestoreCheckpointState(
        bool consumed,
        TapeEventID? executionEventID,
        TapeEventID? supportEventID,
        IReadOnlyList<int> generatedPredictionIDs)
    {
        if (supportEventID is TapeEventID packet) BindSupportPacket(packet);
        if (executionEventID is TapeEventID execution) BindExecution(execution, generatedPredictionIDs);
        else if (generatedPredictionIDs.Count != 0)
            throw new InvalidDataException("verified-law support state has generated claims without execution custody");
        if (consumed && !Consumed) MarkConsumed();
        if (!consumed && Consumed)
            throw new InvalidDataException("verified-law support state attempts to unconsume a receipt");
        ValidateAfterLoad();
    }

    internal static EmlVerifiedLawSupportReceipt Create(
        EmlVerifiedLaw law,
        SemanticCASAdmission<EmlLawBehaviorCertificate, EmlVerifiedLaw> admission,
        IReadOnlyList<EmlLawPrediction> support,
        IReadOnlyDictionary<int, IReadOnlyList<TapeEventID>> sourcePredictionOpportunityEvents,
        IReadOnlyDictionary<int, EmlSourcePredictionAdmission> sourcePredictionAdmissions,
        IReadOnlyDictionary<int, string> sourcePredictionMintDigests,
        IReadOnlyDictionary<int, string> sourcePredictionMintLineDigests,
        IReadOnlyList<TapeEventID> worldOpportunityEventIDs,
        int captureStep,
        int captureIndex)
    {
        if (worldOpportunityEventIDs.Count > EmlVerifiedLawSupportReceipt.MaxWorldOpportunityEvents)
            throw new InvalidDataException($"verified-law support names {worldOpportunityEventIDs.Count} world opportunities; maximum is {EmlVerifiedLawSupportReceipt.MaxWorldOpportunityEvents}");
        for (int i = 0; i < worldOpportunityEventIDs.Count; i++)
            if (worldOpportunityEventIDs[i].Value < 0)
                throw new InvalidDataException("verified-law support names a negative world opportunity event");
        List<(int ID, string Digest, string MintLineDigest, IReadOnlyList<TapeEventID> Events, EmlSourcePredictionAdmission? Admission, SupportPrediction Prediction)> claims = new();
        for (int i = 0; i < support.Count; i++)
        {
            EmlLawPrediction claim = support[i];
            if (claim.SourcePredictionID is not EmlPredictionID source)
            {
                if (worldOpportunityEventIDs.Count > 0)
                    throw new InvalidDataException("powered verified-law support omits a source claim identity");
                continue;
            }
            if (source.Value < 0)
                throw new InvalidDataException("verified-law support names a negative source claim identity");
            string material = string.Join('|', source.Value, claim.Cert.Hex(), claim.LeftRpn, claim.RightRpn,
                sourcePredictionMintDigests.TryGetValue(source.Value, out string? mintDigest) ? mintDigest : "mint=none");
            IReadOnlyList<TapeEventID> events = sourcePredictionOpportunityEvents.TryGetValue(source.Value, out IReadOnlyList<TapeEventID>? found)
                ? found.Count <= MaxWorldOpportunityEvents
                    ? found.Distinct().OrderBy(static id => id.Value).ToArray()
                    : throw new InvalidDataException("verified-law support claim exceeds its raw world opportunity enclosure")
                : Array.Empty<TapeEventID>();
            if (events.Count > MaxWorldOpportunityEvents || events.Any(static id => id.Value < 0))
                throw new InvalidDataException("verified-law support claim exceeds its world opportunity enclosure");
            EmlSourcePredictionAdmission? admissionPath = sourcePredictionAdmissions.TryGetValue(source.Value, out EmlSourcePredictionAdmission bound)
                ? bound : null;
            if (admissionPath is EmlSourcePredictionAdmission boundAdmissionPath && !boundAdmissionPath.IsValid)
                throw new InvalidDataException("verified-law support names an invalid source admission path");
            if (!sourcePredictionMintLineDigests.TryGetValue(source.Value, out string? mintLineDigest)
                || !IsCanonicalDigest(mintLineDigest))
                throw new InvalidDataException("verified-law support omits the canonical source mint line digest");
            if (worldOpportunityEventIDs.Count > 0
                && (admissionPath is null || !sourcePredictionMintDigests.TryGetValue(source.Value, out string? persistedMintDigest)
                    || persistedMintDigest.Length != 64 || !persistedMintDigest.All(Uri.IsHexDigit)))
                throw new InvalidDataException("powered verified-law support omits persisted source mint custody");
            claims.Add((source.Value, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material))), mintLineDigest, events, admissionPath,
                new SupportPrediction(source.Value, claim.Cert.Hex(), claim.LeftRpn, claim.RightRpn)));
        }
        claims.Sort(static (left, right) => left.ID.CompareTo(right.ID));
        int[] sourcePredictionIDs = claims.Select(static pair => pair.ID).ToArray();
        string[] sourcePredictionDigests = claims.Select(static pair => pair.Digest).ToArray();
        string[] claimMintLineDigestArray = claims.Select(static pair => pair.MintLineDigest).ToArray();
        IReadOnlyList<TapeEventID>[] sourcePredictionEvents = claims.Select(static pair => pair.Events).ToArray();
        EmlSourcePredictionAdmission?[] claimAdmissionArray = claims.Select(static pair => pair.Admission).ToArray();
        SupportPrediction[] candidateSupport = claims.Select(static pair => pair.Prediction).ToArray();
        TapeEventID[] opportunities = worldOpportunityEventIDs
            .Distinct().OrderBy(static id => id.Value).ToArray();
        if (opportunities.Length > MaxWorldOpportunityEvents)
            throw new InvalidDataException($"verified-law support names {opportunities.Length} world opportunities; maximum is {MaxWorldOpportunityEvents}");
        TapeEventID[] claimOpportunityUnion = sourcePredictionEvents.SelectMany(static events => events)
            .Distinct().OrderBy(static id => id.Value).ToArray();
        if (!claimOpportunityUnion.SequenceEqual(opportunities))
            throw new InvalidDataException("verified-law support world opportunities do not match source-claim custody");
        if (opportunities.Length > 0 && sourcePredictionIDs.Length == 0)
            throw new InvalidDataException("powered verified-law support has no source claims");
        string candidateAdmissionID = EmlLawStore.CreateAdmissionID(law);
        string canonicalAuthorityID = EmlLawStore.CreateAdmissionID(admission.Class.Rep);
        EmlLawBehaviorCertificate certificate = law.Certificate;
        string supportSetDigest = ComputeSupportSetDigest(law, sourcePredictionIDs, sourcePredictionDigests, claimMintLineDigestArray, sourcePredictionEvents, claimAdmissionArray);
        string digest = ComputeDigest(candidateAdmissionID, in certificate, canonicalAuthorityID,
            sourcePredictionIDs, sourcePredictionDigests, claimMintLineDigestArray, sourcePredictionEvents, claimAdmissionArray, opportunities, captureStep, captureIndex,
            admission.FirstCapture, admission.RepresentativeChanged, consumed: false, candidateSupport);
        return new EmlVerifiedLawSupportReceipt(candidateAdmissionID, law, supportSetDigest, candidateSupport, law.Certificate, canonicalAuthorityID,
            sourcePredictionIDs, sourcePredictionDigests, claimMintLineDigestArray, sourcePredictionEvents, claimAdmissionArray, opportunities, captureStep, captureIndex,
            admission.FirstCapture, admission.RepresentativeChanged, digest, consumed: false);
    }

    private static string ComputeSupportSetDigest(
        EmlVerifiedLaw candidate,
        IReadOnlyList<int> claimIDs,
        IReadOnlyList<string> claimDigests,
        IReadOnlyList<string> mintLineDigests,
        IReadOnlyList<IReadOnlyList<TapeEventID>> claimEvents,
        IReadOnlyList<EmlSourcePredictionAdmission?> admissions)
    {
        StringBuilder material = new(candidate.Proof.OccurrenceDigest.ToString("X16", CultureInfo.InvariantCulture));
        for (int i = 0; i < claimIDs.Count; i++)
            material.Append('|').Append(claimIDs[i]).Append(':').Append(claimDigests[i]).Append(":line=").Append(mintLineDigests[i]).Append(":mint=")
                .Append(admissions[i]?.Species.ToString() ?? "none").Append(':')
                .Append(admissions[i]?.EventID.Value.ToString() ?? "none").Append(':')
                .Append(string.Join(',', claimEvents[i].Select(static id => id.Value)));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString())));
    }

    internal static string ComputeDigest(
        string candidateAdmissionID,
        in EmlLawBehaviorCertificate certificate,
        string canonicalAuthorityID,
        IReadOnlyList<int> sourcePredictionIDs,
        IReadOnlyList<string> sourcePredictionDigests,
        IReadOnlyList<string> sourcePredictionMintLineDigests,
        IReadOnlyList<IReadOnlyList<TapeEventID>> sourcePredictionOpportunityEvents,
        IReadOnlyList<EmlSourcePredictionAdmission?> sourcePredictionAdmissions,
        IReadOnlyList<TapeEventID> opportunities,
        int captureStep,
        int captureIndex,
        bool firstCapture,
        bool representativeChanged,
        bool consumed,
        IReadOnlyList<SupportPrediction>? candidateSupport = null)
    {
        _ = consumed;
        StringBuilder material = new();
        material.Append(candidateAdmissionID).Append('|').Append(canonicalAuthorityID).Append('|')
            .Append(certificate.AtOne).Append('|').Append(certificate.AtX).Append('|').Append(certificate.AtY)
            .Append('|').Append(captureStep).Append('|').Append(captureIndex).Append('|')
            .Append(firstCapture ? 1 : 0).Append('|').Append(representativeChanged ? 1 : 0);
        for (int i = 0; i < sourcePredictionIDs.Count; i++)
        {
            material.Append("|claim:").Append(sourcePredictionIDs[i]).Append(':').Append(sourcePredictionDigests[i]).Append(":line=").Append(sourcePredictionMintLineDigests[i]);
            for (int j = 0; j < sourcePredictionOpportunityEvents[i].Count; j++)
                material.Append(':').Append(sourcePredictionOpportunityEvents[i][j].Value);
            material.Append(":admission=").Append(sourcePredictionAdmissions[i]?.Species.ToString() ?? "none")
                .Append('@').Append(sourcePredictionAdmissions[i]?.EventID.Value.ToString() ?? "none");
        }
        if (candidateSupport is not null)
        {
            material.Append("|package-digest=").Append(ComputeCandidatePackageDigest(candidateSupport));
            for (int i = 0; i < candidateSupport.Count; i++)
                material.Append("|package:").Append(candidateSupport[i].SourcePredictionID).Append(':')
                    .Append(candidateSupport[i].Certificate).Append(':').Append(candidateSupport[i].LeftRpn)
                    .Append(':').Append(candidateSupport[i].RightRpn);
        }
        for (int i = 0; i < opportunities.Count; i++) material.Append("|world:").Append(opportunities[i].Value);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString())));
    }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(CandidateAdmissionID) || string.IsNullOrWhiteSpace(CanonicalAuthorityID)
            || string.IsNullOrWhiteSpace(Digest) || Digest.Length != 64
            || SourcePredictionIDs.Count != SourcePredictionDigests.Count || SourcePredictionIDs.Count != SourcePredictionOpportunityEvents.Count
            || SourcePredictionIDs.Count != SourcePredictionMintLineDigests.Count
            || SourcePredictionIDs.Count != SourcePredictionAdmissions.Count
            || CaptureStep < 0 || CaptureIndex < 0
            || WorldOpportunityEventIDs.Count > MaxWorldOpportunityEvents)
            throw new InvalidDataException("verified-law support receipt is malformed");
        if (!string.Equals(EmlLawStore.CreateAdmissionID(Candidate), CandidateAdmissionID, StringComparison.Ordinal))
            throw new InvalidDataException("verified-law support candidate disagrees with its admission identity");
        if (SupportSetDigest.Length != 64 || !SupportSetDigest.All(Uri.IsHexDigit))
            throw new InvalidDataException("verified-law support set digest is malformed");
        if (!IsCanonicalDigest(CandidatePackageDigest))
            throw new InvalidDataException("verified-law support candidate package digest is malformed");
        for (int i = 1; i < SourcePredictionIDs.Count; i++)
            if (SourcePredictionIDs[i] <= SourcePredictionIDs[i - 1]) throw new InvalidDataException("verified-law support claims are not sorted and unique");
        for (int i = 0; i < SourcePredictionDigests.Count; i++)
            if (!IsCanonicalDigest(SourcePredictionDigests[i]) || !IsCanonicalDigest(SourcePredictionMintLineDigests[i]))
                throw new InvalidDataException("verified-law support claim digest is malformed");
        for (int i = 0; i < SourcePredictionOpportunityEvents.Count; i++)
        {
            if (SourcePredictionOpportunityEvents[i].Count > MaxWorldOpportunityEvents)
                throw new InvalidDataException("verified-law support claim opportunity count is too large");
            for (int j = 1; j < SourcePredictionOpportunityEvents[i].Count; j++)
                if (SourcePredictionOpportunityEvents[i][j].Value <= SourcePredictionOpportunityEvents[i][j - 1].Value)
                    throw new InvalidDataException("verified-law support claim opportunities are not sorted and unique");
        }
        for (int i = 0; i < SourcePredictionAdmissions.Count; i++)
            if (SourcePredictionAdmissions[i] is EmlSourcePredictionAdmission admission && !admission.IsValid)
                throw new InvalidDataException("verified-law support claim admission is invalid");
        for (int i = 0; i < GeneratedPredictionIDs.Count; i++)
            if (GeneratedPredictionIDs[i] < 0 || (i > 0 && GeneratedPredictionIDs[i] <= GeneratedPredictionIDs[i - 1]))
                throw new InvalidDataException("verified-law generated claim IDs are not sorted and unique");
        if (GeneratedPredictionIDs.Count > 0 && ExecutionEventID is not TapeEventID)
            throw new InvalidDataException("verified-law generated claims have no execution custody");
        if (Consumed && (ExecutionEventID is not TapeEventID || GeneratedPredictionIDs.Count == 0))
            throw new InvalidDataException("consumed verified-law support has incomplete execution custody");
        for (int i = 1; i < WorldOpportunityEventIDs.Count; i++)
            if (WorldOpportunityEventIDs[i].Value <= WorldOpportunityEventIDs[i - 1].Value)
                throw new InvalidDataException("verified-law support opportunities are not sorted and unique");
        TapeEventID[] claimOpportunityUnion = SourcePredictionOpportunityEvents.SelectMany(static events => events)
            .Distinct().OrderBy(static id => id.Value).ToArray();
        if (!claimOpportunityUnion.SequenceEqual(WorldOpportunityEventIDs)
            || (WorldOpportunityEventIDs.Count > 0 && SourcePredictionIDs.Count == 0))
            throw new InvalidDataException("verified-law support world custody does not match source claims");
        if (!string.Equals(SupportSetDigest,
                ComputeSupportSetDigest(Candidate, SourcePredictionIDs, SourcePredictionDigests, SourcePredictionMintLineDigests, SourcePredictionOpportunityEvents, SourcePredictionAdmissions),
                StringComparison.Ordinal))
            throw new InvalidDataException("verified-law support set digest mismatch");
        EmlLawBehaviorCertificate certificate = Certificate;
        if (!string.Equals(Digest, ComputeDigest(CandidateAdmissionID, in certificate, CanonicalAuthorityID,
                SourcePredictionIDs, SourcePredictionDigests, SourcePredictionMintLineDigests, SourcePredictionOpportunityEvents, SourcePredictionAdmissions, WorldOpportunityEventIDs, CaptureStep, CaptureIndex,
                FirstCapture, RepresentativeChanged, Consumed, CandidateSupport), StringComparison.Ordinal))
            throw new InvalidDataException("verified-law support receipt digest mismatch");
    }

    internal void ValidateAfterLoad() => Validate();

    private static bool IsCanonicalDigest(string value)
        => value.Length == 64 && value.All(Uri.IsHexDigit)
            && string.Equals(value, value.ToLowerInvariant(), StringComparison.Ordinal);
}

internal readonly record struct EmlLawProof(
    ulong OccurrenceDigest,
    string AbsentFiller,
    string OccurrenceCheckPrediction,
    int VerifierVersion,
    EmlLawExactEvidence AtOne,
    EmlLawExactEvidence AtX,
    EmlLawExactEvidence AtY,
    EmlLawExactEvidence AtAbsentFiller,
    EmlDomainGuardSet? DomainGuards = null,
    EmlGuardWitness GuardWitness = default,
    int SearchRevision = 0,
    int SearchBudget = 0,
    ulong CompositionDigest = 0,
    string GuardScheme = "")
{
    public const string LogExpGuardScheme = "log-exp-v1";
    public const string ExpLogGuardScheme = "exp-log-v1";
    public const string ParameterErasureGuardScheme = "parameter-erasure-v1";
    public ulong DomainGuardDigest => DomainGuards?.Digest ?? 0;
    public bool IsRung0Eligible => DomainGuards?.IsGuarded == true
        && GuardWitness.IsInstanceBound
        && IsTypedGuardScheme(GuardScheme)
        && SearchRevision > 0
        && SearchBudget > 0;
    public bool IsGuarded => IsRung0Eligible;

    public static bool IsTypedGuardScheme(string scheme)
        => string.Equals(scheme, LogExpGuardScheme, StringComparison.Ordinal)
            || string.Equals(scheme, ExpLogGuardScheme, StringComparison.Ordinal)
            || string.Equals(scheme, ParameterErasureGuardScheme, StringComparison.Ordinal);
};

/// Exact tape identity of one admitted basis law. Support digest alone is not a
/// foreign key: multiple law templates may be admitted from the same support.
internal readonly record struct EmlRung0BasisLawIdentity(string AdmissionID)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(AdmissionID);
}

internal readonly record struct EmlLawCandidateCensus(
    int NumericallyVerified,
    int BasisRepresentatives,
    int BehaviorSpan,
    int DirectWitnessComposed,
    int SampledJoinComposed,
    int NovelBehavior);

internal readonly record struct EmlPredictionBoundRewriteCensus(
    int Calls,
    int Forms,
    int CarrierBound,
    int FormsWithRewrites,
    int Rewrites,
    int GuardEligible,
    int RankReducing,
    int MaxForms,
    int MaxCarrierBound,
    int MaxFormsWithRewrites,
    int MaxRewrites,
    int MaxGuardEligible,
    int MaxRankReducing,
    int FirstPredictionID,
    string FirstLawID,
    string FirstRewriteID,
    string FirstOrientation,
    string FirstForm,
    string FirstRulePattern,
    string FirstMatchedTerm,
    string FirstRewriteAntecedent,
    string FirstRewriteConsequent,
    int FirstReducingPredictionID,
    string FirstReducingLawID,
    string FirstReducingRewriteID,
    string FirstReducingOrientation,
    string FirstReducingForm,
    string FirstReducingAntecedent,
    string FirstReducingConsequent)
{
    public static EmlPredictionBoundRewriteCensus Empty => new(
        Calls: 0,
        Forms: 0,
        CarrierBound: 0,
        FormsWithRewrites: 0,
        Rewrites: 0,
        GuardEligible: 0,
        RankReducing: 0,
        MaxForms: 0,
        MaxCarrierBound: 0,
        MaxFormsWithRewrites: 0,
        MaxRewrites: 0,
        MaxGuardEligible: 0,
        MaxRankReducing: 0,
        FirstPredictionID: -1,
        FirstLawID: "",
        FirstRewriteID: "",
        FirstOrientation: "",
        FirstForm: "",
        FirstRulePattern: "",
        FirstMatchedTerm: "",
        FirstRewriteAntecedent: "",
        FirstRewriteConsequent: "",
        FirstReducingPredictionID: -1,
        FirstReducingLawID: "",
        FirstReducingRewriteID: "",
        FirstReducingOrientation: "",
        FirstReducingForm: "",
        FirstReducingAntecedent: "",
        FirstReducingConsequent: "");

    public bool HasNaturalOpportunity => CarrierBound > 0 && GuardEligible > 0 && RankReducing > 0;
}

/// A law can enter this type only after the current evaluator has witnessed its canonical
/// extension and an absent-filler instance as exact. Checkpoint loading reconstitutes that receipt;
/// it does not rerun or weaken verification.
internal sealed class EmlVerifiedLaw
{
    internal const int CurrentVerifierVersion = 1;
    private const double MdlEligibilityFloor = 1.0;

    private EmlVerifiedLaw(
        EmlLaw law,
        EmlLawBehaviorCertificate certificate,
        EmlLawProof proof,
        int templateCostBits)
    {
        Law = law;
        Certificate = certificate;
        Proof = proof;
        TemplateCostBits = templateCostBits;
    }

    public EmlLaw Law { get; }
    public EmlLawBehaviorCertificate Certificate { get; }
    public EmlLawProof Proof { get; }
    public int TemplateCostBits { get; }

    public static bool TryVerify(
        in EmlLaw candidate,
        IReadOnlyList<EmlLawPrediction> support,
        int signatureDigits,
        out EmlVerifiedLaw? verified)
    {
        verified = null;
        if (!double.IsFinite(candidate.MdlGain)
            || candidate.MdlGain <= MdlEligibilityFloor
            || candidate.CertificateClasses < 2
            || candidate.Fillers < 2
            || signatureDigits is < 1 or > 9
            || support.Count < candidate.CertificateClasses
            || string.IsNullOrEmpty(candidate.OccurrenceCheckFiller)) return false;

        if (!EmlLawInstantiation.TryCreate(candidate.Template, candidate.OccurrenceCheckFiller,
                out EmlLawInstantiation absentInstantiation)) return false;
        string expectedPrediction = absentInstantiation.LeftRpn + " = " + absentInstantiation.RightRpn;
        if (!string.Equals(expectedPrediction, candidate.OccurrenceCheckPrediction, StringComparison.Ordinal)) return false;

        EmlGrader grader = new();
        if (!TryVerifySupport(grader, support, absentInstantiation.LeftRpn, absentInstantiation.RightRpn, signatureDigits,
                out ulong supportDigest, out int supportClasses)
            || supportClasses < candidate.CertificateClasses) return false;
        if (!TryWitness(grader, candidate.Template, "1", signatureDigits,
                out EmlSig atOne, out EmlLawExactEvidence oneEvidence)) return false;
        if (!TryWitness(grader, candidate.Template, "x", signatureDigits,
                out EmlSig atX, out EmlLawExactEvidence xEvidence)) return false;
        if (!TryWitness(grader, candidate.Template, "y", signatureDigits,
                out EmlSig atY, out EmlLawExactEvidence yEvidence)) return false;
        if (!TryWitness(grader, candidate.Template, candidate.OccurrenceCheckFiller, signatureDigits,
                out _, out EmlLawExactEvidence absentEvidence)) return false;

        EmlLawBehaviorCertificate certificate = new(atOne, atX, atY);
        bool parameterErasure = string.Equals(candidate.Template, "xx?E1EE = 11?E1EE", StringComparison.Ordinal);
        EmlDomainGuardSet guards = EmlDomainGuardSet.Empty;
        EmlGuardWitness guardWitness = default;
        TryDeriveDomainProof(candidate, absentInstantiation, out guards, out guardWitness);
        EmlLawProof proof = new(
            supportDigest,
            candidate.OccurrenceCheckFiller,
            candidate.OccurrenceCheckPrediction,
            CurrentVerifierVersion,
            oneEvidence,
            xEvidence,
            yEvidence,
            absentEvidence,
            guards,
            guardWitness,
            guards.IsGuarded ? 1 : 0,
            guards.IsGuarded ? 16 : 0,
            0,
            guards.IsGuarded
                ? parameterErasure ? EmlLawProof.ParameterErasureGuardScheme : EmlLawProof.ExpLogGuardScheme
                : "");
        verified = new EmlVerifiedLaw(candidate, certificate, proof, CalculateTemplateCostBits(candidate.Template));
        return true;
    }

    private static bool TryDeriveDomainProof(
        in EmlLaw candidate,
        in EmlLawInstantiation absentInstantiation,
        out EmlDomainGuardSet guards,
        out EmlGuardWitness witness)
    {
        guards = EmlDomainGuardSet.Empty;
        witness = default;
        string candidateTemplate = candidate.Template;
        bool Reject(string reason)
        {
            Trace.Cortex.Boundary("eml.guard-package", $"template={candidateTemplate} reason={reason}");
            return false;
        }
        // Eligibility is structural: every template gets a named typed law family.
        // Numeric exactness alone never supplies a branch certificate.
        bool parameterErasure = string.Equals(candidate.Template, "xx?E1EE = 11?E1EE", StringComparison.Ordinal);
        bool logExp = string.Equals(candidate.Template, "11?E1EE1E = ?", StringComparison.Ordinal);
        if ((!parameterErasure && !logExp)
            || !EmlLawInstantiation.TryCreate(candidate.Template, "1", out EmlLawInstantiation guardInstantiation)
            || !EmlTree.TryParseRPN(guardInstantiation.LeftRpn, out EmlTree? tree)) return Reject("template-or-safe-filler-parse");

        EmlTreeEvaluation evaluation = tree!.EvaluateAt(EmlTree.P1.X, EmlTree.P1.Y);
        if (!evaluation.TryGetNode(EmlPath.Root, out EmlNodeEvaluation node) || !node.P1.Valid) return Reject("safe-filler-root-probe-invalid");
        EmlProbeEvaluation probe = node.P1;
        EmlEnclosureWitness enclosure = EmlEnclosureWitness.FromConcreteProbe(probe);
        EmlPrincipalBranch branch = probe.PrincipalBranch;
        EmlBranchWitness branchWitness = new(
            branch.LogDefined,
            branch.EnclosureCrossesNegativeRealCut,
            branch.ExpAfterLogRoundTrips,
            branch.LogAfterExpRoundTrips,
            branch.ExponentialTurn);
        EmlTree guardShape = tree!;
        EmlOneHoleLaw templateLaw = default;
        if (!EmlOneHoleLaw.TryParse(candidate.Template, out templateLaw)) return Reject("template-law-parse");
        guardShape = templateLaw.Left;
        guards = parameterErasure
            ? EmlDomainGuardSet.ForParameterErasure(guardShape)
            : EmlDomainGuardSet.ForExpLog(guardShape);
        if (!guards.IsGuarded) return Reject("typed-guard-set-empty");
        EmlTreeEvaluation? consequentEvaluation = null;
        if (parameterErasure
            && EmlTree.TryParseRPN(guardInstantiation.RightRpn, out EmlTree? consequentTree))
            consequentEvaluation = consequentTree!.EvaluateAt(EmlTree.P1.X, EmlTree.P1.Y);
        List<EmlGuardNodeFact> nodeFacts = CreateNodeFacts(evaluation, consequentEvaluation);
        try
        {
            witness = EmlGuardWitness.Create(
                EmlPath.Root,
                guardInstantiation.LeftRpn,
                guardInstantiation.Filler,
                guardInstantiation.LeftRpn,
                guardInstantiation.RightRpn,
                in enclosure,
                in branchWitness,
                nodeFacts);
        }
        catch (ArgumentException)
        {
            guards = EmlDomainGuardSet.Empty;
            witness = default;
            return Reject("witness-create");
        }
        if (!guards.TryValidate(in witness))
        {
            guards = EmlDomainGuardSet.Empty;
            witness = default;
            return Reject("typed-guard-validate");
        }
        return true;
    }

    private static List<EmlGuardNodeFact> CreateNodeFacts(EmlTreeEvaluation antecedent, EmlTreeEvaluation? consequent)
    {
        List<EmlGuardNodeFact> facts = new(antecedent.Nodes.Count + (consequent?.Nodes.Count ?? 0));
        AppendNodeFacts(facts, EmlGuardSides.Antecedent, antecedent);
        if (consequent is EmlTreeEvaluation right) AppendNodeFacts(facts, EmlGuardSides.Consequent, right);
        facts.Sort(static (left, right) =>
        {
            int side = left.Side.CompareTo(right.Side);
            return side != 0 ? side : string.CompareOrdinal(left.Path.Steps, right.Path.Steps);
        });
        return facts;
    }

    private static void AppendNodeFacts(List<EmlGuardNodeFact> facts, EmlGuardSides side, EmlTreeEvaluation evaluation)
    {
        foreach ((EmlPath path, EmlNodeEvaluation node) in evaluation.Nodes)
        {
            EmlProbeEvaluation probe = node.P1;
            if (!probe.Valid || !probe.Plain.Finite) continue;
            facts.Add(new EmlGuardNodeFact(
                side,
                path,
                EmlEnclosureWitness.FromConcreteProbe(probe),
                new EmlBranchWitness(
                    probe.PrincipalBranch.LogDefined,
                    probe.PrincipalBranch.EnclosureCrossesNegativeRealCut,
                    probe.PrincipalBranch.ExpAfterLogRoundTrips,
                    probe.PrincipalBranch.LogAfterExpRoundTrips,
                    probe.PrincipalBranch.ExponentialTurn)));
        }
    }

    internal static bool TryReverifyPackage(
        in EmlLaw law,
        in EmlLawBehaviorCertificate certificate,
        in EmlLawProof proof,
        int signatureDigits,
        int templateCostBits,
        out EmlVerifiedLaw? verified)
    {
        verified = null;
        if (proof.VerifierVersion != CurrentVerifierVersion
            || proof.OccurrenceDigest == 0
            || templateCostBits != CalculateTemplateCostBits(law.Template)
            || !double.IsFinite(law.MdlGain)
            || law.MdlGain <= MdlEligibilityFloor
            || law.CertificateClasses < 2
            || law.Fillers < 2
            || signatureDigits is < 1 or > 9
            || string.IsNullOrEmpty(law.OccurrenceCheckFiller)
            || !string.Equals(proof.AbsentFiller, law.OccurrenceCheckFiller, StringComparison.Ordinal)
            || !string.Equals(proof.OccurrenceCheckPrediction, law.OccurrenceCheckPrediction, StringComparison.Ordinal)
            || !EmlLawInstantiation.TryCreate(law.Template, law.OccurrenceCheckFiller,
                out EmlLawInstantiation absentInstantiation)
            || !string.Equals(absentInstantiation.LeftRpn + " = " + absentInstantiation.RightRpn,
                law.OccurrenceCheckPrediction, StringComparison.Ordinal))
            return false;

        if (proof.DomainGuards is null
            || proof.DomainGuards.Digest != proof.DomainGuardDigest
            || !proof.GuardWitness.HasValidDigest)
            return false;

        if (proof.DomainGuards.IsGuarded)
        {
            string expectedScheme = law.Template switch
            {
                "xx?E1EE = 11?E1EE" => EmlLawProof.ParameterErasureGuardScheme,
                "11?E1EE1E = ?" => EmlLawProof.ExpLogGuardScheme,
                _ => string.Empty,
            };
            if (!proof.DomainGuards.TryValidate(proof.GuardWitness)
                || !TryDeriveDomainProof(law, absentInstantiation, out EmlDomainGuardSet expectedGuards,
                    out EmlGuardWitness expectedWitness)
                || proof.DomainGuards.Canonical() != expectedGuards.Canonical()
                || proof.GuardWitness.Canonical() != expectedWitness.Canonical()
                || !string.Equals(proof.GuardScheme, expectedScheme, StringComparison.Ordinal)
                || proof.SearchRevision < 1
                || proof.SearchBudget < 1)
                return false;
        }
        else if (proof.GuardWitness.IsInstanceBound || !string.IsNullOrEmpty(proof.GuardScheme)
            || proof.SearchRevision != 0 || proof.SearchBudget != 0 || proof.CompositionDigest != 0)
        {
            return false;
        }

        EmlGrader grader = new();
        if (!TryWitness(grader, law.Template, "1", signatureDigits,
                out EmlSig atOne, out EmlLawExactEvidence oneEvidence)
            || !TryWitness(grader, law.Template, "x", signatureDigits,
                out EmlSig atX, out EmlLawExactEvidence xEvidence)
            || !TryWitness(grader, law.Template, "y", signatureDigits,
                out EmlSig atY, out EmlLawExactEvidence yEvidence)
            || !TryWitness(grader, law.Template, law.OccurrenceCheckFiller, signatureDigits,
                out _, out EmlLawExactEvidence absentEvidence))
            return false;

        EmlLawBehaviorCertificate current = new(atOne, atX, atY);
        if (current != certificate
            || oneEvidence != proof.AtOne
            || xEvidence != proof.AtX
            || yEvidence != proof.AtY
            || absentEvidence != proof.AtAbsentFiller)
            return false;
        verified = new EmlVerifiedLaw(law, certificate, proof, templateCostBits);
        return true;
    }

    internal void Save(CkptWriter writer)
    {
        writer.Str(Law.Template);
        writer.I32(Law.CertificateClasses);
        writer.I32(Law.Fillers);
        writer.F64(Law.MdlGain);
        writer.Str(Law.OccurrenceCheckFiller);
        writer.Str(Law.OccurrenceCheckPrediction);
        WriteCertificate(writer, Certificate);
        WriteProof(writer, Proof);
        writer.I32(TemplateCostBits);
    }

    internal static EmlVerifiedLaw LoadVerified(CkptReader reader, bool hasGuardSchema = true, bool hasWitnessContext = true, bool hasNodeFacts = false)
    {
        EmlLaw law = new(reader.Str(), reader.I32(), reader.I32(), reader.F64(), reader.Str(), reader.Str());
        EmlLawBehaviorCertificate certificate = ReadCertificate(reader);
        EmlLawProof proof = ReadProof(reader, hasGuardSchema, hasWitnessContext, hasNodeFacts);
        int templateCostBits = reader.I32();
        if (proof.VerifierVersion != CurrentVerifierVersion)
            throw new InvalidDataException($"EML law verifier version {proof.VerifierVersion} is not supported");
        if (proof.OccurrenceDigest == 0
            || !double.IsFinite(law.MdlGain)
            || law.MdlGain <= MdlEligibilityFloor
            || law.CertificateClasses < 2
            || law.Fillers < 2
            || string.IsNullOrEmpty(law.OccurrenceCheckFiller)
            || !EmlLawInstantiation.TryCreate(law.Template, law.OccurrenceCheckFiller, out EmlLawInstantiation absentInstantiation)
            || !string.Equals(absentInstantiation.LeftRpn + " = " + absentInstantiation.RightRpn,
                law.OccurrenceCheckPrediction, StringComparison.Ordinal))
            throw new InvalidDataException("EML law checkpoint carries an invalid verified law package");
        if (templateCostBits != CalculateTemplateCostBits(law.Template))
            throw new InvalidDataException("EML law checkpoint carries a stale representative price");
        if (!string.Equals(proof.AbsentFiller, law.OccurrenceCheckFiller, StringComparison.Ordinal)
            || !string.Equals(proof.OccurrenceCheckPrediction, law.OccurrenceCheckPrediction, StringComparison.Ordinal)
            || !proof.AtOne.IsExact
            || !proof.AtX.IsExact
            || !proof.AtY.IsExact
            || !proof.AtAbsentFiller.IsExact)
            throw new InvalidDataException("EML law checkpoint carries an invalid verification receipt");
        if (hasGuardSchema && (proof.DomainGuards is null
            || proof.DomainGuards.Digest != proof.DomainGuardDigest
            || !proof.GuardWitness.HasValidDigest
            || proof.DomainGuards.IsGuarded && (!proof.IsRung0Eligible
                || !proof.DomainGuards.TryValidate(proof.GuardWitness))))
            throw new InvalidDataException("EML law checkpoint carries an invalid domain guard receipt");
        if (hasGuardSchema && proof.DomainGuards is { IsGuarded: true } guarded)
        {
            if (!EmlLawInstantiation.TryCreate(law.Template, law.OccurrenceCheckFiller, out EmlLawInstantiation absent)
                || !TryDeriveDomainProof(law, absent, out EmlDomainGuardSet expectedGuards, out EmlGuardWitness expectedWitness)
                || guarded.Canonical() != expectedGuards.Canonical()
                || proof.GuardWitness.Canonical() != expectedWitness.Canonical())
                throw new InvalidDataException("EML law checkpoint guard package does not rederive from its law");
            string expectedScheme = law.Template switch
            {
                "xx?E1EE = 11?E1EE" => EmlLawProof.ParameterErasureGuardScheme,
                "11?E1EE1E = ?" => EmlLawProof.ExpLogGuardScheme,
                _ => string.Empty,
            };
            if (!string.Equals(proof.GuardScheme, expectedScheme, StringComparison.Ordinal))
                throw new InvalidDataException("EML law checkpoint guard scheme does not match its law");
        }
        return new EmlVerifiedLaw(law, certificate, proof, templateCostBits);
    }

    private static bool TryVerifySupport(
        EmlGrader grader,
        IReadOnlyList<EmlLawPrediction> support,
        string absentLeft,
        string absentRight,
        int signatureDigits,
        out ulong digest,
        out int certificateClasses)
    {
        List<EmlLawPrediction> ordered = new(support.Count);
        HashSet<EmlCert> certificates = new();
        for (int i = 0; i < support.Count; i++)
        {
            EmlLawPrediction claim = support[i];
            if (claim.Cert.Grade != 'E'
                || (string.Equals(claim.LeftRpn, absentLeft, StringComparison.Ordinal)
                    && string.Equals(claim.RightRpn, absentRight, StringComparison.Ordinal)))
            {
                digest = 0;
                certificateClasses = 0;
                return false;
            }
            certificates.Add(claim.Cert);
            ordered.Add(claim);
        }
        ordered.Sort(static (left, right) =>
        {
            int byCertificate = string.CompareOrdinal(left.Cert.Hex(), right.Cert.Hex());
            if (byCertificate != 0) return byCertificate;
            int byLeft = string.CompareOrdinal(left.LeftRpn, right.LeftRpn);
            return byLeft != 0 ? byLeft : string.CompareOrdinal(left.RightRpn, right.RightRpn);
        });

        ulong hash = 14695981039346656037UL;
        for (int i = 0; i < ordered.Count; i++)
        {
            EmlLawPrediction claim = ordered[i];
            EmlVerdict verdict = grader.GradeRpn(claim.LeftRpn, claim.RightRpn);
            EmlCert currentCertificate = EmlCert.Of(in verdict, signatureDigits);
            if (verdict.Grade != 'E' || currentCertificate != claim.Cert)
            {
                digest = 0;
                certificateClasses = 0;
                return false;
            }
            HashText(ref hash, claim.Cert.Hex());
            HashText(ref hash, claim.LeftRpn);
            HashText(ref hash, claim.RightRpn);
        }
        digest = hash;
        certificateClasses = certificates.Count;
        return true;
    }

    private static bool TryWitness(
        EmlGrader grader,
        string template,
        string filler,
        int signatureDigits,
        out EmlSig signature,
        out EmlLawExactEvidence evidence)
    {
        if (!EmlLawInstantiation.TryCreate(template, filler, out EmlLawInstantiation instantiation))
        {
            signature = default;
            evidence = default;
            return false;
        }

        EmlVerdict verdict = grader.GradeRpn(instantiation.LeftRpn, instantiation.RightRpn);
        evidence = EmlLawExactEvidence.FromVerdict(in verdict);
        if (!evidence.IsExact)
        {
            signature = default;
            return false;
        }

        signature = Eml.Signature(
            new EmlValue(verdict.Rhs1, true),
            new EmlValue(verdict.Rhs2, true),
            signatureDigits);
        return true;
    }

    private static int CalculateTemplateCostBits(string template)
    {
        int tokens = 0;
        for (int i = 0; i < template.Length; i++)
            if (template[i] is '1' or 'x' or 'y' or 'E' or '?') tokens++;
        return checked(2 * tokens + 8);
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

    private static void WriteCertificate(CkptWriter writer, in EmlLawBehaviorCertificate certificate)
    {
        WriteSignature(writer, certificate.AtOne);
        WriteSignature(writer, certificate.AtX);
        WriteSignature(writer, certificate.AtY);
    }

    private static EmlLawBehaviorCertificate ReadCertificate(CkptReader reader)
        => new(ReadSignature(reader), ReadSignature(reader), ReadSignature(reader));

    private static void WriteProof(CkptWriter writer, in EmlLawProof proof)
    {
        writer.U64(proof.OccurrenceDigest);
        writer.Str(proof.AbsentFiller);
        writer.Str(proof.OccurrenceCheckPrediction);
        writer.I32(proof.VerifierVersion);
        WriteEvidence(writer, proof.AtOne);
        WriteEvidence(writer, proof.AtX);
        WriteEvidence(writer, proof.AtY);
        WriteEvidence(writer, proof.AtAbsentFiller);
        EmlDomainGuardSet guards = proof.DomainGuards ?? EmlDomainGuardSet.Empty;
        writer.I32(guards.Atoms.Count);
        for (int i = 0; i < guards.Atoms.Count; i++)
        {
            EmlDomainAtom atom = guards.Atoms[i];
            writer.U8((byte)atom.Kind);
            writer.Str(atom.Path.Steps);
            writer.F64(atom.Lower);
            writer.F64(atom.Upper);
            writer.U8((byte)atom.Side);
        }
        writer.U64(guards.Digest);
        WriteGuardWitness(writer, proof.GuardWitness);
        writer.I32(proof.SearchRevision);
        writer.I32(proof.SearchBudget);
        writer.U64(proof.CompositionDigest);
        writer.Str(proof.GuardScheme);
    }

    private static EmlLawProof ReadProof(CkptReader reader, bool hasGuardSchema = true, bool hasWitnessContext = true, bool hasNodeFacts = false)
    {
        EmlLawProof proof = new(
            reader.U64(),
            reader.Str(),
            reader.Str(),
            reader.I32(),
            ReadEvidence(reader),
            ReadEvidence(reader),
            ReadEvidence(reader),
            ReadEvidence(reader));
        if (!hasGuardSchema) return proof;
        int atomCount = reader.I32();
        if (atomCount < 0 || atomCount > 64) throw new InvalidDataException("EML law proof has an invalid guard atom count");
        List<EmlDomainAtom> atoms = new(atomCount);
        for (int i = 0; i < atomCount; i++)
        {
            EmlDomainGuardKinds kind = (EmlDomainGuardKinds)reader.U8();
            if (!Enum.IsDefined(kind)) throw new InvalidDataException("EML law proof has an unknown guard kind");
            EmlPath path = new(reader.Str());
            double lower = reader.F64();
            double upper = reader.F64();
            EmlGuardSides side = hasNodeFacts ? (EmlGuardSides)reader.U8() : EmlGuardSides.Antecedent;
            if (!Enum.IsDefined(side)) throw new InvalidDataException("EML law proof has an unknown guard side");
            atoms.Add(new EmlDomainAtom(kind, path, lower, upper, side));
        }
        EmlDomainGuardSet guards = EmlDomainGuardSet.Create(atoms);
        if (guards.Digest != reader.U64()) throw new InvalidDataException("EML law proof guard digest mismatch");
        return proof with
        {
            DomainGuards = guards,
            GuardWitness = ReadGuardWitness(reader, hasWitnessContext, hasNodeFacts),
            SearchRevision = reader.I32(),
            SearchBudget = reader.I32(),
            CompositionDigest = reader.U64(),
            GuardScheme = hasWitnessContext ? reader.Str() : string.Empty,
        };
    }

    private static void WriteGuardWitness(CkptWriter writer, in EmlGuardWitness witness)
    {
        writer.Str(witness.MatchedTermRpn ?? string.Empty);
        writer.Str(witness.SubstitutionRpn ?? string.Empty);
        writer.Str(witness.MatchedPath.Steps);
        writer.Str(witness.AntecedentRpn ?? string.Empty);
        writer.Str(witness.ConsequentRpn ?? string.Empty);
        writer.F64(witness.Enclosure.RealLower);
        writer.F64(witness.Enclosure.RealUpper);
        writer.F64(witness.Enclosure.ImaginaryLower);
        writer.F64(witness.Enclosure.ImaginaryUpper);
        writer.Bool(witness.Branch.LogDefined);
        writer.Bool(witness.Branch.EnclosureCrossesNegativeRealCut);
        writer.Bool(witness.Branch.ExpAfterLogRoundTrips);
        writer.Bool(witness.Branch.LogAfterExpRoundTrips);
        writer.I64(witness.Branch.ExponentialTurn);
        writer.U64(witness.Digest);
        int factCount = witness.NodeFacts?.Count ?? 0;
        writer.I32(factCount);
        for (int i = 0; i < factCount; i++)
        {
            EmlGuardNodeFact fact = witness.NodeFacts![i];
            writer.U8((byte)fact.Side);
            writer.Str(fact.Path.Steps);
            writer.F64(fact.Enclosure.RealLower);
            writer.F64(fact.Enclosure.RealUpper);
            writer.F64(fact.Enclosure.ImaginaryLower);
            writer.F64(fact.Enclosure.ImaginaryUpper);
            writer.Bool(fact.Branch.LogDefined);
            writer.Bool(fact.Branch.EnclosureCrossesNegativeRealCut);
            writer.Bool(fact.Branch.ExpAfterLogRoundTrips);
            writer.Bool(fact.Branch.LogAfterExpRoundTrips);
            writer.I64(fact.Branch.ExponentialTurn);
        }
    }

    private static EmlGuardWitness ReadGuardWitness(CkptReader reader, bool hasWitnessContext = true, bool hasNodeFacts = false)
    {
        string matchedTerm = reader.Str();
        string substitution = reader.Str();
        EmlPath path = hasWitnessContext ? new EmlPath(reader.Str()) : EmlPath.Root;
        string antecedent = hasWitnessContext ? reader.Str() : string.Empty;
        string consequent = hasWitnessContext ? reader.Str() : string.Empty;
        EmlEnclosureWitness enclosure = new(reader.F64(), reader.F64(), reader.F64(), reader.F64());
        EmlBranchWitness branch = new(reader.Bool(), reader.Bool(), reader.Bool(), reader.Bool(), reader.I64());
        ulong digest = reader.U64();
        List<EmlGuardNodeFact>? facts = null;
        if (hasNodeFacts)
        {
            int count = reader.I32();
            if (count < 0 || count > 4096) throw new InvalidDataException("EML law proof has an invalid node-fact count");
            facts = new List<EmlGuardNodeFact>(count);
            for (int i = 0; i < count; i++)
            {
                EmlGuardSides side = (EmlGuardSides)reader.U8();
                if (!Enum.IsDefined(side)) throw new InvalidDataException("EML law proof has an unknown node-fact side");
                facts.Add(new EmlGuardNodeFact(
                    side,
                    new EmlPath(reader.Str()),
                    new EmlEnclosureWitness(reader.F64(), reader.F64(), reader.F64(), reader.F64()),
                    new EmlBranchWitness(reader.Bool(), reader.Bool(), reader.Bool(), reader.Bool(), reader.I64())));
            }
        }
        return new EmlGuardWitness(
            matchedTerm,
            substitution,
            enclosure, branch, digest, path, antecedent, consequent, facts);
    }

    private static void WriteEvidence(CkptWriter writer, in EmlLawExactEvidence evidence)
    {
        writer.U8((byte)evidence.Grade);
        writer.Bool(evidence.Q12Home);
        writer.Bool(evidence.Q12Regime);
        writer.Str(evidence.EnclosureColumns);
    }

    private static EmlLawExactEvidence ReadEvidence(CkptReader reader)
        => new((char)reader.U8(), reader.Bool(), reader.Bool(), reader.Str());

    private static void WriteSignature(CkptWriter writer, in EmlSig signature)
    {
        writer.I64(signature.R1);
        writer.I64(signature.I1);
        writer.I64(signature.R2);
        writer.I64(signature.I2);
    }

    private static EmlSig ReadSignature(CkptReader reader)
        => new(reader.I64(), reader.I64(), reader.I64(), reader.I64());

}

internal sealed partial class EmlLawStore
{
    private readonly record struct LawExecutionKey(string Digest, string Authority);
    private readonly record struct PersistedLawExecution(
        TapeEventID EventID,
        TapeEventView View,
        TapePacketCreator.EmlLawExecutionSupportPacket Packet);

    private const int CheckpointSchema = 18;
    private const int NodeFactsCheckpointSchema = 9;
    private const int Rung0CheckpointSchema = 9;
    private const int Rung0BasisArchiveSchema = 10;
    private const int Rung0BasisCertificateSchema = 11;
    private const int MaxRung0BasisArchiveEntries = 4096 * 32;
    private const int LegacyCheckpointSchema = 4;
    private readonly SemanticCAS<EmlLawBehaviorCertificate, EmlVerifiedLaw> _classes =
        new(CompareRepresentatives);
    private readonly HashSet<string> _admissions = new(StringComparer.Ordinal);
    // The set is the membership index; this journal preserves append order for typed deltas.
    private readonly List<string> _admissionJournal = new();
    private EmlRewriteSystem? _rewriteSystem;
    private int _rewriteSearchRevision = 1;
    private int _rewriteSearchBudget = 16;
    private ulong _derivationDigest;
    private readonly List<EmlCompositionStep> _derivationSteps = new();
    private readonly List<EmlRung0Proof> _rung0Proofs = new();
    // Digest -> index into the append-only proof journal, and ProofDigest -> index into the audit
    // journal (unique per digest by the dedup/re-audit guards). Collapses the per-record/per-load
    // find-by-digest scans (proof dedup, audit dedup, ValidateRung0Audit, promotion, repromotion,
    // Rung0ProofCarriesRule) from O(journal) to O(1). Audit replacements preserve ProofDigest, so
    // in-place replaces leave the index valid; only append and Clear touch it.
    private readonly Dictionary<ulong, int> _rung0ProofIndex = new();
    private readonly Dictionary<ulong, int> _rung0AuditIndex = new();
    private readonly SortedDictionary<string, EmlVerifiedLaw> _rung0BasisArchive =
        new(StringComparer.Ordinal);
    // Resolution index over the archive keyed by the two discriminators every rung-0 basis
    // match leads with (BasisLawDigest == Proof.OccurrenceDigest, RulePattern == Law.Template).
    // Collapses TryFindRung0Basis from O(archive) tree-instantiations per derivation step to
    // O(bucket≈1). Maintained as an invariant of the archive — every mutation routes through
    // Add/Remove/ClearRung0BasisArchive so the two never drift.
    private readonly Dictionary<Rung0BasisKey, List<EmlVerifiedLaw>> _rung0BasisArchiveIndex = new();
    private readonly List<EmlRung0Audit> _rung0Audits = new();
    private readonly List<EmlRung0RuleTransition> _rung0RuleTransitions = new();
    private readonly HashSet<EmlRuleID> _quarantinedRung0Rules = new();
    private readonly List<EmlVerifiedLawSupportReceipt> _verifiedLawSupports = new();
    private int _pendingVerifiedLawSupports;                             // unconsumed world-opportunity receipts — the per-step flush gate reads this, never the full list
    private readonly Dictionary<string, EmlVerifiedLawSupportReceipt> _verifiedLawSupportsByDigest = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _verifiedLawSupportIndexByDigest = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SupportValidationState> _validatedSupportStates = new(StringComparer.Ordinal);
    private readonly HashSet<string> _verifiedLawSupportDigests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EmlVerifiedLaw> _verifiedLawAuthorities = new(StringComparer.Ordinal);
    private readonly List<EmlPatternGrammarAdmissionReceipt> _theoryGrammarAdmissions = new();
    private readonly Dictionary<string, int> _theoryGrammarAdmissionIndexByAuthorityDomain = new(StringComparer.Ordinal);
    private readonly List<int> _pendingPatternGrammarAdmissionIndices = new();
    private readonly HashSet<int> _dirtyPatternGrammarAdmissionIndices = new();
    private readonly List<EmlPatternGrammarAdmissionEconomicsRecord> _theoryGrammarAdmissionEconomics = new();
    private readonly Dictionary<string, int> _theoryGrammarAdmissionEconomicsIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<LawExecutionKey, PersistedLawExecution> _persistedLawExecutions = new();
    private long _persistedLawExecutionIndexMark;

    private int _checkpointAdmissionCount;
    private int _checkpointCompositionCount;
    private int _checkpointRung0ProofCount;
    private int _checkpointRung0AuditCount;
    private int _checkpointRung0TransitionCount;
    private int _checkpointVerifiedLawSupportCount;
    private int _checkpointPatternGrammarAdmissionCount;
    private int _checkpointPatternGrammarAdmissionEconomicsCount;

    private readonly record struct SupportValidationState(
        TapeEventID? SupportEventID,
        TapeEventID? ExecutionEventID,
        int[] GeneratedPredictionIDs)
    {
        internal bool Matches(EmlVerifiedLawSupportReceipt support)
            => SupportEventID == support.SupportEventID
                && ExecutionEventID == support.ExecutionEventID
                && GeneratedPredictionIDs.SequenceEqual(support.GeneratedPredictionIDs);

        internal static SupportValidationState Capture(EmlVerifiedLawSupportReceipt support)
            => new(support.SupportEventID, support.ExecutionEventID, support.GeneratedPredictionIDs.ToArray());
    }
    internal bool LegacyWorldSupportUnavailable { get; private set; }

    public int Count => _classes.Count;
    public long GeneratedOffers { get; private set; }
    public long GeneratedMints { get; private set; }
    public long DirectWitnessMatches { get; private set; }
    public long FormFarmAttempted { get; private set; }
    public long FormFarmAccepted { get; private set; }
    public long FormFarmRejected { get; private set; }
    public EmlEvaluatorInterval LastFormFarmEvaluation { get; private set; }
    internal EmlPredictionBoundRewriteCensus LastPredictionBoundRewriteCensus { get; private set; } = EmlPredictionBoundRewriteCensus.Empty;
    public IReadOnlyDictionary<EmlLawBehaviorCertificate, SemanticCASClass<EmlVerifiedLaw>> Classes => _classes.Classes;
    internal IReadOnlyList<EmlRung0Proof> Rung0Proofs => _rung0Proofs;
    internal IReadOnlyList<EmlRung0Audit> Rung0Audits => _rung0Audits;
    internal IReadOnlyList<EmlRung0RuleTransition> Rung0RuleTransitions => _rung0RuleTransitions;
    internal IReadOnlyList<EmlVerifiedLawSupportReceipt> VerifiedLawSupports => _verifiedLawSupports;
    internal IReadOnlyList<EmlPatternGrammarAdmissionReceipt> PatternGrammarAdmissions => _theoryGrammarAdmissions;
    internal IReadOnlyList<EmlPatternGrammarAdmissionEconomicsRecord> PatternGrammarAdmissionEconomics => _theoryGrammarAdmissionEconomics;
    internal bool HasPendingVerifiedLawSupports => _pendingVerifiedLawSupports > 0;

    internal void Clear()
    {
        _classes.Clear();
        _admissions.Clear();
        _admissionJournal.Clear();
        _rewriteSystem = null;
        _rewriteSearchRevision = 1;
        _rewriteSearchBudget = 16;
        _derivationDigest = 0;
        _derivationSteps.Clear();
        _rung0Proofs.Clear();
        _rung0ProofIndex.Clear();
        ClearRung0BasisArchive();
        _rung0Audits.Clear();
        _rung0AuditIndex.Clear();
        _rung0RuleTransitions.Clear();
        _quarantinedRung0Rules.Clear();
        _verifiedLawSupports.Clear();
        _pendingVerifiedLawSupports = 0;
        _verifiedLawSupportsByDigest.Clear();
        _verifiedLawSupportIndexByDigest.Clear();
        _validatedSupportStates.Clear();
        _verifiedLawSupportDigests.Clear();
        _verifiedLawAuthorities.Clear();
        _theoryGrammarAdmissions.Clear();
        _theoryGrammarAdmissionIndexByAuthorityDomain.Clear();
        _pendingPatternGrammarAdmissionIndices.Clear();
        _dirtyPatternGrammarAdmissionIndices.Clear();
        _theoryGrammarAdmissionEconomics.Clear();
        _theoryGrammarAdmissionEconomicsIndex.Clear();
        _persistedLawExecutions.Clear();
        _persistedLawExecutionIndexMark = 0;
        LegacyWorldSupportUnavailable = false;
        GeneratedOffers = 0;
        GeneratedMints = 0;
        DirectWitnessMatches = 0;
        FormFarmAttempted = 0;
        FormFarmAccepted = 0;
        FormFarmRejected = 0;
        LastFormFarmEvaluation = EmlEvaluatorInterval.EmptyAt(0);
        LastPredictionBoundRewriteCensus = EmlPredictionBoundRewriteCensus.Empty;
        _checkpointAdmissionCount = 0;
        _checkpointCompositionCount = 0;
        _checkpointRung0ProofCount = 0;
        _checkpointRung0AuditCount = 0;
        _checkpointRung0TransitionCount = 0;
        _checkpointVerifiedLawSupportCount = 0;
        _checkpointPatternGrammarAdmissionCount = 0;
        _checkpointPatternGrammarAdmissionEconomicsCount = 0;
        _checkpointClasses = null;
        _checkpointBasis = null;
        _checkpointQuarantine = null;
        _checkpointSupportStates = null;
        _checkpointAudits = null;
    }

    public void AppendVerifiedLaws(List<EmlVerifiedLaw> laws)
    {
        foreach (SemanticCASClass<EmlVerifiedLaw> lawClass in _classes.Values)
            laws.Add(lawClass.Rep);
        laws.Sort(CompareRepresentatives);
    }

    public EmlLawCandidateCensus MeasureCandidates(
        IReadOnlyList<EmlLawCandidate> candidates,
        int signatureDigits)
    {
        EmlRewriteSystem rewriteSystem = GetRewriteSystem();
        int verifiedCount = 0;
        int basisRepresentatives = 0;
        int behaviorSpan = 0;
        int directWitnessComposed = 0;
        int sampledJoinComposed = 0;
        int novelBehavior = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            EmlLawCandidate candidate = candidates[i];
            if (!EmlVerifiedLaw.TryVerify(candidate.Law, candidate.Support, signatureDigits,
                    out EmlVerifiedLaw? verified) || verified is null) continue;
            verifiedCount++;
            if (!_classes.Classes.TryGetValue(verified.Certificate, out SemanticCASClass<EmlVerifiedLaw> lawClass))
            {
                novelBehavior++;
                continue;
            }

            if (string.Equals(CreateAdmissionID(verified), CreateAdmissionID(lawClass.Rep), StringComparison.Ordinal))
            {
                basisRepresentatives++;
                continue;
            }

            behaviorSpan++;
            if (_classes.Count > 0)
            {
                MeasureProbeWitnessRelations(
                    verified,
                    rewriteSystem,
                    out bool directlyComposed,
                    out bool sampledJoin);
                if (directlyComposed) directWitnessComposed++;
                if (sampledJoin) sampledJoinComposed++;
            }
        }
        return new EmlLawCandidateCensus(
            verifiedCount,
            basisRepresentatives,
            behaviorSpan,
            directWitnessComposed,
            sampledJoinComposed,
            novelBehavior);
    }

    public void AppendCandidateRewrites(
        in EmlObligationResolution obligation,
        IReadOnlyCollection<string> knownAntecedents,
        List<EmlLawCandidateInstantiation> candidates,
        EmlDeliberationLease? deliberationLease = null)
    {
        List<EmlLawRewrite> rewrites = new();
        AppendRewrites(knownAntecedents, rewrites, deliberationLease);
        int firstCandidate = candidates.Count;
        for (int rewriteIndex = 0; rewriteIndex < rewrites.Count; rewriteIndex++)
        {
            deliberationLease?.ReserveCandidateSupplyItem();
            candidates.Add(new EmlLawCandidateInstantiation(obligation, rewrites[rewriteIndex]));
        }
        candidates.Sort(firstCandidate, candidates.Count - firstCandidate,
            Comparer<EmlLawCandidateInstantiation>.Create(CompareCandidateRewrites));
    }

    /// Enumerate only exact mint forms and bind each candidate to the same
    /// authoritative concrete carrier used to construct its guard witness.
    /// Probe-only canonical representatives never enter the rung-0 funnel.
    public void AppendPredictionBoundCandidateRewrites(
        in EmlObligationResolution obligation,
        EmlSieve sieve,
        List<EmlLawCandidateInstantiation> candidates,
        EmlDeliberationLease? deliberationLease = null)
    {
        ArgumentNullException.ThrowIfNull(sieve);
        List<EmlExactRPNForm> forms = new();
        // The exact claim's LHS is the proof antecedent and therefore the carrier's
        // custody key. Resolve that source directly; scanning every exact claim here
        // made each obligation pay the whole frontier cost again.
        if (sieve.TryReadExactRPNLhsForm(obligation.SourcePredictionID, out EmlExactRPNForm sourceForm))
            forms.Add(sourceForm);
        int firstCandidate = candidates.Count;
        int carrierBound = 0;
        int formsWithRewrites = 0;
        int rewriteCount = 0;
        int guardEligible = 0;
        int rankReducing = 0;
        int firstPredictionID = -1;
        string firstLawID = "";
        string firstRewriteID = "";
        string firstOrientation = "";
        string firstForm = "";
        string firstRulePattern = "";
        string firstMatchedTerm = "";
        string firstRewriteAntecedent = "";
        string firstRewriteConsequent = "";
        int firstReducingPredictionID = -1;
        string firstReducingLawID = "";
        string firstReducingRewriteID = "";
        string firstReducingOrientation = "";
        string firstReducingForm = "";
        string firstReducingAntecedent = "";
        string firstReducingConsequent = "";
        for (int formIndex = 0; formIndex < forms.Count && candidates.Count - firstCandidate < 512; formIndex++)
        {
            EmlExactRPNForm form = forms[formIndex];
            // The obligation is the source-claim custody key.  A form from a
            // different exact claim may share a certificate and still cannot
            // fund this obligation's closure receipt.
            if (form.PredictionID != obligation.SourcePredictionID) continue;
            if (!sieve.TryCreateRewriteCarrier(in form, out EmlRewritePredictionCarrier carrier)) continue;
            carrierBound++;
            EmlRewriteState state = carrier.CreateState(form.Program);
            List<EmlLawRewrite> rewrites = new();
            AppendRewritesForEvaluation(form.Program, rewrites, state.Evaluation, deliberationLease);
            if (rewrites.Count > 0)
            {
                formsWithRewrites++;
                if (LastPredictionBoundRewriteCensus.FirstForm.Length == 0 && firstForm.Length == 0)
                {
                    EmlLawRewrite firstRewrite = rewrites[0];
                    firstPredictionID = form.PredictionID.Value;
                    firstLawID = $"{firstRewrite.BasisLawDigest:X16}";
                    firstRewriteID = firstRewrite.RuleID.Value;
                    firstOrientation = firstRewrite.Orientation.ToString();
                    firstForm = form.Program;
                    firstRulePattern = firstRewrite.RulePattern;
                    firstMatchedTerm = firstRewrite.MatchedTermRpn;
                    firstRewriteAntecedent = firstRewrite.AntecedentRpn;
                    firstRewriteConsequent = firstRewrite.ConsequentRpn;
                }
            }
            for (int rewriteIndex = 0; rewriteIndex < rewrites.Count && candidates.Count - firstCandidate < 512; rewriteIndex++)
            {
                deliberationLease?.ReserveCandidateSupplyItem();
            candidates.Add(new EmlLawCandidateInstantiation(obligation, rewrites[rewriteIndex], carrier));
                rewriteCount++;
                EmlLawRewrite rewrite = rewrites[rewriteIndex];
                if (rewrite.IsRung0Eligible) guardEligible++;
                if (EmlRewriteSystem.ReducesRank(rewrite.AntecedentRpn, rewrite.ConsequentRpn))
                {
                    rankReducing++;
                    if (LastPredictionBoundRewriteCensus.FirstReducingForm.Length == 0 && firstReducingForm.Length == 0)
                    {
                        firstReducingPredictionID = form.PredictionID.Value;
                        firstReducingLawID = $"{rewrite.BasisLawDigest:X16}";
                        firstReducingRewriteID = rewrite.RuleID.Value;
                        firstReducingOrientation = rewrite.Orientation.ToString();
                        firstReducingForm = form.Program;
                        firstReducingAntecedent = rewrite.AntecedentRpn;
                        firstReducingConsequent = rewrite.ConsequentRpn;
                    }
                }
            }
        }
        EmlPredictionBoundRewriteCensus previous = LastPredictionBoundRewriteCensus;
        LastPredictionBoundRewriteCensus = new(
            previous.Calls + 1,
            previous.Forms + forms.Count,
            previous.CarrierBound + carrierBound,
            previous.FormsWithRewrites + formsWithRewrites,
            previous.Rewrites + rewriteCount,
            previous.GuardEligible + guardEligible,
            previous.RankReducing + rankReducing,
            Math.Max(previous.MaxForms, forms.Count),
            Math.Max(previous.MaxCarrierBound, carrierBound),
            Math.Max(previous.MaxFormsWithRewrites, formsWithRewrites),
            Math.Max(previous.MaxRewrites, rewriteCount),
            Math.Max(previous.MaxGuardEligible, guardEligible),
            Math.Max(previous.MaxRankReducing, rankReducing),
            previous.FirstForm.Length != 0 ? previous.FirstPredictionID : firstPredictionID,
            previous.FirstForm.Length != 0 ? previous.FirstLawID : firstLawID,
            previous.FirstForm.Length != 0 ? previous.FirstRewriteID : firstRewriteID,
            previous.FirstForm.Length != 0 ? previous.FirstOrientation : firstOrientation,
            previous.FirstForm.Length != 0 ? previous.FirstForm : firstForm,
            previous.FirstRulePattern.Length != 0 ? previous.FirstRulePattern : firstRulePattern,
            previous.FirstMatchedTerm.Length != 0 ? previous.FirstMatchedTerm : firstMatchedTerm,
            previous.FirstRewriteAntecedent.Length != 0 ? previous.FirstRewriteAntecedent : firstRewriteAntecedent,
            previous.FirstRewriteConsequent.Length != 0 ? previous.FirstRewriteConsequent : firstRewriteConsequent,
            previous.FirstReducingForm.Length != 0 ? previous.FirstReducingPredictionID : firstReducingPredictionID,
            previous.FirstReducingForm.Length != 0 ? previous.FirstReducingLawID : firstReducingLawID,
            previous.FirstReducingForm.Length != 0 ? previous.FirstReducingRewriteID : firstReducingRewriteID,
            previous.FirstReducingForm.Length != 0 ? previous.FirstReducingOrientation : firstReducingOrientation,
            previous.FirstReducingForm.Length != 0 ? previous.FirstReducingForm : firstReducingForm,
            previous.FirstReducingForm.Length != 0 ? previous.FirstReducingAntecedent : firstReducingAntecedent,
            previous.FirstReducingForm.Length != 0 ? previous.FirstReducingConsequent : firstReducingConsequent);
        candidates.Sort(firstCandidate, candidates.Count - firstCandidate,
            Comparer<EmlLawCandidateInstantiation>.Create(CompareCandidateRewrites));
    }

    internal void AppendExactPredictionBoundCandidateRewrites(
        in EmlExactCompositionObligation target,
        EmlSieve sieve,
        List<EmlLawCandidateInstantiation> candidates,
        EmlDeliberationLease? deliberationLease = null)
    {
        EmlObligationResolution address = new(
            target.SourcePredictionID, default, "exact-derivation", default, default, 0,
            target.Supports, target.MintEventID);
        List<EmlExactRPNForm> forms = new();
        if (sieve.TryReadExactRPNLhsForm(target.SourcePredictionID, out EmlExactRPNForm sourceForm))
            forms.Add(sourceForm);
        for (int formIndex = 0; formIndex < forms.Count && candidates.Count < 512; formIndex++)
        {
            EmlExactRPNForm form = forms[formIndex];
            if (form.PredictionID != target.SourcePredictionID || !sieve.TryCreateRewriteCarrier(in form, out EmlRewritePredictionCarrier carrier)) continue;
            EmlRewriteState state = carrier.CreateState(form.Program);
            List<EmlLawRewrite> rewrites = new();
            AppendRewritesForEvaluation(form.Program, rewrites, state.Evaluation, deliberationLease);
            for (int rewriteIndex = 0; rewriteIndex < rewrites.Count && candidates.Count < 512; rewriteIndex++)
            {
                EmlLawRewrite rewrite = rewrites[rewriteIndex];
                if (!rewrite.IsRung0Eligible || rewrite.IsRelationNull
                    || !EmlRewriteSystem.ReducesRank(rewrite.AntecedentRpn, rewrite.ConsequentRpn)) continue;
                deliberationLease?.ReserveCandidateSupplyItem();
                candidates.Add(new EmlLawCandidateInstantiation(address, rewrite, carrier,
                    EmlObligationTarget.ExactComposition(target.SourcePredictionID)));
            }
        }
        candidates.Sort(Comparer<EmlLawCandidateInstantiation>.Create(CompareCandidateRewrites));
    }

    /// Collect the deterministic cross-target law frontier used only as a
    /// relation-null donor pool. Donors remain ordinary guarded, rank-reducing
    /// orientations; the null constructor still owns the shape and divergence
    /// gates before any execution is funded.
    internal void AppendRelationNullDonorRewrites(
        EmlSieve sieve,
        List<EmlRelationNullDonor> donors,
        EmlDeliberationLease? deliberationLease = null)
    {
        ArgumentNullException.ThrowIfNull(sieve);
        IReadOnlyList<EmlExactCompositionObligation> targets = sieve.ExactCompositionObligations;
        for (int targetIndex = 0; targetIndex < targets.Count && donors.Count < 512; targetIndex++)
        {
            EmlExactCompositionObligation target = targets[targetIndex];
            if (!sieve.TryReadExactRPNLhsForm(target.SourcePredictionID, out EmlExactRPNForm form)) continue;
            if (!sieve.TryCreateRewriteCarrier(in form, out EmlRewritePredictionCarrier carrier)) continue;
            EmlRewriteState state = carrier.CreateState(form.Program);
            List<EmlLawRewrite> rewrites = new();
            AppendRewritesForEvaluation(form.Program, rewrites, state.Evaluation, deliberationLease);
            for (int rewriteIndex = 0; rewriteIndex < rewrites.Count && donors.Count < 512; rewriteIndex++)
            {
                EmlLawRewrite rewrite = rewrites[rewriteIndex];
                if (!rewrite.IsRung0Eligible || rewrite.IsRelationNull
                    || !EmlRewriteSystem.ReducesRank(rewrite.AntecedentRpn, rewrite.ConsequentRpn)) continue;
                string admissionID = rewrite.RulePattern + "\u0001"
                    + rewrite.LawProof.OccurrenceDigest.ToString("X16", System.Globalization.CultureInfo.InvariantCulture)
                    + "\u0001" + rewrite.LawProof.OccurrenceCheckPrediction;
                donors.Add(new EmlRelationNullDonor(
                    rewrite,
                    new EmlRelationNullDonorProvenance(form.PredictionID, sieve.ExactCompositionObligationIdentity(form.PredictionID),
                        target.Supports, [admissionID])));
            }
        }
        donors.Sort(static (left, right) => string.CompareOrdinal(
            string.Concat(left.Rewrite.RuleID.Value, "|", left.Rewrite.AntecedentRpn, "|", left.Rewrite.ConsequentRpn),
            string.Concat(right.Rewrite.RuleID.Value, "|", right.Rewrite.AntecedentRpn, "|", right.Rewrite.ConsequentRpn)));
    }

    internal bool HasPredictionBoundGuardedRankReducingRewrite(EmlPredictionID sourcePredictionID, EmlSieve sieve)
    {
        ArgumentNullException.ThrowIfNull(sieve);
        List<EmlExactRPNForm> forms = new();
        if (sieve.TryReadExactRPNLhsForm(sourcePredictionID, out EmlExactRPNForm sourceForm))
            forms.Add(sourceForm);
        for (int i = 0; i < forms.Count; i++)
        {
            EmlExactRPNForm form = forms[i];
            if (form.PredictionID != sourcePredictionID || !sieve.TryCreateRewriteCarrier(in form, out EmlRewritePredictionCarrier carrier)) continue;
            EmlRewriteState state = carrier.CreateState(form.Program);
            List<EmlLawRewrite> rewrites = new();
            AppendRewritesForEvaluation(form.Program, rewrites, state.Evaluation);
            for (int j = 0; j < rewrites.Count; j++)
            {
                EmlLawRewrite rewrite = rewrites[j];
                if (rewrite.IsRung0Eligible && !rewrite.IsRelationNull
                    && EmlRewriteSystem.ReducesRank(rewrite.AntecedentRpn, rewrite.ConsequentRpn)) return true;
            }
        }
        return false;
    }

    public void AppendRewrites(IReadOnlyCollection<string> knownAntecedents, List<EmlLawRewrite> rewrites, EmlDeliberationLease? deliberationLease = null)
        => GetRewriteSystem().AppendRewrites(knownAntecedents, rewrites, deliberationLease);

    internal void AppendRewritesForEvaluation(
        string antecedentRpn,
        List<EmlLawRewrite> rewrites,
        EmlTreeEvaluation enclosureCarrier,
        EmlDeliberationLease? deliberationLease = null)
        => GetRewriteSystem().AppendRewritesForEvaluation(antecedentRpn, rewrites, enclosureCarrier, deliberationLease);

    internal EmlRung0Result DeriveRung0(
        in EmlRewritePredictionCarrier carrier,
        string antecedentRPN,
        string consequentRPN,
        in EmlRung0Budget budget,
        EmlDeliberationLease? deliberationLease = null)
        => GetRewriteSystem().Derive(
            in carrier,
            antecedentRPN,
            consequentRPN,
            in budget,
            deliberationLease);

    internal EmlRung0NullExecution DeriveRung0Null(
        in EmlRewritePredictionCarrier carrier,
        string antecedentRPN,
        in EmlLawRewrite relationNull,
        in EmlRung0Budget budget,
        EmlDeliberationLease? deliberationLease = null)
        => GetRewriteSystem().Derive(
            in carrier,
            antecedentRPN,
            in relationNull,
            in budget,
            deliberationLease);

    public void RecordGeneration(int offers, int mints)
    {
        GeneratedOffers += offers;
        GeneratedMints += mints;
    }

    public void RecordFormFarm(in EmlFormFarmResult result)
    {
        FormFarmAttempted = checked(FormFarmAttempted + result.Attempted);
        FormFarmAccepted = checked(FormFarmAccepted + result.Accepted);
        FormFarmRejected = checked(FormFarmRejected + result.Rejected);
        LastFormFarmEvaluation = result.Evaluation;
    }

    public void AppendCandidateInstantiations(
        in EmlObligationResolution obligation,
        IReadOnlyCollection<string> knownAntecedents,
        List<EmlLawCandidateInstantiation> candidates,
        EmlDeliberationLease? deliberationLease = null)
        => AppendCandidateRewrites(in obligation, knownAntecedents, candidates, deliberationLease);

    public bool TryAdmit(
        EmlVerifiedLaw law,
        int captureIndex,
        out SemanticCASAdmission<EmlLawBehaviorCertificate, EmlVerifiedLaw> admission)
    {
        string admissionID = CreateAdmissionID(law);
        if (!_admissions.Add(admissionID))
        {
            admission = default;
            return false;
        }
        _admissionJournal.Add(admissionID);
        if (IsDirectlyComposedByProbeWitnesses(law)) DirectWitnessMatches++;
        admission = _classes.Admit(law.Certificate, law, captureIndex);
        if (admission.RepresentativeChanged) _rewriteSystem = null;
        return true;
    }

    internal EmlVerifiedLawSupportReceipt RecordVerifiedLawSupport(
        EmlVerifiedLaw law,
        in SemanticCASAdmission<EmlLawBehaviorCertificate, EmlVerifiedLaw> admission,
        IReadOnlyList<EmlLawPrediction> support,
        IReadOnlyDictionary<int, IReadOnlyList<TapeEventID>> sourcePredictionOpportunityEvents,
        IReadOnlyDictionary<int, EmlSourcePredictionAdmission> sourcePredictionAdmissions,
        IReadOnlyDictionary<int, string> sourcePredictionMintDigests,
        IReadOnlyDictionary<int, string> sourcePredictionMintLineDigests,
        IReadOnlyList<TapeEventID> worldOpportunityEventIDs,
        int captureStep,
        int captureIndex)
    {
        EmlVerifiedLawSupportReceipt receipt = EmlVerifiedLawSupportReceipt.Create(
            law, admission, support, sourcePredictionOpportunityEvents, sourcePredictionAdmissions, sourcePredictionMintDigests, sourcePredictionMintLineDigests, worldOpportunityEventIDs, captureStep, captureIndex);
        if (!_verifiedLawSupportDigests.Add(receipt.Digest))
            throw new InvalidDataException("verified-law support receipt was admitted twice");
        IndexVerifiedLawAuthority(receipt);
        _verifiedLawSupportIndexByDigest.Add(receipt.Digest, _verifiedLawSupports.Count);
        _verifiedLawSupports.Add(receipt);
        _verifiedLawSupportsByDigest.Add(receipt.Digest, receipt);
        if (receipt.HasWorldOpportunity && !receipt.Consumed) _pendingVerifiedLawSupports++;
        return receipt;
    }

    internal static bool HasPoweredSupportCustody(
        IReadOnlyList<EmlLawPrediction> support,
        IReadOnlyDictionary<int, IReadOnlyList<TapeEventID>> sourcePredictionOpportunityEvents,
        IReadOnlyDictionary<int, EmlSourcePredictionAdmission> sourcePredictionAdmissions,
        IReadOnlyDictionary<int, string> sourcePredictionMintDigests,
        IReadOnlyDictionary<int, string> sourcePredictionMintLineDigests)
    {
        for (int i = 0; i < support.Count; i++)
        {
            if (support[i].SourcePredictionID is not EmlPredictionID source)
                return false;
            if (!sourcePredictionOpportunityEvents.TryGetValue(source.Value, out IReadOnlyList<TapeEventID>? opportunities)
                || opportunities.Count == 0
                || opportunities.Any(static id => id.Value < 0)
                || !sourcePredictionAdmissions.TryGetValue(source.Value, out EmlSourcePredictionAdmission admission)
                || !admission.IsValid
                || !sourcePredictionMintDigests.TryGetValue(source.Value, out string? mintDigest)
                || mintDigest.Length != 64 || !mintDigest.All(Uri.IsHexDigit) || !string.Equals(mintDigest, mintDigest.ToLowerInvariant(), StringComparison.Ordinal)
                || !sourcePredictionMintLineDigests.TryGetValue(source.Value, out string? lineDigest)
                || lineDigest.Length != 64 || !lineDigest.All(Uri.IsHexDigit) || !string.Equals(lineDigest, lineDigest.ToLowerInvariant(), StringComparison.Ordinal))
                return false;
        }
        return support.Count > 0;
    }

    internal bool TryAdmitWithSupportCustody(
        EmlVerifiedLaw law,
        ref int nextCaptureIndex,
        IReadOnlyList<EmlLawPrediction> support,
        IReadOnlyDictionary<int, IReadOnlyList<TapeEventID>> sourcePredictionOpportunityEvents,
        IReadOnlyDictionary<int, EmlSourcePredictionAdmission> sourcePredictionAdmissions,
        IReadOnlyDictionary<int, string> sourcePredictionMintDigests,
        IReadOnlyDictionary<int, string> sourcePredictionMintLineDigests,
        IReadOnlyList<TapeEventID> worldOpportunityEventIDs,
        out SemanticCASAdmission<EmlLawBehaviorCertificate, EmlVerifiedLaw> admission)
    {
        admission = default;
        if (worldOpportunityEventIDs.Count > 0
            && !HasPoweredSupportCustody(support, sourcePredictionOpportunityEvents, sourcePredictionAdmissions, sourcePredictionMintDigests, sourcePredictionMintLineDigests))
            return false;
        int captureIndex = nextCaptureIndex;
        nextCaptureIndex = checked(nextCaptureIndex + 1);
        return TryAdmit(law, captureIndex, out admission);
    }

    internal EmlVerifiedLawSupportReceipt RecordVerifiedLawSupport(
        EmlVerifiedLaw law,
        in SemanticCASAdmission<EmlLawBehaviorCertificate, EmlVerifiedLaw> admission,
        IReadOnlyList<EmlLawPrediction> support,
        IReadOnlyDictionary<int, IReadOnlyList<TapeEventID>> sourcePredictionOpportunityEvents,
        IReadOnlyDictionary<int, TapeEventID> sourcePredictionMintEvents,
        IReadOnlyDictionary<int, string> sourcePredictionMintDigests,
        IReadOnlyDictionary<int, string> sourcePredictionMintLineDigests,
        IReadOnlyList<TapeEventID> worldOpportunityEventIDs,
        int captureStep,
        int captureIndex)
    {
        Dictionary<int, EmlSourcePredictionAdmission> admissions = sourcePredictionMintEvents.ToDictionary(
            static pair => pair.Key,
            static pair => new EmlSourcePredictionAdmission(EmlSourcePredictionAdmissionSpecies.MintPacket, pair.Value));
        return RecordVerifiedLawSupport(law, in admission, support, sourcePredictionOpportunityEvents, admissions,
            sourcePredictionMintDigests, sourcePredictionMintLineDigests, worldOpportunityEventIDs, captureStep, captureIndex);
    }

    private void IndexVerifiedLawAuthority(EmlVerifiedLawSupportReceipt support)
    {
        if (_verifiedLawAuthorities.TryGetValue(support.CandidateAdmissionID, out EmlVerifiedLaw? existing))
        {
            if (!MatchesVerifiedLawAuthority(existing, support.Candidate))
                throw new InvalidDataException("verified-law admission identity names divergent authorities");
            return;
        }
        _verifiedLawAuthorities.Add(support.CandidateAdmissionID, support.Candidate);
    }

    private bool TryResolveVerifiedLawAuthority(EmlVerifiedLawSupportReceipt support, out EmlVerifiedLaw authority)
    {
        if (_admissions.Contains(support.CanonicalAuthorityID)
            && _verifiedLawAuthorities.TryGetValue(support.CanonicalAuthorityID, out EmlVerifiedLaw? found)
            && string.Equals(CreateAdmissionID(found), support.CanonicalAuthorityID, StringComparison.Ordinal)
            && found.Certificate == support.Certificate)
        {
            authority = found;
            return true;
        }
        authority = null!;
        return false;
    }

    private static bool MatchesVerifiedLawAuthority(EmlVerifiedLaw left, EmlVerifiedLaw right)
    {
        EmlLawProof leftProof = left.Proof;
        EmlLawProof rightProof = right.Proof;
        return left.Law == right.Law
            && left.Certificate == right.Certificate
            && left.TemplateCostBits == right.TemplateCostBits
            && leftProof.OccurrenceDigest == rightProof.OccurrenceDigest
            && string.Equals(leftProof.AbsentFiller, rightProof.AbsentFiller, StringComparison.Ordinal)
            && string.Equals(leftProof.OccurrenceCheckPrediction, rightProof.OccurrenceCheckPrediction, StringComparison.Ordinal)
            && leftProof.VerifierVersion == rightProof.VerifierVersion
            && leftProof.AtOne == rightProof.AtOne
            && leftProof.AtX == rightProof.AtX
            && leftProof.AtY == rightProof.AtY
            && leftProof.AtAbsentFiller == rightProof.AtAbsentFiller
            && leftProof.DomainGuardDigest == rightProof.DomainGuardDigest
            && string.Equals(leftProof.DomainGuards?.Canonical(), rightProof.DomainGuards?.Canonical(), StringComparison.Ordinal)
            && string.Equals(leftProof.GuardWitness.Canonical(), rightProof.GuardWitness.Canonical(), StringComparison.Ordinal)
            && leftProof.SearchRevision == rightProof.SearchRevision
            && leftProof.SearchBudget == rightProof.SearchBudget
            && leftProof.CompositionDigest == rightProof.CompositionDigest
            && string.Equals(leftProof.GuardScheme, rightProof.GuardScheme, StringComparison.Ordinal);
    }

    internal void AppendPendingVerifiedLawSupports(
        List<(EmlVerifiedLaw Law, EmlVerifiedLawSupportReceipt Support)> pending)
    {
        for (int i = 0; i < _verifiedLawSupports.Count; i++)
        {
            EmlVerifiedLawSupportReceipt support = _verifiedLawSupports[i];
            if (support.Consumed || !support.HasWorldOpportunity) continue;
            if (!_classes.Contains(support.Certificate))
                throw new InvalidDataException("verified-law support receipt has no semantic class authority");
            if (!TryResolveVerifiedLawAuthority(support, out EmlVerifiedLaw authority))
                throw new InvalidDataException("verified-law support receipt canonical authority is not retained");
            pending.Add((authority, support));
        }
    }

    internal void MarkVerifiedLawSupportConsumed(EmlVerifiedLawSupportReceipt support)
    {
        if (!_verifiedLawSupportsByDigest.TryGetValue(support.Digest, out EmlVerifiedLawSupportReceipt? retained))
            throw new KeyNotFoundException("verified-law support receipt is not retained");
        if (retained.HasWorldOpportunity && !retained.Consumed) _pendingVerifiedLawSupports--;
        retained.MarkConsumed();
    }

    internal void BindVerifiedLawSupportExecution(
        EmlVerifiedLawSupportReceipt support,
        TapeEventID executionEventID,
        IReadOnlyList<int> generatedPredictionIDs)
    {
        if (!_verifiedLawSupportsByDigest.TryGetValue(support.Digest, out EmlVerifiedLawSupportReceipt? retained))
            throw new KeyNotFoundException("verified-law support receipt is not retained");
        retained.BindExecution(executionEventID, generatedPredictionIDs);
    }

    internal void BindVerifiedLawSupportPacket(EmlVerifiedLawSupportReceipt support, TapeEventID supportEventID)
    {
        if (!_verifiedLawSupportsByDigest.TryGetValue(support.Digest, out EmlVerifiedLawSupportReceipt? retained))
            throw new KeyNotFoundException("verified-law support receipt is not retained");
        retained.BindSupportPacket(supportEventID);
    }

    internal bool EnsurePatternGrammarAdmission(
        EmlVerifiedLaw law,
        EmlVerifiedLawSupportReceipt support,
        EmlSieve sieve,
        Tape tape,
        Journal journal,
        int step,
        GrammarRevisionID admissionRevision,
        out EmlPatternGrammarAdmissionReceipt? promotion,
        int wScale = 8)
    {
        promotion = null;
        if (law is null || support is null || admissionRevision == GrammarRevisionID.Zero)
            return false;
        string authority = support.CanonicalAuthorityID;
        EmlLawDomainID domain;
        try { domain = EmlLawDomainID.Derive(law.Law, law.Certificate, law.Proof, authority); }
        catch (ArgumentException) { return false; }
        string key = AdmissionKey(authority, domain);
        if (support.ExecutionEventID is not TapeEventID executionEvent
            || support.SupportEventID is not TapeEventID supportEvent
            || executionEvent.Value < 0 || supportEvent.Value < 0)
            return false;
        if (!TrySelectPatternGrammarPrediction(law, support, sieve, out EmlPatternGrammarGeneratedPrediction generatedPrediction))
            return false;
        if (!EmlPatternGrammarAdmissionAdmission.TryCreateFromVerifiedLaw(
            law, support, generatedPrediction, domain, admissionRevision,
            out EmlPatternGrammarAdmissionReceipt? admittedCandidate, out _)
            || admittedCandidate is null)
            return false;
        if (TryReusePatternGrammarAdmission(admittedCandidate, tape, journal, out promotion))
            return true;
        (Symbol[] RawTape, int N, RePairResult Result) induced = Engine.Induce(tape, wScale);
        byte[] rawWeights = tape.GrammarWeightsFor(wScale);
        EmlPatternGrammarAdmissionEconomicsReceipt economics;
        try
        {
            economics = EvaluatePatternGrammarAdmissionEconomics(
                authority, support.SupportSetDigest, support.CandidateAdmissionID, domain, generatedPrediction,
                in induced.Result, induced.RawTape.AsSpan(0, induced.N), rawWeights.AsSpan(0, induced.N), admissionRevision, wScale,
                tape, journal, step);
        }
        finally { System.Buffers.ArrayPool<byte>.Shared.Return(rawWeights); }
        // A typed marginal-MDL refusal is a terminal outcome for this exact
        // verified-law support, admission revision, and priced basis.  The
        // caller may consume that support after the receipt is durable; a
        // later grammar frontier is a new candidate, never silent repricing.
        if (!economics.MaterializationAdmitted)
            return true;
        if (!string.Equals(economics.AdmissionIdentityDigest, admittedCandidate.Digest, StringComparison.Ordinal))
            throw new InvalidDataException("theory-to-grammar economics promotion identity drifted");
        if (_theoryGrammarAdmissionIndexByAuthorityDomain.TryGetValue(key, out int existingIndex))
        {
            EmlPatternGrammarAdmissionReceipt retained = _theoryGrammarAdmissions[existingIndex];
            if (!string.Equals(retained.SupportSetDigest, support.SupportSetDigest, StringComparison.Ordinal)
                || !string.Equals(retained.AdmissionID, support.CandidateAdmissionID, StringComparison.Ordinal)
                || !string.Equals(retained.Digest, admittedCandidate.Digest, StringComparison.Ordinal))
                throw new InvalidDataException("theory-to-grammar promotion authority/domain was rebound");
            promotion = retained;
            return true;
        }
        promotion = RegisterPatternGrammarAdmission(admittedCandidate, tape, journal, step);
        return true;
    }

    /// Fixture-facing admission seam: exercise the same economics gate with a
    /// previously validated receipt when constructing a full law/support
    /// custody graph is outside the focused promotion fixture.
    internal bool EnsurePatternGrammarAdmission(
        EmlPatternGrammarAdmissionReceipt admittedCandidate,
        Tape tape,
        Journal journal,
        int step,
        out EmlPatternGrammarAdmissionReceipt? promotion,
        int wScale = 1)
    {
        promotion = null;
        admittedCandidate.Validate();
        if (TryReusePatternGrammarAdmission(admittedCandidate, tape, journal, out promotion))
            return true;
        (Symbol[] rawTape, int rawCount, RePairResult baseline) = Engine.Induce(tape, wScale);
        byte[] rawWeights = tape.GrammarWeightsFor(wScale);
        EmlPatternGrammarAdmissionEconomicsReceipt economics;
        try
        {
            economics = EvaluatePatternGrammarAdmissionEconomics(
                admittedCandidate.AuthorityID, admittedCandidate.SupportSetDigest, admittedCandidate.AdmissionID,
                admittedCandidate.Domain, admittedCandidate.GeneratedPrediction,
                in baseline, rawTape.AsSpan(0, rawCount), rawWeights.AsSpan(0, rawCount),
                admittedCandidate.AdmissionRevision, wScale, tape, journal, step);
        }
        finally { System.Buffers.ArrayPool<byte>.Shared.Return(rawWeights); }
        if (!economics.MaterializationAdmitted) return true;
        if (!string.Equals(economics.AdmissionIdentityDigest, admittedCandidate.Digest, StringComparison.Ordinal))
            throw new InvalidDataException("theory-to-grammar fixture promotion identity drifted");
        promotion = RegisterPatternGrammarAdmission(admittedCandidate, tape, journal, step);
        return true;
    }

    private bool TryReusePatternGrammarAdmission(
        EmlPatternGrammarAdmissionReceipt admittedCandidate,
        Tape tape,
        Journal journal,
        out EmlPatternGrammarAdmissionReceipt? promotion)
    {
        // A settled promotion may be revisited after later grammar folds; its original
        // economics receipt remains the admission authority, while the consumed receipt gains a new digest.
        promotion = null;
        string key = AdmissionKey(admittedCandidate.AuthorityID, admittedCandidate.Domain);
        if (!_theoryGrammarAdmissionIndexByAuthorityDomain.TryGetValue(key, out int existingIndex))
            return false;
        EmlPatternGrammarAdmissionReceipt retained = _theoryGrammarAdmissions[existingIndex];
        if (!string.Equals(retained.SupportSetDigest, admittedCandidate.SupportSetDigest, StringComparison.Ordinal)
            || !string.Equals(retained.AdmissionID, admittedCandidate.AdmissionID, StringComparison.Ordinal)
            || !string.Equals(retained.CandidatePackageDigest, admittedCandidate.CandidatePackageDigest, StringComparison.Ordinal)
            || !string.Equals(retained.CanonicalFiller, admittedCandidate.CanonicalFiller, StringComparison.Ordinal)
            || !retained.GeneratedPrediction.Equals(admittedCandidate.GeneratedPrediction)
            || retained.AdmissionRevision != admittedCandidate.AdmissionRevision
            || (!retained.Consumed && !string.Equals(retained.Digest, admittedCandidate.Digest, StringComparison.Ordinal)))
            throw new InvalidDataException("theory-to-grammar promotion authority/domain was rebound");
        EmlPatternGrammarAdmissionEconomicsRecord? economics = null;
        for (int index = 0; index < _theoryGrammarAdmissionEconomics.Count; index++)
        {
            EmlPatternGrammarAdmissionEconomicsRecord candidate = _theoryGrammarAdmissionEconomics[index];
            if (string.Equals(candidate.Receipt.AdmissionIdentityDigest, admittedCandidate.Digest, StringComparison.Ordinal))
            {
                if (economics is not null)
                    throw new InvalidDataException("theory-to-grammar economics identity was duplicated");
                economics = candidate;
            }
        }
        if (economics is null)
            throw new InvalidDataException("theory-to-grammar promotion has no economics admission");
        VerifyEconomicsTapeBinding(economics, tape, journal);
        promotion = retained;
        return true;
    }

    /// Price one exact law/claim candidate once and retain the decision as a
    /// durable side receipt.  Re-observing the same identity returns the exact
    /// prior bytes; a refusal never appends a grammar-input event.
    internal EmlPatternGrammarAdmissionEconomicsReceipt EvaluatePatternGrammarAdmissionEconomics(
        string authorityID,
        string supportSetDigest,
        string admissionID,
        EmlLawDomainID domain,
        EmlPatternGrammarGeneratedPrediction generatedPrediction,
        in RePairResult baseline,
        ReadOnlySpan<Symbol> rawTape,
        ReadOnlySpan<byte> rawWeights,
        GrammarRevisionID admissionRevision,
        int wScale,
        Tape tape,
        Journal journal,
        int step)
    {
        generatedPrediction.Validate();
        if (rawWeights.Length != rawTape.Length)
            throw new InvalidDataException("theory-to-grammar economics symbols and weights differ");
        string candidateSHA256 = Convert.ToHexStringLower(SHA256.HashData(generatedPrediction.CreateLinePayload()));
        EmlPatternGrammarAdmissionReceipt promotionIdentity = EmlPatternGrammarAdmissionReceipt.Create(
            domain, authorityID, authorityID, supportSetDigest, admissionID, candidateSHA256,
            generatedPrediction.LhsRPN, generatedPrediction, admissionRevision);
        string basisDigest = EmlPatternGrammarAdmissionEconomicsReceipt.ComputeBasisDigest(
            in baseline, rawTape, rawWeights, generatedPrediction.CreateLinePayload(), wScale);
        int existingIndex = -1;
        for (int index = 0; index < _theoryGrammarAdmissionEconomics.Count; index++)
        {
            EmlPatternGrammarAdmissionEconomicsReceipt candidate = _theoryGrammarAdmissionEconomics[index].Receipt;
            if (candidate.AuthorityID != authorityID || candidate.Domain != domain || candidate.SupportSetDigest != supportSetDigest
                || candidate.AdmissionID != admissionID || candidate.CandidateSHA256 != candidateSHA256
                || candidate.AdmissionRevision != admissionRevision || candidate.AdmissionIdentityDigest != promotionIdentity.Digest) continue;
            existingIndex = index;
            break;
        }
        if (existingIndex >= 0)
        {
            EmlPatternGrammarAdmissionEconomicsReceipt retained = _theoryGrammarAdmissionEconomics[existingIndex].Receipt;
            if (retained.WScale != wScale) throw new InvalidDataException("theory-to-grammar economics WScale was rebound");
            if (retained.PricingBasisDigest != basisDigest || retained.BaselineRuleCount != baseline.Rules.Length
                || retained.BaselineCompressedLength != baseline.Compressed.Length || retained.RawSymbolLength != rawTape.Length
                || retained.RawWeightLength != rawWeights.Length)
                throw new InvalidDataException("theory-to-grammar economics pricing basis was rebound");
            EmlPatternGrammarAdmissionEconomicsRecord persisted = _theoryGrammarAdmissionEconomics[existingIndex];
            VerifyEconomicsTapeBinding(persisted, tape, journal);
            return persisted.Receipt;
        }
        EmlPatternGrammarAdmissionEconomicsReceipt receipt = EmlPatternGrammarAdmissionEconomicsReceipt.CreateFromInduced(
            authorityID, supportSetDigest, admissionID, domain, generatedPrediction,
            in baseline, rawTape, rawWeights, admissionRevision, wScale);
        if (!_theoryGrammarAdmissionEconomicsIndex.TryAdd(receipt.IdentityKey, _theoryGrammarAdmissionEconomics.Count))
            throw new InvalidDataException("theory-to-grammar economics identity was admitted twice");
        EmlPatternGrammarAdmissionEconomicsRecord binding = EmitEconomicsTapeBinding(receipt, tape, journal, step);
        _theoryGrammarAdmissionEconomics.Add(binding);
        return receipt;
    }

    private static EmlPatternGrammarAdmissionEconomicsRecord VerifyEconomicsTapeBinding(
        EmlPatternGrammarAdmissionEconomicsRecord persisted,
        Tape tape,
        Journal journal)
    {
        EmlPatternGrammarAdmissionEconomicsReceipt receipt = persisted.Receipt;
        byte[] expected = receipt.Encode();
        if (persisted.EventID is not TapeEventID eventID)
            throw new InvalidDataException("theory-to-grammar economics tape binding has no event");
        int matches = 0;
        byte[]? boundPayload = null;
        foreach (TapeEventView view in tape.GetEventViews())
        {
            if (view.Source != "eml:theory-grammar-economics") continue;
            if (view.Roles != (TapeEventRoles.Measurement | TapeEventRoles.AuditOnly))
                throw new InvalidDataException("theory-to-grammar economics tape packet has invalid roles");
            if (!tape.Resolve(view.Id, out byte[] payload)
                || !TapePacketCreator.TryDecodeEmlPatternGrammarAdmissionEconomics(payload, out EmlPatternGrammarAdmissionEconomicsReceipt observed))
                throw new InvalidDataException("theory-to-grammar economics tape packet was malformed");
            if (observed.IdentityKey != receipt.IdentityKey) continue;
            if (++matches > 1) throw new InvalidDataException("theory-to-grammar economics tape packet was duplicated");
            if (view.Id != eventID || !payload.AsSpan().SequenceEqual(expected))
                throw new InvalidDataException("theory-to-grammar economics tape packet was mutated");
            boundPayload = payload;
        }
        if (matches != 1 || boundPayload is null)
            throw new InvalidDataException("theory-to-grammar economics tape binding was omitted");
        if (!string.Equals(persisted.PayloadSHA256, Convert.ToHexStringLower(SHA256.HashData(boundPayload)), StringComparison.Ordinal))
            throw new InvalidDataException("theory-to-grammar economics tape packet digest was mutated");
        if (persisted.JournalBinding is not JournalRowBinding binding)
            throw new InvalidDataException("theory-to-grammar economics journal binding was omitted");
        // Resident rows are checked in memory; shed rows resolve through the
        // mounted journal.log authority. Both paths must validate the exact
        // row rather than trusting a persisted digest alone.
        if (!journal.VerifyBinding(in binding))
            throw new InvalidDataException("theory-to-grammar economics journal row was mutated");
        persisted.Validate(boundPayload);
        return persisted;
    }

    private static EmlPatternGrammarAdmissionEconomicsRecord EmitEconomicsTapeBinding(
        EmlPatternGrammarAdmissionEconomicsReceipt receipt,
        Tape tape,
        Journal journal,
        int step)
    {
        TapeEventID eventID = TapePacketCreator.AppendEmlPatternGrammarAdmissionEconomics(tape, journal, step, receipt, out JournalRowBinding journalBinding);
        return EmlPatternGrammarAdmissionEconomicsRecord.Create(receipt, eventID, receipt.Encode(), in journalBinding);
    }


    private EmlPatternGrammarAdmissionReceipt RegisterPatternGrammarAdmission(
        EmlPatternGrammarAdmissionReceipt admitted, Tape tape, Journal journal, int step)
    {
        admitted.Validate();
        string key = AdmissionKey(admitted.AuthorityID, admitted.Domain);
        if (_theoryGrammarAdmissionIndexByAuthorityDomain.TryGetValue(key, out int existingIndex))
            return _theoryGrammarAdmissions[existingIndex];
        TapeEventID reflectedEvent = TapePacketCreator.AppendEmlPatternGrammarAdmission(tape, journal, step, admitted);
        EmlPatternGrammarAdmissionReceipt reflected = admitted.BindReflection(reflectedEvent);
        reflected.Validate();
        _theoryGrammarAdmissionIndexByAuthorityDomain.Add(key, _theoryGrammarAdmissions.Count);
        _theoryGrammarAdmissions.Add(reflected);
        AddPendingPatternGrammarAdmission(_theoryGrammarAdmissions.Count - 1);
        return reflected;
    }

    internal bool SettlePatternGrammarAdmissions(
        GrammarRevisionID consumedRevision,
        IReadOnlyList<TapeEventID> foldedAppends,
        Func<TapeEventID, bool> foldedPredicate,
        LoopLineageTurnstile? lineage,
        Tape tape,
        Journal journal,
        int step)
    {
        if (consumedRevision == GrammarRevisionID.Zero || _pendingPatternGrammarAdmissionIndices.Count == 0) return false;
        bool changed = false;
        for (int pendingIndex = _pendingPatternGrammarAdmissionIndices.Count - 1; pendingIndex >= 0; pendingIndex--)
        {
            int i = _pendingPatternGrammarAdmissionIndices[pendingIndex];
            EmlPatternGrammarAdmissionReceipt prior = _theoryGrammarAdmissions[i];
            if (prior.Consumed || prior.ReflectedTapeEventID is not TapeEventID || consumedRevision.Value <= prior.AdmissionRevision.Value)
                continue;
            TapeEventID reflectedEvent = prior.ReflectedTapeEventID.Value;
            if (!foldedAppends.Contains(reflectedEvent)
                || !foldedPredicate(reflectedEvent)
                || !tape.TryGetEventView(reflectedEvent, out TapeEventView reflectedView)
                || reflectedView.Provenance != Provenances.Reflected
                || reflectedView.Source != "eml:theory-grammar"
                || !tape.Resolve(reflectedEvent, out byte[] reflectedPayload)
                || !reflectedPayload.AsSpan().SequenceEqual(prior.GeneratedPrediction.CreateLinePayload()))
                continue;
            LoopLineageNodeID lineageNode = default;
            if (lineage is not null)
            {
                if (!TryFindAdmissionPredecessors(prior, lineage, tape, out LoopLineageNodeID lawNode, out LoopLineageNodeID supportNode))
                    throw new InvalidDataException("theory-to-grammar promotion cannot settle without law/support lineage predecessors");
                LoopLineageNodeID[] predecessors = [lawNode, supportNode];
                LoopLineageCausalID causal = LoopLineageCausalID.Merge(LoopLineageNodeSpecies.PatternGrammarAdmission, predecessors);
                if (!lineage.TryEmit(step, LoopLineageNodeSpecies.PatternGrammarAdmission, reflectedEvent,
                        consumedRevision, predecessors, causal))
                    throw new InvalidDataException("theory-to-grammar promotion lineage emission did not close");
                if (!lineage.TryGetNodeForEvent(reflectedEvent, out LoopLineageNode node))
                    throw new InvalidDataException("theory-to-grammar promotion lineage node did not persist");
                lineageNode = node.NodeID;
            }
            EmlPatternGrammarAdmissionReceipt settled = prior.BindConsumption(consumedRevision, reflectedEvent, lineageNode);
            settled.Validate();
            _theoryGrammarAdmissions[i] = settled;
            _dirtyPatternGrammarAdmissionIndices.Add(i);
            _pendingPatternGrammarAdmissionIndices.RemoveAt(pendingIndex);
            changed = true;
        }
        return changed;
    }

    private void AddPendingPatternGrammarAdmission(int index)
    {
        _pendingPatternGrammarAdmissionIndices.Add(index);
    }

    private static string AdmissionKey(string authority, EmlLawDomainID domain)
        => authority + "\u0001" + domain.Value;

    private bool TrySelectPatternGrammarPrediction(
        EmlVerifiedLaw law,
        EmlVerifiedLawSupportReceipt support,
        EmlSieve sieve,
        out EmlPatternGrammarGeneratedPrediction generatedPrediction)
    {
        generatedPrediction = default;
        if (support.ExecutionEventID is not TapeEventID executionEvent
            || support.SupportEventID is not TapeEventID supportEvent)
            return false;
        if (!EmlLawInstantiation.TryCreate(law.Law.Template, law.Proof.AbsentFiller, out EmlLawInstantiation canonicalInstance))
            return false;
        int[] candidates = support.GeneratedPredictionIDs.OrderBy(static id => id).ToArray();
        for (int i = 0; i < candidates.Length; i++)
        {
            int claimIndex = candidates[i];
            if ((uint)claimIndex >= (uint)sieve.MintLog.Count
                || !sieve.TryReadPredictionMintEvent(new EmlPredictionID(claimIndex), out TapeEventID claimEvent)
                || claimEvent != executionEvent)
                continue;
            EmlMint mint = sieve.MintLog[claimIndex];
            if (mint.Grade != 'E' || !EmlPrediction.TryParse(mint.Line, out EmlPrediction claim) || !claim.RhsRpn
                || !EmlRung0Digest.IsCanonicalRPN(claim.Lhs)
                || !EmlRung0Digest.IsCanonicalRPN(claim.Rhs)
                || !string.Equals(claim.Lhs, canonicalInstance.LeftRpn, StringComparison.Ordinal)
                || !string.Equals(claim.Rhs, canonicalInstance.RightRpn, StringComparison.Ordinal)
                || !EmlRewriteSystem.ReducesRank(claim.Lhs, claim.Rhs)
                || !HasPredictionBoundGuardedRankReducingRewrite(new EmlPredictionID(claimIndex), sieve))
                continue;
            generatedPrediction = EmlPatternGrammarGeneratedPrediction.Create(
                new EmlPredictionID(claimIndex), executionEvent, supportEvent, claim.Lhs, claim.Rhs);
            return true;
        }
        return false;
    }

    private bool TryFindAdmissionPredecessors(
        EmlPatternGrammarAdmissionReceipt promotion,
        LoopLineageTurnstile lineage,
        Tape tape,
        out LoopLineageNodeID lawNode,
        out LoopLineageNodeID supportNode)
    {
        lawNode = default;
        supportNode = default;
        foreach (LoopLineageEdgeReceipt edge in lineage.Receipts)
        {
            if (edge.Node.Species == LoopLineageNodeSpecies.VerifiedLaw
                && tape.Resolve(edge.Node.EventID, out byte[] lawPayload)
                && TapePacketCreator.TryReadEmlLawAdmissionID(lawPayload, out string authority)
                && string.Equals(authority, promotion.AuthorityID, StringComparison.Ordinal))
                lawNode = edge.Node.NodeID;
            if (edge.Node.Species == LoopLineageNodeSpecies.VerifiedLawSupport
                && edge.Node.EventID == promotion.GeneratedPrediction.SupportEventID)
                supportNode = edge.Node.NodeID;
        }
        return lawNode.IsValid && supportNode.IsValid;
    }

    /// Index persisted execution packets once over the tape append delta.  Law
    /// custody lookup is keyed by the immutable support digest + canonical
    /// authority pair; callers never rescan the whole tape for each receipt.
    internal void IndexPersistedLawExecutions(Tape tape)
    {
        if (_persistedLawExecutionIndexMark > tape.NextId)
        {
            _persistedLawExecutions.Clear();
            _persistedLawExecutionIndexMark = 0;
        }
        foreach (TapeEventView view in tape.EnumerateAppendedSince(_persistedLawExecutionIndexMark))
        {
            if (!string.Equals(view.Source, "eml:law-execution", StringComparison.Ordinal)
                || view.Provenance != Provenances.Reflected
                || !tape.Resolve(view.Id, out byte[] payload)
                || !TapePacketCreator.TryReadEmlLawExecutionSupports(payload,
                    out TapePacketCreator.EmlLawExecutionSupportPacket packet)) continue;
            for (int supportIndex = 0; supportIndex < packet.Digests.Count; supportIndex++)
            {
                LawExecutionKey key = new(packet.Digests[supportIndex], packet.Authorities[supportIndex]);
                if (!_persistedLawExecutions.TryAdd(key, new PersistedLawExecution(view.Id, view, packet)))
                    throw new InvalidDataException(
                        $"duplicate persisted EML law execution for support {key.Digest} and authority {key.Authority}");
            }
        }
        _persistedLawExecutionIndexMark = tape.NextId;
    }

    internal bool TryFindPersistedLawExecution(
        Tape tape,
        EmlVerifiedLawSupportReceipt support,
        out TapeEventID executionEventID,
        out IReadOnlyList<int> generatedPredictionIDs)
    {
        IndexPersistedLawExecutions(tape);
        executionEventID = default;
        generatedPredictionIDs = Array.Empty<int>();
        LawExecutionKey key = new(support.Digest, support.CanonicalAuthorityID);
        if (!_persistedLawExecutions.TryGetValue(key, out PersistedLawExecution execution)) return false;
        if (!tape.TryGetEventView(execution.EventID, out TapeEventView currentView)
            || currentView != execution.View) return false;
        TapePacketCreator.EmlLawExecutionSupportPacket packet = execution.Packet;
        if (!MatchesPersistedLawExecution(execution.View, in packet, support,
                out IReadOnlyList<int> candidateGeneratedPredictionIDs)) return false;
        executionEventID = execution.EventID;
        generatedPredictionIDs = candidateGeneratedPredictionIDs;
        return true;
    }

    internal bool ValidateVerifiedLawSupportCustody(EmlSieve sieve, Tape tape)
        => ValidateVerifiedLawSupportCustody(sieve, tape, digest: null);

    /// Validate one support's current custody after its packet or execution
    /// binding.  The full overload above remains the load/terminal certifier.
    internal bool ValidateVerifiedLawSupportCustody(
        EmlSieve sieve,
        Tape tape,
        EmlVerifiedLawSupportReceipt support)
    {
        if (!_verifiedLawSupportsByDigest.ContainsKey(support.Digest)) return false;
        if (_validatedSupportStates.TryGetValue(support.Digest, out SupportValidationState state)
            && state.Matches(support)) return true;
        if (!ValidateVerifiedLawSupportCustody(sieve, tape, support.Digest)) return false;
        _validatedSupportStates[support.Digest] = SupportValidationState.Capture(support);
        return true;
    }

    private bool ValidateVerifiedLawSupportCustody(EmlSieve sieve, Tape tape, string? digest)
    {
        if (LegacyWorldSupportUnavailable) return false;
        int targetStart = 0;
        int targetEnd = _verifiedLawSupports.Count;
        if (digest is not null)
        {
            if (!_verifiedLawSupportIndexByDigest.TryGetValue(digest, out targetStart)) return false;
            targetEnd = targetStart + 1;
        }
        for (int i = targetStart; i < targetEnd; i++)
        {
            EmlVerifiedLawSupportReceipt support = _verifiedLawSupports[i];
            if (digest is not null && !string.Equals(support.Digest, digest, StringComparison.Ordinal)) continue;
            bool Reject(string reason)
            {
                Trace.Cortex.Boundary("eml.law-custody",
                    $"support={i} digest={support.Digest} candidate={support.CandidateAdmissionID} reason={reason}");
                return false;
            }
            support.ValidateAfterLoad();
            if (support.HasWorldOpportunity && support.SupportEventID is not TapeEventID)
                return Reject("world-support-packet-missing");
            if (support.SupportEventID is TapeEventID supportEventOrder
                && support.ExecutionEventID is TapeEventID executionEventOrder
                && executionEventOrder.Value <= supportEventOrder.Value)
                return Reject("execution-precedes-support");
            if (support.SupportEventID is TapeEventID supportEvent)
            {
                if (!TryResolveTapeEvent(tape, supportEvent, "eml:law-support", out byte[] supportPayload)
                    || !TapePacketCreator.TryReadEmlLawSupport(supportPayload, out TapePacketCreator.EmlLawSupportPacket packet)
                    || !MatchesLawSupportPacket(support, in packet)) return Reject("support-packet-mismatch");
            }
            if (support.CandidateSupport.Count != support.SourcePredictionIDs.Count) return Reject("source-count-mismatch");
            if (!_admissions.Contains(support.CandidateAdmissionID)
                || !string.Equals(CreateAdmissionID(support.Candidate), support.CandidateAdmissionID, StringComparison.Ordinal)
                || support.Candidate.Certificate != support.Certificate
                || !_classes.Contains(support.Certificate)
                || !TryResolveVerifiedLawAuthority(support, out _))
                return Reject("canonical-authority-mismatch");
            for (int claimIndex = 0; claimIndex < support.SourcePredictionIDs.Count; claimIndex++)
            {
                EmlPredictionID claimID = new(support.SourcePredictionIDs[claimIndex]);
                EmlVerifiedLawSupportReceipt.SupportPrediction supportPrediction = support.CandidateSupport[claimIndex];
                if (supportPrediction.SourcePredictionID != claimID.Value
                    || !string.Equals(supportPrediction.Certificate, sieve.GetPredictionCertificate(claimID).Hex(), StringComparison.Ordinal)
                    || (uint)claimID.Value >= (uint)sieve.MintLog.Count
                    || !EmlPrediction.TryParse(sieve.MintLog[claimID.Value].Line, out EmlPrediction persistedPrediction)
                    || !string.Equals(supportPrediction.LeftRpn, persistedPrediction.Lhs, StringComparison.Ordinal)
                    || !string.Equals(supportPrediction.RightRpn, persistedPrediction.Rhs, StringComparison.Ordinal)) return Reject("source-claim-mismatch");
                EmlMint persistedMint = sieve.MintLog[claimID.Value];
                string mintMaterial = persistedMint.Line + "|" + persistedMint.Prog + "|"
                    + persistedMint.Sig.R1.ToString("X16", CultureInfo.InvariantCulture)
                    + persistedMint.Sig.I1.ToString("X16", CultureInfo.InvariantCulture)
                    + persistedMint.Sig.R2.ToString("X16", CultureInfo.InvariantCulture)
                    + persistedMint.Sig.I2.ToString("X16", CultureInfo.InvariantCulture)
                    + "|" + persistedMint.Grade + "|" + (persistedMint.Corrob ? "1" : "0");
                string claimMaterial = string.Join('|', claimID.Value, supportPrediction.Certificate, supportPrediction.LeftRpn, supportPrediction.RightRpn,
                    Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(mintMaterial))));
                if (!string.Equals(support.SourcePredictionDigests[claimIndex],
                        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(claimMaterial))), StringComparison.Ordinal)) return Reject("source-digest-mismatch");
                string mintLineDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(persistedMint.Line)));
                if (!string.Equals(support.SourcePredictionMintLineDigests[claimIndex], mintLineDigest, StringComparison.Ordinal)) return Reject("source-line-digest-mismatch");
                if (!sieve.TryReadPredictionAdmission(claimID, out EmlSourcePredictionAdmission admissionPath)
                    || support.SourcePredictionAdmissions[claimIndex] is not EmlSourcePredictionAdmission expectedAdmission)
                {
                    return Reject("source-admission-event-mismatch");
                }
                if (!TryGetTapeEventView(tape, admissionPath.EventID, out TapeEventView mintView)
                    || mintView.Provenance != Provenances.Reflected
                    || !tape.Resolve(admissionPath.EventID, out byte[] mintPayload))
                    return Reject("source-admission-event-mismatch");
                EmlSourcePredictionAdmission liveAdmission = mintView.Source == "eml:law-execution"
                    ? admissionPath with { Species = EmlSourcePredictionAdmissionSpecies.LawExecutionPacket }
                    : admissionPath;
                if (liveAdmission != expectedAdmission)
                    return Reject("source-admission-species-mismatch");
                admissionPath = liveAdmission;
                if ((uint)claimID.Value >= (uint)sieve.MintLog.Count) return Reject("source-claim-out-of-range");
                byte[] expectedMintPayload = Encoding.ASCII.GetBytes(sieve.MintLog[claimID.Value].Line);
                if (admissionPath.Species == EmlSourcePredictionAdmissionSpecies.MintPacket
                    && mintView.Source is ("eml" or "node0"))
                {
                    if (!mintPayload.AsSpan().SequenceEqual(expectedMintPayload)) return Reject("source-mint-payload-mismatch");
                }
                else if (admissionPath.Species == EmlSourcePredictionAdmissionSpecies.LawExecutionPacket
                    && mintView.Source == "eml:law-execution")
                {
                    if (!TapePacketCreator.TryReadEmlLawExecutionSupports(mintPayload,
                            out TapePacketCreator.EmlLawExecutionSupportPacket execution)
                        || !execution.PredictionIDs.Contains(claimID.Value)
                        || !execution.Ranges.Any(range =>
                            range.Start <= claimID.Value && range.Start + range.Count > claimID.Value)) return Reject("source-law-execution-range-mismatch");
                }
                else if (admissionPath.Species == EmlSourcePredictionAdmissionSpecies.Rung0CompositionPacket
                    && mintView.Source == "eml:rung0-derivation")
                {
                    if (!TapePacketCreator.TryReadEmlRung0Closure(mintPayload,
                            out TapePacketCreator.EmlRung0ClosurePacket derivation)
                        || derivation.Kind != "RUNG0-DERIVATION"
                        || derivation.ComposedPredictionID != claimID
                        || !string.Equals(derivation.RhsRPN, persistedPrediction.Lhs, StringComparison.Ordinal)
                        || !string.Equals(derivation.LhsRPN, persistedPrediction.Rhs, StringComparison.Ordinal))
                        return Reject("source-rung0-derivation-mismatch");
                }
                else return Reject("source-admission-species-mismatch");
                if ((uint)claimID.Value >= (uint)sieve.MintLog.Count
                    || sieve.MintLog[claimID.Value].Grade != 'E'
                    || !EmlPrediction.TryParse(sieve.MintLog[claimID.Value].Line, out EmlPrediction sourcePrediction)
                    || !sourcePrediction.RhsRpn) return Reject("source-exactness-mismatch");
                if (!sieve.TryReadMintOpportunityEvents(claimID, out IReadOnlyList<TapeEventID> opportunities)
                    || !opportunities.SequenceEqual(support.SourcePredictionOpportunityEvents[claimIndex])) return Reject("source-opportunity-mismatch");
                for (int eventIndex = 0; eventIndex < opportunities.Count; eventIndex++)
                    if (!tape.Resolve(opportunities[eventIndex], out _)) return Reject("source-opportunity-unresolved");
            }
            List<EmlLawPrediction> liveSupport = new(support.CandidateSupport.Count);
            for (int claimIndex = 0; claimIndex < support.CandidateSupport.Count; claimIndex++)
            {
                EmlVerifiedLawSupportReceipt.SupportPrediction persisted = support.CandidateSupport[claimIndex];
                EmlPredictionID claimID = new(persisted.SourcePredictionID);
                EmlCert liveCertificate = sieve.GetPredictionCertificate(claimID);
                liveSupport.Add(new EmlLawPrediction(liveCertificate, persisted.LeftRpn, persisted.RightRpn, claimID));
            }
            if (!EmlVerifiedLaw.TryVerify(support.Candidate.Law, liveSupport, sieve.SignatureDigits, out EmlVerifiedLaw? recomputed)
                || recomputed is null
                || recomputed.Certificate != support.Candidate.Certificate
                || recomputed.Proof.OccurrenceDigest != support.Candidate.Proof.OccurrenceDigest
                || !string.Equals(CreateAdmissionID(recomputed), support.CandidateAdmissionID, StringComparison.Ordinal)) return Reject("candidate-reverification-mismatch");
            for (int eventIndex = 0; eventIndex < support.WorldOpportunityEventIDs.Count; eventIndex++)
                if (!TryResolveWorldOpportunity(tape, support.WorldOpportunityEventIDs[eventIndex])) return Reject("world-opportunity-unresolved");
            if (support.ExecutionEventID is TapeEventID executionEvent)
            {
                if (support.SourcePredictionAdmissions.Any(admission =>
                        admission is EmlSourcePredictionAdmission sourceAdmission && sourceAdmission.EventID.Value >= executionEvent.Value)) return Reject("source-admission-after-execution");
                if (!TryResolveTapeEvent(tape, executionEvent, "eml:law-execution", out byte[] executionPayload)
                    || !TapePacketCreator.TryReadEmlLawExecutionSupports(executionPayload,
                        out TapePacketCreator.EmlLawExecutionSupportPacket execution)) return Reject("execution-packet-unresolved");
                if (!TryGetTapeEventView(tape, executionEvent, out TapeEventView executionView)
                    || !MatchesPersistedLawExecution(executionView, in execution, support)) return Reject("execution-packet-mismatch");
                if (support.GeneratedPredictionIDs.Count == 0) return Reject("generated-claims-missing");
                for (int generatedIndex = 0; generatedIndex < support.GeneratedPredictionIDs.Count; generatedIndex++)
                {
                    int generatedPredictionID = support.GeneratedPredictionIDs[generatedIndex];
                    bool packetBound = execution.PredictionIDs.Contains(generatedPredictionID);
                    bool eventBound = sieve.TryReadPredictionMintEvent(new EmlPredictionID(generatedPredictionID), out TapeEventID generatedMintEvent);
                    bool inRange = (uint)generatedPredictionID < (uint)sieve.MintLog.Count;
                    EmlMint generatedMint = inRange ? sieve.MintLog[generatedPredictionID] : default;
                    EmlPrediction generatedPrediction = default;
                    bool parsed = inRange && EmlPrediction.TryParse(generatedMint.Line, out generatedPrediction);
                    if (!packetBound || !eventBound || generatedMintEvent != executionEvent || !inRange
                        || generatedMint.Grade != 'E' || !parsed || !generatedPrediction.RhsRpn)
                        return Reject($"generated-claim-mismatch claim={generatedPredictionID} packet={packetBound} event={eventBound}:{generatedMintEvent.Value} expected={executionEvent.Value} range={inRange} grade={generatedMint.Grade} parsed={parsed} rhs={generatedPrediction.RhsRpn}");
                }
                bool rangeFound = false;
                foreach ((string rangeDigest, int start, int count) in execution.Ranges)
                {
                    if (!string.Equals(rangeDigest, support.Digest, StringComparison.Ordinal)) continue;
                    rangeFound = true;
                    for (int claimOffset = 0; claimOffset < count; claimOffset++)
                    {
                        int claimIndex = start + claimOffset;
                        if (!execution.PredictionIDs.Contains(claimIndex)
                            || !sieve.TryReadPredictionMintEvent(new EmlPredictionID(claimIndex), out TapeEventID claimEvent)
                            || claimEvent != executionEvent) return Reject("generated-range-mismatch");
                    }
                }
                if (!rangeFound) return Reject("support-range-missing");
            }
        }
        return true;
    }

    private static bool TryResolveTapeEvent(Tape tape, TapeEventID eventID, out byte[] payload)
    {
        payload = Array.Empty<byte>();
        return tape.TryGetEventView(eventID, out _) && tape.Resolve(eventID, out payload);
    }

    private static bool TryGetTapeEventView(Tape tape, TapeEventID eventID, out TapeEventView view)
        => tape.TryGetEventView(eventID, out view);

    private static bool TryResolveWorldOpportunity(Tape tape, TapeEventID corpusEventID)
    {
        if (!TryGetTapeEventView(tape, corpusEventID, out TapeEventView corpusView)
            || !string.Equals(corpusView.Source, "corpus", StringComparison.Ordinal)
            || corpusView.Provenance != Provenances.Real
            || !tape.Resolve(corpusEventID, out _)) return false;
        if (corpusEventID.Value <= 0
            || !TryGetTapeEventView(tape, new TapeEventID(corpusEventID.Value - 1), out TapeEventView receiptView)
            || !string.Equals(receiptView.Source, "world:encounter", StringComparison.Ordinal)
            || receiptView.Provenance != Provenances.Execution
            || !tape.Resolve(receiptView.Id, out byte[] payload)
            || !TapePacketCreator.TryReadWorldEncounterObservation(payload, out TapeEventID observationID)) return false;
        return observationID == corpusEventID;
    }

    private static bool TryResolveTapeEvent(Tape tape, TapeEventID eventID, string expectedSource, out byte[] payload)
    {
        payload = Array.Empty<byte>();
        return tape.TryGetEventView(eventID, out TapeEventView view)
            && string.Equals(view.Source, expectedSource, StringComparison.Ordinal)
            && view.Provenance == Provenances.Reflected
            && tape.Resolve(eventID, out payload);
    }

    private static bool MatchesLawSupportPacket(
        EmlVerifiedLawSupportReceipt support,
        in TapePacketCreator.EmlLawSupportPacket packet)
    {
        if (!string.Equals(packet.CandidateAdmissionID, support.CandidateAdmissionID, StringComparison.Ordinal)
            || !string.Equals(packet.CanonicalAuthorityID, support.CanonicalAuthorityID, StringComparison.Ordinal)
            || !string.Equals(packet.Certificate, support.Certificate.ToString(), StringComparison.Ordinal)
            || !string.Equals(packet.CandidatePackageDigest, support.CandidatePackageDigest, StringComparison.Ordinal)
            || !packet.SourcePredictionIDs.SequenceEqual(support.SourcePredictionIDs)
            || !packet.SourcePredictionDigests.SequenceEqual(support.SourcePredictionDigests)
            || !packet.SourcePredictionMintLineDigests.SequenceEqual(support.SourcePredictionMintLineDigests)
            || !packet.SourcePredictionAdmissions.SequenceEqual(support.SourcePredictionAdmissions)
            || !packet.WorldOpportunityEventIDs.SequenceEqual(support.WorldOpportunityEventIDs)
            || !string.Equals(packet.SupportSetDigest, support.SupportSetDigest, StringComparison.Ordinal)
            || packet.CaptureStep != support.CaptureStep
            || packet.CaptureIndex != support.CaptureIndex
            || packet.FirstCapture != support.FirstCapture
            || packet.RepresentativeChanged != support.RepresentativeChanged
            || !string.Equals(packet.Digest, support.Digest, StringComparison.Ordinal)
            || packet.SourcePredictionOpportunityEvents.Count != support.SourcePredictionOpportunityEvents.Count) return false;
        for (int i = 0; i < support.SourcePredictionOpportunityEvents.Count; i++)
            if (!packet.SourcePredictionOpportunityEvents[i].SequenceEqual(support.SourcePredictionOpportunityEvents[i])) return false;
        return true;
    }

    public string Report()
    {
        List<SemanticCASClass<EmlVerifiedLaw>> rows = new(_classes.Values);
        rows.Sort(static (left, right) => CompareRepresentatives(left.Rep, right.Rep));
        StringBuilder report = new();
        report.Append("form_farm\tattempted=").Append(FormFarmAttempted)
            .Append("\taccepted=").Append(FormFarmAccepted)
            .Append("\trejected=").Append(FormFarmRejected)
            .Append("\tevaluator_start=").Append(LastFormFarmEvaluation.Start)
            .Append("\tevaluator_end=").Append(LastFormFarmEvaluation.End).AppendLine();
        report.Append("verified_law_support\tcount=").Append(_verifiedLawSupports.Count)
            .Append("\tpending=").Append(_verifiedLawSupports.Count(static support => !support.Consumed && support.HasWorldOpportunity))
            .Append("\tempty=").Append(_verifiedLawSupports.Count(static support => !support.HasWorldOpportunity))
            .Append("\tlegacy_unavailable=").Append(LegacyWorldSupportUnavailable ? 1 : 0).AppendLine();
        report.Append("template\tbehavior_at_one\tbehavior_at_x\tbehavior_at_y\tmembers\ttemplate_cost_bits\tverification_claim\tsupport_digest\n");
        for (int i = 0; i < rows.Count; i++)
        {
            SemanticCASClass<EmlVerifiedLaw> row = rows[i];
            EmlVerifiedLaw law = row.Rep;
            report.Append(law.Law.Template).Append('\t')
                .Append(FormatSignature(law.Certificate.AtOne)).Append('\t')
                .Append(FormatSignature(law.Certificate.AtX)).Append('\t')
                .Append(FormatSignature(law.Certificate.AtY)).Append('\t')
                .Append(row.Members).Append('\t').Append(law.TemplateCostBits).Append('\t')
                .Append(law.Proof.OccurrenceCheckPrediction).Append('\t')
                .Append(law.Proof.OccurrenceDigest.ToString("X16")).AppendLine();
        }
        return report.ToString();
    }

    public string ReportProofQueue()
    {
        List<SemanticCASClass<EmlVerifiedLaw>> rows = new(_classes.Values);
        rows.Sort(static (left, right) => CompareRepresentatives(left.Rep, right.Rep));
        StringBuilder report = new("status\tproof_scope\texecution_authority\tglobal_domain\tverifier_version\ttemplate\tsupport_digest\tdomain_guard_digest\tguarded\tabsent_filler\tverification_claim\tbehavior_at_one\tbehavior_at_x\tbehavior_at_y\tone_evidence\tx_evidence\ty_evidence\tabsent_evidence\n");
        for (int i = 0; i < rows.Count; i++)
        {
            EmlVerifiedLaw law = rows[i].Rep;
            report.Append("numeric-certified\tprobe-witness\tproposal-only\tunproven\t")
                .Append(law.Proof.VerifierVersion).Append('\t')
                .Append(law.Law.Template).Append('\t').Append(law.Proof.OccurrenceDigest.ToString("X16")).Append('\t')
                .Append(law.Proof.DomainGuardDigest.ToString("X16")).Append('\t').Append(law.Proof.IsGuarded ? 1 : 0).Append('\t')
                .Append(law.Proof.AbsentFiller).Append('\t').Append(law.Proof.OccurrenceCheckPrediction).Append('\t')
                .Append(FormatSignature(law.Certificate.AtOne)).Append('\t')
                .Append(FormatSignature(law.Certificate.AtX)).Append('\t')
                .Append(FormatSignature(law.Certificate.AtY)).Append('\t')
                .Append(FormatEvidence(law.Proof.AtOne)).Append('\t')
                .Append(FormatEvidence(law.Proof.AtX)).Append('\t')
                .Append(FormatEvidence(law.Proof.AtY)).Append('\t')
                .Append(FormatEvidence(law.Proof.AtAbsentFiller)).AppendLine();
        }
        return report.ToString();
    }

    public void Save(CkptWriter writer)
    {
        writer.I32(CheckpointSchema);
        List<KeyValuePair<EmlLawBehaviorCertificate, SemanticCASClass<EmlVerifiedLaw>>> ordered = new(_classes.Classes);
        ordered.Sort(static (left, right) => CompareCertificates(left.Key, right.Key));
        writer.I32(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
        {
            KeyValuePair<EmlLawBehaviorCertificate, SemanticCASClass<EmlVerifiedLaw>> row = ordered[i];
            WriteCertificate(writer, row.Key);
            writer.I32(row.Value.Members);
            writer.I32(row.Value.FirstCapture);
            row.Value.Rep.Save(writer);
        }
        List<string> admissions = new(_admissions);
        admissions.Sort(StringComparer.Ordinal);
        writer.I32(admissions.Count);
        for (int i = 0; i < admissions.Count; i++) writer.Str(admissions[i]);
        writer.I64(GeneratedOffers);
        writer.I64(GeneratedMints);
        writer.I64(DirectWitnessMatches);
        writer.I64(FormFarmAttempted);
        writer.I64(FormFarmAccepted);
        writer.I64(FormFarmRejected);
        writer.I64(LastFormFarmEvaluation.Start);
        writer.I64(LastFormFarmEvaluation.End);
        writer.I32(_rung0BasisArchive.Count);
        foreach (EmlVerifiedLaw basis in _rung0BasisArchive.Values)
        {
            WriteCertificate(writer, basis.Certificate);
            basis.Save(writer);
        }
        EmlCompositionSearch search = GetRewriteSystem().GetCompositionSearch();
        writer.I32(search.Revision);
        writer.I32(search.Budget);
        writer.U64(search.Digest);
        writer.I32(_derivationSteps.Count);
        for (int i = 0; i < _derivationSteps.Count; i++) WriteCompositionStep(writer, _derivationSteps[i]);
        writer.I32(_rung0Proofs.Count);
        for (int i = 0; i < _rung0Proofs.Count; i++) WriteRung0Proof(writer, _rung0Proofs[i]);
        writer.I32(_rung0Audits.Count);
        for (int i = 0; i < _rung0Audits.Count; i++) WriteRung0Audit(writer, _rung0Audits[i]);
        writer.I32(_rung0RuleTransitions.Count);
        for (int i = 0; i < _rung0RuleTransitions.Count; i++) WriteRung0RuleTransition(writer, _rung0RuleTransitions[i]);
        writer.I32(_verifiedLawSupports.Count);
        for (int i = 0; i < _verifiedLawSupports.Count; i++) WriteVerifiedLawSupport(writer, _verifiedLawSupports[i]);
        writer.I32(_theoryGrammarAdmissions.Count);
        for (int i = 0; i < _theoryGrammarAdmissions.Count; i++) writer.Bytes(_theoryGrammarAdmissions[i].Encode());
        writer.I32(_theoryGrammarAdmissionEconomics.Count);
        for (int i = 0; i < _theoryGrammarAdmissionEconomics.Count; i++) writer.Bytes(_theoryGrammarAdmissionEconomics[i].Encode());
    }

    public void Load(CkptReader reader)
    {
        int schema = reader.I32();
        if (schema is < 1 or > CheckpointSchema)
            throw new InvalidDataException($"EML law store checkpoint schema {schema} is not supported");
        Clear();
        LegacyWorldSupportUnavailable = schema < CheckpointSchema;
        int count = reader.I32();
        if (count < 0) throw new InvalidDataException("EML law store checkpoint has a negative class count");
        for (int i = 0; i < count; i++)
        {
            EmlLawBehaviorCertificate certificate = ReadCertificate(reader);
            int members = reader.I32();
            int firstCapture = reader.I32();
            EmlVerifiedLaw representative = EmlVerifiedLaw.LoadVerified(reader, schema >= 6, schema >= 8, schema >= NodeFactsCheckpointSchema);
            if (members <= 0 || representative.Certificate != certificate)
                throw new InvalidDataException("EML law store checkpoint carries an invalid semantic class");
            _classes.Admit(certificate, representative, firstCapture);
            for (int member = 1; member < members; member++)
                _classes.Admit(certificate, representative, firstCapture);
        }
        int admissionCount = reader.I32();
        if (admissionCount < 0) throw new InvalidDataException("EML law store checkpoint has a negative admission count");
        for (int i = 0; i < admissionCount; i++)
        {
            string admissionID = reader.Str();
            if (!_admissions.Add(admissionID)) throw new InvalidDataException("EML law store checkpoint repeats an admission identity");
            _admissionJournal.Add(admissionID);
        }
        GeneratedOffers = schema >= 2 ? reader.I64() : 0;
        GeneratedMints = schema >= 2 ? reader.I64() : 0;
        if (schema >= 6)
        {
            DirectWitnessMatches = reader.I64();
        }
        else if (schema >= 3)
        {
            // Schema 3/4 called this counter ComposedCandidates. It counted rejected candidates,
            // not direct witness matches, so consume the field but do not relabel its history.
            _ = reader.I64();
        }
        if (schema >= LegacyCheckpointSchema)
        {
            FormFarmAttempted = reader.I64();
            FormFarmAccepted = reader.I64();
            FormFarmRejected = reader.I64();
            LastFormFarmEvaluation = new EmlEvaluatorInterval(reader.I64(), reader.I64());
        }
        if (schema >= Rung0BasisArchiveSchema)
        {
            int archiveCount = reader.I32();
            if (archiveCount < 0 || archiveCount > MaxRung0BasisArchiveEntries)
                throw new InvalidDataException("EML law checkpoint carries an invalid rung-0 basis archive count");
            for (int i = 0; i < archiveCount; i++)
            {
                EmlLawBehaviorCertificate certificate = schema >= Rung0BasisCertificateSchema
                    ? ReadCertificate(reader)
                    : default;
                EmlVerifiedLaw basis = EmlVerifiedLaw.LoadVerified(
                    reader,
                    hasGuardSchema: true,
                    hasWitnessContext: true,
                    hasNodeFacts: schema >= NodeFactsCheckpointSchema);
                if (schema >= Rung0BasisCertificateSchema && basis.Certificate != certificate)
                    throw new InvalidDataException("EML law checkpoint rung-0 basis certificate disagrees with its archive key");
                string admissionID = CreateAdmissionID(basis);
                if (!AddRung0BasisArchive(admissionID, basis))
                    throw new InvalidDataException("EML law checkpoint repeats a rung-0 basis archive entry");
            }
        }
        if (schema >= 6)
        {
            _rewriteSearchRevision = reader.I32();
            _rewriteSearchBudget = reader.I32();
            _derivationDigest = reader.U64();
            int derivationCount = reader.I32();
            if (_rewriteSearchRevision < 1 || _rewriteSearchBudget < 1 || derivationCount < 0 || derivationCount > 1024)
                throw new InvalidDataException("EML law checkpoint carries an invalid derivation journal");
            for (int i = 0; i < derivationCount; i++)
            {
                EmlCompositionStep step = ReadCompositionStep(reader, legacy: schema < 8, hasNodeFacts: schema >= NodeFactsCheckpointSchema);
                if (!TryValidateCompositionStep(step))
                    throw new InvalidDataException("EML law checkpoint carries an invalid derivation step");
                _derivationSteps.Add(step);
            }
            if (EmlCompositionDigest.Calculate(_rewriteSearchRevision, _rewriteSearchBudget, _derivationSteps) != _derivationDigest)
                throw new InvalidDataException("EML law checkpoint derivation digest mismatch");
        }
        if (schema >= Rung0CheckpointSchema)
        {
            int proofCount = reader.I32();
            if (proofCount < 0 || proofCount > 4096)
                throw new InvalidDataException("EML law checkpoint carries an invalid rung-0 proof count");
            for (int i = 0; i < proofCount; i++)
            {
                EmlRung0Proof proof = ReadRung0Proof(reader, hasNodeFacts: schema >= NodeFactsCheckpointSchema);
                ValidateRung0Proof(in proof);
                AppendRung0Proof(in proof);
            }
            if (schema >= Rung0BasisArchiveSchema) ValidateRung0BasisArchive();
            else
                for (int proofIndex = 0; proofIndex < _rung0Proofs.Count; proofIndex++)
                {
                    EmlRung0Proof proof = _rung0Proofs[proofIndex];
                    CaptureRung0Basis(in proof);
                }
            int auditCount = reader.I32();
            if (auditCount < 0 || auditCount > proofCount)
                throw new InvalidDataException("EML law checkpoint carries an invalid rung-0 audit count");
            for (int i = 0; i < auditCount; i++)
            {
                EmlRung0Audit audit = ReadRung0Audit(reader, hasSelection: schema >= 16);
                ValidateRung0Audit(in audit);
                AppendRung0Audit(in audit);
            }
            int transitionCount = reader.I32();
            if (transitionCount < 0 || transitionCount > proofCount * 32)
                throw new InvalidDataException("EML law checkpoint carries an invalid rung-0 transition count");
            for (int i = 0; i < transitionCount; i++)
            {
                EmlRung0RuleTransition transition = ReadRung0RuleTransition(reader);
                if (transition.Sequence != i || transition.RuleID.IsEmpty || transition.ProofDigest == 0
                    || transition.EvaluatorCalls <= 0)
                    throw new InvalidDataException("EML law checkpoint carries an invalid rung-0 authority transition");
                ApplyRung0RuleTransition(in transition);
            }
        }
        if (schema >= 13)
        {
            int supportCount = reader.I32();
            if (supportCount < 0 || supportCount > MaxRung0BasisArchiveEntries)
                throw new InvalidDataException("EML law checkpoint carries an invalid verified-law support count");
            if (schema < CheckpointSchema)
            {
                if (supportCount != 0)
                    throw new InvalidDataException("legacy verified-law support lacks per-claim mint line custody");
            }
            else
            for (int i = 0; i < supportCount; i++)
            {
                EmlVerifiedLawSupportReceipt support = ReadVerifiedLawSupport(reader, schema >= 13, schema >= 15);
                support.ValidateAfterLoad();
                if (!_verifiedLawSupportDigests.Add(support.Digest))
                    throw new InvalidDataException("EML law checkpoint repeats a verified-law support receipt");
                IndexVerifiedLawAuthority(support);
                _verifiedLawSupportIndexByDigest.Add(support.Digest, _verifiedLawSupports.Count);
                _verifiedLawSupports.Add(support);
                _verifiedLawSupportsByDigest.Add(support.Digest, support);
                if (support.HasWorldOpportunity && !support.Consumed) _pendingVerifiedLawSupports++;
            }
        }
        if (schema >= 17)
        {
            int promotionCount = reader.I32();
            if (promotionCount < 0 || promotionCount > MaxRung0BasisArchiveEntries)
                throw new InvalidDataException("EML law checkpoint carries an invalid theory-grammar promotion count");
            for (int i = 0; i < promotionCount; i++)
            {
                EmlPatternGrammarAdmissionReceipt promotion = EmlPatternGrammarAdmissionReceipt.Decode(reader.Bytes(1 << 20));
                string key = AdmissionKey(promotion.AuthorityID, promotion.Domain);
                if (!_theoryGrammarAdmissionIndexByAuthorityDomain.TryAdd(key, _theoryGrammarAdmissions.Count))
                    throw new InvalidDataException("EML law checkpoint repeats a theory-grammar promotion authority/domain");
                _theoryGrammarAdmissions.Add(promotion);
                if (!promotion.Consumed) AddPendingPatternGrammarAdmission(_theoryGrammarAdmissions.Count - 1);
            }
        }
        if (schema >= 18)
        {
            int economicsCount = reader.I32();
            if (economicsCount < 0 || economicsCount > MaxRung0BasisArchiveEntries)
                throw new InvalidDataException("EML law checkpoint carries an invalid theory-grammar economics count");
            for (int i = 0; i < economicsCount; i++)
            {
                EmlPatternGrammarAdmissionEconomicsRecord receipt = EmlPatternGrammarAdmissionEconomicsRecord.Decode(reader.Bytes(1 << 20));
                if (!_theoryGrammarAdmissionEconomicsIndex.TryAdd(receipt.IdentityKey, _theoryGrammarAdmissionEconomics.Count))
                    throw new InvalidDataException("EML law checkpoint repeats a theory-grammar economics identity");
                _theoryGrammarAdmissionEconomics.Add(receipt);
            }
        }
        if (FormFarmAttempted != FormFarmAccepted + FormFarmRejected
            || LastFormFarmEvaluation.End < LastFormFarmEvaluation.Start)
            throw new InvalidDataException("EML law store checkpoint carries invalid form-farm accounting");
        _checkpointAdmissionCount = _admissionJournal.Count;
        _checkpointCompositionCount = _derivationSteps.Count;
        _checkpointRung0ProofCount = _rung0Proofs.Count;
        _checkpointRung0AuditCount = _rung0Audits.Count;
        _checkpointRung0TransitionCount = _rung0RuleTransitions.Count;
        _checkpointVerifiedLawSupportCount = _verifiedLawSupports.Count;
        _checkpointPatternGrammarAdmissionCount = _theoryGrammarAdmissions.Count;
        _checkpointPatternGrammarAdmissionEconomicsCount = _theoryGrammarAdmissionEconomics.Count;
        CaptureCheckpointBaseline();
    }

    private static int CompareRepresentatives(EmlVerifiedLaw left, EmlVerifiedLaw right)
    {
        int byCost = left.TemplateCostBits.CompareTo(right.TemplateCostBits);
        if (byCost != 0) return byCost;
        int byTemplate = string.CompareOrdinal(left.Law.Template, right.Law.Template);
        if (byTemplate != 0) return byTemplate;
        int byPrediction = string.CompareOrdinal(left.Proof.OccurrenceCheckPrediction, right.Proof.OccurrenceCheckPrediction);
        return byPrediction != 0 ? byPrediction : left.Proof.OccurrenceDigest.CompareTo(right.Proof.OccurrenceDigest);
    }

    private bool IsDirectlyComposedByProbeWitnesses(EmlVerifiedLaw candidate)
    {
        if (_classes.Count == 0) return false;
        return IsDirectlyComposedByProbeWitnesses(candidate, GetRewriteSystem());
    }

    private static bool IsDirectlyComposedByProbeWitnesses(
        EmlVerifiedLaw candidate,
        EmlRewriteSystem rewriteSystem)
    {
        string[] fillers = ["1", "x", "y", candidate.Proof.AbsentFiller];
        for (int i = 0; i < fillers.Length; i++)
        {
            if (!EmlLawInstantiation.TryCreate(candidate.Law.Template, fillers[i], out EmlLawInstantiation instance))
                return false;
            if (string.Equals(instance.LeftRpn, instance.RightRpn, StringComparison.Ordinal)) continue;
            if (!rewriteSystem.HasDirectReduction(instance.LeftRpn, instance.RightRpn)) return false;
        }
        return true;
    }

    private static void MeasureProbeWitnessRelations(
        EmlVerifiedLaw candidate,
        EmlRewriteSystem rewriteSystem,
        out bool directlyComposed,
        out bool sampledJoin)
    {
        directlyComposed = true;
        sampledJoin = true;
        string[] fillers = ["1", "x", "y", candidate.Proof.AbsentFiller];
        for (int i = 0; i < fillers.Length; i++)
        {
            if (!EmlLawInstantiation.TryCreate(candidate.Law.Template, fillers[i], out EmlLawInstantiation instance))
            {
                directlyComposed = false;
                sampledJoin = false;
                return;
            }
            if (string.Equals(instance.LeftRpn, instance.RightRpn, StringComparison.Ordinal)) continue;
            if (directlyComposed && !rewriteSystem.HasDirectReduction(instance.LeftRpn, instance.RightRpn))
                directlyComposed = false;
            if (sampledJoin && !rewriteSystem.MeasureSampledJoin(instance.LeftRpn, instance.RightRpn).Joined)
                sampledJoin = false;
            if (!directlyComposed && !sampledJoin) return;
        }
    }

    public string ReportRewriteSystem()
    {
        EmlPredictionBoundRewriteCensus census = LastPredictionBoundRewriteCensus;
        return new StringBuilder(GetRewriteSystem().Report())
            .Append("claim_bound_calls\t").Append(census.Calls).AppendLine()
            .Append("claim_bound_forms\t").Append(census.Forms).AppendLine()
            .Append("claim_bound_carrier_bound\t").Append(census.CarrierBound).AppendLine()
            .Append("claim_bound_forms_with_rewrites\t").Append(census.FormsWithRewrites).AppendLine()
            .Append("claim_bound_rewrites\t").Append(census.Rewrites).AppendLine()
            .Append("claim_bound_guard_eligible\t").Append(census.GuardEligible).AppendLine()
            .Append("claim_bound_rank_reducing\t").Append(census.RankReducing).AppendLine()
            .Append("claim_bound_max_forms\t").Append(census.MaxForms).AppendLine()
            .Append("claim_bound_max_carrier_bound\t").Append(census.MaxCarrierBound).AppendLine()
            .Append("claim_bound_max_forms_with_rewrites\t").Append(census.MaxFormsWithRewrites).AppendLine()
            .Append("claim_bound_max_rewrites\t").Append(census.MaxRewrites).AppendLine()
            .Append("claim_bound_max_guard_eligible\t").Append(census.MaxGuardEligible).AppendLine()
            .Append("claim_bound_max_rank_reducing\t").Append(census.MaxRankReducing).AppendLine()
            .Append("claim_bound_first_claim_id\t").Append(census.FirstPredictionID).AppendLine()
            .Append("claim_bound_first_law_id\t").Append(census.FirstLawID).AppendLine()
            .Append("claim_bound_first_rewrite_id\t").Append(census.FirstRewriteID).AppendLine()
            .Append("claim_bound_first_orientation\t").Append(census.FirstOrientation).AppendLine()
            .Append("claim_bound_first_form\t").Append(census.FirstForm).AppendLine()
            .Append("claim_bound_first_rule_pattern\t").Append(census.FirstRulePattern).AppendLine()
            .Append("claim_bound_first_matched_term\t").Append(census.FirstMatchedTerm).AppendLine()
            .Append("claim_bound_first_rewrite_antecedent\t").Append(census.FirstRewriteAntecedent).AppendLine()
            .Append("claim_bound_first_rewrite_consequent\t").Append(census.FirstRewriteConsequent).AppendLine()
            .Append("claim_bound_first_reducing_claim_id\t").Append(census.FirstReducingPredictionID).AppendLine()
            .Append("claim_bound_first_reducing_law_id\t").Append(census.FirstReducingLawID).AppendLine()
            .Append("claim_bound_first_reducing_rewrite_id\t").Append(census.FirstReducingRewriteID).AppendLine()
            .Append("claim_bound_first_reducing_orientation\t").Append(census.FirstReducingOrientation).AppendLine()
            .Append("claim_bound_first_reducing_form\t").Append(census.FirstReducingForm).AppendLine()
            .Append("claim_bound_first_reducing_antecedent\t").Append(census.FirstReducingAntecedent).AppendLine()
            .Append("claim_bound_first_reducing_consequent\t").Append(census.FirstReducingConsequent).AppendLine()
            .ToString();
    }

    internal int RewriteSearchRevision => _rewriteSearchRevision;
    internal int RewriteSearchBudget => _rewriteSearchBudget;
    internal ulong CompositionDigest => _derivationDigest;

    internal void RecordComposition(in EmlCompositionSearch search)
    {
        _rewriteSearchRevision = search.Revision;
        _rewriteSearchBudget = search.Budget;
        _derivationDigest = search.Digest;
        _derivationSteps.Clear();
        _derivationSteps.AddRange(search.Steps);
    }

    internal bool IsRung0RuleQuarantined(EmlRuleID ruleID) => _quarantinedRung0Rules.Contains(ruleID);

    internal void RecordRung0Proof(in EmlRung0Proof proof)
    {
        ValidateRung0Proof(in proof);
        if (_rung0ProofIndex.ContainsKey(proof.Digest)) return;
        CaptureRung0Basis(in proof);
        AppendRung0Proof(in proof);
    }

    internal void RecordRung0Audit(in EmlRung0Audit audit)
    {
        ValidateRung0Audit(in audit);
        if (_rung0AuditIndex.TryGetValue(audit.ProofDigest, out int existing))
        {
            EmlRung0Audit prior = _rung0Audits[existing];
            if (prior.Status != audit.Status
                || prior.EvaluatorCalls != audit.EvaluatorCalls
                || prior.NumericVerified != audit.NumericVerified
                || prior.GuardVerified != audit.GuardVerified
                || prior.Selection != audit.Selection
                || !prior.Rules.SequenceEqual(audit.Rules))
                throw new InvalidOperationException("rung-0 proof was audited twice with different evidence");
            return;
        }
        AppendRung0Audit(in audit);
        ApplyRung0AuditQuarantine(in audit);
    }

    internal void PromoteRung0Audit(in EmlRung0Audit promoted)
    {
        ValidateRung0Audit(in promoted);
        if (promoted.Selection != EmlRung0AuditSelectionSpecies.MinimumOne)
            throw new InvalidOperationException("rung-0 audit promotion must select MinimumOne");
        if (_rung0AuditIndex.TryGetValue(promoted.ProofDigest, out int i))
        {
            EmlRung0Audit prior = _rung0Audits[i];
            if (prior.Status != EmlRung0AuditStatuses.NotSelected
                || prior.Selection != EmlRung0AuditSelectionSpecies.DigestCadence
                || prior.GuardVerified != promoted.GuardVerified
                || !prior.Rules.SequenceEqual(promoted.Rules))
                throw new InvalidOperationException("rung-0 audit mutation is not the retained NotSelected promotion");
            _rung0Audits[i] = promoted;
            ApplyRung0AuditQuarantine(in promoted);
            return;
        }
        throw new InvalidOperationException("rung-0 audit promotion names no retained audit");
    }

    private void ApplyRung0AuditQuarantine(in EmlRung0Audit audit)
    {
        if (audit.Status != EmlRung0AuditStatuses.Disagreed) return;
        List<EmlRuleID> rules = new(audit.Rules);
        rules.Sort(static (left, right) => string.CompareOrdinal(left.Value, right.Value));
        for (int i = 0; i < rules.Count; i++)
        {
            if (i > 0 && rules[i] == rules[i - 1]) continue;
            EmlRung0RuleTransition transition = new(
                _rung0RuleTransitions.Count,
                rules[i],
                EmlRung0RuleTransitionKinds.Quarantined,
                audit.ProofDigest,
                audit.NumericVerified,
                audit.GuardVerified,
                audit.EvaluatorCalls);
            ApplyRung0RuleTransition(in transition);
        }
    }

    internal bool TryGetRung0Audit(ulong proofDigest, out EmlRung0Audit audit)
    {
        if (_rung0AuditIndex.TryGetValue(proofDigest, out int i))
        {
            audit = _rung0Audits[i];
            return true;
        }
        audit = default;
        return false;
    }

    internal bool TryRepromoteRung0Rule(
        EmlRuleID ruleID,
        ulong proofDigest,
        in EmlRewritePredictionCarrier carrier,
        EmlGrader grader)
    {
        ArgumentNullException.ThrowIfNull(grader);
        if (!_quarantinedRung0Rules.Contains(ruleID)) return false;
        EmlRung0Proof? proof = _rung0ProofIndex.TryGetValue(proofDigest, out int proofSlot) ? _rung0Proofs[proofSlot] : null;
        if (proof is null) return false;
        if (carrier.PredictionID != proof.Value.PredictionID
            || !string.Equals(carrier.SourceDigest, proof.Value.SourceDigest, StringComparison.Ordinal)) return false;
        bool carriesRule = false;
        bool guardVerified = false;
        for (int i = 0; i < proof.Value.Steps.Count; i++)
            if (proof.Value.Steps[i].RuleID == ruleID && TryValidateCompositionStep(proof.Value.Steps[i]))
            {
                carriesRule = true;
                EmlCompositionStep step = proof.Value.Steps[i];
                EmlRewriteState current = carrier.CreateState(step.AntecedentRpn);
                List<EmlLawRewrite> fresh = new();
                AppendRewritesForEvaluation(step.AntecedentRpn, fresh, current.Evaluation);
                for (int rewriteIndex = 0; rewriteIndex < fresh.Count; rewriteIndex++)
                {
                    EmlLawRewrite rewrite = fresh[rewriteIndex];
                    if (rewrite.IsRung0Eligible
                        && !rewrite.IsRelationNull
                        && rewrite.RuleID == ruleID
                        && rewrite.MatchedPath == step.Path
                        && string.Equals(rewrite.SubstitutionRpn, step.SubstitutionRpn, StringComparison.Ordinal)
                        && string.Equals(rewrite.ConsequentRpn, step.ConsequentRpn, StringComparison.Ordinal))
                    { guardVerified = true; break; }
                }
                if (guardVerified) break;
            }
        if (!carriesRule || !guardVerified) return false;
        EmlEvaluatorClock clock = grader.Clock;
        long start = clock.ProgramPointEvaluations;
        bool numericVerified = grader.GradeRpn(proof.Value.AntecedentRPN, proof.Value.ConsequentRPN).Grade == 'E';
        long evaluatorCalls = clock.ProgramPointEvaluations - start;
        if (!numericVerified || !guardVerified || evaluatorCalls <= 0) return false;
        EmlRung0RuleTransition transition = new(
            _rung0RuleTransitions.Count,
            ruleID,
            EmlRung0RuleTransitionKinds.Repromoted,
            proofDigest,
            numericVerified,
            guardVerified,
            evaluatorCalls);
        ApplyRung0RuleTransition(in transition);
        return true;
    }

    private void ApplyRung0RuleTransition(in EmlRung0RuleTransition transition)
    {
        if (transition.Kind == EmlRung0RuleTransitionKinds.Quarantined)
        {
            if (transition.EvaluatorCalls <= 0)
                throw new InvalidDataException("rung-0 quarantine lacks numeric audit work");
            bool supported = false;
            for (int i = 0; i < _rung0Audits.Count; i++)
            {
                EmlRung0Audit audit = _rung0Audits[i];
                if (audit.ProofDigest != transition.ProofDigest
                    || audit.Status != EmlRung0AuditStatuses.Disagreed) continue;
                for (int rule = 0; rule < audit.Rules.Count; rule++)
                    if (audit.Rules[rule] == transition.RuleID) { supported = true; break; }
                if (supported) break;
            }
            if (!supported) throw new InvalidDataException("rung-0 quarantine lacks its disagreement audit");
            _quarantinedRung0Rules.Add(transition.RuleID);
        }
        else if (transition.Kind == EmlRung0RuleTransitionKinds.Repromoted)
        {
            if (!transition.NumericVerified || !transition.GuardVerified
                || transition.EvaluatorCalls <= 0
                || !Rung0ProofCarriesRule(transition.ProofDigest, transition.RuleID)
                || !_quarantinedRung0Rules.Remove(transition.RuleID))
                throw new InvalidDataException("rung-0 re-promotion lacks a fresh numeric and guard verification");
        }
        else throw new InvalidDataException("unknown rung-0 authority transition");
        _rung0RuleTransitions.Add(transition);
    }

    private bool Rung0ProofCarriesRule(ulong proofDigest, EmlRuleID ruleID)
    {
        if (!_rung0ProofIndex.TryGetValue(proofDigest, out int proofSlot)) return false;
        EmlRung0Proof proof = _rung0Proofs[proofSlot];
        for (int stepIndex = 0; stepIndex < proof.Steps.Count; stepIndex++)
            if (proof.Steps[stepIndex].RuleID == ruleID) return true;
        return false;
    }

    internal bool VerifyRung0ProofGuards(in EmlRung0Proof proof)
    {
        try { ValidateRung0Proof(in proof); }
        catch (InvalidDataException) { return false; }
        return true;
    }

    private EmlRewriteSystem GetRewriteSystem() => _rewriteSystem ??= new EmlRewriteSystem(this);

    private void ValidateRung0Proof(in EmlRung0Proof proof)
    {
        try { proof.Budget.Validate(); }
        catch (ArgumentOutOfRangeException exception)
        { throw new InvalidDataException("rung-0 proof carries an invalid search budget", exception); }
        if (!proof.IsValidShape
            || proof.Work.ExpandedStates > proof.Budget.MaxStates
            || proof.Work.VisitedStates > proof.Budget.MaxStates
            || proof.Work.Applications > proof.Budget.MaxApplications
            || proof.Steps.Count > proof.Budget.MaxDepth)
            throw new InvalidDataException(
                "rung-0 proof carries an invalid claim or work shape: "
                + $"valid={proof.IsValidShape}, claim={proof.PredictionID.Value}, source={proof.SourceDigest.Length}, "
                + $"steps={proof.Steps.Count}/{proof.Budget.MaxDepth}, visited={proof.Work.VisitedStates}/{proof.Budget.MaxStates}, "
                + $"applications={proof.Work.Applications}/{proof.Budget.MaxApplications}, expanded={proof.Work.ExpandedStates}, "
                + $"guards={proof.Work.GuardRejections}, portable={EmlRung0Digest.HasPortableStepChain(in proof)}, "
                + $"portable-detail={EmlRung0Digest.DescribeNonPortableStepChain(in proof)}, "
                + $"digest-match={EmlRung0Digest.Calculate(proof with { Digest = 0 }) == proof.Digest}, digest={proof.Digest:X16}");
        string expectedAntecedent = proof.AntecedentRPN;
        for (int i = 0; i < proof.Steps.Count; i++)
        {
            EmlCompositionStep step = proof.Steps[i];
            if (!string.Equals(step.AntecedentRpn, expectedAntecedent, StringComparison.Ordinal)
                || !TryValidateCompositionStep(in step))
                throw new InvalidDataException("rung-0 proof carries a broken guarded step chain");
            expectedAntecedent = step.ConsequentRpn;
        }
        if (!string.Equals(expectedAntecedent, proof.ConsequentRPN, StringComparison.Ordinal))
            throw new InvalidDataException("rung-0 proof does not reach its claimed consequent");
    }

    private void CaptureRung0Basis(in EmlRung0Proof proof)
    {
        for (int i = 0; i < proof.Steps.Count; i++)
        {
            EmlCompositionStep step = proof.Steps[i];
            if (!TryFindRung0Basis(in step, out EmlVerifiedLaw basis))
                throw new InvalidDataException("rung-0 proof step has no retained verified law basis");
            string admissionID = CreateAdmissionID(basis);
            if (_rung0BasisArchive.ContainsKey(admissionID)) continue;
            if (_rung0BasisArchive.Count >= MaxRung0BasisArchiveEntries)
                throw new InvalidDataException("rung-0 proof basis archive exceeds its checkpoint enclosure");
            AddRung0BasisArchive(admissionID, basis);
        }
    }

    private readonly record struct Rung0BasisKey(ulong OccurrenceDigest, string Template);

    private static Rung0BasisKey Rung0KeyOf(EmlVerifiedLaw law)
        => new(law.Proof.OccurrenceDigest, law.Law.Template);

    private bool AddRung0BasisArchive(string admissionID, EmlVerifiedLaw basis)
    {
        if (!_rung0BasisArchive.TryAdd(admissionID, basis)) return false;
        Rung0BasisKey key = Rung0KeyOf(basis);
        if (!_rung0BasisArchiveIndex.TryGetValue(key, out List<EmlVerifiedLaw>? bucket))
            _rung0BasisArchiveIndex[key] = bucket = new List<EmlVerifiedLaw>(1);
        bucket.Add(basis);
        return true;
    }

    private void RemoveRung0BasisArchive(string admissionID)
    {
        if (!_rung0BasisArchive.Remove(admissionID, out EmlVerifiedLaw? basis)) return;
        Rung0BasisKey key = Rung0KeyOf(basis);
        if (!_rung0BasisArchiveIndex.TryGetValue(key, out List<EmlVerifiedLaw>? bucket)) return;
        bucket.Remove(basis);
        if (bucket.Count == 0) _rung0BasisArchiveIndex.Remove(key);
    }

    private void ClearRung0BasisArchive()
    {
        _rung0BasisArchive.Clear();
        _rung0BasisArchiveIndex.Clear();
    }

    private void AppendRung0Proof(in EmlRung0Proof proof)
    {
        _rung0ProofIndex[proof.Digest] = _rung0Proofs.Count;
        _rung0Proofs.Add(proof);
    }

    private void AppendRung0Audit(in EmlRung0Audit audit)
    {
        _rung0AuditIndex[audit.ProofDigest] = _rung0Audits.Count;
        _rung0Audits.Add(audit);
    }

    private void ValidateRung0BasisArchive()
    {
        HashSet<string> referenced = new(StringComparer.Ordinal);
        for (int proofIndex = 0; proofIndex < _rung0Proofs.Count; proofIndex++)
        {
            EmlRung0Proof proof = _rung0Proofs[proofIndex];
            for (int stepIndex = 0; stepIndex < proof.Steps.Count; stepIndex++)
            {
                EmlCompositionStep step = proof.Steps[stepIndex];
                if (!TryFindArchivedRung0Basis(in step, out EmlVerifiedLaw basis))
                    throw new InvalidDataException("EML law checkpoint is missing a retained rung-0 basis archive entry");
                referenced.Add(CreateAdmissionID(basis));
            }
        }
        foreach (string admissionID in _rung0BasisArchive.Keys)
            if (!referenced.Contains(admissionID))
                throw new InvalidDataException("EML law checkpoint carries an unreferenced rung-0 basis archive entry");
    }

    private void ValidateRung0Audit(in EmlRung0Audit audit)
    {
        EmlRung0Proof? proof = _rung0ProofIndex.TryGetValue(audit.ProofDigest, out int proofSlot) ? _rung0Proofs[proofSlot] : null;
        if (proof is null || audit.EvaluatorCalls < 0 || audit.Rules.Count == 0)
            throw new InvalidDataException("rung-0 audit does not address one retained proof");
        bool selected = EmlRung0Digest.SelectNumericAudit(audit.ProofDigest);
        if (!Enum.IsDefined(audit.Selection))
            throw new InvalidDataException("rung-0 audit carries an unknown selection species");
        selected |= audit.Selection == EmlRung0AuditSelectionSpecies.MinimumOne;
        bool validStatus = audit.Status switch
        {
            EmlRung0AuditStatuses.NotSelected => !selected && audit.EvaluatorCalls == 0
                && !audit.NumericVerified && audit.GuardVerified,
            EmlRung0AuditStatuses.Agreed => selected && audit.EvaluatorCalls > 0
                && audit.NumericVerified && audit.GuardVerified,
            EmlRung0AuditStatuses.Disagreed => selected && audit.EvaluatorCalls > 0
                && (!audit.NumericVerified || !audit.GuardVerified),
            _ => false,
        };
        if (!validStatus) throw new InvalidDataException("rung-0 audit status disagrees with its deterministic cadence or evidence");
        HashSet<EmlRuleID> proofRules = new();
        for (int i = 0; i < proof.Value.Steps.Count; i++) proofRules.Add(proof.Value.Steps[i].RuleID);
        HashSet<EmlRuleID> auditRules = new(audit.Rules);
        if (!proofRules.SetEquals(auditRules))
            throw new InvalidDataException("rung-0 audit does not bind every involved rule");
    }

    private static void WriteRung0Proof(CkptWriter writer, in EmlRung0Proof proof)
    {
        writer.I32(proof.PredictionID.Value);
        writer.Str(proof.SourceDigest);
        writer.Str(proof.AntecedentRPN);
        writer.Str(proof.ConsequentRPN);
        writer.I32(proof.SearchRevision);
        writer.I32(proof.Budget.MaxDepth);
        writer.I32(proof.Budget.MaxStates);
        writer.I32(proof.Budget.MaxApplications);
        writer.I32(proof.Work.ExpandedStates);
        writer.I32(proof.Work.VisitedStates);
        writer.I32(proof.Work.Applications);
        writer.I32(proof.Work.GuardRejections);
        writer.U64(proof.Digest);
        writer.I32(proof.Steps.Count);
        for (int i = 0; i < proof.Steps.Count; i++) WriteCompositionStep(writer, proof.Steps[i]);
    }

    private static EmlRung0Proof ReadRung0Proof(CkptReader reader, bool hasNodeFacts = false)
    {
        EmlPredictionID claimID = new(reader.I32());
        string sourceDigest = reader.Str();
        string antecedent = reader.Str();
        string consequent = reader.Str();
        int revision = reader.I32();
        EmlRung0Budget budget = new(reader.I32(), reader.I32(), reader.I32());
        EmlRung0Work work = new(reader.I32(), reader.I32(), reader.I32(), reader.I32());
        ulong digest = reader.U64();
        int stepCount = reader.I32();
        if (stepCount < 1 || stepCount > 1024)
            throw new InvalidDataException("rung-0 proof carries an invalid step count");
        EmlCompositionStep[] steps = new EmlCompositionStep[stepCount];
        for (int i = 0; i < steps.Length; i++) steps[i] = ReadCompositionStep(reader, legacy: false, hasNodeFacts: hasNodeFacts);
        return new EmlRung0Proof(claimID, sourceDigest, antecedent, consequent, revision, budget, steps, work, digest);
    }

    private static void WriteRung0Audit(CkptWriter writer, in EmlRung0Audit audit)
    {
        writer.U64(audit.ProofDigest);
        writer.U8((byte)audit.Status);
        writer.I64(audit.EvaluatorCalls);
        writer.Bool(audit.NumericVerified);
        writer.Bool(audit.GuardVerified);
        writer.I32(audit.Rules.Count);
        for (int i = 0; i < audit.Rules.Count; i++) writer.Str(audit.Rules[i].Value);
        writer.U8((byte)audit.Selection);
    }

    private static EmlRung0Audit ReadRung0Audit(CkptReader reader, bool hasSelection = true)
    {
        ulong proofDigest = reader.U64();
        EmlRung0AuditStatuses status = (EmlRung0AuditStatuses)reader.U8();
        long evaluatorCalls = reader.I64();
        bool numericVerified = reader.Bool();
        bool guardVerified = reader.Bool();
        int ruleCount = reader.I32();
        if (ruleCount < 1 || ruleCount > 1024)
            throw new InvalidDataException("rung-0 audit carries an invalid rule count");
        EmlRuleID[] rules = new EmlRuleID[ruleCount];
        for (int i = 0; i < rules.Length; i++) rules[i] = new EmlRuleID(reader.Str());
        EmlRung0AuditSelectionSpecies selection = hasSelection
            ? (EmlRung0AuditSelectionSpecies)reader.U8()
            : EmlRung0AuditSelectionSpecies.DigestCadence;
        return new EmlRung0Audit(proofDigest, status, evaluatorCalls, numericVerified, guardVerified, rules, selection);
    }

    private static void WriteRung0RuleTransition(CkptWriter writer, in EmlRung0RuleTransition transition)
    {
        writer.I64(transition.Sequence);
        writer.Str(transition.RuleID.Value);
        writer.U8((byte)transition.Kind);
        writer.U64(transition.ProofDigest);
        writer.Bool(transition.NumericVerified);
        writer.Bool(transition.GuardVerified);
        writer.I64(transition.EvaluatorCalls);
    }

    private static EmlRung0RuleTransition ReadRung0RuleTransition(CkptReader reader)
        => new(reader.I64(), new EmlRuleID(reader.Str()), (EmlRung0RuleTransitionKinds)reader.U8(),
            reader.U64(), reader.Bool(), reader.Bool(), reader.I64());

    private static void WriteVerifiedLawSupport(CkptWriter writer, EmlVerifiedLawSupportReceipt support)
    {
        support.Validate();
        writer.Str(support.CandidateAdmissionID);
        support.Candidate.Save(writer);
        writer.Str(support.SupportSetDigest);
        writer.I32(support.CandidateSupport.Count);
        for (int i = 0; i < support.CandidateSupport.Count; i++)
        {
            writer.I32(support.CandidateSupport[i].SourcePredictionID);
            writer.Str(support.CandidateSupport[i].Certificate);
            writer.Str(support.CandidateSupport[i].LeftRpn);
            writer.Str(support.CandidateSupport[i].RightRpn);
        }
        WriteCertificate(writer, support.Certificate);
        writer.Str(support.CanonicalAuthorityID);
        writer.I32(support.SourcePredictionIDs.Count);
        for (int i = 0; i < support.SourcePredictionIDs.Count; i++)
        {
            writer.I32(support.SourcePredictionIDs[i]);
            writer.Str(support.SourcePredictionDigests[i]);
            writer.Str(support.SourcePredictionMintLineDigests[i]);
            writer.Bool(support.SourcePredictionAdmissions[i] is EmlSourcePredictionAdmission);
            if (support.SourcePredictionAdmissions[i] is EmlSourcePredictionAdmission admission)
            {
                writer.U8((byte)admission.Species);
                writer.I64(admission.EventID.Value);
            }
            writer.I32(support.SourcePredictionOpportunityEvents[i].Count);
            for (int j = 0; j < support.SourcePredictionOpportunityEvents[i].Count; j++)
                writer.I64(support.SourcePredictionOpportunityEvents[i][j].Value);
        }
        writer.I32(support.WorldOpportunityEventIDs.Count);
        for (int i = 0; i < support.WorldOpportunityEventIDs.Count; i++)
            writer.I64(support.WorldOpportunityEventIDs[i].Value);
        writer.I32(support.CaptureStep);
        writer.I32(support.CaptureIndex);
        writer.Bool(support.FirstCapture);
        writer.Bool(support.RepresentativeChanged);
        writer.Str(support.Digest);
        writer.Bool(support.SupportEventID is TapeEventID);
        if (support.SupportEventID is TapeEventID supportEvent) writer.I64(supportEvent.Value);
        writer.Bool(support.ExecutionEventID is TapeEventID);
        if (support.ExecutionEventID is TapeEventID executionEvent) writer.I64(executionEvent.Value);
        writer.I32(support.GeneratedPredictionIDs.Count);
        for (int i = 0; i < support.GeneratedPredictionIDs.Count; i++) writer.I32(support.GeneratedPredictionIDs[i]);
        writer.Bool(support.Consumed);
    }

    private static EmlVerifiedLawSupportReceipt ReadVerifiedLawSupport(CkptReader reader, bool hasExecution, bool hasGeneratedPredictions)
    {
        string candidateAdmissionID = reader.Str();
        EmlVerifiedLaw candidate = EmlVerifiedLaw.LoadVerified(reader, hasGuardSchema: true, hasWitnessContext: true, hasNodeFacts: true);
        string supportSetDigest = reader.Str();
        int candidateSupportCount = reader.I32();
        if (candidateSupportCount < 0 || candidateSupportCount > MaxRung0BasisArchiveEntries)
            throw new InvalidDataException("verified-law support candidate set count is invalid");
        EmlVerifiedLawSupportReceipt.SupportPrediction[] candidateSupport = new EmlVerifiedLawSupportReceipt.SupportPrediction[candidateSupportCount];
        for (int i = 0; i < candidateSupportCount; i++)
            candidateSupport[i] = new EmlVerifiedLawSupportReceipt.SupportPrediction(reader.I32(), reader.Str(), reader.Str(), reader.Str());
        EmlLawBehaviorCertificate certificate = ReadCertificate(reader);
        string canonicalAuthorityID = reader.Str();
        int claimCount = reader.I32();
        if (claimCount < 0 || claimCount > MaxRung0BasisArchiveEntries)
            throw new InvalidDataException("verified-law support receipt carries an invalid claim count");
        int[] claimIDs = new int[claimCount];
        string[] claimDigests = new string[claimCount];
        string[] claimMintLineDigests = new string[claimCount];
        EmlSourcePredictionAdmission?[] claimAdmissions = new EmlSourcePredictionAdmission?[claimCount];
        IReadOnlyList<TapeEventID>[] claimEvents = new IReadOnlyList<TapeEventID>[claimCount];
        for (int i = 0; i < claimCount; i++)
        {
            claimIDs[i] = reader.I32();
            claimDigests[i] = reader.Str();
            claimMintLineDigests[i] = reader.Str();
            if (reader.Bool()) claimAdmissions[i] = new EmlSourcePredictionAdmission((EmlSourcePredictionAdmissionSpecies)reader.U8(), new TapeEventID(reader.I64()));
            int eventCount = reader.I32();
            if (eventCount < 0 || eventCount > EmlVerifiedLawSupportReceipt.MaxWorldOpportunityEvents)
                throw new InvalidDataException("verified-law support claim carries an invalid opportunity count");
            TapeEventID[] events = new TapeEventID[eventCount];
            for (int j = 0; j < eventCount; j++) events[j] = new TapeEventID(reader.I64());
            claimEvents[i] = events;
        }
        int worldCount = reader.I32();
        if (worldCount < 0 || worldCount > EmlVerifiedLawSupportReceipt.MaxWorldOpportunityEvents)
            throw new InvalidDataException("verified-law support receipt carries an invalid world opportunity count");
        TapeEventID[] worldIDs = new TapeEventID[worldCount];
        for (int i = 0; i < worldCount; i++) worldIDs[i] = new TapeEventID(reader.I64());
        int captureStep = reader.I32();
        int captureIndex = reader.I32();
        bool firstCapture = reader.Bool();
        bool representativeChanged = reader.Bool();
        string digest = reader.Str();
        TapeEventID? supportEventID = hasExecution && reader.Bool() ? new TapeEventID(reader.I64()) : null;
        TapeEventID? executionEventID = hasExecution && reader.Bool() ? new TapeEventID(reader.I64()) : null;
        int generatedPredictionCount = hasGeneratedPredictions ? reader.I32() : 0;
        if (generatedPredictionCount < 0 || generatedPredictionCount > MaxRung0BasisArchiveEntries)
            throw new InvalidDataException("verified-law support receipt carries an invalid generated claim count");
        int[] generatedPredictionIDs = new int[generatedPredictionCount];
        for (int i = 0; i < generatedPredictionIDs.Length; i++) generatedPredictionIDs[i] = reader.I32();
        bool consumed = reader.Bool();
        return new EmlVerifiedLawSupportReceipt(
            candidateAdmissionID,
            candidate,
            supportSetDigest,
            candidateSupport,
            certificate,
            canonicalAuthorityID,
            claimIDs,
            claimDigests,
            claimMintLineDigests,
            claimEvents,
            claimAdmissions,
            worldIDs,
            captureStep,
            captureIndex,
            firstCapture,
            representativeChanged,
            digest,
            consumed,
            executionEventID,
            supportEventID,
            generatedPredictionIDs);
    }

    private static void WriteCompositionStep(CkptWriter writer, in EmlCompositionStep step)
    {
        writer.Str(step.RuleID.Value);
        writer.U8((byte)step.Orientation);
        writer.Str(step.Path.Steps);
        writer.Str(step.SubstitutionRpn);
        writer.Str(step.AntecedentRpn);
        writer.Str(step.ConsequentRpn);
        writer.Str(step.RulePattern);
        writer.U64(step.BasisLawDigest);
        writer.U64(step.DomainGuardDigest);
        writer.Str(step.GuardWitness.MatchedTermRpn ?? string.Empty);
        writer.Str(step.GuardWitness.SubstitutionRpn ?? string.Empty);
        writer.Str(step.GuardWitness.MatchedPath.Steps);
        writer.Str(step.GuardWitness.AntecedentRpn ?? string.Empty);
        writer.Str(step.GuardWitness.ConsequentRpn ?? string.Empty);
        writer.F64(step.GuardWitness.Enclosure.RealLower);
        writer.F64(step.GuardWitness.Enclosure.RealUpper);
        writer.F64(step.GuardWitness.Enclosure.ImaginaryLower);
        writer.F64(step.GuardWitness.Enclosure.ImaginaryUpper);
        writer.Bool(step.GuardWitness.Branch.LogDefined);
        writer.Bool(step.GuardWitness.Branch.EnclosureCrossesNegativeRealCut);
        writer.Bool(step.GuardWitness.Branch.ExpAfterLogRoundTrips);
        writer.Bool(step.GuardWitness.Branch.LogAfterExpRoundTrips);
        writer.I64(step.GuardWitness.Branch.ExponentialTurn);
        writer.U64(step.GuardWitness.Digest);
        int factCount = step.GuardWitness.NodeFacts?.Count ?? 0;
        writer.I32(factCount);
        for (int i = 0; i < factCount; i++)
        {
            EmlGuardNodeFact fact = step.GuardWitness.NodeFacts![i];
            writer.U8((byte)fact.Side);
            writer.Str(fact.Path.Steps);
            writer.F64(fact.Enclosure.RealLower);
            writer.F64(fact.Enclosure.RealUpper);
            writer.F64(fact.Enclosure.ImaginaryLower);
            writer.F64(fact.Enclosure.ImaginaryUpper);
            writer.Bool(fact.Branch.LogDefined);
            writer.Bool(fact.Branch.EnclosureCrossesNegativeRealCut);
            writer.Bool(fact.Branch.ExpAfterLogRoundTrips);
            writer.Bool(fact.Branch.LogAfterExpRoundTrips);
            writer.I64(fact.Branch.ExponentialTurn);
        }
        writer.I32(step.RankBefore);
        writer.I32(step.RankAfter);
    }

    private static EmlCompositionStep ReadCompositionStep(CkptReader reader, bool legacy, bool hasNodeFacts = false)
    {
        EmlRuleID ruleID = new(reader.Str());
        EmlLawOrientations orientation = (EmlLawOrientations)reader.U8();
        EmlPath path = new(reader.Str());
        string substitution = reader.Str();
        string antecedent = reader.Str();
        string consequent = reader.Str();
        string rulePattern = legacy ? string.Empty : reader.Str();
        ulong basisDigest = legacy ? 0 : reader.U64();
        ulong domainDigest = legacy ? 0 : reader.U64();
        string matchedTerm = reader.Str();
        string witnessSubstitution = reader.Str();
        EmlPath witnessPath = legacy ? EmlPath.Root : new EmlPath(reader.Str());
        string witnessAntecedent = legacy ? string.Empty : reader.Str();
        string witnessConsequent = legacy ? string.Empty : reader.Str();
        EmlEnclosureWitness enclosure = new(reader.F64(), reader.F64(), reader.F64(), reader.F64());
        EmlBranchWitness branch = new(reader.Bool(), reader.Bool(), reader.Bool(), reader.Bool(), reader.I64());
        ulong digest = reader.U64();
        List<EmlGuardNodeFact>? facts = null;
        if (hasNodeFacts)
        {
            int count = reader.I32();
            if (count < 0 || count > 4096) throw new InvalidDataException("EML derivation step has an invalid node-fact count");
            facts = new List<EmlGuardNodeFact>(count);
            for (int i = 0; i < count; i++)
            {
                EmlGuardSides side = (EmlGuardSides)reader.U8();
                if (!Enum.IsDefined(side)) throw new InvalidDataException("EML derivation step has an unknown node-fact side");
                facts.Add(new EmlGuardNodeFact(
                    side,
                    new EmlPath(reader.Str()),
                    new EmlEnclosureWitness(reader.F64(), reader.F64(), reader.F64(), reader.F64()),
                    new EmlBranchWitness(reader.Bool(), reader.Bool(), reader.Bool(), reader.Bool(), reader.I64())));
            }
        }
        EmlGuardWitness witness = new(
            matchedTerm,
            witnessSubstitution,
            enclosure, branch, digest,
            witnessPath,
            witnessAntecedent,
            witnessConsequent,
            facts);
        return new EmlCompositionStep(
            ruleID,
            orientation,
            path,
            substitution,
            antecedent,
            consequent,
            witness,
            reader.I32(),
            reader.I32(),
            rulePattern,
            basisDigest,
            domainDigest);
    }

    private bool TryValidateCompositionStep(in EmlCompositionStep step)
    {
        if (step.RuleID.IsEmpty || !Enum.IsDefined(step.Orientation)
            || string.IsNullOrEmpty(step.RulePattern)
            || step.BasisLawDigest == 0 || step.DomainGuardDigest == 0
            || step.RankBefore <= 0 || step.RankAfter <= 0
            || !EmlRewriteSystem.ReducesRank(step.AntecedentRpn, step.ConsequentRpn)
            || step.RankBefore != step.AntecedentRpn.Length
            || step.RankAfter != step.ConsequentRpn.Length
            || !EmlRung0Digest.IsCanonicalRPN(step.AntecedentRpn)
            || !EmlRung0Digest.IsCanonicalRPN(step.SubstitutionRpn)
            || !EmlRung0Digest.IsCanonicalRPN(step.ConsequentRpn)
            || EmlRuleID.Create(step.RulePattern, step.Orientation, step.BasisLawDigest, step.DomainGuardDigest) != step.RuleID
            || !step.GuardWitness.HasValidDigest
            || !step.GuardWitness.Matches(step.Path, step.GuardWitness.MatchedTermRpn,
                step.SubstitutionRpn, step.AntecedentRpn, step.ConsequentRpn)) return false;
        if (!EmlTree.TryParseRPN(step.AntecedentRpn, out EmlTree? antecedent)
            || !EmlTree.TryParseRPN(step.SubstitutionRpn, out EmlTree? substitution)
            || !EmlTree.TryParseRPN(step.ConsequentRpn, out _)
            || !antecedent!.TryGetNode(step.Path, out EmlTree.Node? matched)
            || matched is null
            || !string.Equals(new EmlTree(matched).RenderRPN(), step.GuardWitness.MatchedTermRpn, StringComparison.Ordinal)
            || !EmlOneHoleLaw.TryParse(step.RulePattern, out EmlOneHoleLaw law)) return false;

        EmlTree replacement = law.InstantiateReplacement(step.Orientation, substitution!);
        EmlTree resulting;
        try { resulting = antecedent.ReplaceSubtree(step.Path, replacement); }
        catch (ArgumentOutOfRangeException) { return false; }
        if (!string.Equals(resulting.RenderRPN(), step.ConsequentRpn, StringComparison.Ordinal)) return false;

        // The journal stores the guard digest, not a second mutable atom list. Rebind the
        // authoritative law's atoms to this match path and validate the witness against them;
        // changing both witness and outer journal digest therefore cannot create a valid import.
        return TryFindRung0Basis(in step, substitution!, replacement, out _);
    }

    private bool TryFindRung0Basis(in EmlCompositionStep step, out EmlVerifiedLaw basis)
    {
        if (!TryPrepareRung0Basis(in step, out EmlTree? substitution, out EmlTree? replacement))
        {
            basis = null!;
            return false;
        }
        return TryFindRung0Basis(in step, substitution!, replacement!, out basis);
    }

    private bool TryFindArchivedRung0Basis(in EmlCompositionStep step, out EmlVerifiedLaw basis)
    {
        if (!TryPrepareRung0Basis(in step, out EmlTree? substitution, out EmlTree? replacement))
        {
            basis = null!;
            return false;
        }
        if (_rung0BasisArchiveIndex.TryGetValue(new Rung0BasisKey(step.BasisLawDigest, step.RulePattern), out List<EmlVerifiedLaw>? bucket))
            foreach (EmlVerifiedLaw archived in bucket)
                if (TryMatchRung0Basis(in step, substitution!, replacement!, archived, out basis)) return true;
        basis = null!;
        return false;
    }

    private static bool TryPrepareRung0Basis(
        in EmlCompositionStep step,
        out EmlTree? substitution,
        out EmlTree? replacement)
    {
        replacement = null;
        if (!EmlTree.TryParseRPN(step.SubstitutionRpn, out substitution)
            || !EmlOneHoleLaw.TryParse(step.RulePattern, out EmlOneHoleLaw law)) return false;
        replacement = law.InstantiateReplacement(step.Orientation, substitution!);
        return true;
    }

    private bool TryFindRung0Basis(
        in EmlCompositionStep step,
        EmlTree substitution,
        EmlTree replacement,
        out EmlVerifiedLaw basis)
    {
        if (_rung0BasisArchiveIndex.TryGetValue(new Rung0BasisKey(step.BasisLawDigest, step.RulePattern), out List<EmlVerifiedLaw>? bucket))
            foreach (EmlVerifiedLaw archived in bucket)
                if (TryMatchRung0Basis(in step, substitution, replacement, archived, out basis)) return true;
        foreach (SemanticCASClass<EmlVerifiedLaw> lawClass in _classes.Values)
            if (TryMatchRung0Basis(in step, substitution, replacement, lawClass.Rep, out basis)) return true;
        basis = null!;
        return false;
    }

    private static bool TryMatchRung0Basis(
        in EmlCompositionStep step,
        EmlTree substitution,
        EmlTree replacement,
        EmlVerifiedLaw admittedLaw,
        out EmlVerifiedLaw basis)
    {
        basis = null!;
        if (admittedLaw.Proof.OccurrenceDigest != step.BasisLawDigest
            || admittedLaw.Proof.DomainGuards is null
            || !string.Equals(admittedLaw.Law.Template, step.RulePattern, StringComparison.Ordinal)
            || !EmlOneHoleLaw.TryParse(admittedLaw.Law.Template, out EmlOneHoleLaw admittedTemplate)) return false;
        EmlTree admittedMatch = admittedTemplate.InstantiateMatch(step.Orientation, substitution);
        EmlTree admittedReplacement = admittedTemplate.InstantiateReplacement(step.Orientation, substitution);
        if (!string.Equals(admittedMatch.RenderRPN(), step.GuardWitness.MatchedTermRpn, StringComparison.Ordinal)
            || !string.Equals(admittedReplacement.RenderRPN(), replacement.RenderRPN(), StringComparison.Ordinal)) return false;
        EmlDomainGuardSet guards = admittedLaw.Proof.DomainGuards.BindToPath(step.Path);
        if (guards.Digest != step.DomainGuardDigest || !guards.TryValidate(step.GuardWitness)) return false;
        basis = admittedLaw;
        return true;
    }

    private static int CompareCandidateRewrites(
        EmlLawCandidateInstantiation left,
        EmlLawCandidateInstantiation right)
    {
        int byObligation = left.Obligation.SourcePredictionID.Value.CompareTo(right.Obligation.SourcePredictionID.Value);
        if (byObligation != 0) return byObligation;
        int byAntecedent = string.CompareOrdinal(left.Rewrite.AntecedentRpn, right.Rewrite.AntecedentRpn);
        if (byAntecedent != 0) return byAntecedent;
        int byPath = string.CompareOrdinal(left.Rewrite.MatchedPath.Steps, right.Rewrite.MatchedPath.Steps);
        if (byPath != 0) return byPath;
        int byOrientation = left.Rewrite.Orientation.CompareTo(right.Rewrite.Orientation);
        if (byOrientation != 0) return byOrientation;
        int byMatchedTerm = string.CompareOrdinal(left.Rewrite.MatchedTermRpn, right.Rewrite.MatchedTermRpn);
        if (byMatchedTerm != 0) return byMatchedTerm;
        int bySubstitution = string.CompareOrdinal(left.Rewrite.SubstitutionRpn, right.Rewrite.SubstitutionRpn);
        if (bySubstitution != 0) return bySubstitution;
        int byConsequent = string.CompareOrdinal(left.Rewrite.ConsequentRpn, right.Rewrite.ConsequentRpn);
        if (byConsequent != 0) return byConsequent;
        int byCertificate = CompareCertificates(left.Rewrite.LawCertificate, right.Rewrite.LawCertificate);
        if (byCertificate != 0) return byCertificate;
        int byProofDigest = left.Rewrite.LawProof.OccurrenceDigest.CompareTo(right.Rewrite.LawProof.OccurrenceDigest);
        if (byProofDigest != 0) return byProofDigest;
        int byProofPrediction = string.CompareOrdinal(left.Rewrite.LawProof.OccurrenceCheckPrediction, right.Rewrite.LawProof.OccurrenceCheckPrediction);
        if (byProofPrediction != 0) return byProofPrediction;
        int byAbsentFiller = string.CompareOrdinal(left.Rewrite.LawProof.AbsentFiller, right.Rewrite.LawProof.AbsentFiller);
        return byAbsentFiller != 0
            ? byAbsentFiller
            : left.Rewrite.IsRelationNull.CompareTo(right.Rewrite.IsRelationNull);
    }

    internal static string CreateAdmissionID(EmlVerifiedLaw law)
        => law.Law.Template + "\u0001" + law.Proof.OccurrenceDigest.ToString("X16") + "\u0001" + law.Proof.OccurrenceCheckPrediction;

    internal bool TryReadRung0BasisLawAdmissionIDs(
        EmlPredictionID sourcePredictionID,
        out IReadOnlyList<EmlRung0BasisLawIdentity> identities)
    {
        identities = Array.Empty<EmlRung0BasisLawIdentity>();
        for (int index = 0; index < _rung0Proofs.Count; index++)
        {
            EmlRung0Proof proof = _rung0Proofs[index];
            if (proof.PredictionID != sourcePredictionID) continue;
            List<EmlRung0BasisLawIdentity> selected = new(proof.Steps.Count);
            for (int stepIndex = 0; stepIndex < proof.Steps.Count; stepIndex++)
            {
                EmlCompositionStep step = proof.Steps[stepIndex];
                if (!TryFindRung0Basis(in step, out EmlVerifiedLaw basis)) return false;
                selected.Add(new EmlRung0BasisLawIdentity(CreateAdmissionID(basis)));
            }
            identities = selected.Distinct().ToArray();
            return identities.Count > 0;
        }
        return false;
    }

    private static string FormatSignature(in EmlSig signature)
        => $"{signature.R1}:{signature.I1}:{signature.R2}:{signature.I2}";

    private static string FormatEvidence(in EmlLawExactEvidence evidence)
        => $"{evidence.Grade}:{(evidence.Q12Home ? 1 : 0)}:{(evidence.Q12Regime ? 1 : 0)}:{evidence.EnclosureColumns}";

    private static int CompareCertificates(EmlLawBehaviorCertificate left, EmlLawBehaviorCertificate right)
    {
        int atOne = CompareSignatures(left.AtOne, right.AtOne);
        if (atOne != 0) return atOne;
        int atX = CompareSignatures(left.AtX, right.AtX);
        return atX != 0 ? atX : CompareSignatures(left.AtY, right.AtY);
    }

    private static int CompareSignatures(EmlSig left, EmlSig right)
    {
        int r1 = left.R1.CompareTo(right.R1);
        if (r1 != 0) return r1;
        int i1 = left.I1.CompareTo(right.I1);
        if (i1 != 0) return i1;
        int r2 = left.R2.CompareTo(right.R2);
        return r2 != 0 ? r2 : left.I2.CompareTo(right.I2);
    }

    private static void WriteCertificate(CkptWriter writer, in EmlLawBehaviorCertificate certificate)
    {
        WriteSignature(writer, certificate.AtOne);
        WriteSignature(writer, certificate.AtX);
        WriteSignature(writer, certificate.AtY);
    }

    private static EmlLawBehaviorCertificate ReadCertificate(CkptReader reader)
        => new(ReadSignature(reader), ReadSignature(reader), ReadSignature(reader));

    private static void WriteSignature(CkptWriter writer, in EmlSig signature)
    {
        writer.I64(signature.R1);
        writer.I64(signature.I1);
        writer.I64(signature.R2);
        writer.I64(signature.I2);
    }

    private static EmlSig ReadSignature(CkptReader reader)
        => new(reader.I64(), reader.I64(), reader.I64(), reader.I64());

    internal static bool MatchesPersistedLawExecution(
        TapeEventView view,
        in TapePacketCreator.EmlLawExecutionSupportPacket packet,
        EmlVerifiedLawSupportReceipt support)
        => MatchesPersistedLawExecution(view, in packet, support, out _);

    internal static bool MatchesPersistedLawExecution(
        TapeEventView view,
        in TapePacketCreator.EmlLawExecutionSupportPacket packet,
        EmlVerifiedLawSupportReceipt support,
        out IReadOnlyList<int> generatedPredictionIDs)
    {
        generatedPredictionIDs = Array.Empty<int>();
        if (!string.Equals(view.Source, "eml:law-execution", StringComparison.Ordinal)
            || view.Provenance != Provenances.Reflected
            || packet.Offers <= 0
            || packet.Mints <= 0
            || packet.Mints != packet.PredictionIDs.Count
            || packet.PredictionIDs.Count == 0
            || support.SourcePredictionIDs.Count == 0
            || support.SourcePredictionAdmissions.Count != support.SourcePredictionIDs.Count
            || support.SourcePredictionAdmissions.Any(admission => admission is not EmlSourcePredictionAdmission)
            || support.SourcePredictionAdmissions.Any(admission =>
                admission is EmlSourcePredictionAdmission sourceAdmissionAfter
                && sourceAdmissionAfter.EventID.Value >= view.Id.Value))
            return false;

        int supportIndex = -1;
        for (int i = 0; i < packet.Digests.Count; i++)
            if (string.Equals(packet.Digests[i], support.Digest, StringComparison.Ordinal))
            {
                supportIndex = i;
                break;
            }
        if (supportIndex < 0
            || !string.Equals(packet.Authorities[supportIndex], support.CanonicalAuthorityID, StringComparison.Ordinal)
            || support.SupportEventID is not TapeEventID supportEvent
            || view.Id.Value <= supportEvent.Value)
            return false;

        HashSet<int> packetPredictionIDs = packet.PredictionIDs.ToHashSet();
        if (packetPredictionIDs.Count != packet.PredictionIDs.Count)
            return false;

        HashSet<int> allRangedPredictionIDs = new();
        HashSet<int> supportRangedPredictionIDs = new();
        for (int rangeIndex = 0; rangeIndex < packet.Ranges.Count; rangeIndex++)
        {
            (string digest, int start, int count) = packet.Ranges[rangeIndex];
            if (start < 0 || count <= 0 || start > int.MaxValue - count) return false;
            for (int offset = 0; offset < count; offset++)
            {
                int claimID = start + offset;
                if (!packetPredictionIDs.Contains(claimID) || !allRangedPredictionIDs.Add(claimID)) return false;
                if (string.Equals(digest, support.Digest, StringComparison.Ordinal)
                    && !supportRangedPredictionIDs.Add(claimID)) return false;
            }
        }
        if (!allRangedPredictionIDs.SetEquals(packetPredictionIDs)
            || supportRangedPredictionIDs.Count == 0
            || (support.GeneratedPredictionIDs.Count > 0 && !supportRangedPredictionIDs.SetEquals(support.GeneratedPredictionIDs)))
            return false;
        generatedPredictionIDs = supportRangedPredictionIDs.OrderBy(static claimID => claimID).ToArray();
        return true;
    }
}

public sealed partial class ReplayCalc
{
    private const uint LawTag = 0x4C415753;
    private readonly record struct LawFrontierSource(
        EmlVerifiedLaw Law,
        TapeEventID[] OpportunityEvents,
        EmlVerifiedLawSupportReceipt Support);
    private readonly EmlLawStore _lawStore = new();
    private int _lawExactPredictionHighWater;
    private int _lawCaptureIndex;
    private bool _lawStatePresent = true;

    internal int LawCount => _lawStore.Count;

    internal void AdmitNewLaws(RePairResult grammar, Tape tape, Journal journal, int step,
        LoopLineageTurnstile? lineage = null, GrammarRevisionID grammarRevision = default, int wScale = 8)
    {
        FlushPendingLawSupports(grammar, tape, journal, step, lineage, grammarRevision, wScale);
        int exactClasses = _sieve.ExactClasses;
        RefineProcessConstants(tape, journal, step, exactClasses);
        int exactPredictions = checked(_sieve.CountExactRPNPredictions() - (int)_lawStore.FormFarmAccepted);
        if (exactPredictions <= _lawExactPredictionHighWater) return;
        _lawStatePresent = true;
        _lawExactPredictionHighWater = exactPredictions;
        if (!_bias) return;

        List<EmlLawCandidate> candidates = EmlAntiUnify.DiscoverCandidates(_sieve, grammar, _seed);
        for (int i = 0; i < candidates.Count; i++)
        {
            EmlLawCandidate candidate = candidates[i];
            if (!EmlVerifiedLaw.TryVerify(candidate.Law, candidate.Support, _sieve.SignatureDigits,
                    out EmlVerifiedLaw? verified) || verified is null) continue;
            List<TapeEventID> opportunityEvents = new();
            Dictionary<int, IReadOnlyList<TapeEventID>> claimOpportunityEvents = new();
            Dictionary<int, EmlSourcePredictionAdmission> claimAdmissions = new();
            Dictionary<int, string> claimMintDigests = new();
            Dictionary<int, string> claimMintLineDigests = new();
            for (int supportIndex = 0; supportIndex < candidate.Support.Count; supportIndex++)
            {
                EmlPredictionID? sourcePredictionID = candidate.Support[supportIndex].SourcePredictionID;
                IReadOnlyList<TapeEventID> sourceOpportunities = Array.Empty<TapeEventID>();
                if (sourcePredictionID is EmlPredictionID source
                    && _sieve.TryReadMintOpportunityEvents(source, out IReadOnlyList<TapeEventID> events))
                {
                    sourceOpportunities = events;
                    opportunityEvents.AddRange(events);
                    claimOpportunityEvents[source.Value] = events;
                }
                if (sourcePredictionID is EmlPredictionID mintSource
                    && (uint)mintSource.Value < (uint)_sieve.MintLog.Count)
                {
                    EmlMint mint = _sieve.MintLog[mintSource.Value];
                    claimMintLineDigests[mintSource.Value] = Convert.ToHexStringLower(
                        SHA256.HashData(Encoding.ASCII.GetBytes(mint.Line)));
                    string mintMaterial = mint.Line + "|" + mint.Prog + "|"
                        + mint.Sig.R1.ToString("X16", CultureInfo.InvariantCulture)
                        + mint.Sig.I1.ToString("X16", CultureInfo.InvariantCulture)
                        + mint.Sig.R2.ToString("X16", CultureInfo.InvariantCulture)
                        + mint.Sig.I2.ToString("X16", CultureInfo.InvariantCulture)
                        + "|" + mint.Grade + "|" + (mint.Corrob ? "1" : "0");
                    claimMintDigests[mintSource.Value] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(mintMaterial)));
                    if (_sieve.TryReadPredictionAdmission(mintSource, out EmlSourcePredictionAdmission admissionPath))
                    {
                        if (tape.TryGetEventView(admissionPath.EventID, out TapeEventView admissionView)
                            && admissionView.Source == "eml:law-execution")
                            admissionPath = admissionPath with { Species = EmlSourcePredictionAdmissionSpecies.LawExecutionPacket };
                        claimAdmissions[mintSource.Value] = admissionPath;
                    }
                }
            }
            TapeEventID[] exactOpportunities = opportunityEvents.Distinct().OrderBy(static id => id.Value).ToArray();
            if (!_lawStore.TryAdmitWithSupportCustody(
                    verified, ref _lawCaptureIndex, candidate.Support, claimOpportunityEvents, claimAdmissions,
                    claimMintDigests, claimMintLineDigests, exactOpportunities,
                    out SemanticCASAdmission<EmlLawBehaviorCertificate, EmlVerifiedLaw> admission)) continue;
            EmlVerifiedLawSupportReceipt supportReceipt = _lawStore.RecordVerifiedLawSupport(
                verified, in admission, candidate.Support, claimOpportunityEvents, claimAdmissions, claimMintDigests, claimMintLineDigests,
                exactOpportunities, step, _lawCaptureIndex - 1);
            IReadOnlyList<LoopLineageNode> worldNodes = Array.Empty<LoopLineageNode>();
            LoopLineageNodeID canonicalLawNodeID = default;
            if (admission.FirstCapture || admission.RepresentativeChanged)
            {
                TapeEventID admissionEventID = TapePacketCreator.AppendEmlLawAdmission(tape, journal, step, verified,
                    !admission.FirstCapture && admission.RepresentativeChanged);
                if (lineage is not null)
                {
                    if (!lineage.EnsureWorldOpportunities(step, admissionEventID, exactOpportunities,
                            out worldNodes)
                        && exactOpportunities.Length > 0)
                        throw new InvalidDataException("registered verified-law support names an invalid world opportunity");
                    if (worldNodes.Count > 0)
                    {
                        LoopLineageNodeID[] predecessorIDs = worldNodes.Select(static node => node.NodeID)
                            .Distinct().OrderBy(static id => id.Value, StringComparer.Ordinal).ToArray();
                        LoopLineageCausalID causalID = LoopLineageCausalID.Merge(LoopLineageNodeSpecies.VerifiedLaw, predecessorIDs);
                        if (!lineage.TryEmit(step, LoopLineageNodeSpecies.VerifiedLaw, admissionEventID,
                                null, predecessorIDs, causalID))
                            throw new InvalidDataException("registered verified-law lineage emission did not close");
                        if (!lineage.TryGetNodeForEvent(admissionEventID, out LoopLineageNode canonicalLawNode))
                            throw new InvalidDataException("registered verified-law lineage node did not persist");
                        canonicalLawNodeID = canonicalLawNode.NodeID;
                    }
                }
            }
            if (supportReceipt.HasWorldOpportunity)
            {
                TapeEventID supportEventID = TapePacketCreator.AppendEmlLawSupport(tape, journal, step, supportReceipt);
                _lawStore.BindVerifiedLawSupportPacket(supportReceipt, supportEventID);
                if (lineage is not null)
                {
                    if (worldNodes.Count == 0
                        && !lineage.EnsureWorldOpportunities(step, supportEventID, exactOpportunities, out worldNodes))
                        throw new InvalidDataException("verified-law support names an invalid world opportunity");
                    if (!canonicalLawNodeID.IsValid)
                    {
                        for (int receiptIndex = lineage.Receipts.Count - 1; receiptIndex >= 0; receiptIndex--)
                        {
                            LoopLineageEdgeReceipt candidateEdge = lineage.Receipts[receiptIndex];
                            if (candidateEdge.Node.Species != LoopLineageNodeSpecies.VerifiedLaw
                                || !tape.Resolve(candidateEdge.Node.EventID, out byte[] lawPayload)
                                || !TapePacketCreator.TryReadEmlLawAdmissionID(lawPayload, out string authorityID)
                                || !string.Equals(authorityID, supportReceipt.CanonicalAuthorityID, StringComparison.Ordinal)) continue;
                            canonicalLawNodeID = candidateEdge.Node.NodeID;
                            break;
                        }
                    }
                    if (!canonicalLawNodeID.IsValid)
                        throw new InvalidDataException("verified-law support has no canonical law authority node");
                    LoopLineageNodeID[] supportPredecessors = worldNodes.Select(static node => node.NodeID)
                        .Append(canonicalLawNodeID)
                        .Distinct().OrderBy(static id => id.Value, StringComparer.Ordinal).ToArray();
                    LoopLineageCausalID supportCausal = LoopLineageCausalID.Merge(LoopLineageNodeSpecies.VerifiedLawSupport, supportPredecessors);
                    if (!lineage.TryEmit(step, LoopLineageNodeSpecies.VerifiedLawSupport, supportEventID,
                            null, supportPredecessors, supportCausal))
                        throw new InvalidDataException("verified-law support lineage emission did not close");
                }
            }
        }
        FlushPendingLawSupports(grammar, tape, journal, step, lineage, grammarRevision, wScale);
    }

    private void FlushPendingLawSupports(RePairResult grammar, Tape tape, Journal journal, int step,
        LoopLineageTurnstile? lineage = null, GrammarRevisionID grammarRevision = default, int wScale = 8)
    {
        _lawStore.IndexPersistedLawExecutions(tape);
        if (!_lawStore.HasPendingVerifiedLawSupports) return;            // runs at every step's entry AND exit — the common case must be alloc-free
        List<(EmlVerifiedLaw Law, EmlVerifiedLawSupportReceipt Support)> pendingSupports = new();
        _lawStore.AppendPendingVerifiedLawSupports(pendingSupports);
        List<(EmlVerifiedLaw Law, EmlVerifiedLawSupportReceipt Support)> executableSupports = new(pendingSupports.Count);
        for (int i = 0; i < pendingSupports.Count; i++)
        {
            EmlVerifiedLawSupportReceipt support = pendingSupports[i].Support;
            if (!_lawStore.ValidateVerifiedLawSupportCustody(_sieve, tape, support))
                throw new InvalidDataException("verified-law support receipt cannot be re-resolved against the live sieve and tape");
            if (_lawStore.TryFindPersistedLawExecution(tape, support, out TapeEventID executionEventID,
                    out IReadOnlyList<int> generatedPredictionIDs))
            {
                _lawStore.BindVerifiedLawSupportExecution(support, executionEventID, generatedPredictionIDs);
                if (!_lawStore.ValidateVerifiedLawSupportCustody(_sieve, tape, support))
                    throw new InvalidDataException("persisted verified-law execution failed generated-claim custody");
                if (grammarRevision == GrammarRevisionID.Zero)
                    continue;
                _ = _lawStore.EnsurePatternGrammarAdmission(
                    pendingSupports[i].Law, support, _sieve, tape, journal, step, grammarRevision, out _, wScale);
                _lawStore.MarkVerifiedLawSupportConsumed(support);
            }
            else
                executableSupports.Add(pendingSupports[i]);
        }
        if (executableSupports.Count > 0)
        {
            List<LawFrontierSource> admittedLaws = new(executableSupports.Count);
            for (int i = 0; i < executableSupports.Count; i++)
                admittedLaws.Add(new LawFrontierSource(executableSupports[i].Law, executableSupports[i].Support.WorldOpportunityEventIDs.ToArray(),
                    executableSupports[i].Support));
            List<EmlVerifiedLawSupportReceipt> contributors = GenerateLawFrontier(grammar, tape, journal, step, admittedLaws);
            for (int i = 0; i < contributors.Count; i++)
            {
                if (!_lawStore.ValidateVerifiedLawSupportCustody(_sieve, tape, contributors[i]))
                    throw new InvalidDataException("generated verified-law execution failed custody after claim binding");
                if (grammarRevision == GrammarRevisionID.Zero)
                    continue;
                EmlVerifiedLawSupportReceipt support = contributors[i];
                EmlVerifiedLaw? law = null;
                for (int pendingIndex = 0; pendingIndex < executableSupports.Count; pendingIndex++)
                    if (string.Equals(executableSupports[pendingIndex].Support.Digest, support.Digest, StringComparison.Ordinal))
                    { law = executableSupports[pendingIndex].Law; break; }
                if (law is not null)
                    _ = _lawStore.EnsurePatternGrammarAdmission(
                        law, support, _sieve, tape, journal, step, grammarRevision, out _, wScale);
                _lawStore.MarkVerifiedLawSupportConsumed(contributors[i]);
            }
        }
    }

    internal bool SettlePatternGrammarAdmissions(
        GrammarRevisionID consumedRevision,
        IReadOnlyList<TapeEventID> foldedAppends,
        Func<TapeEventID, bool> foldedPredicate,
        LoopLineageTurnstile? lineage,
        Tape tape,
        Journal journal,
        int step)
        => _lawStore.SettlePatternGrammarAdmissions(consumedRevision, foldedAppends, foldedPredicate, lineage, tape, journal, step);

    public bool SettleInstallRevision(
        in InstallRevision publication,
        IReadOnlyList<TapeEventID> foldedAppends,
        Func<TapeEventID, bool> foldedPredicate,
        LoopLineageTurnstile? lineage,
        Tape tape,
        Journal journal,
        int step)
        => SettlePatternGrammarAdmissions(publication.Revision, foldedAppends, foldedPredicate, lineage, tape, journal, step);

    internal IReadOnlyList<EmlPatternGrammarAdmissionReceipt> PatternGrammarAdmissions
        => _lawStore.PatternGrammarAdmissions;

    internal string ReportLaws() => _lawStore.Report();
    internal string ReportLawProofQueue() => _lawStore.ReportProofQueue();
    internal string ReportRewriteSystem() => _lawStore.ReportRewriteSystem();
    internal string ReportLawFunnel(RePairResult grammar)
    {
        List<EmlLawCandidate> candidates = EmlAntiUnify.DiscoverCandidates(_sieve, grammar, _seed, out EmlLawFunnel funnel);
        EmlLawCandidateCensus census = _lawStore.MeasureCandidates(candidates, _sieve.SignatureDigits);
        StringBuilder report = new(EmlAntiUnify.ReportFunnel(in funnel));
        report.Append("numerically_verified\t").Append(census.NumericallyVerified).AppendLine()
            .Append("basis_representatives\t").Append(census.BasisRepresentatives).AppendLine()
            .Append("behavior_span\t").Append(census.BehaviorSpan).AppendLine()
            .Append("direct_witness_span\t").Append(census.DirectWitnessComposed).AppendLine()
            .Append("sampled_join_span\t").Append(census.SampledJoinComposed).AppendLine()
            .Append("novel_behavior\t").Append(census.NovelBehavior).AppendLine();
        return report.ToString();
    }

    internal long LawGeneratedOffers => _lawStore.GeneratedOffers;
    internal long LawGeneratedMints => _lawStore.GeneratedMints;
    internal long LawDirectWitnessMatches => _lawStore.DirectWitnessMatches;
    internal long LawFormFarmAttempted => _lawStore.FormFarmAttempted;
    internal long LawFormFarmAccepted => _lawStore.FormFarmAccepted;
    internal long LawFormFarmRejected => _lawStore.FormFarmRejected;

    internal (long ProofCount, long AuditCount, string Digest) CaptureDeepRematchRung0Cursor()
    {
        StringBuilder material = new();
        for (int i = 0; i < _lawStore.Rung0Proofs.Count; i++)
            material.Append("proof:").Append(_lawStore.Rung0Proofs[i].Digest.ToString("X16", System.Globalization.CultureInfo.InvariantCulture)).Append('|');
        for (int i = 0; i < _lawStore.Rung0Audits.Count; i++)
        {
            EmlRung0Audit audit = _lawStore.Rung0Audits[i];
            material.Append("audit:").Append(audit.ProofDigest.ToString("X16", System.Globalization.CultureInfo.InvariantCulture)).Append(':')
                .Append(audit.Status).Append(':').Append(audit.EvaluatorCalls.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(':')
                .Append(audit.Selection).Append('|');
        }
        return (_lawStore.Rung0Proofs.Count, _lawStore.Rung0Audits.Count,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString()))));
    }

    internal (long ComposedPredictions, long EvaluatorCalls, long AuditFailures, long RelationNullExecutions, long RelationNullAuthorityPredictions) ReadDeepRematchRung0Metrics(
        (long ProofCount, long AuditCount, string Digest) baseline)
    {
        if (baseline.ProofCount < 0 || baseline.ProofCount > _lawStore.Rung0Proofs.Count
            || baseline.AuditCount < 0 || baseline.AuditCount > _lawStore.Rung0Audits.Count)
            throw new InvalidDataException("rung-0 cursor is outside the retained proof/audit journal");
        long derived = _lawStore.Rung0Proofs.Count - baseline.ProofCount;
        long evaluatorCalls = 0;
        long auditFailures = 0;
        for (int i = checked((int)baseline.AuditCount); i < _lawStore.Rung0Audits.Count; i++)
        {
            EmlRung0Audit audit = _lawStore.Rung0Audits[i];
            if (audit.Status == EmlRung0AuditStatuses.Disagreed) auditFailures++;
        }
        // Audit evaluator calls are sampled evidence for the audit, not derivation calls. The
        // derivation path records no evaluator execution; report that contract directly rather
        // than laundering audit work into the rung-0 admission meter. Relation-null executions
        // are a separate powered arm and remain zero until that arm is actually run.
        return (derived, evaluatorCalls, auditFailures, 0, 0);
    }

    internal (long ComposedPredictions, long EvaluatorCalls, long AuditFailures, long RelationNullExecutions, long RelationNullAuthorityPredictions) ReadDeepRematchRung0Metrics()
        => ReadDeepRematchRung0Metrics((0L, 0L, ""));

    internal EmlDeliberationCounts ReadDeepRematchFuelTotals(bool planned, bool refund)
    {
        return ReadDeepRematchFuelTotals(0, planned, refund);
    }

    internal EmlDeepRematchFuelCursor CaptureDeepRematchFuelCursor(string pointID, string pointDigest)
    {
        if (string.IsNullOrWhiteSpace(pointID) || pointDigest.Length != 64)
            throw new InvalidDataException("deep-rematch EML fuel cursor requires a handshake point identity");
        EmlDeliberationCounts planned = ReadDeepRematchFuelTotals(planned: true, refund: false);
        EmlDeliberationCounts actual = ReadDeepRematchFuelTotals(planned: false, refund: false);
        EmlDeliberationCounts refund = ReadDeepRematchFuelTotals(planned: false, refund: true);
        int settlementCount = _sieve.DeliberationJournal.Settlements.Count;
        string settlementDigest = ComputeSettlementDigest(settlementCount);
        string digest = EmlDeepRematchFuelCursor.ComputeDigest(
            settlementCount,
            _sieve.EvaluatorClock.ProgramPointEvaluations,
            in planned, in actual, in refund, pointID, pointDigest, settlementDigest);
        return new(
            settlementCount,
            _sieve.EvaluatorClock.ProgramPointEvaluations,
            planned, actual, refund, digest, pointID, pointDigest, settlementDigest);
    }

    internal EmlDeliberationCounts ReadDeepRematchFuelTotals(in EmlDeepRematchFuelCursor cursor, bool planned, bool refund)
    {
        cursor.Validate();
        if (cursor.SettlementCount > _sieve.DeliberationJournal.Settlements.Count)
            throw new InvalidDataException("deep-rematch EML fuel cursor is ahead of the settlement journal");
        if (!string.Equals(cursor.SettlementDigest, ComputeSettlementDigest(cursor.SettlementCount), StringComparison.Ordinal))
            throw new InvalidDataException("deep-rematch EML fuel cursor settlement prefix was truncated or reordered");
        if (_sieve.EvaluatorClock.ProgramPointEvaluations < cursor.EvaluatorCalls)
            throw new InvalidDataException("deep-rematch EML evaluator high-water regressed below the handshake cursor");
        EmlDeliberationCounts current = ReadDeepRematchFuelTotals(planned, refund);
        EmlDeliberationCounts baseline = refund ? cursor.Refund : planned ? cursor.Planned : cursor.Actual;
        EmlDeliberationCounts delta = EmlDeliberationCounts.Subtract(in current, in baseline);
        delta.ValidateNonnegative("deep-rematch EML fuel since handshake");
        return delta;
    }

    private string ComputeSettlementDigest(int count)
    {
        if (count < 0 || count > _sieve.DeliberationJournal.Settlements.Count)
            throw new InvalidDataException("deep-rematch EML settlement digest prefix is outside the journal");
        StringBuilder material = new();
        for (int i = 0; i < count; i++)
        {
            EmlDeliberationSettlement settlement = _sieve.DeliberationJournal.Settlements[i];
            material.Append(settlement.ReservationID).Append('|').Append((byte)settlement.Outcome).Append('|');
            EmlDeliberationCounts planned = settlement.Planned;
            EmlDeliberationCounts reserved = settlement.Held;
            EmlDeliberationCounts actual = settlement.Actual;
            EmlDeliberationCounts refund = settlement.Refund;
            AppendCounts(material, in planned);
            AppendCounts(material, in reserved);
            AppendCounts(material, in actual);
            AppendCounts(material, in refund);
            material.Append(settlement.WallTicks).Append('|').Append(settlement.Detail).Append('\n');
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString())));
    }

    private static void AppendCounts(StringBuilder material, in EmlDeliberationCounts counts)
        => material.Append(counts.CandidateEvaluations).Append(',').Append(counts.LogicalProgramPoints).Append(',')
            .Append(counts.ExecutedProgramPoints).Append(',').Append(counts.InverseTransforms).Append(',')
            .Append(counts.HashProbes).Append(',').Append(counts.JoinAttempts).Append(',').Append(counts.JoinHits).Append(',')
            .Append(counts.ProcessTerms).Append(',').Append(counts.VerifierProgramPoints).Append(',')
            .Append(counts.CandidateSupplyItems).Append(',').Append(counts.LawRewriteApplications).Append(',')
            .Append(counts.LawRewriteTreeNodes).Append('|');

    private EmlDeliberationCounts ReadDeepRematchFuelTotals(int start, bool planned, bool refund)
    {
        EmlDeliberationCounts total = EmlDeliberationCounts.Zero;
        for (int i = start; i < _sieve.DeliberationJournal.Settlements.Count; i++)
        {
            EmlDeliberationSettlement settlement = _sieve.DeliberationJournal.Settlements[i];
            EmlDeliberationCounts value = refund ? settlement.Refund : planned ? settlement.Planned : settlement.Actual;
            total = new(
                checked(total.CandidateEvaluations + value.CandidateEvaluations),
                checked(total.LogicalProgramPoints + value.LogicalProgramPoints),
                checked(total.ExecutedProgramPoints + value.ExecutedProgramPoints),
                checked(total.InverseTransforms + value.InverseTransforms),
                checked(total.HashProbes + value.HashProbes),
                checked(total.JoinAttempts + value.JoinAttempts),
                checked(total.JoinHits + value.JoinHits),
                checked(total.ProcessTerms + value.ProcessTerms),
                checked(total.VerifierProgramPoints + value.VerifierProgramPoints),
                checked(total.CandidateSupplyItems + value.CandidateSupplyItems),
                checked(total.LawRewriteApplications + value.LawRewriteApplications),
                checked(total.LawRewriteTreeNodes + value.LawRewriteTreeNodes));
        }
        return total;
    }

    private List<EmlVerifiedLawSupportReceipt> GenerateLawFrontier(RePairResult grammar, Tape tape, Journal journal, int step,
        List<LawFrontierSource> admittedLaws)
    {
        const int FillersPerLaw = 8;
        HashSet<string> offered = new(StringComparer.Ordinal);
        int offers = 0;
        string firstPrediction = "";
        ulong firstProof = 0;
        List<EmlVerifiedLawSupportReceipt> supportReceipts = new(admittedLaws.Count);
        List<(string Digest, int Start, int Count)> supportRanges = new(admittedLaws.Count);
        List<EmlPredictionID> executionPredictions = new();

        EmlFormFarmPlan formFarmPlan = EmlFormFarm.CreatePlan(_sieve, _lawStore, 32);
        EmlFormFarmResult formFarm = EmlFormFarm.Execute(_sieve, formFarmPlan, retainAdmissions: true);
        _lawStore.RecordFormFarm(in formFarm);
        int firstMint = _sieve.NewMints.Count;

        List<EmlGen.Chunk> chunks = EmlGen.PureChunks(grammar);
        ulong rng = _seed ^ (ulong)_lawExactPredictionHighWater * 0x9E3779B97F4A7C15UL;
        StringBuilder builder = new();
        List<(string Toks, int Weight, int DeltaH)> pool = new();
        for (int lawIndex = 0; lawIndex < admittedLaws.Count; lawIndex++)
        {
            LawFrontierSource source = admittedLaws[lawIndex];
            if (source.OpportunityEvents.Length == 0 || source.OpportunityEvents.Length > 1024) continue;
            source.Support.Validate();
            if (!source.Support.HasWorldOpportunity)
                throw new InvalidDataException("law frontier source omitted its world support receipt");
            EmlVerifiedLaw law = source.Law;
            bool contributed = false;
            int sourceMintStart = _sieve.MintLog.Count;
            EmlOfferContext context = new(source.OpportunityEvents);
            for (int fillerIndex = 0; fillerIndex < FillersPerLaw; fillerIndex++)
            {
                string filler = EmlGen.Sample(chunks, 6, 24, 4, 0.25, ref rng, builder, pool);
                if (!EmlLawInstantiation.TryCreate(law.Law.Template, filler, out EmlLawInstantiation instance)) continue;
                bool leftFresh = offered.Add(instance.LeftRpn);
                bool rightFresh = offered.Add(instance.RightRpn);
                if (!leftFresh && !rightFresh) continue;
                contributed = true;
                if (firstPrediction.Length == 0)
                {
                    firstPrediction = instance.LeftRpn + " = " + instance.RightRpn;
                    firstProof = law.Proof.OccurrenceDigest;
                }
                // EmlSieve's exact identity mint is `prog = canonical`. Seed the
                // replacement first so the reducing law side is the exact claim
                // LHS, preserving its claim ID as rung-0 proof/closure custody.
                if (rightFresh) { _sieve.Offer(instance.RightRpn, in context); offers++; }
                if (leftFresh) { _sieve.Offer(instance.LeftRpn, in context); offers++; }
            }
            if (contributed && _sieve.MintLog.Count > sourceMintStart)
            {
                int rangeStart = -1;
                int rangeCount = 0;
                for (int claimIndex = sourceMintStart; claimIndex < _sieve.MintLog.Count; claimIndex++)
                {
                    EmlMint generated = _sieve.MintLog[claimIndex];
                    bool exact = generated.Grade == 'E'
                        && EmlPrediction.TryParse(generated.Line, out EmlPrediction claim)
                        && claim.RhsRpn;
                    if (exact)
                    {
                        executionPredictions.Add(new EmlPredictionID(claimIndex));
                        if (rangeStart < 0) rangeStart = claimIndex;
                        rangeCount++;
                    }
                    else if (rangeStart >= 0)
                    {
                        supportRanges.Add((source.Support.Digest, rangeStart, rangeCount));
                        rangeStart = -1;
                        rangeCount = 0;
                    }
                }
                if (rangeStart >= 0) supportRanges.Add((source.Support.Digest, rangeStart, rangeCount));
                if (supportRanges.Any(range => string.Equals(range.Digest, source.Support.Digest, StringComparison.Ordinal)))
                    supportReceipts.Add(source.Support);
            }
        }
        int mints = _sieve.NewMints.Count - firstMint;
        _lawStore.RecordGeneration(offers, mints);
        if (offers > 0 && executionPredictions.Count > 0 && supportReceipts.Count > 0)
        {
            TapeEventID executionEventID = TapePacketCreator.AppendEmlLawExecution(tape, journal, step, offers, executionPredictions.Count, firstPrediction, firstProof,
                in formFarm, executionPredictions, supportReceipts, supportRanges);
            for (int supportIndex = 0; supportIndex < supportReceipts.Count; supportIndex++)
            {
                List<int> generatedPredictionIDs = new();
                for (int rangeIndex = 0; rangeIndex < supportRanges.Count; rangeIndex++)
                {
                    (string digest, int start, int count) = supportRanges[rangeIndex];
                    if (!string.Equals(digest, supportReceipts[supportIndex].Digest, StringComparison.Ordinal)) continue;
                    for (int claimOffset = 0; claimOffset < count; claimOffset++)
                        generatedPredictionIDs.Add(start + claimOffset);
                }
                _lawStore.BindVerifiedLawSupportExecution(
                    supportReceipts[supportIndex], executionEventID, generatedPredictionIDs);
            }
            for (int claimIndex = 0; claimIndex < executionPredictions.Count; claimIndex++)
                _sieve.BindPredictionEvent(executionPredictions[claimIndex], executionEventID);
            for (int executionPredictionIndex = 0; executionPredictionIndex < executionPredictions.Count; executionPredictionIndex++)
            {
                EmlPredictionID sourcePredictionID = executionPredictions[executionPredictionIndex];
                EmlMint mint = _sieve.MintLog[sourcePredictionID.Value];
                if (mint.Grade != 'E' || !EmlPrediction.TryParse(mint.Line, out EmlPrediction claim) || !claim.RhsRpn
                    || !_sieve.TryReadMintOpportunityEvents(sourcePredictionID, out IReadOnlyList<TapeEventID> supports)
                    || !_lawStore.HasPredictionBoundGuardedRankReducingRewrite(sourcePredictionID, _sieve)) continue;
                _sieve.RegisterExactCompositionObligation(sourcePredictionID, supports, executionEventID);
            }
        }
        // The semantic store is the law miner's input. Copying every generated theorem onto the grammar tape
        // repeats one procedure shape per filler and turns successful induction into structural self-feed.
        _sieve.DrainNewMints();
        _sieve.DrainSemanticDeltas();
        _accretionEvaluatorStart = _sieve.EvaluatorClock.ProgramPointEvaluations;
        return supportReceipts;
    }

    private void SaveLawState(CkptWriter writer)
    {
        if (!_lawStatePresent) return;
        writer.Section(LawTag);
        writer.I32(_lawExactPredictionHighWater);
        writer.I32(_lawCaptureIndex);
        _lawStore.Save(writer);
    }

    private void LoadLawState(CkptReader reader)
    {
        if (!reader.TryExpect(LawTag))
        {
            // Pre-law and disarmed checkpoints intentionally omitted this optional section.
            // Leave the store empty; the following process-constant tag remains unread for its owner.
            _lawStatePresent = false;
            _lawExactPredictionHighWater = 0;
            _lawCaptureIndex = 0;
            _lawStore.Clear();
            return;
        }
        _lawStatePresent = true;
        _lawExactPredictionHighWater = reader.I32();
        _lawCaptureIndex = reader.I32();
        _lawStore.Load(reader);
    }
}
