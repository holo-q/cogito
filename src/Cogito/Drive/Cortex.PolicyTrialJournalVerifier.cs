namespace Cogito;

using System.Globalization;
using Cogito.Grammar;

internal readonly record struct CortexPolicyTrialJournalOccurrenceCheck(
    bool Passed,
    int QuotaRows,
    int PaidRows,
    int ReusedRows,
    int DeniedRows,
    int CompletionRows,
    long PlannedArmSteps,
    long ActualArmSteps,
    long ReclaimedOrUnused)
{
    public bool AccountingClosed => PlannedArmSteps == ActualArmSteps + ReclaimedOrUnused;
}

/// Validates the durable policy journal without consulting an adjacent aggregate counter. This is the receipt used
/// by the CLI gate: IDs, decision identity, settlement linkage, and planned/actual/refund arithmetic all come from
/// the typed TSV rows that the policy owner emits and checkpoints.
internal static class CortexPolicyTrialJournalVerifier
{
    internal static CortexPolicyTrialJournalOccurrenceCheck Verify(string runDirectory, TextWriter output)
    {
        using CortexPolicyOccurrenceCheckBundle bundle = new(runDirectory);
        return Verify(bundle, output);
    }

    internal static CortexPolicyTrialJournalOccurrenceCheck VerifyReadout(string runDirectory, TextWriter output)
    {
        using CortexPolicyOccurrenceCheckBundle bundle = new(runDirectory);
        return VerifyReadout(bundle, output);
    }

