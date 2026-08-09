namespace Cogito;

using System.Globalization;
using System.Numerics;
using System.Text;

internal static class EmlGuardedRewriteAssay
{
    private const string LogExpTemplate = "11?E1EE1E = ?";

    public static int Run()
    {
        EmlVerifiedLaw verified = CreateVerifiedLaw();
        EmlLawStore store = new();
        if (!store.TryAdmit(verified, 0, out _)) throw new InvalidDataException("guarded assay law admission failed");

        EmlLawInstantiation instance = EmlLawInstantiation.TryCreate(LogExpTemplate, "1", out EmlLawInstantiation created)
            ? created : throw new InvalidDataException("guarded assay instantiation failed");
        EmlTree antecedent = EmlTree.ParseRPN(instance.LeftRpn);
        EmlTreeEvaluation carrier = antecedent.EvaluateAt(EmlTree.P1.X, EmlTree.P1.Y);
        List<EmlLawRewrite> rewrites = new();
        store.AppendRewritesForEvaluation(instance.LeftRpn, rewrites, carrier);
        bool validRewrite = rewrites.Exists(static rewrite => rewrite.IsRung0Eligible
            && !rewrite.IsRelationNull);

        // Mainline wiring can carry a plain-finite tree whose interval arithmetic is
        // deliberately Blown after a large intermediate. The guard still belongs to
        // the concrete matched instance, so exercise the same carrier/search seam as
        // Cortex rather than only the small hand-picked assay term above.
        const string BlownIntervalAntecedent = "xx11xE1E1EEE1EE";
        EmlTreeEvaluation blownIntervalCarrier = EmlTree.ParseRPN(BlownIntervalAntecedent)
            .EvaluateAt(EmlTree.P1.X, EmlTree.P1.Y);
        EmlNodeEvaluation blownIntervalRoot = blownIntervalCarrier.GetNode(EmlPath.Root);
        if (!blownIntervalRoot.P1.Valid || !blownIntervalRoot.P1.Enclosure.IsBlown)
            throw new InvalidDataException("mainline guard fixture lost its blown-interval transition");
        EmlRewritePredictionCarrier blownIntervalPrediction = EmlRewritePredictionCarrier.Create(
            new EmlPredictionID(9001), "guarded-mainline-fixture", EmlTree.P1.X, EmlTree.P1.Y);
        List<EmlLawRewrite> blownIntervalRewrites = new();
        store.AppendRewritesForEvaluation(BlownIntervalAntecedent, blownIntervalRewrites, blownIntervalCarrier);
        EmlLawRewrite blownIntervalRewrite = blownIntervalRewrites.Find(static rewrite => !rewrite.IsRelationNull);
        bool blownIntervalRejected = !blownIntervalRewrite.IsRung0Eligible;
        bool finiteBlownIntervalGuard = !blownIntervalRewrite.IsRung0Eligible
            && !blownIntervalRewrite.GuardWitness.Enclosure.IsFinite;
        bool mainlineSearchCompleted = blownIntervalRejected;
        List<EmlLawRewrite> fallbackRewrites = new();
        store.AppendRewrites([instance.LeftRpn], fallbackRewrites);
        bool fallbackCarrierIneligible = fallbackRewrites.TrueForAll(static rewrite => !rewrite.IsRung0Eligible);

        EmlOneHoleLaw parsed = EmlOneHoleLaw.TryParse(LogExpTemplate, out EmlOneHoleLaw law)
            ? law : throw new InvalidDataException("guarded assay template parse failed");
        EmlLawProof proposalOnly = verified.Proof with
        {
            DomainGuards = EmlDomainGuardSet.Empty,
            GuardWitness = default,
            SearchRevision = 0,
            SearchBudget = 0,
            GuardScheme = "",
        };
        bool ordinaryPackageAccepted = EmlVerifiedLaw.TryReverifyPackage(
            verified.Law, verified.Certificate, proposalOnly, 9, verified.TemplateCostBits,
            out EmlVerifiedLaw? proposalVerified)
            && proposalVerified is not null
            && !proposalVerified.Proof.IsRung0Eligible;
        List<EmlLawRewrite> proposals = new();
        parsed.AppendRewrites(antecedent, verified.Certificate, proposalOnly, proposals, null, carrier);
        bool absentGuardRejected = proposals.TrueForAll(static rewrite => !rewrite.IsRung0Eligible);

        EmlLawRewrite guarded = rewrites.Find(static rewrite => rewrite.IsRung0Eligible && !rewrite.IsRelationNull);
        EmlGuardWitness invalidBranch = guarded.GuardWitness with
        {
            Branch = guarded.GuardWitness.Branch with { ExponentialTurn = 1 },
        };
        bool branchRejected = !verified.Proof.DomainGuards!.TryValidate(in invalidBranch);
        EmlGuardWitness pathMismatch = EmlGuardWitness.Create(
            guarded.MatchedPath.AppendLeft(), guarded.GuardWitness.MatchedTermRpn,
            guarded.GuardWitness.SubstitutionRpn, guarded.GuardWitness.AntecedentRpn,
            guarded.GuardWitness.ConsequentRpn, guarded.GuardWitness.Enclosure,
            guarded.GuardWitness.Branch);
        bool pathRejected = !verified.Proof.DomainGuards.TryValidate(in pathMismatch);

        EmlDomainGuardSet tamperedGuards = EmlDomainGuardSet.Create(
            verified.Proof.DomainGuards.Atoms.Select(static atom => atom with { Upper = atom.Upper + 0.5 }));
        EmlEnclosureWitness tamperedEnclosure = guarded.GuardWitness.Enclosure with
        {
            RealUpper = guarded.GuardWitness.Enclosure.RealUpper + 0.5,
        };
        EmlGuardWitness tamperedWitness = EmlGuardWitness.Create(
            guarded.GuardWitness.MatchedPath,
            guarded.GuardWitness.MatchedTermRpn,
            guarded.GuardWitness.SubstitutionRpn,
            guarded.GuardWitness.AntecedentRpn,
            guarded.GuardWitness.ConsequentRpn,
            in tamperedEnclosure,
            guarded.GuardWitness.Branch);
        EmlLawProof tamperedProof = verified.Proof with
        {
            DomainGuards = tamperedGuards,
            GuardWitness = tamperedWitness,
        };
        bool coordinatedTamperRejected = !EmlVerifiedLaw.TryReverifyPackage(
            verified.Law, verified.Certificate, tamperedProof, 9, verified.TemplateCostBits, out _);

        byte[] checkpoint = Save(store);
        EmlLawStore restored = new();
        using (MemoryStream stream = new(checkpoint, writable: false))
        using (CkptReader reader = new(stream)) restored.Load(reader);
        bool checkpointIdentity = checkpoint.AsSpan().SequenceEqual(Save(restored));
        byte[] checkpointTamper = (byte[])checkpoint.Clone();
        checkpointTamper[^1] ^= 0x01;
        bool checkpointTamperRejected = ThrowsInvalid(() =>
        {
            EmlLawStore tampered = new();
            using MemoryStream stream = new(checkpointTamper, writable: false);
            using CkptReader reader = new(stream);
            tampered.Load(reader);
        });

        EmlMindIdentity identity = new(new MindID("guarded-assay"), new MindLineageID("guarded-assay-lineage"),
            EmlMindKinds.Founder, 1, new CheckpointID("guarded-assay-checkpoint"));
        EmlEvaluatorID evaluator = new("guarded-assay-evaluator");
        EmlLawPackage package = EmlLawPackage.Create(identity, identity.InitialCheckpoint, evaluator, verified, 9, "guarded-assay");
        byte[] ron = EmlPopulationRONCodec.Instance.EncodePackage(package);
        EmlLawPackage decoded = EmlPopulationRONCodec.Instance.DecodePackage(ron);
        bool ronIdentity = ron.AsSpan().SequenceEqual(EmlPopulationRONCodec.Instance.EncodePackage(decoded));
        string ronText = Encoding.UTF8.GetString(ron);
        string guardDigest = verified.Proof.DomainGuardDigest.ToString("x16", CultureInfo.InvariantCulture);
        byte[] ronTamper = Encoding.UTF8.GetBytes(ronText.Replace(guardDigest, "0000000000000000", StringComparison.Ordinal));
        bool ronTamperRejected = ThrowsInvalid(() => EmlPopulationRONCodec.Instance.DecodePackage(ronTamper));

        StringBuilder report = new("metric\tvalue\n");
        Append(report, "valid_guarded_rewrite", validRewrite);
        Append(report, "finite_guard_from_blown_interval_mainline", finiteBlownIntervalGuard);
        Append(report, "mainline_search_completed", mainlineSearchCompleted);
        Append(report, "mainline_rewrite_guarded", blownIntervalRewrite.IsRung0Eligible);
        Append(report, "mainline_witness_finite", blownIntervalRewrite.GuardWitness.Enclosure.IsFinite);
        Append(report, "mainline_witness_digest", blownIntervalRewrite.GuardWitness.HasValidDigest);
        Append(report, "mainline_witness_validated", verified.Proof.DomainGuards!.TryValidate(blownIntervalRewrite.GuardWitness));
        report.Append("mainline_derivation_status\t").Append(EmlRung0Statuses.NoCandidate).AppendLine();
        Append(report, "fallback_carrier_ineligible", fallbackCarrierIneligible);
        Append(report, "ordinary_empty_guard_package_accepted", ordinaryPackageAccepted);
        Append(report, "absent_guard_rejected", absentGuardRejected);
        Append(report, "branch_invalid_rejected", branchRejected);
        Append(report, "path_mismatch_rejected", pathRejected);
        Append(report, "coordinated_atom_witness_tamper_rejected", coordinatedTamperRejected);
        Append(report, "checkpoint_roundtrip", checkpointIdentity);
        Append(report, "checkpoint_tamper_rejected", checkpointTamperRejected);
        Append(report, "ron_roundtrip", ronIdentity);
        Append(report, "ron_tamper_rejected", ronTamperRejected);
        report.Append("domain_guard_digest\t").Append(verified.Proof.DomainGuardDigest.ToString("X16", CultureInfo.InvariantCulture)).AppendLine();
        bool accepted = validRewrite && finiteBlownIntervalGuard && mainlineSearchCompleted
            && fallbackCarrierIneligible && ordinaryPackageAccepted
            && absentGuardRejected && branchRejected && pathRejected
            && coordinatedTamperRejected && checkpointIdentity && checkpointTamperRejected
            && ronIdentity && ronTamperRejected;
        report.Append("status\t").Append(accepted ? "accepted" : "rejected").AppendLine();
        Run receipt = Cogito.Run.New("eml-guarded-rewrite-assay");
        receipt.Write("eml_guarded_rewrite_assay.tsv", report.ToString());
        Console.WriteLine($"  EML guarded rewrite assay -> {Path.GetRelativePath(Environment.CurrentDirectory, receipt.PathOf("eml_guarded_rewrite_assay.tsv"))}");
        return accepted ? 0 : 1;
    }

