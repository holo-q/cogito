namespace Cogito;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// Where an assay of one sealed native run stopped. Exactly one clause is reached per
/// input, and the clause — not a boolean — is what a report of the assay prints, because
/// "did not close" is four different findings and only one of them is about the organism.
public enum RepositoryLoopClosureAdjudicationClauses : byte
{
    /// Every composition succeeded and the sealed input carried a whole report.
    Adjudicated,
    /// The sealed input did not validate against its own authorities; nothing downstream
    /// of it can be trusted, so no evidence was composed from it at all.
    SealedInputRefused,
    /// The run's own ancestry does not verify. A tape that cannot vouch for its causal
    /// order cannot be asked whether a loop closed inside it.
    CanonicalLineageFailed,
    /// No causal-link evidence is present. This is the ABSENT case, never a denied link.
    LinkChainAbsent,
    /// Link evidence is present in the sealed tape but no channel in the sealed input carries
    /// it whole, so it cannot be admitted without inventing the fields the channel drops.
    LinkEvidenceUnreconstructible,
    /// None of the three verdict species found evidence in the sealed input.
    VerdictEvidenceAbsent,
    /// The report was assembled but its own cross-domain bindings rejected it.
    ReportRefused,
}

/// What the sealed input actually CONTAINS, counted before anything is concluded from it.
/// Every arm of an assay reports this census whether or not it reaches a report, because a
/// refusal that names no quantities is indistinguishable from a refusal that measured nothing.
public readonly record struct RepositoryLoopClosureEvidenceCensus(
    int TapeEvents,
    int JournalRows,
    int WorldFiles,
    int AccessEntries,
    int FrontierCandidates,
    int FrontierTransitions,
    int FrontierSelections,
    int PatternOccurrences,
    int PatternCompositions,
    int PatternAdmissions,
    int LinkPackets,
    int DivergenceReceipts,
    int TaskActions,
    int TaskOccurrenceChecks,
    int TaskOutcomes,
    /// Committed transitions whose candidate species the registered task can consume. A task
    /// chain can only be minted on one of these, so a zero here says the crawler never proposed
    /// the KIND of action the task was registered for — a very different absence from a crawler
    /// that proposed one and failed to satisfy the oracle.
    int TaskEligibleTransitions,
    long SealEventID)
{
    private static int CountTaskEligibleTransitions(
        RepositoryLoopClosureFrontierSnapshot frontier,
        RepositoryLoopClosureTaskSpecies taskSpecies)
        => frontier.Transitions.Count(transition =>
            RepositoryCandidate.TryParseCanonical(transition.CandidateCanonical, out RepositoryCandidate candidate)
            && RepositoryLoopTaskSpeciesRules.MatchesCandidate(taskSpecies, candidate.Species));

    internal const string LinkPacketSource = "repository:loop-link";
    private const string LineageSource = "repository:lineage";
    // Frozen journal row kind; identifier-side name is Divergence.
    private const string DivergenceReceiptKind = "dissent";

    public static RepositoryLoopClosureEvidenceCensus Measure(RepositoryLoopClosureAdjudicationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Measure(input.Tape, input.Journal, input.World, input.Access, input.Frontier, input.Pattern,
            input.Task.Species);
    }

    /// The census is taken from snapshots rather than a sealed input on purpose: an input that
    /// cannot be ASSEMBLED still has evidence worth counting, and an arm reported as a blank row
    /// would be indistinguishable from an arm that produced nothing.
    public static RepositoryLoopClosureEvidenceCensus Measure(
        RepositoryLoopClosureTapeSnapshot tape,
        RepositoryLoopClosureJournalSnapshot journal,
        RepositoryLoopClosureWorldSnapshot world,
        RepositoryLoopClosureAccessSnapshot access,
        RepositoryLoopClosureFrontierSnapshot frontier,
        RepositoryLoopClosurePatternSnapshot pattern,
        RepositoryLoopClosureTaskSpecies taskSpecies)
    {
        ArgumentNullException.ThrowIfNull(tape);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(frontier);
        ArgumentNullException.ThrowIfNull(pattern);
        int linkPackets = 0;
        int divergenceReceipts = 0;
        int actions = 0;
        int occurrenceChecks = 0;
        int outcomes = 0;
        foreach (LoopLineageTapeEvent tapeEvent in tape.Events)
        {
            switch (tapeEvent.Source)
            {
                case LinkPacketSource:
                    linkPackets++;
                    break;
                case LineageSource
                    when TapePacketCreator.TryReadRepositoryLineageReceipt(tapeEvent.Payload.Span, out string kind, out _, out _)
                        && kind == DivergenceReceiptKind:
                    divergenceReceipts++;
                    break;
                case "repository-action" when RepositoryLoopTaskActionReceipt.TryDecode(tapeEvent.Payload.Span, out _):
                    actions++;
                    break;
                case RepositoryLoopTaskReceiptCodec.OccurrenceCheckSource when RepositoryLoopTaskOccurrenceCheckReceipt.TryDecode(tapeEvent.Payload.Span, out _):
                    occurrenceChecks++;
                    break;
                case "repository-outcome" when RepositoryLoopTaskOutcomeReceipt.TryDecode(tapeEvent.Payload.Span, out _):
                    outcomes++;
                    break;
            }
        }
        return new(tape.Events.Count, journal.Rows.Count, world.Files.Count,
            access.Entries.Count, frontier.Candidates.Count, frontier.Transitions.Count,
            frontier.Selections.Count, pattern.Occurrences.Count, pattern.Compositions.Count,
            pattern.Admissions.Count, linkPackets, divergenceReceipts, actions, occurrenceChecks, outcomes,
            CountTaskEligibleTransitions(frontier, taskSpecies), tape.SealEventID);
    }
}

