namespace Cogito;

using System.Globalization;
using Ronmamon;

internal sealed class EmlPopulationRONCodec : IEmlPopulationRONCodec
{
    private const int SchemaVersion = 2;

    public static EmlPopulationRONCodec Instance { get; } = new();

    private EmlPopulationRONCodec() { }

    public byte[] EncodeCohort(EmlCohortSnapshot cohort)
    {
        EmlRONCohort document = CreateCohortDocument(cohort);
        return RonSerializer.SerializeToUtf8(in document);
    }

    public EmlCohortSnapshot DecodeCohort(ReadOnlySpan<byte> bytes)
    {
        EmlRONCohort document = RonSerializer.Deserialize<EmlRONCohort>(bytes);
        RequireVersion(document.schemaVersion, "cohort");
        return RestoreCohort(document);
    }

    public byte[] EncodePackage(EmlLawPackage package)
    {
        EmlRONPackage document = CreatePackageDocument(package);
        return RonSerializer.SerializeToUtf8(in document);
    }

    public EmlLawPackage DecodePackage(ReadOnlySpan<byte> bytes)
    {
        EmlRONPackage document = RonSerializer.Deserialize<EmlRONPackage>(bytes);
        RequireVersion(document.schemaVersion, "law package");
        return RestorePackage(document);
    }

    private static EmlRONCohort CreateCohortDocument(EmlCohortSnapshot cohort)
    {
        EmlRONCohort document = new()
        {
            schemaVersion = SchemaVersion,
            manifest = CreateManifestDocument(cohort.Manifest),
            residencyHorizon = cohort.ResidencyHorizon,
            epoch = cohort.Epoch,
            eventIndex = cohort.EventIndex,
        };
        for (int i = 0; i < cohort.Minds.Count; i++)
            document.minds.Add(CreateMindSnapshotDocument(cohort.Minds[i]));
        for (int i = 0; i < cohort.ChimeraClasses.Count; i++)
            document.chimeraClasses.Add(cohort.ChimeraClasses[i].Value);
        return document;
    }

    private static EmlRONManifest CreateManifestDocument(EmlCohortManifest manifest)
    {
        EmlRONManifest document = new()
        {
            cohort = manifest.Cohort.Value,
            evaluator = manifest.Evaluator.Value,
            configurationDigest = manifest.ConfigurationDigest,
            intakeDigest = manifest.IntakeDigest,
        };
        for (int i = 0; i < manifest.Minds.Count; i++)
        {
            EmlMindIdentity mind = manifest.Minds[i];
            document.minds.Add(new EmlRONMindIdentity
            {
                mind = mind.Mind.Value,
                lineage = mind.Lineage.Value,
                kind = mind.Kind,
                searchSeed = mind.SearchSeed.ToString("x16", CultureInfo.InvariantCulture),
                initialCheckpoint = mind.InitialCheckpoint.Value,
            });
        }
        return document;
    }

    private static EmlRONMindSnapshot CreateMindSnapshotDocument(EmlPopulationMindSnapshot mind)
    {
        EmlRONMindSnapshot document = new()
        {
            mind = mind.Mind.Value,
            checkpoint = mind.Checkpoint.Value,
            membrane = CreateMembraneDocument(mind.Membrane),
        };
        for (int i = 0; i < mind.IsolatedClasses.Count; i++)
            document.isolatedClasses.Add(mind.IsolatedClasses[i].Value);
        return document;
    }

    private static EmlRONMembrane CreateMembraneDocument(EmlMembraneSnapshot membrane)
    {
        EmlRONMembrane document = new()
        {
            host = membrane.Host.Value,
            evaluator = membrane.Evaluator.Value,
            residencyHorizon = membrane.ResidencyHorizon,
        };
        for (int i = 0; i < membrane.Laws.Count; i++)
            document.laws.Add(CreateMembraneLawDocument(membrane.Laws[i]));
        return document;
    }