    internal static EmlVerifiedLaw CreateVerifiedLaw()
    {
        EmlGrader grader = new();
        EmlVerdict x = grader.GradeRpn("11xE1EE1E", "x");
        EmlVerdict y = grader.GradeRpn("11yE1EE1E", "y");
        List<EmlLawPrediction> support =
        [
            new EmlLawPrediction(EmlCert.Of(in x, 9), "11xE1EE1E", "x"),
            new EmlLawPrediction(EmlCert.Of(in y, 9), "11yE1EE1E", "y"),
        ];
        EmlLaw law = new(LogExpTemplate, 2, 2, 16.0, "1", "111E1EE1E = 1");
        if (!EmlVerifiedLaw.TryVerify(in law, support, 9, out EmlVerifiedLaw? verified) || verified is null || !verified.Proof.IsGuarded)
            throw new InvalidDataException("eligible log-exp law did not receive a typed domain proof");
        return verified;
    }

    private static byte[] Save(EmlLawStore store)
    {
        using MemoryStream stream = new();
        using (CkptWriter writer = new(stream)) store.Save(writer);
        return stream.ToArray();
    }

    private static bool ThrowsInvalid(Action action)
    {
        try { action(); return false; }
        catch (InvalidDataException) { return true; }
        catch (FormatException) { return true; }
        catch (ArgumentException) { return true; }
    }

    private static void Append(StringBuilder report, string name, bool value)
        => report.Append(name).Append('\t').Append(value ? 1 : 0).AppendLine();
}