    internal static CortexPolicyTrialJournalOccurrenceCheck VerifyReadout(CortexPolicyOccurrenceCheckBundle bundle, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(output);
        List<CortexPolicyReadoutQuotaDecision> decisions = bundle.ReadoutFundingDecisions;
        List<CortexPolicyTrialCompletion> settlements = bundle.ReadoutCompletions;
        List<CortexPolicyReadoutQuotaDecision> funded = new();
        Dictionary<CortexPolicyQuotaDecisionID, CortexPolicyReadoutQuotaDecision> fundedByID = new();
        HashSet<CortexPolicyQuotaDecisionID> ids = new();
        bool passed = true;
        long planned = 0;
        int reused = 0, denied = 0;
        for (int i = 0; i < decisions.Count; i++)
        {
            CortexPolicyReadoutQuotaDecision decision = decisions[i];
            if (!ids.Add(decision.QuotaDecisionID)) passed = false;
            CortexPolicyQuotaDecisionID expected = Cortex.ComputePolicyReadoutQuotaDecisionID(
                decision.Policy, decision.CandidateFingerprint, decision.GrammarRevision, decision.QuotaStep,
                decision.ContextDigest, decision.ContextBytes, decision.DeliberationDepth, decision.PlannedUnits);
            if (!decision.QuotaDecisionID.Equals(expected) || decision.CandidateFingerprint == 0
                || decision.GrammarRevision == GrammarRevisionID.Zero || decision.ContextDigest == 0 || decision.ContextBytes <= 0
                || decision.DeliberationDepth < 0 || decision.QuotaStep < 0
                || decision.PlannedUnits != decision.DeliberationDepth + 1 || decision.PlannedUnits <= 0
                || decision.RemainingQuota < 0 || string.IsNullOrWhiteSpace(decision.RosterDigest)
                || decision.AvailableBefore < 0 || decision.AvailableAfter < 0
                || decision.AvailableAfter != decision.AvailableBefore - decision.HeldUnits) passed = false;
            switch (decision.Decision)
            {
                case CortexPolicyQuotaDecisions.Paid:
                    if (decision.HeldUnits != decision.PlannedUnits || decision.UsedUnits != decision.PlannedUnits) passed = false;
                    funded.Add(decision); fundedByID.TryAdd(decision.QuotaDecisionID, decision);
                    planned = checked(planned + decision.PlannedUnits); break;
                case CortexPolicyQuotaDecisions.Reused:
                    if (decision.HeldUnits != decision.PlannedUnits || decision.UsedUnits != 0) passed = false;
                    reused++; break;
                case CortexPolicyQuotaDecisions.Denied:
                    if (decision.HeldUnits != 0 || decision.UsedUnits != 0) passed = false;
                    denied++; break;
                default: passed = false; break;
            }
        }
        long actual = 0, refund = 0;
        for (int i = 0; i < settlements.Count; i++)
        {
            CortexPolicyTrialCompletion settlement = settlements[i];
            bool linked = fundedByID.TryGetValue(settlement.QuotaDecisionID, out CortexPolicyReadoutQuotaDecision funding);
            if (!linked || settlement.VerifierOutcome != CortexPolicyVerifierOutcomes.ReadoutCompleted
                || settlement.ActualExecutedArmSteps < 0 || settlement.ActualExecutedArmSteps > funding.PlannedUnits
                || settlement.ReclaimedOrUnused != funding.PlannedUnits - settlement.ActualExecutedArmSteps
                || settlement.EvaluatorWorkUnits is not >= 0) passed = false;
            actual = checked(actual + settlement.ActualExecutedArmSteps);
            refund = checked(refund + settlement.ReclaimedOrUnused);
        }
        if (funded.Count != settlements.Count || planned != actual + refund) passed = false;
        string? rosterDigest = decisions.Count == 0 ? null : decisions[0].RosterDigest;
        for (int i = 0; i < decisions.Count; i++)
            if (decisions[i].RosterDigest != rosterDigest) passed = false;
        if (decisions.Count > 0 || settlements.Count > 0)
        {
            List<CortexPolicyReadoutAllocation>? allocations = bundle.ReadoutAllocations;
            if (allocations is null)
            {
                passed = false;
            }
            else
            {
                Dictionary<long, CortexPolicyReadoutAllocation> allocationsBySequence = new(allocations.Count);
                for (int i = 0; i < allocations.Count; i++)
                {
                    CortexPolicyReadoutAllocation row = allocations[i];
                    if (row.Sequence != i + 1 || row.Step != i + 1 || row.AvailableBefore < 0 || row.AvailableAfter < 0
                        || row.AllocatedUnits < 0 || row.ExpiredUnits < 0 || row.AllocatedUnits + row.ExpiredUnits != 1
                        || row.AvailableAfter != row.AvailableBefore + row.AllocatedUnits
                        || !allocationsBySequence.TryAdd(row.Sequence, row)) passed = false;
                    if (i > 0 && row.RosterDigest != allocations[i - 1].RosterDigest) passed = false;
                }
                if (allocations.Count == 0) passed = false;
                if (rosterDigest is not null)
                    for (int i = 0; i < allocations.Count; i++)
                        if (allocations[i].RosterDigest != rosterDigest) passed = false;
                for (int i = 0; i < decisions.Count; i++)
                {
                    CortexPolicyReadoutQuotaDecision decision = decisions[i];
                    if (decision.AllocationSequence < 0)
                    {
                        passed = false;
                        continue;
                    }
                    if (decision.AllocationSequence == 0)
                    {
                        if (decision.AvailableBefore != 0 || decision.AvailableAfter != 0) passed = false;
                        continue;
                    }
                    if (!allocationsBySequence.TryGetValue(decision.AllocationSequence, out CortexPolicyReadoutAllocation allocation)
                        || !allocation.Policy.Equals(decision.Policy) || allocation.RosterDigest != decision.RosterDigest)
                        passed = false;
                }
            }
        }
        output.WriteLine($"  policy-readout-journal · rows={decisions.Count} funded={funded.Count} reused={reused} denied={denied} settlements={settlements.Count}");
        output.WriteLine($"  readout accounting · planned={planned} actual={actual} refund={refund} closed={(planned == actual + refund ? "yes" : "NO")} verifier={(passed ? "PASS" : "FAIL")}");
        return new CortexPolicyTrialJournalOccurrenceCheck(passed, decisions.Count, funded.Count, reused, denied, settlements.Count, planned, actual, refund);
    }