    private static EmlRONMembraneLaw CreateMembraneLawDocument(EmlMembraneLawSnapshot law)
    {
        EmlRONMembraneLaw document = new()
        {
            lawClass = law.LawClass.Value,
            residency = law.Residency,
            canonicalDeltas = law.CanonicalDeltas,
            trialStartedAt = law.TrialStartedAt,
            lastChangedAt = law.LastChangedAt,
            representativePackage = law.RepresentativePackage?.Value,
        };
        for (int i = 0; i < law.Packages.Count; i++)
        {
            EmlMembranePackageSnapshot package = law.Packages[i];
            document.packages.Add(new EmlRONMembranePackage
            {
                package = CreatePackageDocument(package.Package),
                admittedAt = package.AdmittedAt,
            });
        }
        return document;
    }

    private static EmlRONPackage CreatePackageDocument(EmlLawPackage package)
        => new()
        {
            schemaVersion = SchemaVersion,
            package = package.Package.Value,
            exporter = package.Exporter.Value,
            lineage = package.Lineage.Value,
            checkpoint = package.Checkpoint.Value,
            evaluator = package.Evaluator.Value,
            lawClass = package.LawClass.Value,
            law = CreateLawDocument(package.Law),
            certificate = CreateCertificateDocument(package.Certificate),
            proof = CreateProofDocument(package.Proof),
            signatureDigits = package.SignatureDigits,
            templateCostBits = package.TemplateCostBits,
            causalProcedureDigest = package.CausalProcedureDigest,
        };

    private static EmlRONLaw CreateLawDocument(in EmlLaw law)
        => new()
        {
            template = law.Template,
            certificateClasses = law.CertificateClasses,
            fillers = law.Fillers,
            mdlGain = law.MdlGain,
            verificationFiller = law.OccurrenceCheckFiller,
            verificationPrediction = law.OccurrenceCheckPrediction,
        };

    private static EmlRONCertificate CreateCertificateDocument(in EmlLawBehaviorCertificate certificate)
        => new()
        {
            atOne = CreateSignatureDocument(certificate.AtOne),
            atX = CreateSignatureDocument(certificate.AtX),
            atY = CreateSignatureDocument(certificate.AtY),
        };

    private static EmlRONSignature CreateSignatureDocument(in EmlSig signature)
        => new()
        {
            r1 = signature.R1,
            i1 = signature.I1,
            r2 = signature.R2,
            i2 = signature.I2,
        };

    private static EmlRONProof CreateProofDocument(in EmlLawProof proof)
        => new()
        {
            supportDigest = proof.OccurrenceDigest.ToString("x16", CultureInfo.InvariantCulture),
            absentFiller = proof.AbsentFiller,
            verificationPrediction = proof.OccurrenceCheckPrediction,
            verifierVersion = proof.VerifierVersion,
            atOne = CreateEvidenceDocument(proof.AtOne),
            atX = CreateEvidenceDocument(proof.AtX),
            atY = CreateEvidenceDocument(proof.AtY),
            atAbsentFiller = CreateEvidenceDocument(proof.AtAbsentFiller),
            domainGuardDigest = proof.DomainGuardDigest.ToString("x16", CultureInfo.InvariantCulture),
            domainGuards = CreateGuardDocuments(proof.DomainGuards ?? EmlDomainGuardSet.Empty),
            guardWitness = CreateGuardWitnessDocument(proof.GuardWitness),
            searchRevision = proof.SearchRevision,
            searchBudget = proof.SearchBudget,
            derivationDigest = proof.CompositionDigest.ToString("x16", CultureInfo.InvariantCulture),
            guardScheme = proof.GuardScheme,
        };

    private static List<EmlRONDomainAtom> CreateGuardDocuments(EmlDomainGuardSet guards)
    {
        List<EmlRONDomainAtom> documents = new(guards.Atoms.Count);
        for (int i = 0; i < guards.Atoms.Count; i++)
        {
            EmlDomainAtom atom = guards.Atoms[i];
            documents.Add(new EmlRONDomainAtom
            {
                kind = atom.Kind.ToString(), path = atom.Path.Steps,
                lower = atom.Lower, upper = atom.Upper,
                side = atom.Side.ToString(),
            });
        }
        return documents;
    }