/// A refusal that names its clause and the counts it measured before stopping. It is its own
/// type rather than an InvalidDataException so a caller cannot confuse "this input is corrupt"
/// with "this input is intact and does not contain what closure requires" — and because
/// System.IO.InvalidDataException is sealed, it could not be specialized anyway.
public sealed class RepositoryLoopClosureAdjudicationRefusal : Exception
{
    internal RepositoryLoopClosureAdjudicationRefusal(
        RepositoryLoopClosureAdjudicationClauses clause,
        string detail,
        RepositoryLoopClosureEvidenceCensus census,
        Exception? cause = null)
        : base($"repository loop adjudication refused at {ClauseName(clause)} — {detail}", cause)
    {
        Clause = clause;
        Detail = detail;
        Census = census;
    }

    public RepositoryLoopClosureAdjudicationClauses Clause { get; }
    public string Detail { get; }
    public RepositoryLoopClosureEvidenceCensus Census { get; }

    public static string ClauseName(RepositoryLoopClosureAdjudicationClauses clause) => clause switch
    {
        RepositoryLoopClosureAdjudicationClauses.Adjudicated => "adjudicated",
        RepositoryLoopClosureAdjudicationClauses.SealedInputRefused => "sealed-input-refused",
        RepositoryLoopClosureAdjudicationClauses.CanonicalLineageFailed => "canonical-lineage-failed",
        RepositoryLoopClosureAdjudicationClauses.LinkChainAbsent => "link-chain-absent",
        RepositoryLoopClosureAdjudicationClauses.LinkEvidenceUnreconstructible => "link-evidence-unreconstructible",
        RepositoryLoopClosureAdjudicationClauses.VerdictEvidenceAbsent => "verdict-evidence-absent",
        RepositoryLoopClosureAdjudicationClauses.ReportRefused => "report-refused",
        _ => throw new InvalidDataException($"repository loop adjudication clause {(byte)clause} is unknown"),
    };
}

/// The measurement an assay returns. It carries what was COMPOSED — census, lineage, task
/// outcome, verdict species — independently of whether those compositions added up to a
/// report, so a refused arm is still a reported quantity rather than a blank.
public sealed class RepositoryLoopClosureFinding
{
    internal RepositoryLoopClosureFinding(
        RepositoryLoopClosureAdjudicationClauses clause,
        RepositoryLoopClosureEvidenceCensus census,
        RepositoryLoopClosureLineageResult? lineage,
        RepositoryLoopClosureTaskOutcome? taskOutcome,
        IReadOnlyList<RepositoryLoopClosureVerdict> verdicts,
        RepositoryLoopClosureReport? report,
        RepositoryLoopClosureAdjudicationRefusal? refusal)
    {
        Clause = clause;
        Census = census;
        Lineage = lineage;
        TaskOutcome = taskOutcome;
        Verdicts = Array.AsReadOnly((verdicts ?? throw new ArgumentNullException(nameof(verdicts))).ToArray());
        Report = report;
        Refusal = refusal;
        if ((report is null) == (refusal is null))
            throw new InvalidDataException("repository loop finding is neither exactly a report nor exactly a refusal");
    }