    internal static CortexPolicyTrialJournalOccurrenceCheck Verify(CortexPolicyOccurrenceCheckBundle bundle, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(output);
        List<CortexPolicyTrialQuotaDecision> decisions = bundle.TrialFundingDecisions;
        List<CortexPolicyTrialCompletion> settlements = bundle.TrialCompletions;
        List<CortexPolicyTrialQuotaDecision> funded = new();
        Dictionary<CortexPolicyQuotaDecisionID, CortexPolicyTrialQuotaDecision> decisionsByID = new();
        Dictionary<CortexPolicyQuotaDecisionID, CortexPolicyTrialQuotaDecision> fundedByID = new();
        int reused = 0, denied = 0;
        bool passed = true;
        long planned = 0;
        for (int i = 0; i < decisions.Count; i++)
        {
            CortexPolicyTrialQuotaDecision decision = decisions[i];
            if (decisionsByID.TryGetValue(decision.QuotaDecisionID, out CortexPolicyTrialQuotaDecision prior))
            {
                if (!SameFundingIdentity(in prior, in decision)) passed = false;
            }
            else
                decisionsByID.Add(decision.QuotaDecisionID, decision);
            if (decision.RequestedHorizonSteps < 0 || decision.ArmCount < 0 || decision.PlannedArmSteps < 0
                || decision.HeldArmSteps < 0 || decision.UsedSteps < 0 || decision.RemainingQuota < 0
                || decision.CandidateFingerprint == 0 || decision.ReadoutFingerprint == 0
                || !Enum.IsDefined(decision.CandidateState) || !Enum.IsDefined(decision.DenialReason)
                || decision.CandidateCurrentStep != decision.QuotaStep
                || decision.CandidateOriginStep < -1 || decision.CandidateRequiredStep < -1
                || decision.CandidateRevision.Value == 0 && decision.CandidateState == CortexPolicyTrialCandidateStates.Active
                || decision.AllocationArmSteps < 0
                || decision.AllocationArmSteps > 0 && decision.AllocationDigest != CortexPolicyTrialAllocation.ComputeDigest(
                    decision.Policy, CortexPolicyAuthorities.Grammar, decision.AllocationArmSteps, decision.AllocationIdentity))
                passed = false;
            if (decision.Decision == CortexPolicyQuotaDecisions.Denied
                && decision.DenialReason == CortexPolicyTrialDenialReasons.None)
                passed = false;
            if (decision.Decision is CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused
                && decision.DenialReason != CortexPolicyTrialDenialReasons.None)
                passed = false;
            if (decision.Decision == CortexPolicyQuotaDecisions.Paid)
            {
                if (!fundedByID.TryAdd(decision.QuotaDecisionID, decision)) passed = false;
                funded.Add(decision);
                planned = checked(planned + decision.PlannedArmSteps);
                if (decision.RequestedHorizonSteps != 0)
                {
                    CortexPolicyQuotaDecisionID expected = Cortex.ComputePolicyTrialQuotaDecisionID(
                        decision.Policy, decision.CandidateFingerprint, decision.ReadoutFingerprint, decision.QuotaStep,
                        decision.RequestedHorizonSteps, decision.ArmCount, decision.AllocationDigest);
                    if (!expected.Equals(decision.QuotaDecisionID)) passed = false;
                }
                if (decision.HeldArmSteps != decision.PlannedArmSteps || decision.UsedSteps != decision.PlannedArmSteps)
                    passed = false;
            }
            else if (decision.Decision == CortexPolicyQuotaDecisions.Reused)
            {
                if (decision.HeldArmSteps != decision.PlannedArmSteps || decision.UsedSteps != 0)
                    passed = false;
                reused++;
            }
            else if (decision.Decision == CortexPolicyQuotaDecisions.Denied) denied++;
            else passed = false;
        }

        for (int i = 0; i < decisions.Count; i++)
            if (decisions[i].Decision == CortexPolicyQuotaDecisions.Reused
                && !fundedByID.ContainsKey(decisions[i].QuotaDecisionID))
                passed = false;

        long actual = 0, refund = 0;
        for (int i = 0; i < settlements.Count; i++)
        {
            CortexPolicyTrialCompletion settlement = settlements[i];
            bool linked = fundedByID.TryGetValue(settlement.QuotaDecisionID, out CortexPolicyTrialQuotaDecision funding);
            if (!linked) passed = false;
            actual = checked(actual + settlement.ActualExecutedArmSteps);
            refund = checked(refund + settlement.ReclaimedOrUnused);
            if (linked)
            {
                if (settlement.ActualExecutedArmSteps < 0
                    || settlement.ActualExecutedArmSteps > funding.PlannedArmSteps
                    || settlement.ReclaimedOrUnused < 0
                    || settlement.ReclaimedOrUnused != funding.PlannedArmSteps - settlement.ActualExecutedArmSteps)
                    passed = false;
                bool readout = funding.RequestedHorizonSteps == 0;
                bool outcomeValid = settlement.VerifierOutcome switch
                {
                    CortexPolicyVerifierOutcomes.ReadoutCompleted => readout,
                    CortexPolicyVerifierOutcomes.Passed or CortexPolicyVerifierOutcomes.Failed => !readout,
                    CortexPolicyVerifierOutcomes.NotRecorded => false,
                    _ => false,
                };
                if (!outcomeValid) passed = false;
                if (readout && (!settlement.EvaluatorWorkUnits.HasValue || settlement.EvaluatorWorkUnits < 0))
                    passed = false;
            }
        }
        if (funded.Count != settlements.Count || planned != actual + refund) passed = false;
        bool resumeReuse = funded.Count == 0 || decisions.Count >= funded.Count;
        bool denyIdentity = denied == 0 || decisions.Count >= denied;
        passed &= resumeReuse && denyIdentity;
        output.WriteLine($"  policy-trial-journal · rows={decisions.Count} funded={funded.Count} reused={reused} denied={denied} settlements={settlements.Count}");
        output.WriteLine($"  accounting · planned={planned} actual={actual} refund={refund} closed={(planned == actual + refund ? "yes" : "NO")}");
        output.WriteLine($"  resume · reuse={(resumeReuse ? "identity-stable" : "FAIL")} deny={(denyIdentity ? "identity-stable" : "FAIL")} verifier={(passed ? "PASS" : "FAIL")}");
        return new CortexPolicyTrialJournalOccurrenceCheck(passed, decisions.Count, funded.Count, reused, denied, settlements.Count, planned, actual, refund);
    }

