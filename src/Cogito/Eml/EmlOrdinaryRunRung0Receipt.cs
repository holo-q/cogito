namespace Cogito;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// The ordinary EML turnstile's typed rung-0 view. It is a record emitted by the run;
/// adjudication reads it later and never participates in admission or authority.
internal readonly record struct EmlOrdinaryRunRung0Receipt(
    EmlRung0Modes Rung0,
    EmlRematchAssayStatuses Assay,
    EmlRematchPowerStatuses Power,
    int Opportunities,
    int CarrierBoundCandidates,
    int GuardEligibleCandidates,
    int PaidAttempts,
    int AttemptedCandidates,
    int Compositions,
    int ZeroEvaluatorCompositions,
    int Audits,
    int AgreedAudits,
    int DisagreedAudits,
    int NotSelectedAudits,
    int RelationNullExecutions,
    int RelationNullDivergences,
    int RelationNullAuthorityPredictions,
    int RelationNullPairsConsidered,
    int RelationNullPairsCreated,
    int RelationNullRejectNoCarrier,
    int RelationNullRejectShape,
    int RelationNullRejectGrade,
    string CompositionDigest,
    string SourceDigest,
    string ConfigDigest,
    int SchemaVersion,
    string Digest)
{
    internal bool HasAuditStatusCensus => SchemaVersion >= 2;
    internal bool HasFunnelCensus => SchemaVersion >= 3;
    internal bool HasCleanSampledAudits
        => HasAuditStatusCensus && AgreedAudits > 0 && DisagreedAudits == 0;

    // Historical journals predate the status census. Keep them readable, but never let them
    // satisfy the powered paired-gate line: the adjudicator requires SchemaVersion 2.
    public static EmlOrdinaryRunRung0Receipt Create(
        EmlRung0Modes rung0,
        EmlRematchAssayStatuses assay,
        EmlRematchPowerStatuses power,
        int opportunities,
        int derivations,
        int zeroEvaluatorCompositions,
        int audits,
        int relationNullExecutions,
        int relationNullDivergences,
        int relationNullAuthorityPredictions,
        string derivationDigest,
        string sourceDigest,
        string configDigest)
        => CreateLegacy(
            rung0, assay, power, opportunities, derivations, zeroEvaluatorCompositions, audits,
            relationNullExecutions, relationNullDivergences, relationNullAuthorityPredictions,
            derivationDigest, sourceDigest, configDigest);

    public static EmlOrdinaryRunRung0Receipt Create(
        EmlRung0Modes rung0,
        EmlRematchAssayStatuses assay,
        EmlRematchPowerStatuses power,
        int opportunities,
        int derivations,
        int zeroEvaluatorCompositions,
        int audits,
        int agreedAudits,
        int disagreedAudits,
        int notSelectedAudits,
        int relationNullExecutions,
        int relationNullDivergences,
        int relationNullAuthorityPredictions,
        string derivationDigest,
        string sourceDigest,
        string configDigest)
    {
        string canonical = Canonical(rung0, assay, power, opportunities, 0, 0, 0, 0,
            derivations, zeroEvaluatorCompositions, audits, agreedAudits, disagreedAudits, notSelectedAudits,
            relationNullExecutions, relationNullDivergences, relationNullAuthorityPredictions, 0, 0, 0, 0, 0,
            derivationDigest, sourceDigest, configDigest, schemaVersion: 2);
        return new(rung0, assay, power, opportunities, 0, 0, 0, 0, derivations, zeroEvaluatorCompositions, audits,
            agreedAudits, disagreedAudits, notSelectedAudits,
            relationNullExecutions, relationNullDivergences, relationNullAuthorityPredictions, 0, 0, 0, 0, 0,
            derivationDigest, sourceDigest, configDigest, 2,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
    }

    public static EmlOrdinaryRunRung0Receipt Create(
        EmlRung0Modes rung0,
        EmlRematchAssayStatuses assay,
        EmlRematchPowerStatuses power,
        int opportunities,
        int carrierBoundCandidates,
        int guardEligibleCandidates,
        int fundedAttempts,
        int attemptedCandidates,
        int derivations,
        int zeroEvaluatorCompositions,
        int audits,
        int agreedAudits,
        int disagreedAudits,
        int notSelectedAudits,
        int relationNullExecutions,
        int relationNullDivergences,
        int relationNullAuthorityPredictions,
        int relationNullPairsConsidered,
        int relationNullPairsCreated,
        int relationNullRejectNoCarrier,
        int relationNullRejectShape,
        int relationNullRejectGrade,
        string derivationDigest,
        string sourceDigest,
        string configDigest)
    {
        string canonical = Canonical(rung0, assay, power, opportunities,
            carrierBoundCandidates, guardEligibleCandidates, fundedAttempts, attemptedCandidates,
            derivations, zeroEvaluatorCompositions, audits, agreedAudits, disagreedAudits, notSelectedAudits,
            relationNullExecutions, relationNullDivergences, relationNullAuthorityPredictions,
            relationNullPairsConsidered, relationNullPairsCreated,
            relationNullRejectNoCarrier, relationNullRejectShape, relationNullRejectGrade,
            derivationDigest, sourceDigest, configDigest, schemaVersion: 3);
        return new(rung0, assay, power, opportunities,
            carrierBoundCandidates, guardEligibleCandidates, fundedAttempts, attemptedCandidates,
            derivations, zeroEvaluatorCompositions, audits, agreedAudits, disagreedAudits, notSelectedAudits,
            relationNullExecutions, relationNullDivergences, relationNullAuthorityPredictions,
            relationNullPairsConsidered, relationNullPairsCreated,
            relationNullRejectNoCarrier, relationNullRejectShape, relationNullRejectGrade,
            derivationDigest, sourceDigest, configDigest, 3,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
    }

    private static EmlOrdinaryRunRung0Receipt CreateLegacy(
        EmlRung0Modes rung0,
        EmlRematchAssayStatuses assay,
        EmlRematchPowerStatuses power,
        int opportunities,
        int derivations,
        int zeroEvaluatorCompositions,
        int audits,
        int relationNullExecutions,
        int relationNullDivergences,
        int relationNullAuthorityPredictions,
        string derivationDigest,
        string sourceDigest,
        string configDigest)
    {
        string canonical = Canonical(rung0, assay, power, opportunities, 0, 0, 0, 0,
            derivations, zeroEvaluatorCompositions, audits, 0, 0, 0,
            relationNullExecutions, relationNullDivergences, relationNullAuthorityPredictions, 0, 0, 0, 0, 0,
            derivationDigest, sourceDigest, configDigest, schemaVersion: 1);
        return new(rung0, assay, power, opportunities, 0, 0, 0, 0, derivations, zeroEvaluatorCompositions, audits,
            0, 0, 0,
            relationNullExecutions, relationNullDivergences, relationNullAuthorityPredictions, 0, 0, 0, 0, 0,
            derivationDigest, sourceDigest, configDigest, 1,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
    }

    public bool IsValid
        => Digest.Length == 64
            && SchemaVersion is 1 or 2 or 3
            && Opportunities >= 0 && CarrierBoundCandidates >= 0 && GuardEligibleCandidates >= 0
            && PaidAttempts >= 0 && AttemptedCandidates >= 0
            && Compositions >= 0 && ZeroEvaluatorCompositions >= 0 && Audits >= 0
            && AgreedAudits >= 0 && DisagreedAudits >= 0 && NotSelectedAudits >= 0
            && RelationNullExecutions >= 0 && RelationNullDivergences >= 0 && RelationNullAuthorityPredictions >= 0
            && RelationNullPairsConsidered >= 0 && RelationNullPairsCreated >= 0
            && RelationNullRejectNoCarrier >= 0 && RelationNullRejectShape >= 0 && RelationNullRejectGrade >= 0
            // v3 carries candidate-attempt census; schema 1/2 must retain their opportunity-only bound.
            && (SchemaVersion >= 3 ? Compositions <= AttemptedCandidates : Compositions <= Opportunities)
            && ZeroEvaluatorCompositions <= Compositions
            && RelationNullDivergences <= RelationNullExecutions
            && RelationNullAuthorityPredictions <= RelationNullExecutions
            && GuardEligibleCandidates <= CarrierBoundCandidates
            && RelationNullPairsCreated <= RelationNullPairsConsidered
            && RelationNullPairsCreated + RelationNullRejectNoCarrier + RelationNullRejectShape + RelationNullRejectGrade
                <= RelationNullPairsConsidered + RelationNullPairsCreated
            && (SchemaVersion < 2 || Audits == AgreedAudits + DisagreedAudits + NotSelectedAudits)
            && string.Equals(Digest, Recreate().Digest, StringComparison.Ordinal);

    public string Canonical()
        => Canonical(Rung0, Assay, Power, Opportunities,
            CarrierBoundCandidates, GuardEligibleCandidates, PaidAttempts, AttemptedCandidates,
            Compositions, ZeroEvaluatorCompositions, Audits,
            AgreedAudits, DisagreedAudits, NotSelectedAudits,
            RelationNullExecutions, RelationNullDivergences, RelationNullAuthorityPredictions,
            RelationNullPairsConsidered, RelationNullPairsCreated,
            RelationNullRejectNoCarrier, RelationNullRejectShape, RelationNullRejectGrade,
            CompositionDigest, SourceDigest, ConfigDigest, SchemaVersion);

    private EmlOrdinaryRunRung0Receipt Recreate()
        => SchemaVersion >= 3
            ? Create(Rung0, Assay, Power, Opportunities,
                CarrierBoundCandidates, GuardEligibleCandidates, PaidAttempts, AttemptedCandidates,
                Compositions, ZeroEvaluatorCompositions, Audits,
                AgreedAudits, DisagreedAudits, NotSelectedAudits,
                RelationNullExecutions, RelationNullDivergences, RelationNullAuthorityPredictions,
                RelationNullPairsConsidered, RelationNullPairsCreated,
                RelationNullRejectNoCarrier, RelationNullRejectShape, RelationNullRejectGrade,
                CompositionDigest, SourceDigest, ConfigDigest)
            : SchemaVersion >= 2
            ? Create(Rung0, Assay, Power, Opportunities, Compositions, ZeroEvaluatorCompositions, Audits,
                AgreedAudits, DisagreedAudits, NotSelectedAudits,
                RelationNullExecutions, RelationNullDivergences, RelationNullAuthorityPredictions,
                CompositionDigest, SourceDigest, ConfigDigest)
            : CreateLegacy(Rung0, Assay, Power, Opportunities, Compositions, ZeroEvaluatorCompositions, Audits,
                RelationNullExecutions, RelationNullDivergences, RelationNullAuthorityPredictions,
                CompositionDigest, SourceDigest, ConfigDigest);

    private static string Canonical(
        EmlRung0Modes rung0,
        EmlRematchAssayStatuses assay,
        EmlRematchPowerStatuses power,
        int opportunities,
        int carrierBoundCandidates,
        int guardEligibleCandidates,
        int fundedAttempts,
        int attemptedCandidates,
        int derivations,
        int zeroEvaluatorCompositions,
        int audits,
        int agreedAudits,
        int disagreedAudits,
        int notSelectedAudits,
        int relationNullExecutions,
        int relationNullDivergences,
        int relationNullAuthorityPredictions,
        int relationNullPairsConsidered,
        int relationNullPairsCreated,
        int relationNullRejectNoCarrier,
        int relationNullRejectShape,
        int relationNullRejectGrade,
        string derivationDigest,
        string sourceDigest,
        string configDigest,
        int schemaVersion)
    {
        if (schemaVersion >= 3)
            return string.Join('|',
                rung0, assay, power,
                opportunities.ToString(CultureInfo.InvariantCulture),
                carrierBoundCandidates.ToString(CultureInfo.InvariantCulture),
                guardEligibleCandidates.ToString(CultureInfo.InvariantCulture),
                fundedAttempts.ToString(CultureInfo.InvariantCulture),
                attemptedCandidates.ToString(CultureInfo.InvariantCulture),
                derivations.ToString(CultureInfo.InvariantCulture),
                zeroEvaluatorCompositions.ToString(CultureInfo.InvariantCulture),
                audits.ToString(CultureInfo.InvariantCulture),
                agreedAudits.ToString(CultureInfo.InvariantCulture),
                disagreedAudits.ToString(CultureInfo.InvariantCulture),
                notSelectedAudits.ToString(CultureInfo.InvariantCulture),
                relationNullExecutions.ToString(CultureInfo.InvariantCulture),
                relationNullDivergences.ToString(CultureInfo.InvariantCulture),
                relationNullAuthorityPredictions.ToString(CultureInfo.InvariantCulture),
                relationNullPairsConsidered.ToString(CultureInfo.InvariantCulture),
                relationNullPairsCreated.ToString(CultureInfo.InvariantCulture),
                relationNullRejectNoCarrier.ToString(CultureInfo.InvariantCulture),
                relationNullRejectShape.ToString(CultureInfo.InvariantCulture),
                relationNullRejectGrade.ToString(CultureInfo.InvariantCulture),
                derivationDigest, sourceDigest, configDigest, "eml-ordinary-rung0-v3");
        if (schemaVersion >= 2)
            return string.Join('|',
                rung0, assay, power,
                opportunities.ToString(CultureInfo.InvariantCulture),
                derivations.ToString(CultureInfo.InvariantCulture),
                zeroEvaluatorCompositions.ToString(CultureInfo.InvariantCulture),
                audits.ToString(CultureInfo.InvariantCulture),
                agreedAudits.ToString(CultureInfo.InvariantCulture),
                disagreedAudits.ToString(CultureInfo.InvariantCulture),
                notSelectedAudits.ToString(CultureInfo.InvariantCulture),
                relationNullExecutions.ToString(CultureInfo.InvariantCulture),
                relationNullDivergences.ToString(CultureInfo.InvariantCulture),
                relationNullAuthorityPredictions.ToString(CultureInfo.InvariantCulture),
                derivationDigest, sourceDigest, configDigest, "eml-ordinary-rung0-v2");
        return string.Join('|',
            rung0, assay, power,
            opportunities.ToString(CultureInfo.InvariantCulture),
            derivations.ToString(CultureInfo.InvariantCulture),
            zeroEvaluatorCompositions.ToString(CultureInfo.InvariantCulture),
            audits.ToString(CultureInfo.InvariantCulture),
            relationNullExecutions.ToString(CultureInfo.InvariantCulture),
            relationNullDivergences.ToString(CultureInfo.InvariantCulture),
            relationNullAuthorityPredictions.ToString(CultureInfo.InvariantCulture),
            derivationDigest, sourceDigest, configDigest, "eml-ordinary-rung0-v1");
    }

    internal static bool VerifyFixture()
    {
        if (!EmlRung0Assay.VerifySamplerFixture()) return false;
        EmlOrdinaryRunRung0Receipt powered = Create(
            EmlRung0Modes.Armed, EmlRematchAssayStatuses.Exact, EmlRematchPowerStatuses.Powered,
            opportunities: 1, derivations: 1, zeroEvaluatorCompositions: 1, audits: 1,
            agreedAudits: 1, disagreedAudits: 0, notSelectedAudits: 0,
            relationNullExecutions: 1, relationNullDivergences: 1, relationNullAuthorityPredictions: 0,
            "D", "S", "C");
        EmlOrdinaryRunRung0Receipt funnel = Create(
            EmlRung0Modes.Armed, EmlRematchAssayStatuses.Exact, EmlRematchPowerStatuses.Unpowered,
            opportunities: 1, carrierBoundCandidates: 2, guardEligibleCandidates: 2,
            fundedAttempts: 2, attemptedCandidates: 2, derivations: 0, zeroEvaluatorCompositions: 0,
            audits: 0, agreedAudits: 0, disagreedAudits: 0, notSelectedAudits: 0,
            relationNullExecutions: 0, relationNullDivergences: 0, relationNullAuthorityPredictions: 0,
            relationNullPairsConsidered: 1, relationNullPairsCreated: 0,
            relationNullRejectNoCarrier: 0, relationNullRejectShape: 1, relationNullRejectGrade: 0,
            "D", "S", "C");
        EmlOrdinaryRunRung0Receipt disabled = Create(
            EmlRung0Modes.Disabled, EmlRematchAssayStatuses.Invalid, EmlRematchPowerStatuses.Unpowered,
            0, 0, 0, 0, 0, 0, 0, "", "S", "F");
        EmlOrdinaryRunRung0Receipt disagreed = Create(
            EmlRung0Modes.Armed, EmlRematchAssayStatuses.Exact, EmlRematchPowerStatuses.Unpowered,
            1, 1, 1, 1, 0, 1, 0, 0, 0, 0, "D", "S", "C");
        EmlOrdinaryRunRung0Receipt candidateCensus = Create(
            EmlRung0Modes.Armed, EmlRematchAssayStatuses.Exact, EmlRematchPowerStatuses.Unpowered,
            opportunities: 1, carrierBoundCandidates: 3, guardEligibleCandidates: 2,
            fundedAttempts: 3, attemptedCandidates: 3, derivations: 2, zeroEvaluatorCompositions: 2,
            audits: 2, agreedAudits: 2, disagreedAudits: 0, notSelectedAudits: 0,
            relationNullExecutions: 0, relationNullDivergences: 0, relationNullAuthorityPredictions: 0,
            relationNullPairsConsidered: 0, relationNullPairsCreated: 0,
            relationNullRejectNoCarrier: 0, relationNullRejectShape: 0, relationNullRejectGrade: 0,
            "D", "S", "C");
        EmlOrdinaryRunRung0Receipt attemptedCensusCorrupt = candidateCensus with { AttemptedCandidates = 1 };
        EmlOrdinaryRunRung0Receipt corrupt = powered with { Digest = powered.Digest[..^1] + "0" };
        EmlOrdinaryRunRung0Receipt censusCorrupt = powered with { DisagreedAudits = 1 };
        return powered.IsValid && funnel.IsValid && funnel.HasFunnelCensus && powered.RelationNullAuthorityPredictions == 0
            && powered.HasCleanSampledAudits && disagreed.IsValid && !disagreed.HasCleanSampledAudits
            && candidateCensus.IsValid && !attemptedCensusCorrupt.IsValid
            && disabled.IsValid && !disabled.HasAuditStatusCensus && disabled.Rung0 == EmlRung0Modes.Disabled
            && !corrupt.IsValid && !censusCorrupt.IsValid;
    }
}