    public RepositoryLoopClosureAdjudicationClauses Clause { get; }
    public RepositoryLoopClosureEvidenceCensus Census { get; }
    public RepositoryLoopClosureLineageResult? Lineage { get; }
    public RepositoryLoopClosureTaskOutcome? TaskOutcome { get; }
    public IReadOnlyList<RepositoryLoopClosureVerdict> Verdicts { get; }
    public RepositoryLoopClosureReport? Report { get; }
    public RepositoryLoopClosureAdjudicationRefusal? Refusal { get; }

    public bool IsAdjudicated => Report is not null;
    public bool RendersClosureCertificate => Report is { } report && report.CanRenderClosureCertificate;
    /// How many of the five causal links the sealed input carried as ADMITTED evidence.
    /// A refused assay reports zero here because nothing was admitted, not because nothing was looked at.
    public int AdmittedLinks => Report is { } report
        ? report.Links.Evidence.Count(static evidence => evidence.State == LoopClosureLinkStates.Admitted)
        : 0;
}

/// Adjudication of ONE sealed native repository run. It consumes snapshots and returns a
/// finding; it never opens a repository, a run directory, a tape, or a journal, and it
/// appends nothing anywhere. Every quantity it reports is composed from the sealed input —
/// a species with no evidence is ABSENT from the report, never admitted with a default.
public static class RepositoryLoopClosureAdjudicator
{
    /// A non-serializable authority minted only after the adjudicator has
    /// validated a complete sealed input. A decoded report never carries one;
    /// it must be adjudicated again before it can render a closure certificate.
    public sealed class AdjudicationCapability
    {
        private AdjudicationCapability(string sealedEvidenceAuthoritySHA256)
        {
            if (sealedEvidenceAuthoritySHA256 is not { Length: 64 }
                || !sealedEvidenceAuthoritySHA256.All(Uri.IsHexDigit))
                throw new ArgumentException("sealed evidence authority digest is malformed", nameof(sealedEvidenceAuthoritySHA256));
            SealedEvidenceAuthoritySHA256 = sealedEvidenceAuthoritySHA256;
        }

        public string SealedEvidenceAuthoritySHA256 { get; }

        /// Minting requires a complete, self-validating sealed input — the capability
        /// cannot be conjured from a digest string alone.
        internal static AdjudicationCapability Mint(RepositoryLoopClosureAdjudicationInput input)
        {
            ArgumentNullException.ThrowIfNull(input);
            input.Validate();
            return new AdjudicationCapability(input.EvidenceAuthority.AuthoritySHA256);
        }
    }

    public static bool IsMounted => true;

    /// Promote a sealed input to a report, or refuse. A caller that wants the measurement
    /// regardless of admission calls Assay instead; this overload exists for the callers
    /// whose next statement genuinely needs a report to exist.
    public static RepositoryLoopClosureReport Adjudicate(RepositoryLoopClosureAdjudicationInput input)
    {
        RepositoryLoopClosureFinding finding = Assay(input);
        return finding.Report ?? throw finding.Refusal!;
    }

    /// An arm whose sealed input could not even be ASSEMBLED still reports its census. The
    /// clause is the same one a malformed input reaches, because both mean the same thing: the
    /// evidence never reached a state where a question about closure could be asked of it.
    public static RepositoryLoopClosureFinding RefuseUnassembled(
        RepositoryLoopClosureEvidenceCensus census,
        string detail,
        Exception? cause = null)
        => new(RepositoryLoopClosureAdjudicationClauses.SealedInputRefused, census, null, null, [], null,
            new RepositoryLoopClosureAdjudicationRefusal(
                RepositoryLoopClosureAdjudicationClauses.SealedInputRefused, detail, census, cause));