    private static EmlRONGuardWitness CreateGuardWitnessDocument(in EmlGuardWitness witness)
    {
        EmlRONGuardWitness document = new()
        {
            matchedTerm = witness.MatchedTermRpn ?? string.Empty,
            substitution = witness.SubstitutionRpn ?? string.Empty,
            matchedPath = witness.MatchedPath.Steps,
            antecedent = witness.AntecedentRpn ?? string.Empty,
            consequent = witness.ConsequentRpn ?? string.Empty,
            realLower = witness.Enclosure.RealLower,
            realUpper = witness.Enclosure.RealUpper,
            imaginaryLower = witness.Enclosure.ImaginaryLower,
            imaginaryUpper = witness.Enclosure.ImaginaryUpper,
            logDefined = witness.Branch.LogDefined,
            enclosureCrossesNegativeRealCut = witness.Branch.EnclosureCrossesNegativeRealCut,
            expAfterLogRoundTrips = witness.Branch.ExpAfterLogRoundTrips,
            logAfterExpRoundTrips = witness.Branch.LogAfterExpRoundTrips,
            exponentialTurn = witness.Branch.ExponentialTurn,
            digest = witness.Digest.ToString("x16", CultureInfo.InvariantCulture),
        };
        if (witness.NodeFacts is { Count: > 0 })
            for (int i = 0; i < witness.NodeFacts.Count; i++)
            {
                EmlGuardNodeFact fact = witness.NodeFacts[i];
                document.nodeFacts.Add(new EmlRONGuardNodeFact
                {
                    side = fact.Side.ToString(),
                    path = fact.Path.Steps,
                    realLower = fact.Enclosure.RealLower,
                    realUpper = fact.Enclosure.RealUpper,
                    imaginaryLower = fact.Enclosure.ImaginaryLower,
                    imaginaryUpper = fact.Enclosure.ImaginaryUpper,
                    logDefined = fact.Branch.LogDefined,
                    enclosureCrossesNegativeRealCut = fact.Branch.EnclosureCrossesNegativeRealCut,
                    expAfterLogRoundTrips = fact.Branch.ExpAfterLogRoundTrips,
                    logAfterExpRoundTrips = fact.Branch.LogAfterExpRoundTrips,
                    exponentialTurn = fact.Branch.ExponentialTurn,
                });
            }
        return document;
    }

    private static EmlRONEvidence CreateEvidenceDocument(in EmlLawExactEvidence evidence)
        => new()
        {
            grade = evidence.Grade.ToString(),
            q12Home = evidence.Q12Home,
            q12Regime = evidence.Q12Regime,
            enclosureColumns = evidence.EnclosureColumns,
        };

    private static EmlCohortSnapshot RestoreCohort(EmlRONCohort document)
    {
        EmlCohortManifest manifest = RestoreManifest(document.manifest);
        List<EmlPopulationMindSnapshot> minds = new(document.minds.Count);
        for (int i = 0; i < document.minds.Count; i++)
            minds.Add(RestoreMindSnapshot(document.minds[i], manifest.Evaluator, document.residencyHorizon));
        List<EmlLawClassID> chimeraClasses = new(document.chimeraClasses.Count);
        for (int i = 0; i < document.chimeraClasses.Count; i++)
            chimeraClasses.Add(new EmlLawClassID(RequireText(document.chimeraClasses[i], "chimera law class")));
        return new EmlCohortSnapshot(
            manifest,
            RequirePositive(document.residencyHorizon, "cohort residency horizon"),
            RequireNonNegative(document.epoch, "cohort epoch"),
            RequireNonNegative(document.eventIndex, "cohort event index"),
            minds,
            chimeraClasses);
    }

