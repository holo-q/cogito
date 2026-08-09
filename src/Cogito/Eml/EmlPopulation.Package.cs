namespace Cogito;

using Cogito.Induct;

internal interface IEmlPopulationRONCodec
{
    byte[] EncodeCohort(EmlCohortSnapshot cohort);
    EmlCohortSnapshot DecodeCohort(ReadOnlySpan<byte> bytes);
    byte[] EncodePackage(EmlLawPackage package);
    EmlLawPackage DecodePackage(ReadOnlySpan<byte> bytes);
}
internal sealed class EmlLawPackage
{
    private EmlLawPackage(
        ImportPackageID package,
        MindID exporter,
        MindLineageID lineage,
        CheckpointID checkpoint,
        EmlEvaluatorID evaluator,
        EmlLawClassID lawClass,
        EmlLaw law,
        EmlLawBehaviorCertificate certificate,
        EmlLawProof proof,
        int signatureDigits,
        int templateCostBits,
        string causalProcedureDigest)
    {
        Package = package;
        Exporter = exporter;
        Lineage = lineage;
        Checkpoint = checkpoint;
        Evaluator = evaluator;
        LawClass = lawClass;
        Law = law;
        Certificate = certificate;
        Proof = proof;
        SignatureDigits = signatureDigits;
        TemplateCostBits = templateCostBits;
        CausalProcedureDigest = causalProcedureDigest;
    }

    public ImportPackageID Package { get; }
    public MindID Exporter { get; }
    public MindLineageID Lineage { get; }
    public CheckpointID Checkpoint { get; }
    public EmlEvaluatorID Evaluator { get; }
    public EmlLawClassID LawClass { get; }
    public EmlLaw Law { get; }
    public EmlLawBehaviorCertificate Certificate { get; }
    public EmlLawProof Proof { get; }
    public int SignatureDigits { get; }
    public int TemplateCostBits { get; }
    public string CausalProcedureDigest { get; }

    public static EmlLawPackage Create(
        EmlMindIdentity exporter,
        CheckpointID checkpoint,
        EmlEvaluatorID evaluator,
        EmlVerifiedLaw law,
        int signatureDigits,
        string causalProcedureDigest)
    {
        EmlLawClassID lawClass = CreateLawClassID(law.Certificate);
        ImportPackageID package = CreatePackageID(
            exporter.Mind,
            exporter.Lineage,
            checkpoint,
            evaluator,
            lawClass,
            law.Law,
            law.Certificate,
            law.Proof,
            signatureDigits,
            law.TemplateCostBits,
            causalProcedureDigest);
        return new EmlLawPackage(
            package,
            exporter.Mind,
            exporter.Lineage,
            checkpoint,
            evaluator,
            lawClass,
            law.Law,
            law.Certificate,
            law.Proof,
            signatureDigits,
            law.TemplateCostBits,
            causalProcedureDigest);
    }

    internal static EmlLawPackage Restore(
        ImportPackageID package,
        MindID exporter,
        MindLineageID lineage,
        CheckpointID checkpoint,
        EmlEvaluatorID evaluator,
        EmlLawClassID lawClass,
        EmlLaw law,
        EmlLawBehaviorCertificate certificate,
        EmlLawProof proof,
        int signatureDigits,
        int templateCostBits,
        string causalProcedureDigest)
    {
        EmlLawPackage restored = new(
            package,
            exporter,
            lineage,
            checkpoint,
            evaluator,
            lawClass,
            law,
            certificate,
            proof,
            signatureDigits,
            templateCostBits,
            causalProcedureDigest);
        if (!restored.HasValidIdentity())
            throw new InvalidDataException("EML import package identity does not match its sealed contents");
        return restored;
    }

    public bool TryVerify(EmlEvaluatorID localEvaluator, out EmlVerifiedLaw? verified)
    {
        verified = null;
        if (Evaluator != localEvaluator || !HasValidIdentity()) return false;
        return EmlVerifiedLaw.TryReverifyPackage(
            Law,
            Certificate,
            Proof,
            SignatureDigits,
            TemplateCostBits,
            out verified);
    }

    public bool HasValidIdentity()
    {
        if (string.IsNullOrWhiteSpace(Exporter.Value)
            || string.IsNullOrWhiteSpace(Lineage.Value)
            || string.IsNullOrWhiteSpace(Checkpoint.Value)
            || string.IsNullOrWhiteSpace(Evaluator.Value)
            || string.IsNullOrWhiteSpace(CausalProcedureDigest)) return false;
        EmlLawClassID expectedClass = CreateLawClassID(Certificate);
        if (LawClass != expectedClass) return false;
        ImportPackageID expectedPackage = CreatePackageID(
            Exporter,
            Lineage,
            Checkpoint,
            Evaluator,
            LawClass,
            Law,
            Certificate,
            Proof,
            SignatureDigits,
            TemplateCostBits,
            CausalProcedureDigest);
        return Package == expectedPackage;
    }