    public static RepositoryLoopClosureFinding Assay(RepositoryLoopClosureAdjudicationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        RepositoryLoopClosureEvidenceCensus census = RepositoryLoopClosureEvidenceCensus.Measure(input);

        AdjudicationCapability capability;
        try { capability = AdjudicationCapability.Mint(input); }
        catch (Exception failure) when (failure is InvalidDataException or ArgumentException)
        {
            return Refuse(RepositoryLoopClosureAdjudicationClauses.SealedInputRefused, failure.Message, census,
                null, null, [], failure);
        }

        RepositoryLoopClosureLineageResult lineage = DeriveLineage(input);
        if (!lineage.Canonical.Passed)
            return Refuse(RepositoryLoopClosureAdjudicationClauses.CanonicalLineageFailed,
                $"canonical ancestry {lineage.Canonical.Status} at edge {lineage.Canonical.FirstDiscriminatingEdge}: {lineage.Canonical.Detail}",
                census, lineage, null, []);

        RepositoryLoopClosureTaskOutcome? taskOutcome = TryComposeTaskOutcome(input);
        RepositoryLoopClosureVerdict[] verdicts = DeriveVerdicts(input, taskOutcome);

        if (census.LinkPackets == 0 && census.DivergenceReceipts == 0)
            return Refuse(RepositoryLoopClosureAdjudicationClauses.LinkChainAbsent,
                $"the sealed tape carries no causal-link evidence: 0 '{RepositoryLoopClosureEvidenceCensus.LinkPacketSource}' packets "
                + $"and 0 'divergence' lineage receipts across {census.TapeEvents} events",
                census, lineage, taskOutcome, verdicts);
        if (verdicts.Length == 0)
            return Refuse(RepositoryLoopClosureAdjudicationClauses.VerdictEvidenceAbsent,
                $"no verdict species found evidence: {census.PatternCompositions} pattern compositions, "
                + $"{census.DivergenceReceipts} divergence receipts, {census.TaskOutcomes} task outcomes",
                census, lineage, taskOutcome, verdicts);

        // Link evidence is carried by neither channel this input exposes. The packet source
        // carries a LoopClosureLinkAttempt, which is a strict SUBSET of the evidence fields the
        // report contract requires (candidate, source locus, access and policy custody are all
        // absent from it), and the terminal document has no divergence-receipt section at all. So a
        // tape that DOES carry link packets still cannot be reconstructed into link evidence here,
        // and saying so is the honest stop.
        return Refuse(RepositoryLoopClosureAdjudicationClauses.LinkEvidenceUnreconstructible,
            $"{census.LinkPackets} link packets and {census.DivergenceReceipts} divergence receipts are present, but neither channel "
            + "carries a complete RepositoryLoopClosureLinkEvidence: the packet is an attempt projection and the sealed terminal "
            + "document has no divergence-receipt section",
            census, lineage, taskOutcome, verdicts);
    }

    private static RepositoryLoopClosureFinding Refuse(
        RepositoryLoopClosureAdjudicationClauses clause,
        string detail,
        RepositoryLoopClosureEvidenceCensus census,
        RepositoryLoopClosureLineageResult? lineage,
        RepositoryLoopClosureTaskOutcome? taskOutcome,
        IReadOnlyList<RepositoryLoopClosureVerdict> verdicts,
        Exception? cause = null)
        => new(clause, census, lineage, taskOutcome, verdicts, null,
            new RepositoryLoopClosureAdjudicationRefusal(clause, detail, census, cause));

    /// The canonical ancestry plus the sharper shuffled-predecessor null, both MEASURED from
    /// the sealed tape under the registration's own null domain — the same domain the result's
    /// own validator will re-derive, so a domain drift fails rather than passes quietly.
    private static RepositoryLoopClosureLineageResult DeriveLineage(RepositoryLoopClosureAdjudicationInput input)
    {
        LoopLineageAuthority authority = LoopLineageAuthority.Capture(input.Tape.LineageEdges);
        LoopLineageOccurrenceCheckResult canonical = LoopLineageVerifier.Verify(
            input.Tape.LineageEdges, input.Tape.Tape, authority);
        // The null derangement is defined against a canonical chain. Running it on a chain that
        // did not verify would report a discrimination the ancestry never had, so the null stays
        // explicitly MISSING and the clause above reports why.
        if (!canonical.Passed)
            return new(canonical, new LoopClosureLineageNullMissing(
                $"canonical ancestry did not verify over {input.Tape.LineageEdges.Count} edge receipts"));
        LoopLineageAdjudication shuffled = LoopLineageVerifier.VerifyShuffledPredecessorNull(
            input.Tape.Tape, input.Tape.LineageEdges, input.Journal.Lines, input.Registration.LineageNullSpec.Domain);
        return new(canonical, new LoopClosureLineageNullExecuted(shuffled.NullReceipt));
    }