    private static EmlCohortManifest RestoreManifest(EmlRONManifest document)
    {
        List<EmlMindIdentity> minds = new(document.minds.Count);
        for (int i = 0; i < document.minds.Count; i++)
        {
            EmlRONMindIdentity mind = document.minds[i];
            minds.Add(new EmlMindIdentity(
                new MindID(RequireText(mind.mind, "mind ID")),
                new MindLineageID(RequireText(mind.lineage, "lineage ID")),
                mind.kind,
                ParseHex(mind.searchSeed, "search seed"),
                new CheckpointID(RequireText(mind.initialCheckpoint, "initial checkpoint ID"))));
        }
        return EmlCohortManifest.Restore(
            new CohortID(RequireText(document.cohort, "cohort ID")),
            new EmlEvaluatorID(RequireText(document.evaluator, "evaluator ID")),
            RequireText(document.configurationDigest, "configuration digest"),
            RequireText(document.intakeDigest, "intake digest"),
            minds);
    }

    private static EmlPopulationMindSnapshot RestoreMindSnapshot(
        EmlRONMindSnapshot document,
        EmlEvaluatorID cohortEvaluator,
        long cohortResidencyHorizon)
    {
        MindID mind = new(RequireText(document.mind, "snapshot mind ID"));
        EmlMembraneSnapshot membrane = RestoreMembrane(document.membrane);
        if (membrane.Host != mind
            || membrane.Evaluator != cohortEvaluator
            || membrane.ResidencyHorizon != cohortResidencyHorizon)
            throw new InvalidDataException("EML membrane identity does not match its cohort mind");
        List<EmlLawClassID> isolatedClasses = new(document.isolatedClasses.Count);
        for (int i = 0; i < document.isolatedClasses.Count; i++)
            isolatedClasses.Add(new EmlLawClassID(RequireText(document.isolatedClasses[i], "isolated law class")));
        return new EmlPopulationMindSnapshot(
            mind,
            new CheckpointID(RequireText(document.checkpoint, "snapshot checkpoint ID")),
            isolatedClasses,
            membrane);
    }

    private static EmlMembraneSnapshot RestoreMembrane(EmlRONMembrane document)
    {
        MindID host = new(RequireText(document.host, "membrane host ID"));
        EmlEvaluatorID evaluator = new(RequireText(document.evaluator, "membrane evaluator ID"));
        List<EmlMembraneLawSnapshot> laws = new(document.laws.Count);
        for (int i = 0; i < document.laws.Count; i++)
            laws.Add(RestoreMembraneLaw(document.laws[i], host, evaluator));
        return new EmlMembraneSnapshot(
            host,
            evaluator,
            RequirePositive(document.residencyHorizon, "membrane residency horizon"),
            laws);
    }

    private static EmlMembraneLawSnapshot RestoreMembraneLaw(
        EmlRONMembraneLaw document,
        MindID host,
        EmlEvaluatorID evaluator)
    {
        EmlLawClassID lawClass = new(RequireText(document.lawClass, "membrane law class"));
        List<EmlMembranePackageSnapshot> packages = new(document.packages.Count);
        HashSet<ImportPackageID> packageIDs = new();
        for (int i = 0; i < document.packages.Count; i++)
        {
            EmlRONMembranePackage package = document.packages[i];
            RequireVersion(package.package.schemaVersion, "embedded law package");
            EmlLawPackage restored = RestorePackage(package.package);
            if (restored.LawClass != lawClass || restored.Exporter == host || restored.Evaluator != evaluator)
                throw new InvalidDataException("EML membrane package does not belong to its host law class");
            packageIDs.Add(restored.Package);
            packages.Add(new EmlMembranePackageSnapshot(
                restored,
                RequireNonNegative(package.admittedAt, "package admission event")));
        }
        ImportPackageID? representative = string.IsNullOrEmpty(document.representativePackage)
            ? null
            : new ImportPackageID(document.representativePackage);
        if (representative is ImportPackageID representativeID && !packageIDs.Contains(representativeID))
            throw new InvalidDataException("EML membrane representative is not one of its sealed packages");
        return new EmlMembraneLawSnapshot(
            lawClass,
            document.residency,
            RequireNonNegative(document.canonicalDeltas, "canonical delta count"),
            RequireNonNegative(document.trialStartedAt, "trial start event"),
            RequireNonNegative(document.lastChangedAt, "last change event"),
            representative,
            packages);
    }