    internal static EmlLawClassID CreateLawClassID(in EmlLawBehaviorCertificate certificate)
    {
        EmlPopulationHash hash = new("cogito/eml/law-class/v1");
        AppendCertificate(hash, certificate);
        string digest = hash.Finish();
        hash.Dispose();
        return new EmlLawClassID(digest);
    }

    private static ImportPackageID CreatePackageID(
        MindID exporter,
        MindLineageID lineage,
        CheckpointID checkpoint,
        EmlEvaluatorID evaluator,
        EmlLawClassID lawClass,
        in EmlLaw law,
        in EmlLawBehaviorCertificate certificate,
        in EmlLawProof proof,
        int signatureDigits,
        int templateCostBits,
        string causalProcedureDigest)
    {
        EmlPopulationHash hash = new("cogito/eml/law-package/v1");
        hash.Append(exporter.Value);
        hash.Append(lineage.Value);
        hash.Append(checkpoint.Value);
        hash.Append(evaluator.Value);
        hash.Append(lawClass.Value);
        AppendLaw(hash, law);
        AppendCertificate(hash, certificate);
        AppendProof(hash, proof);
        hash.Append(signatureDigits);
        hash.Append(templateCostBits);
        hash.Append(causalProcedureDigest);
        string digest = hash.Finish();
        hash.Dispose();
        return new ImportPackageID(digest);
    }

    private static void AppendLaw(EmlPopulationHash hash, in EmlLaw law)
    {
        hash.Append(law.Template);
        hash.Append(law.CertificateClasses);
        hash.Append(law.Fillers);
        hash.Append(BitConverter.DoubleToInt64Bits(law.MdlGain));
        hash.Append(law.OccurrenceCheckFiller);
        hash.Append(law.OccurrenceCheckPrediction);
    }

    private static void AppendCertificate(EmlPopulationHash hash, in EmlLawBehaviorCertificate certificate)
    {
        AppendSignature(hash, certificate.AtOne);
        AppendSignature(hash, certificate.AtX);
        AppendSignature(hash, certificate.AtY);
    }

    private static void AppendProof(EmlPopulationHash hash, in EmlLawProof proof)
    {
        hash.Append(proof.OccurrenceDigest);
        hash.Append(proof.AbsentFiller);
        hash.Append(proof.OccurrenceCheckPrediction);
        hash.Append(proof.VerifierVersion);
        AppendEvidence(hash, proof.AtOne);
        AppendEvidence(hash, proof.AtX);
        AppendEvidence(hash, proof.AtY);
        AppendEvidence(hash, proof.AtAbsentFiller);
        hash.Append(proof.DomainGuardDigest);
        hash.Append(proof.DomainGuards?.Canonical() ?? string.Empty);
        hash.Append(proof.GuardWitness.Canonical());
        hash.Append(proof.SearchRevision);
        hash.Append(proof.SearchBudget);
        hash.Append(proof.CompositionDigest);
        hash.Append(proof.GuardScheme);
    }

    private static void AppendSignature(EmlPopulationHash hash, in EmlSig signature)
    {
        hash.Append(signature.R1);
        hash.Append(signature.I1);
        hash.Append(signature.R2);
        hash.Append(signature.I2);
    }

    private static void AppendEvidence(EmlPopulationHash hash, in EmlLawExactEvidence evidence)
    {
        hash.Append((int)evidence.Grade);
        hash.Append(evidence.Q12Home ? 1 : 0);
        hash.Append(evidence.Q12Regime ? 1 : 0);
        hash.Append(evidence.EnclosureColumns);
    }
}

public sealed partial class ReplayCalc
{
    internal void AppendLawPackages(
        EmlMindIdentity exporter,
        CheckpointID checkpoint,
        EmlEvaluatorID evaluator,
        string causalProcedureDigest,
        List<EmlLawPackage> packages)
    {
        List<EmlLawPackage> exported = new(_lawStore.Count);
        foreach (SemanticCASClass<EmlVerifiedLaw> lawClass in _lawStore.Classes.Values)
        {
            exported.Add(EmlLawPackage.Create(
                exporter,
                checkpoint,
                evaluator,
                lawClass.Rep,
                _sieve.SignatureDigits,
                causalProcedureDigest));
        }
        exported.Sort(static (left, right) => string.CompareOrdinal(left.Package.Value, right.Package.Value));
        packages.AddRange(exported);
    }
}
