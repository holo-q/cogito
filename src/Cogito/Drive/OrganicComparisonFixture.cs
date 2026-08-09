namespace Cogito;

using System.Security.Cryptography;
using System.Text;
using Cogito.Grammar;
using Cogito.Induct;

/// End-to-end custody gate for ordinary Homeostat comparison receipts.
/// The fixture builds policy decision packets first, then appends the typed
/// measurement through the production tape/journal seam.
internal static class OrganicComparisonFixture
{
    internal static bool Run(TextWriter output)
    {
        try
        {
            using Tape tape = new();
            Journal journal = new();
            OrganicComparisonReceipt[] receipts = AppendFixtureReceipts(tape, journal);
            bool counts = CheckCounts(receipts, output);
            bool codec = CheckCodec(tape, receipts, output);
            bool persistence = CheckPersistence(tape, receipts, output);
            bool rejection = CheckRejections(receipts, tape, output);
            bool observer = CheckObserver(receipts, output);
            bool passed = counts && codec && persistence && rejection && observer;
            output.WriteLine($"  organic-comparison fixture · counts={(counts ? "exact" : "BROKEN")} codec={(codec ? "exact" : "BROKEN")} persistence={(persistence ? "checkpoint/delta/replay" : "BROKEN")} rejection={(rejection ? "closed" : "OPEN")} observer={(observer ? "A/B" : "BROKEN")} · {(passed ? "PASS" : "FAIL")}");
            return passed;
        }
        catch (Exception ex)
        {
            output.WriteLine($"  organic-comparison fixture · FAIL · {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static OrganicComparisonReceipt[] AppendFixtureReceipts(Tape tape, Journal journal)
    {
        CortexPolicyID policy = Homeostat.PolicyID;
        CortexPolicyDecisionReadout[] readouts =
        [
            CortexPolicyDecisionBuilder.CreatePolicyDecisionReadout(0, -1, -1, 0, CortexPolicyAuthorities.Launchpad, new GrammarRevisionID(1)),
            CortexPolicyDecisionBuilder.CreatePolicyDecisionReadout(0, -1, -1, 0, CortexPolicyAuthorities.Launchpad, new GrammarRevisionID(1)),
            CortexPolicyDecisionBuilder.CreatePolicyDecisionReadout(0, 0, 0, 0, CortexPolicyAuthorities.Shadow, new GrammarRevisionID(1), readoutCandidateFingerprint: 0x1111, readoutCandidateOccurrenceDigest: 0x2222),
            CortexPolicyDecisionBuilder.CreatePolicyDecisionReadout(0, 1, 1, 1, CortexPolicyAuthorities.Grammar, new GrammarRevisionID(1), readoutCandidateFingerprint: 0x3333, readoutCandidateOccurrenceDigest: 0x4444),
        ];
        OrganicComparisonOutcomeKinds[] outcomes =
        [OrganicComparisonOutcomeKinds.ReadoutQuotaDenied, OrganicComparisonOutcomeKinds.ReadoutCompletedNoMatch, OrganicComparisonOutcomeKinds.CandidateAgreement, OrganicComparisonOutcomeKinds.CandidateDivergence];
        OrganicComparisonReceipt[] receipts = new OrganicComparisonReceipt[readouts.Length];
        MetricSample[] features = [new(new MetricID(0), NumericValue.FromI64(1))];
        for (int i = 0; i < readouts.Length; i++)
        {
            CortexPolicyDecision decision = new(new CortexPolicyDecisionID((ulong)i + 1), policy, readouts[i]);
            TapeEventID sourceEvent = TapePacketCreator.AppendPolicyDecision(tape, journal, i, in decision, features, Homeostat.PolicySchema.ActionCount, out byte[] payload);
            string payloadDigest = Digest(payload);
            string journalDigest = Digest(Encoding.UTF8.GetBytes(journal.ResidentLines[^1]));
            CortexPolicyQuotaDecisionID fundingID = new((ulong)i + 100);
            CortexPolicyQuotaDecisions? funding = i == 0 ? CortexPolicyQuotaDecisions.Denied : i == 1 ? CortexPolicyQuotaDecisions.Paid : null;
            CortexPolicyQuotaDecisionID? receiptQuotaID = i < 2 ? fundingID : null;
            string fundingDigest = i < 2 ? Digest(Encoding.UTF8.GetBytes($"funding-{fundingID.Value:X16}")) : "";
            string settlementDigest = i == 1 ? Digest(Encoding.UTF8.GetBytes($"settlement-{fundingID.Value:X16}")) : "";
            OrganicComparisonReceipt receipt = new(i, policy, decision.DecisionID, sourceEvent, payloadDigest, journalDigest,
                readouts[i].GrammarRevision, readouts[i].ReadoutCandidateFingerprint, readouts[i].ReadoutCandidateFingerprint,
                readouts[i].ReadoutCandidateOccurrenceDigest, readouts[i].LaunchpadAction, readouts[i].RawCandidateAction,
                readouts[i].SelectedCandidateAction, outcomes[i], receiptQuotaID, funding, fundingDigest, settlementDigest, "");
            receipt = receipt with { CanonicalReceiptSHA256 = OrganicComparisonReceipt.ComputeCanonicalReceiptSHA256(receipt) };
            TapePacketCreator.AppendOrganicComparison(tape, journal, i, in receipt);
            receipts[i] = receipt;
        }
        return receipts;
    }

    private static bool CheckCounts(OrganicComparisonReceipt[] receipts, TextWriter output)
    {
        int denied = receipts.Count(static r => r.Outcome == OrganicComparisonOutcomeKinds.ReadoutQuotaDenied);
        int noMatch = receipts.Count(static r => r.Outcome == OrganicComparisonOutcomeKinds.ReadoutCompletedNoMatch);
        int agreements = receipts.Count(static r => r.Outcome == OrganicComparisonOutcomeKinds.CandidateAgreement);
        int divergence = receipts.Count(static r => r.Outcome == OrganicComparisonOutcomeKinds.CandidateDivergence);
        bool comparisons = receipts.Count(static r => r.RawCandidateAction >= 0) == agreements + divergence;
        bool exact = receipts.Length == denied + noMatch + agreements + divergence && denied == 1 && noMatch == 1 && agreements == 1 && divergence == 1 && comparisons;
        output.WriteLine($"  organic-comparison conservation · eligible={receipts.Length} denied={denied} no-match={noMatch} agreement={agreements} divergence={divergence} comparisons={agreements + divergence} · {(exact ? "PASS" : "FAIL")}");
        return exact;
    }

    private static bool CheckCodec(Tape tape, OrganicComparisonReceipt[] receipts, TextWriter output)
    {
        OrganicComparisonReceipt[] decoded = [.. tape.GetEventViews().Where(v => v.Source == "policy:homeostat:organic-comparison").Select(v => tape.Resolve(v.Id, out byte[] bytes) ? TapePacketCreator.DecodeOrganicComparison(bytes) : throw new InvalidDataException("organic receipt did not resolve"))];
        bool exact = decoded.SequenceEqual(receipts) && decoded.All(static r => r.CanonicalReceiptSHA256 == OrganicComparisonReceipt.ComputeCanonicalReceiptSHA256(r));
        output.WriteLine($"  organic-comparison codec · receipts={decoded.Length} source=policy-decision+funding+settlement · {(exact ? "PASS" : "FAIL")}");
        return exact;
    }

    private static bool CheckPersistence(Tape source, OrganicComparisonReceipt[] expected, TextWriter output)
    {
        byte[] image;
        using (MemoryStream stream = new()) { using (CkptWriter writer = new(stream)) source.Save(writer); image = stream.ToArray(); }
        using Tape loaded = new();
        using (MemoryStream stream = new(image)) using (CkptReader reader = new(stream)) loaded.Load(reader);
        bool full = source.Concat().AsSpan().SequenceEqual(loaded.Concat()) && Decode(loaded).SequenceEqual(expected);
        TapeCheckpointDelta delta = source.CaptureCheckpointDelta();
        using Tape replay = new(); replay.ApplyCheckpointDelta(in delta);
        bool deltaExact = source.Concat().AsSpan().SequenceEqual(replay.Concat()) && Decode(replay).SequenceEqual(expected);
        output.WriteLine($"  organic-comparison persistence · full={(full ? "exact" : "BROKEN")} delta={(deltaExact ? "exact" : "BROKEN")} replay={(deltaExact ? "idempotent" : "BROKEN")} · {(full && deltaExact ? "PASS" : "FAIL")}");
        return full && deltaExact;
    }

    private static bool CheckRejections(OrganicComparisonReceipt[] receipts, Tape tape, TextWriter output)
    {
        OrganicComparisonReceipt source = receipts[2];
        bool identity = Reject(source with { DecisionID = new CortexPolicyDecisionID(99) });
        bool sourcePayload = Reject(source with { SourceDecisionPayloadSHA256 = new string('a', 64) });
        bool sourceEvent = Reject(source with { SourceDecisionEventID = new TapeEventID(99) });
        bool outcome = Reject(source with { Outcome = OrganicComparisonOutcomeKinds.ReadoutQuotaDenied });
        bool actions = Reject(source with { RawCandidateAction = source.LaunchpadAction + 1 });
        bool funding = Reject(receipts[0] with { FundingDecision = CortexPolicyQuotaDecisions.Paid });
        bool settlement = Reject(receipts[1] with { SettlementJournalRowSHA256 = "" });
        bool digest = Reject(source with { CanonicalReceiptSHA256 = new string('b', 64) });
        byte[] encoded = TapePacketCreator.EncodeOrganicComparison(in source);
        byte[] mutated = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(encoded).Replace("decision=u:0000000000000003", "decision=u:0000000000000063", StringComparison.Ordinal));
        bool packetMutation = !TapePacketCreator.TryDecodeOrganicComparison(mutated, out _);
        TapeCheckpointDelta delta = tape.CaptureCheckpointDelta();
        TapeCheckpointDelta roleMutation = delta with { Appended = [.. delta.Appended.Select((row, i) => i == delta.Appended.Length - 1 ? row with { Roles = (TapeEventRoles)0x80 } : row)] };
        using Tape roleReplay = new();
        bool roleRejected = Reject(() => roleReplay.ApplyCheckpointDelta(in roleMutation));
        bool all = identity && sourcePayload && sourceEvent && outcome && actions && funding && settlement && digest && packetMutation && roleRejected;
        output.WriteLine($"  organic-comparison rejection · decision/event/payload/journal={identity && sourcePayload && sourceEvent} outcome/actions={outcome && actions} funding/settlement={funding && settlement} digest={digest && packetMutation} role={(roleRejected ? "rejected" : "ACCEPTED")} · {(all ? "PASS" : "FAIL")}");
        return all;
    }

    private static bool CheckObserver(OrganicComparisonReceipt[] receipts, TextWriter output)
    {
        using Tape baseline = new();
        baseline.Append("abababababab"u8.ToArray(), "fixture", Provenances.Real, TapeEventRoles.GrammarInput);
        using Tape control = new();
        control.Append("abababababab"u8.ToArray(), "fixture", Provenances.Real, TapeEventRoles.GrammarInput);
        using Tape repaired = new();
        repaired.Append("abababababab"u8.ToArray(), "fixture", Provenances.Real, TapeEventRoles.GrammarInput);
        byte[] bytes = TapePacketCreator.EncodeOrganicComparison(in receipts[2]);
        // The repaired arm carries the exact producer bytes. The control keeps
        // the receipt body/provenance but uses a non-protocol marker so the
        // deliberate GrammarInput observer can pass Tape's role invariant.
        byte[] controlBytes = bytes.ToArray();
        controlBytes[0] = (byte)'o';
        control.Append(controlBytes, "fixture:organic-comparison", Provenances.Execution, TapeEventRoles.GrammarInput);
        repaired.Append(bytes, "fixture:organic-comparison", Provenances.Execution, TapeEventRoles.Measurement | TapeEventRoles.AuditOnly);
        bool perturbs = GrammarDigest(Engine.Induce(control, 1).Result) != GrammarDigest(Engine.Induce(baseline, 1).Result);
        bool stable = GrammarDigest(Engine.Induce(repaired, 1).Result) == GrammarDigest(Engine.Induce(baseline, 1).Result);
        output.WriteLine($"  organic-comparison observer A/B · control={(perturbs ? "perturbs" : "stable")} repaired={(stable ? "baseline" : "DRIFT")} · {(perturbs && stable ? "PASS" : "FAIL")}");
        return perturbs && stable;
    }

    private static OrganicComparisonReceipt[] Decode(Tape tape)
        => [.. tape.GetEventViews().Where(v => v.Source == "policy:homeostat:organic-comparison").Select(v => tape.Resolve(v.Id, out byte[] bytes) ? TapePacketCreator.DecodeOrganicComparison(bytes) : throw new InvalidDataException("missing receipt"))];

    private static bool Reject(OrganicComparisonReceipt receipt) { try { receipt.Validate(); return false; } catch (InvalidDataException) { return true; } }
    private static bool Reject(Action action) { try { action(); return false; } catch (Exception error) when (error is InvalidDataException or ArgumentException or InvalidOperationException) { return true; } }
    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string GrammarDigest(in RePairResult grammar)
    {
        StringBuilder text = new(); text.Append(grammar.AlphabetSize).Append('|');
        foreach (GrammarRule rule in grammar.Rules) { text.Append(rule.Id).Append(':').Append(rule.Cost.Value).Append(':'); foreach (Symbol symbol in rule.Pattern) text.Append(symbol.Value).Append(','); text.Append('|'); }
        text.Append("/|"); foreach (Symbol symbol in grammar.Compressed) text.Append(symbol.Value).Append(',');
        return Digest(Encoding.UTF8.GetBytes(text.ToString()));
    }
}