    private static EmlLawPackage RestorePackage(EmlRONPackage document)
    {
        EmlRONLaw law = document.law;
        EmlRONCertificate certificate = document.certificate;
        EmlRONProof proof = document.proof;
        List<EmlDomainAtom> guardAtoms = new(proof.domainGuards.Count);
        for (int i = 0; i < proof.domainGuards.Count; i++)
        {
            EmlRONDomainAtom atom = proof.domainGuards[i];
            if (!Enum.TryParse(atom.kind, out EmlDomainGuardKinds kind))
                throw new InvalidDataException("EML RON proof contains an unknown domain guard kind");
            if (!Enum.TryParse(atom.side, out EmlGuardSides side))
                side = EmlGuardSides.Antecedent;
            guardAtoms.Add(new EmlDomainAtom(kind, new EmlPath(atom.path), atom.lower, atom.upper, side));
        }
        EmlDomainGuardSet guards = EmlDomainGuardSet.Create(guardAtoms);
        if (!string.Equals(guards.Digest.ToString("x16", CultureInfo.InvariantCulture), proof.domainGuardDigest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("EML RON proof domain guard digest mismatch");
        if (guards.IsGuarded && (proof.searchRevision < 1 || proof.searchBudget < 1))
            throw new InvalidDataException("EML RON guarded proof has no positive derivation revision/budget");
        if (proof.searchRevision < 0 || proof.searchBudget < 0)
            throw new InvalidDataException("EML RON proof derivation revision/budget is negative");
        string expectedScheme = law.template switch
        {
            "xx?E1EE = 11?E1EE" => EmlLawProof.ParameterErasureGuardScheme,
            "11?E1EE1E = ?" => EmlLawProof.ExpLogGuardScheme,
            _ => string.Empty,
        };
        if (guards.IsGuarded && !string.Equals(proof.guardScheme, expectedScheme, StringComparison.Ordinal)
            || !guards.IsGuarded && !string.IsNullOrEmpty(proof.guardScheme))
            throw new InvalidDataException("EML RON proof carries an invalid guard scheme");
        return EmlLawPackage.Restore(
            new ImportPackageID(RequireText(document.package, "package ID")),
            new MindID(RequireText(document.exporter, "package exporter")),
            new MindLineageID(RequireText(document.lineage, "package lineage")),
            new CheckpointID(RequireText(document.checkpoint, "package checkpoint")),
            new EmlEvaluatorID(RequireText(document.evaluator, "package evaluator")),
            new EmlLawClassID(RequireText(document.lawClass, "package law class")),
            new EmlLaw(
                RequireText(law.template, "law template"),
                RequireNonNegative(law.certificateClasses, "law certificate classes"),
                RequireNonNegative(law.fillers, "law fillers"),
                law.mdlGain,
                RequireText(law.verificationFiller, "law verification filler"),
                RequireText(law.verificationPrediction, "law verification claim")),
            new EmlLawBehaviorCertificate(
                RestoreSignature(certificate.atOne),
                RestoreSignature(certificate.atX),
                RestoreSignature(certificate.atY)),
            new EmlLawProof(
                ParseHex(proof.supportDigest, "proof support digest"),
                RequireText(proof.absentFiller, "proof absent filler"),
                RequireText(proof.verificationPrediction, "proof verification claim"),
                RequirePositive(proof.verifierVersion, "proof verifier version"),
                RestoreEvidence(proof.atOne),
                RestoreEvidence(proof.atX),
                RestoreEvidence(proof.atY),
                RestoreEvidence(proof.atAbsentFiller),
                guards,
                RestoreGuardWitness(proof.guardWitness),
                proof.searchRevision,
                proof.searchBudget,
                ParseHex(proof.derivationDigest, "proof derivation digest"),
                RequireTextOrEmpty(proof.guardScheme, "proof guard scheme")),
            RequirePositive(document.signatureDigits, "signature digits"),
            RequireNonNegative(document.templateCostBits, "template cost bits"),
            RequireText(document.causalProcedureDigest, "causal procedure digest"));
    }

    private static EmlSig RestoreSignature(EmlRONSignature signature)
        => new(signature.r1, signature.i1, signature.r2, signature.i2);

    private static EmlLawExactEvidence RestoreEvidence(EmlRONEvidence evidence)
    {
        string grade = RequireText(evidence.grade, "evidence grade");
        if (grade.Length != 1) throw new InvalidDataException("EML evidence grade must contain one character");
        return new EmlLawExactEvidence(
            grade[0],
            evidence.q12Home,
            evidence.q12Regime,
            RequireText(evidence.enclosureColumns, "evidence enclosure columns"));
    }

    private static EmlGuardWitness RestoreGuardWitness(EmlRONGuardWitness witness)
    {
        List<EmlGuardNodeFact> facts = new(witness.nodeFacts.Count);
        for (int i = 0; i < witness.nodeFacts.Count; i++)
        {
            EmlRONGuardNodeFact fact = witness.nodeFacts[i];
            if (!Enum.TryParse(fact.side, out EmlGuardSides side))
                throw new InvalidDataException("EML RON guard witness has an unknown side");
            facts.Add(new EmlGuardNodeFact(
                side,
                new EmlPath(fact.path),
                new EmlEnclosureWitness(fact.realLower, fact.realUpper, fact.imaginaryLower, fact.imaginaryUpper),
                new EmlBranchWitness(fact.logDefined, fact.enclosureCrossesNegativeRealCut,
                    fact.expAfterLogRoundTrips, fact.logAfterExpRoundTrips, fact.exponentialTurn)));
        }
        return new EmlGuardWitness(
            witness.matchedTerm,
            witness.substitution,
            new EmlEnclosureWitness(witness.realLower, witness.realUpper, witness.imaginaryLower, witness.imaginaryUpper),
            new EmlBranchWitness(witness.logDefined, witness.enclosureCrossesNegativeRealCut,
                witness.expAfterLogRoundTrips, witness.logAfterExpRoundTrips, witness.exponentialTurn),
            ParseHex(witness.digest, "guard witness digest"),
            new EmlPath(witness.matchedPath),
            witness.antecedent,
            witness.consequent,
            facts);
    }

    private static void RequireVersion(int actual, string artifact)
    {
        if (actual != SchemaVersion)
            throw new InvalidDataException($"unsupported EML {artifact} RON schema {actual}; expected {SchemaVersion}");
    }

    private static string RequireText(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"EML RON omits {field}");
        return value;
    }

    private static string RequireTextOrEmpty(string? value, string field)
        => value ?? throw new InvalidDataException($"EML RON omits {field}");

    private static int RequireNonNegative(int value, string field)
    {
        if (value < 0) throw new InvalidDataException($"EML RON {field} is negative");
        return value;
    }

    private static long RequireNonNegative(long value, string field)
    {
        if (value < 0) throw new InvalidDataException($"EML RON {field} is negative");
        return value;
    }

    private static int RequirePositive(int value, string field)
    {
        if (value <= 0) throw new InvalidDataException($"EML RON {field} is not positive");
        return value;
    }

    private static long RequirePositive(long value, string field)
    {
        if (value <= 0) throw new InvalidDataException($"EML RON {field} is not positive");
        return value;
    }

    private static ulong ParseHex(string? value, string field)
    {
        if (value is null || value.Length != 16
            || !ulong.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong parsed))
            throw new InvalidDataException($"EML RON {field} is not a canonical 16-digit hexadecimal value");
        return parsed;
    }
}