    internal static List<CortexPolicyTrialQuotaDecision> ReadFundingDecisions(string path)
    {
        string[] lines = File.ReadAllLines(path);
        const string legacyHeader = "funding_id\tpolicy\tcandidate_fingerprint\tfunding_step\trequested_horizon_steps\tarm_count\tplanned_arm_steps\treserved_arm_steps\tdecision\tcharged_steps\tremaining_budget";
        const string stateHeader = legacyHeader + "\tcandidate_state\tdenial_reason\tcandidate_origin_step\tcandidate_current_step\tcandidate_required_step\tcandidate_revision";
        const string allocationHeader = stateHeader + "\tallocation_identity\tallocation_digest\tallocation_arm_steps";
        const string custodyHeader = allocationHeader + "\tseed_custody_digest";
        const string currentHeader = custodyHeader + "\treadout_fingerprint";
        string header = lines.Length == 0 ? "" : lines[0].TrimStart('\uFEFF');
        int minimumColumns = header == legacyHeader ? 11 : header == stateHeader ? 17 :
            header == allocationHeader ? 20 : header == custodyHeader ? 21 : header == currentHeader ? 22 : -1;
        if (minimumColumns < 0)
            throw new InvalidDataException("policy funding journal header is not the typed schema");
        List<CortexPolicyTrialQuotaDecision> rows = new(Math.Max(0, lines.Length - 1));
        int priorColumns = minimumColumns;
        for (int i = 1; i < lines.Length; i++)
        {
            string[] c = lines[i].Split('\t');
            if (c.Length is not (11 or 17 or 20 or 21 or 22) || c.Length < minimumColumns || c.Length < priorColumns)
                throw new InvalidDataException($"policy funding row {i + 1} regresses the journal schema");
            priorColumns = c.Length;
            CortexPolicyTrialQuotaDecision row = new(
                new CortexPolicyQuotaDecisionID(ulong.Parse(c[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture)),
                new CortexPolicyID(c[1]), ulong.Parse(c[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(c[3], CultureInfo.InvariantCulture), int.Parse(c[4], CultureInfo.InvariantCulture),
                int.Parse(c[5], CultureInfo.InvariantCulture), long.Parse(c[6], CultureInfo.InvariantCulture),
                long.Parse(c[7], CultureInfo.InvariantCulture), Enum.Parse<CortexPolicyQuotaDecisions>(c[8]),
                long.Parse(c[9], CultureInfo.InvariantCulture), long.Parse(c[10], CultureInfo.InvariantCulture))
            {
                CandidateState = c.Length is 17 or 20 or 21 or 22 ? Enum.Parse<CortexPolicyTrialCandidateStates>(c[11]) : CortexPolicyTrialCandidateStates.Active,
                DenialReason = c.Length is 17 or 20 or 21 or 22 ? Enum.Parse<CortexPolicyTrialDenialReasons>(c[12]) : CortexPolicyTrialDenialReasons.None,
                CandidateOriginStep = c.Length is 17 or 20 or 21 or 22 ? int.Parse(c[13], CultureInfo.InvariantCulture) : int.Parse(c[3], CultureInfo.InvariantCulture),
                CandidateCurrentStep = c.Length is 17 or 20 or 21 or 22 ? int.Parse(c[14], CultureInfo.InvariantCulture) : int.Parse(c[3], CultureInfo.InvariantCulture),
                CandidateRequiredStep = c.Length is 17 or 20 or 21 or 22 ? int.Parse(c[15], CultureInfo.InvariantCulture) : -1,
                CandidateRevision = c.Length is 17 or 20 or 21 or 22 ? new GrammarRevisionID(ulong.Parse(c[16], CultureInfo.InvariantCulture)) : GrammarRevisionID.Zero,
                AllocationIdentity = c.Length is 20 or 21 or 22 ? c[17] : "",
                AllocationDigest = c.Length is 20 or 21 or 22 ? c[18] : "",
                AllocationArmSteps = c.Length is 20 or 21 or 22 ? long.Parse(c[19], CultureInfo.InvariantCulture) : 0,
                SeedAuditOnlyDigest = c.Length is 21 or 22 ? c[20] : "",
                ReadoutFingerprint = c.Length == 22 ? ulong.Parse(c[21], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : 0,
            };
            if (c.Length == 22 && row.ReadoutFingerprint == 0)
                throw new InvalidDataException($"policy funding row {i + 1} omits its full readout fingerprint");
            if (c.Length is 21 or 22 && (row.SeedAuditOnlyDigest.Length != 0
                    && !IsCanonicalSeedAuditOnlyDigest(row.SeedAuditOnlyDigest)
                || row.Policy.Equals(Homeostat.PolicyID)
                    && row.Decision is (CortexPolicyQuotaDecisions.Paid or CortexPolicyQuotaDecisions.Reused)
                    && !IsCanonicalSeedAuditOnlyDigest(row.SeedAuditOnlyDigest)))
                throw new InvalidDataException($"policy funding row {i + 1} has invalid seed custody");
            rows.Add(row);
        }
        return rows;
    }

    private static bool SameFundingIdentity(in CortexPolicyTrialQuotaDecision first, in CortexPolicyTrialQuotaDecision second)
        => first.QuotaDecisionID.Equals(second.QuotaDecisionID)
            && first.Policy.Equals(second.Policy)
            && first.CandidateFingerprint == second.CandidateFingerprint
            && first.ReadoutFingerprint == second.ReadoutFingerprint
            && first.QuotaStep == second.QuotaStep
            && first.RequestedHorizonSteps == second.RequestedHorizonSteps
            && first.ArmCount == second.ArmCount
            && first.PlannedArmSteps == second.PlannedArmSteps
            && first.HeldArmSteps == second.HeldArmSteps
            && first.CandidateState == second.CandidateState
            && first.DenialReason == second.DenialReason
            && first.CandidateOriginStep == second.CandidateOriginStep
            && first.CandidateCurrentStep == second.CandidateCurrentStep
            && first.CandidateRequiredStep == second.CandidateRequiredStep
            && first.CandidateRevision == second.CandidateRevision
            && first.AllocationIdentity == second.AllocationIdentity
            && first.AllocationDigest == second.AllocationDigest
            && first.AllocationArmSteps == second.AllocationArmSteps
            && SameSeedCustodyIdentity(in first, in second);

    private static bool SameSeedCustodyIdentity(in CortexPolicyTrialQuotaDecision first, in CortexPolicyTrialQuotaDecision second)
    {
        if (!first.Policy.Equals(Homeostat.PolicyID))
            return first.SeedAuditOnlyDigest == second.SeedAuditOnlyDigest;
        if (first.Decision == CortexPolicyQuotaDecisions.Denied
            && second.Decision == CortexPolicyQuotaDecisions.Denied)
            return first.SeedAuditOnlyDigest == second.SeedAuditOnlyDigest;
        return IsCanonicalSeedAuditOnlyDigest(first.SeedAuditOnlyDigest)
                && first.SeedAuditOnlyDigest == second.SeedAuditOnlyDigest
            || first.SeedAuditOnlyDigest.Length == 0
                && first.Decision == CortexPolicyQuotaDecisions.Paid
                && second.Decision == CortexPolicyQuotaDecisions.Reused
                && IsCanonicalSeedAuditOnlyDigest(second.SeedAuditOnlyDigest);
    }

    private static bool IsCanonicalSeedAuditOnlyDigest(string digest)
        => digest.Length == 64 && digest.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static List<CortexPolicyTrialCompletion> ReadSettlements(string path)
    {
        string[] lines = File.ReadAllLines(path);
        if (lines.Length == 0 || lines[0].TrimStart('\uFEFF') != "funding_id\tactual_executed_arm_steps\trefund_or_slack\tevaluator_work_units\tverifier_outcome\twall_milliseconds")
            throw new InvalidDataException("policy settlement journal header is not the typed schema");
        List<CortexPolicyTrialCompletion> rows = new(Math.Max(0, lines.Length - 1));
        for (int i = 1; i < lines.Length; i++)
        {
            string[] c = lines[i].Split('\t');
            if (c.Length != 6) throw new InvalidDataException($"policy settlement row {i + 1} has the wrong shape");
            rows.Add(new CortexPolicyTrialCompletion(
                new CortexPolicyQuotaDecisionID(ulong.Parse(c[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture)),
                long.Parse(c[1], CultureInfo.InvariantCulture), long.Parse(c[2], CultureInfo.InvariantCulture),
                string.IsNullOrEmpty(c[3]) ? null : long.Parse(c[3], CultureInfo.InvariantCulture),
                Enum.Parse<CortexPolicyVerifierOutcomes>(c[4]),
                string.IsNullOrEmpty(c[5]) ? null : long.Parse(c[5], CultureInfo.InvariantCulture)));
        }
        return rows;
    }

    internal static List<CortexPolicyReadoutQuotaDecision> ReadReadoutFundingDecisions(string path)
    {
        string[] lines = File.ReadAllLines(path);
        const string header = "funding_id\tpolicy\tcandidate_fingerprint\tgrammar_revision\tcontext_digest\tcontext_bytes\tdeliberation_depth\tfunding_step\tplanned_units\treserved_units\tdecision\tcharged_units\tremaining_budget\tallocation_sequence\troster_digest\taccount_balance_before\taccount_balance_after";
        if (lines.Length == 0 || lines[0].TrimStart('\uFEFF') != header)
            throw new InvalidDataException("policy readout funding journal header is not the dedicated typed schema");
        List<CortexPolicyReadoutQuotaDecision> rows = new(Math.Max(0, lines.Length - 1));
        for (int i = 1; i < lines.Length; i++)
        {
            string[] c = lines[i].Split('\t');
            if (c.Length != 17) throw new InvalidDataException($"policy readout funding row {i + 1} has the wrong shape");
            rows.Add(new CortexPolicyReadoutQuotaDecision(
                new CortexPolicyQuotaDecisionID(ulong.Parse(c[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture)),
                new CortexPolicyID(c[1]), ulong.Parse(c[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                new GrammarRevisionID(ulong.Parse(c[3], CultureInfo.InvariantCulture)),
                ulong.Parse(c[4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(c[5], CultureInfo.InvariantCulture), int.Parse(c[6], CultureInfo.InvariantCulture),
                int.Parse(c[7], CultureInfo.InvariantCulture), long.Parse(c[8], CultureInfo.InvariantCulture),
                long.Parse(c[9], CultureInfo.InvariantCulture), Enum.Parse<CortexPolicyQuotaDecisions>(c[10]),
                long.Parse(c[11], CultureInfo.InvariantCulture), long.Parse(c[12], CultureInfo.InvariantCulture),
                long.Parse(c[13], CultureInfo.InvariantCulture), c[14], long.Parse(c[15], CultureInfo.InvariantCulture), long.Parse(c[16], CultureInfo.InvariantCulture)));
        }
        return rows;
    }

    internal static List<CortexPolicyReadoutAllocation> ReadReadoutAllocations(string path)
    {
        string[] lines = File.ReadAllLines(path);
        const string header = "sequence\tstep\troster_digest\tpolicy\tbalance_before\tcredited_units\texpired_units\tbalance_after";
        if (lines.Length == 0 || lines[0].TrimStart('\uFEFF') != header)
            throw new InvalidDataException("policy readout allocation journal header is not the dedicated typed schema");
        List<CortexPolicyReadoutAllocation> rows = new(Math.Max(0, lines.Length - 1));
        for (int i = 1; i < lines.Length; i++)
        {
            string[] c = lines[i].Split('\t');
            if (c.Length != 8) throw new InvalidDataException($"policy readout allocation row {i + 1} has the wrong shape");
            rows.Add(new CortexPolicyReadoutAllocation(
                long.Parse(c[0], CultureInfo.InvariantCulture), int.Parse(c[1], CultureInfo.InvariantCulture), c[2], new CortexPolicyID(c[3]),
                long.Parse(c[4], CultureInfo.InvariantCulture), long.Parse(c[5], CultureInfo.InvariantCulture), long.Parse(c[6], CultureInfo.InvariantCulture), long.Parse(c[7], CultureInfo.InvariantCulture)));
        }
        return rows;
    }
}