    /// Rebuild the task's action→occurrenceCheck→outcome closure from the three sealed packets.
    /// The rebuilt objects are re-encoded by the outcome's own validator and compared against
    /// the sealed payload bytes, so a decode that drifts from the encode cannot survive here.
    private static RepositoryLoopClosureTaskOutcome? TryComposeTaskOutcome(RepositoryLoopClosureAdjudicationInput input)
    {
        string taskID = input.Task.TaskID;
        Dictionary<long, (RepositoryLoopTaskActionReceipt Receipt, string PayloadSHA256)> actions = new();
        Dictionary<long, (RepositoryLoopTaskOccurrenceCheckReceipt Receipt, string PayloadSHA256)> occurrenceChecks = new();
        List<(TapeEventID EventID, RepositoryLoopTaskOutcomeReceipt Receipt, string PayloadSHA256)> outcomes = new();
        foreach (LoopLineageTapeEvent tapeEvent in input.Tape.Events)
        {
            string payloadSHA256 = Convert.ToHexStringLower(SHA256.HashData(tapeEvent.Payload.Span));
            if (tapeEvent.Source == "repository-action"
                && RepositoryLoopTaskActionReceipt.TryDecode(tapeEvent.Payload.Span, out RepositoryLoopTaskActionReceipt action)
                && action.TaskID == taskID)
                actions[tapeEvent.EventID.Value] = (action, payloadSHA256);
            else if (tapeEvent.Source == RepositoryLoopTaskReceiptCodec.OccurrenceCheckSource
                && RepositoryLoopTaskOccurrenceCheckReceipt.TryDecode(tapeEvent.Payload.Span, out RepositoryLoopTaskOccurrenceCheckReceipt decodedOccurrenceCheck)
                && decodedOccurrenceCheck.TaskID == taskID)
                occurrenceChecks[tapeEvent.EventID.Value] = (decodedOccurrenceCheck, payloadSHA256);
            else if (tapeEvent.Source == "repository-outcome"
                && RepositoryLoopTaskOutcomeReceipt.TryDecode(tapeEvent.Payload.Span, out RepositoryLoopTaskOutcomeReceipt outcome)
                && outcome.TaskID == taskID)
                outcomes.Add((tapeEvent.EventID, outcome, payloadSHA256));
        }
        if (outcomes.Count == 0) return null;

        // The run's answer on the task is its LAST outcome, not the friendliest one: an earlier
        // Confirmed that a later step superseded is a snapshot of a mind that changed itself.
        (TapeEventID outcomeEventID, RepositoryLoopTaskOutcomeReceipt outcomeReceipt, string outcomePayloadSHA256) =
            outcomes.OrderBy(static row => row.EventID.Value).Last();
        if (!occurrenceChecks.TryGetValue(outcomeReceipt.OccurrenceCheckEventID.Value,
                out (RepositoryLoopTaskOccurrenceCheckReceipt Receipt, string PayloadSHA256) occurrenceCheckRow)
            || !actions.TryGetValue(occurrenceCheckRow.Receipt.ActionEventID.Value,
                out (RepositoryLoopTaskActionReceipt Receipt, string PayloadSHA256) actionRow))
            return null;

        RepositoryLoopTaskOccurrenceCheckReceipt occurrenceCheckReceipt = occurrenceCheckRow.Receipt;
        RepositoryLoopTaskActionReceipt actionReceipt = actionRow.Receipt;
        // The typed-prediction receipt lives only in its own repository:occurrenceCheck packet; a
        // source-result oracle has none, and reconstructing a typed one from a digest alone
        // would be exactly the fabrication the report contract exists to catch.
        if (occurrenceCheckReceipt.OracleMode == RepositoryLoopClosureTaskOracleModes.TypedPrediction) return null;

        RepositoryLoopClosureTaskOccurrenceCheck occurrenceCheck = new(
            occurrenceCheckReceipt.OracleMode, occurrenceCheckReceipt.Outcome, occurrenceCheckReceipt.OracleSHA256,
            occurrenceCheckReceipt.Prediction, null, occurrenceCheckReceipt.WorldSHA256, occurrenceCheckReceipt.AccessSHA256,
            occurrenceCheckReceipt.EvaluatorCost, occurrenceCheckReceipt.AccessCost, occurrenceCheckReceipt.AccessSequence,
            occurrenceCheckReceipt.AccessEntrySHA256, occurrenceCheckReceipt.AccessEntryCount,
            occurrenceCheckReceipt.ActionEventID, occurrenceCheckReceipt.CallSHA256, occurrenceCheckReceipt.EvidenceSHA256);

        RepositoryLoopClosureTaskOutcome outcomeValue = RepositoryLoopClosureTaskOutcome.Create(
            taskID, input.Task.Species, actionReceipt.Candidate,
            input.Frontier.Revision, input.Frontier.RuntimeAuthoritySHA256,
            actionReceipt.FrontierRevision, actionReceipt.FrontierAuthoritySHA256,
            actionReceipt.SelectionOrdinal, actionReceipt.SelectionEventID, actionReceipt.SelectionReceiptSHA256,
            occurrenceCheckReceipt.ActionEventID, outcomeReceipt.OccurrenceCheckEventID, outcomeEventID,
            outcomeReceipt.OccurrenceCheckEventID,
            actionRow.PayloadSHA256, occurrenceCheckRow.PayloadSHA256, outcomePayloadSHA256,
            outcomeReceipt.SourcePath, outcomeReceipt.SourceBytes, outcomeReceipt.SourceSHA256, outcomeReceipt.SourceLine,
            outcomeReceipt.ResultSpecies, outcomeReceipt.ResultContent, occurrenceCheck);
        try { outcomeValue.Validate(input); }
        catch (InvalidDataException) { return null; }
        return outcomeValue;
    }