[RonObject]
internal partial class EmlRONCohort
{
    public int schemaVersion;
    public EmlRONManifest manifest = new();
    public long residencyHorizon;
    public int epoch;
    public long eventIndex;
    public List<EmlRONMindSnapshot> minds = new();
    public List<string> chimeraClasses = new();
}

[RonObject]
internal partial class EmlRONManifest
{
    public string cohort = "";
    public string evaluator = "";
    public string configurationDigest = "";
    public string intakeDigest = "";
    public List<EmlRONMindIdentity> minds = new();
}

[RonObject]
internal partial class EmlRONMindIdentity
{
    public string mind = "";
    public string lineage = "";
    public EmlMindKinds kind;
    public string searchSeed = "";
    public string initialCheckpoint = "";
}

[RonObject]
internal partial class EmlRONMindSnapshot
{
    public string mind = "";
    public string checkpoint = "";
    public List<string> isolatedClasses = new();
    public EmlRONMembrane membrane = new();
}

[RonObject]
internal partial class EmlRONMembrane
{
    public string host = "";
    public string evaluator = "";
    public long residencyHorizon;
    public List<EmlRONMembraneLaw> laws = new();
}

[RonObject]
internal partial class EmlRONMembraneLaw
{
    public string lawClass = "";
    public LawResidencies residency;
    public int canonicalDeltas;
    public long trialStartedAt;
    public long lastChangedAt;
    public string? representativePackage;
    public List<EmlRONMembranePackage> packages = new();
}

[RonObject]
internal partial class EmlRONMembranePackage
{
    public EmlRONPackage package = new();
    public long admittedAt;
}

[RonObject]
internal partial class EmlRONPackage
{
    public int schemaVersion;
    public string package = "";
    public string exporter = "";
    public string lineage = "";
    public string checkpoint = "";
    public string evaluator = "";
    public string lawClass = "";
    public EmlRONLaw law = new();
    public EmlRONCertificate certificate = new();
    public EmlRONProof proof = new();
    public int signatureDigits;
    public int templateCostBits;
    public string causalProcedureDigest = "";
}

[RonObject]
internal partial class EmlRONLaw
{
    public string template = "";
    public int certificateClasses;
    public int fillers;
    public double mdlGain;
    public string verificationFiller = "";
    public string verificationPrediction = "";
}

[RonObject]
internal partial class EmlRONCertificate
{
    public EmlRONSignature atOne = new();
    public EmlRONSignature atX = new();
    public EmlRONSignature atY = new();
}

[RonObject]
internal partial class EmlRONSignature
{
    public long r1;
    public long i1;
    public long r2;
    public long i2;
}

[RonObject]
internal partial class EmlRONProof
{
    public string supportDigest = "";
    public string absentFiller = "";
    public string verificationPrediction = "";
    public int verifierVersion;
    public EmlRONEvidence atOne = new();
    public EmlRONEvidence atX = new();
    public EmlRONEvidence atY = new();
    public EmlRONEvidence atAbsentFiller = new();
    public string domainGuardDigest = "";
    public List<EmlRONDomainAtom> domainGuards = new();
    public EmlRONGuardWitness guardWitness = new();
    public int searchRevision;
    public int searchBudget;
    public string derivationDigest = "";
    public string guardScheme = "";
}

[RonObject]
internal partial class EmlRONDomainAtom
{
    public string kind = "";
    public string path = "";
    public double lower;
    public double upper;
    public string side = "Antecedent";
}

[RonObject]
internal partial class EmlRONGuardWitness
{
    public string matchedTerm = "";
    public string substitution = "";
    public string matchedPath = "";
    public string antecedent = "";
    public string consequent = "";
    public double realLower;
    public double realUpper;
    public double imaginaryLower;
    public double imaginaryUpper;
    public bool logDefined;
    public bool enclosureCrossesNegativeRealCut;
    public bool expAfterLogRoundTrips;
    public bool logAfterExpRoundTrips;
    public long exponentialTurn;
    public string digest = "";
    public List<EmlRONGuardNodeFact> nodeFacts = new();
}

[RonObject]
internal partial class EmlRONGuardNodeFact
{
    public string side = "";
    public string path = "";
    public double realLower;
    public double realUpper;
    public double imaginaryLower;
    public double imaginaryUpper;
    public bool logDefined;
    public bool enclosureCrossesNegativeRealCut;
    public bool expAfterLogRoundTrips;
    public bool logAfterExpRoundTrips;
    public long exponentialTurn;
}

[RonObject]
internal partial class EmlRONEvidence
{
    public string grade = "";
    public bool q12Home;
    public bool q12Regime;
    public string enclosureColumns = "";
}