    /// One verdict per species that FOUND evidence. A species with none is left out of the
    /// report entirely; emitting it as a FAIL would put a verdict on the record for a
    /// question the sealed input never answered.
    private static RepositoryLoopClosureVerdict[] DeriveVerdicts(
        RepositoryLoopClosureAdjudicationInput input,
        RepositoryLoopClosureTaskOutcome? taskOutcome)
    {
        List<RepositoryLoopClosureVerdict> verdicts = new();

        // Pattern became thought when a composition DISPLACED evaluation: the receipt's own
        // evaluator delta is the displacement, and the verdict may carry no other composition.
        RepositoryPatternComposition[] displacing = input.Pattern.Compositions
            .Where(composition => composition.Receipt.EvaluatorDelta > 0
                && (taskOutcome is null
                    || (composition.Conclusion.CandidateDigest == taskOutcome.Candidate.Digest
                        && composition.Conclusion.Candidate.Canonical == taskOutcome.Candidate.Canonical)))
            .OrderBy(static composition => composition.Receipt.CompositionEventID.Value)
            .ToArray();
        if (displacing.Length > 0)
        {
            RepositoryPatternComposition composition = displacing[^1];
            verdicts.Add(new RepositoryPatternBecameThoughtVerdict(
                LoopClosureAssayStatuses.Exact, LoopClosurePowerStatuses.Powered, LoopClosureVerdictStatuses.PASS,
                new LoopClosureDigest(composition.Receipt.ReceiptSHA256), composition));
        }

        // Thought overruled instinct is the admitted executed divergence, and nothing else may
        // stand in for it. No link evidence reaches this input, so the species stays absent.

        if (taskOutcome is { Species: RepositoryLoopClosureTaskOutcomeSpecies.Confirmed } confirmed)
            verdicts.Add(new RepositoryObjectLoopClosedVerdict(
                LoopClosureAssayStatuses.Exact, LoopClosurePowerStatuses.Powered, LoopClosureVerdictStatuses.PASS,
                new LoopClosureDigest(confirmed.OutcomeSHA256), new LoopClosureDigest(confirmed.OutcomeSHA256)));

        return verdicts.ToArray();
    }

    /// Render one finding as an assay row. The clause is always printed, because a row that
    /// showed only counts would let a refused arm read as a quiet arm.
    public static string RenderRow(string armName, RepositoryLoopClosureFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        RepositoryLoopClosureEvidenceCensus census = finding.Census;
        string lineage = finding.Lineage is { } result
            ? $"{result.Canonical.Status}/{(result.NullDiscriminates ? "null-kills" : "null-blind")}"
            : "unmeasured";
        string task = finding.TaskOutcome is { } outcome ? outcome.Species.ToString() : "absent";
        string species = finding.Verdicts.Count == 0
            ? "none"
            : string.Join('+', finding.Verdicts.Select(static verdict => verdict.SpeciesName));
        return string.Format(CultureInfo.InvariantCulture,
            "    {0,-15} spans={1,-5} access={2,-4} select={3,-3} derive={4,-3} eligible={11,-3} task={5,-11} links={6}/5 verdicts={7} lineage={8} certificate={9} · {10}",
            armName, census.TapeEvents, census.AccessEntries, census.FrontierSelections, census.PatternCompositions,
            task, finding.AdmittedLinks, species, lineage,
            finding.RendersClosureCertificate ? "RENDERED" : "no",
            RepositoryLoopClosureAdjudicationRefusal.ClauseName(finding.Clause),
            census.TaskEligibleTransitions);
    }
}
