namespace Cogito;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// A typed, read-only view over the ordinary land reports used by paired-gate lines 4 and 7.
/// The run remains the authority: this reader never writes a report, tape packet, or recovery marker.
internal static class PairedGateVitalsReader
{
    private static readonly Regex Census = Create(
        "^  census: day (?<day>[0-9]+) \\((?<dayPct>[^)]+)\\) · dream (?<dream>[0-9]+) \\((?<dreamPct>[^)]+)\\) · aestivation (?<aestivation>[0-9]+) \\((?<aestivationPct>[^)]+)\\) over (?<total>[0-9]+) steps$");
    private static readonly Regex PolicyOutcomes = Create(
        "^  policy outcomes: (?<total>[0-9]+) resolved \\(day (?<day>[0-9]+) · dream (?<dream>[0-9]+) · aestivation (?<aestivation>[0-9]+)\\) · productivity Σ(?<productivity>[^ ]+) · magnitude Σ(?<magnitude>[^ ]+)$");
    private static readonly Regex ReplayOutcomeCredit = Create(
        "^  dream vesting: (?<resolved>[0-9]+) cohorts resolved · (?<productive>[0-9]+) productive \\(≥1 vest\\) · yield⌂ (?<yield>.+)$");
    private static readonly Regex Window = Create(
        "^  window: dream_frac (?<dreamFrac>[^ ]+) · residual⌂ (?<residual>[^ ]+) · last worth (?<worth>[^ ]+)$");
    private static readonly Regex Bootstrap = Create("^  bootstrap: .* · final eps (?<eps>[^ ]+)$");
    private static readonly Regex Closes = Create("^  closes (?<closes>[0-9]+) \\(wasted (?<wasted>[0-9]+)\\) · reversals (?<reversals>[0-9]+) · census: .+$");
    private static readonly Regex Grammar = Create("^  valid/paid grammar: (?<valid>[0-9]+)/(?<paid>[0-9]+) · outcomes (?<outcomes>[0-9]+)$");
    private static readonly Regex Conservation = Create("^  decision conservation: (?<outcomes>[0-9]+)/(?<decisions>[0-9]+) closed · unresolved (?<unresolved>[0-9]+)$");
    private static readonly byte[] BoundaryPrefix = Encoding.ASCII.GetBytes("POLICY-BOUNDARY\t");

    private static Regex Create(string pattern) => new(pattern, RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal readonly record struct RhythmReadout(
        bool HasOpportunity,
        long Day,
        long Replay,
        long ConsolidationPhase,
        long Total,
        double ReplayFraction,
        double Residual,
        double LastWorth,
        double FinalEpsilon,
        long PolicyOutcomes,
        long DayPolicyOutcomes,
        long ReplayPolicyOutcomes,
        long ConsolidationPhasePolicyOutcomes,
        double PolicyProductivity,
        double PolicyMagnitude,
        long ReplayCohortsResolved,
        long ProductiveReplayCohorts,
        double ReplayYield)
    {
        internal bool DayPresent => HasOpportunity && Day > 0;
        internal bool ResidualThawed => HasOpportunity && double.IsFinite(Residual) && Residual > 0;
    }

    internal readonly record struct HomeostatReadout(
        long Closes,
        long WastedCloses,
        long SignReversals,
        long ValidGrammarExecutions,
        long PaidGrammarOutcomes,
        long GrammarOutcomes,
        long DecisionOutcomes,
        long Decisions,
        long UnresolvedDecisions)
    {
        internal bool AccountingClosed => DecisionOutcomes + UnresolvedDecisions == Decisions;
        internal bool HasPaidClose => PaidGrammarOutcomes > 0;
    }

    internal readonly record struct PolicyReadout(
        CortexPolicyTrialJournalOccurrenceCheck Trial,
        CortexPolicyTrialJournalOccurrenceCheck ReadoutTrial,
        CortexPolicyDecisionReadoutOccurrenceCheck DecisionReadout,
        int BoundaryReceipts,
        long PaidCloseDelta,
        long BaselinePaidCloseDelta,
        long ForcedDivergentNullPaidCloseDelta,
        long ReflexPaidCloseDelta,
        long GrammarExecutions,
        long BaselineGrammarExecutions,
        long ForcedDivergentNullGrammarExecutions,
        long ReflexGrammarExecutions,
        long ForcedDivergentNullExecutions,
        long ReflexAdaptationTransitions,
        bool ForcedDivergentNullBehaviorExecuted,
        bool HasBoundaryOpportunity)
    {
        internal int PaidTrials => Trial.PaidRows + ReadoutTrial.PaidRows;
        internal bool VerifiersPassed => Trial.Passed && ReadoutTrial.Passed && DecisionReadout.Passed;
        internal bool ReflexAdaptationZero => ReflexAdaptationTransitions == 0;
    }

    internal readonly record struct RunReadout(RhythmReadout Rhythm, HomeostatReadout Homeostat, PolicyReadout Policy);

    /// Read one ordinary run. Every verifier is run against TextWriter.Null; no adjacent report is synthesized.
    internal static RunReadout Read(string runDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);
        if (!Directory.Exists(runDirectory)) throw new DirectoryNotFoundException(runDirectory);
        using Tape tape = Checkpoint.LoadTape(runDirectory);
        return Read(runDirectory, tape);
    }

    /// Read vitals against a tape decoded from the caller's immutable arm
    /// checkpoint snapshot. This keeps policy-boundary evidence single-pass
    /// when several adjudication lines consume the same arm.
    internal static RunReadout Read(string runDirectory, Tape tape)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);
        ArgumentNullException.ThrowIfNull(tape);
        if (!Directory.Exists(runDirectory)) throw new DirectoryNotFoundException(runDirectory);
        RhythmReadout rhythm = ReadRhythm(Path.Combine(runDirectory, "rhythm.txt"));
        HomeostatReadout homeostat = ReadHomeostat(Path.Combine(runDirectory, "homeostat.txt"));

        try
        {
            using CortexPolicyOccurrenceCheckBundle bundle = new(runDirectory, tape);
            CortexPolicyTrialJournalOccurrenceCheck trial = CortexPolicyTrialJournalVerifier.Verify(bundle, TextWriter.Null);
            CortexPolicyTrialJournalOccurrenceCheck readoutTrial = CortexPolicyTrialJournalVerifier.VerifyReadout(bundle, TextWriter.Null);
            CortexPolicyDecisionReadoutOccurrenceCheck decisionReadout = CortexPolicyDecisionReadoutVerifier.Verify(bundle, TextWriter.Null);
            PolicyEvidence boundary = ReadPolicyBoundaryEvidence(tape);
            return new RunReadout(rhythm, homeostat, new PolicyReadout(
                trial, readoutTrial, decisionReadout, boundary.BoundaryReceipts, boundary.PaidCloseDelta,
                boundary.BaselinePaidCloseDelta, boundary.ForcedDivergentNullPaidCloseDelta, boundary.ReflexPaidCloseDelta,
                boundary.GrammarExecutions, boundary.BaselineGrammarExecutions, boundary.ForcedDivergentNullGrammarExecutions,
                boundary.ReflexGrammarExecutions, boundary.ForcedDivergentNullExecutions, boundary.ReflexAdaptationTransitions,
                boundary.ForcedDivergentNullBehaviorExecuted, boundary.HasBoundaryOpportunity));
        }
        catch (Exception error) when (error is FileNotFoundException or InvalidDataException or FormatException or OverflowException)
        {
            throw new InvalidDataException("paired-gate policy evidence is incomplete or malformed", error);
        }
    }

    internal static RhythmReadout ReadRhythm(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("rhythm report is missing", path);
        return ParseRhythm(File.ReadAllLines(path), path);
    }

    internal static HomeostatReadout ReadHomeostat(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("homeostat report is missing", path);
        return ParseHomeostat(File.ReadAllLines(path), path);
    }

    internal static bool VerifyFixture(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        const string rhythm = "── RHYTHM · emergent metabolism — the machine schedules its own day/dream/aestivation ──\n"
            + "  census: day 4 (40%) · dream 5 (50%) · aestivation 1 (10%) over 10 steps\n"
            + "  bootstrap: 1 ε-dreams fired · headroom-bound days 0 · fresh-frontier re-seeds 0 (the oscillator's return-swings) · final eps 0.125\n"
            + "  policy outcomes: 3 resolved (day 1 · dream 1 · aestivation 1) · productivity Σ1.500 · magnitude Σ3\n"
            + "  dream vesting: 2 cohorts resolved · 1 productive (≥1 vest) · yield⌂ 0.500\n"
            + "  window: dream_frac 0.500 · residual⌂ 0.750 · last worth 0.600\n"
            + "  self-prediction (the rhythm channel): 3 events · hit 50.0% · rolling mint 25%\n"
            + "    mint-rate decay: 1.0\n"
            + "    top rhythm motifs (the machine's metabolic vocabulary):\n";
        const string homeostat = "── HOMEOSTAT · the policy plane (Wired/Autonomic) — wired senses + forecast leads on the vest seam ──\n"
            + "  closes 4 (wasted 1) · reversals 0 · census: relax 4 ·\n"
            + "  valid/paid grammar: 3/2 · outcomes 2\n"
            + "  decision conservation: 2/3 closed · unresolved 1\n";
        bool valid = ParseRhythm(SplitFixture(rhythm), "fixture rhythm").DayPresent && ParseHomeostat(SplitFixture(homeostat), "fixture homeostat").HasPaidClose;
        bool corrupt = Rejects(() => ParseRhythm(SplitFixture(rhythm.Replace("over 10 steps", "over 11 steps", StringComparison.Ordinal)), "corrupt rhythm"));
        bool ambiguous = Rejects(() => ParseRhythm(SplitFixture(rhythm + "  window: dream_frac 0.500 · residual⌂ 0.750 · last worth 0.600\n"), "ambiguous rhythm"));
        RhythmReadout noOpportunity = ParseRhythm(SplitFixture("── RHYTHM · emergent metabolism — the machine schedules its own day/dream/aestivation ──\n  (never consulted — the arm was off)\n"), "no-opportunity rhythm");
        bool noOpportunityFixture = !noOpportunity.HasOpportunity && noOpportunity.Total == 0;
        bool passed = valid && corrupt && ambiguous && noOpportunityFixture;
        output.WriteLine($"  paired-gate vitals fixture · valid={(valid ? "typed" : "BROKEN")} · corrupt={(corrupt ? "rejected" : "ACCEPTED")} · ambiguous={(ambiguous ? "rejected" : "ACCEPTED")} · no-opportunity={(noOpportunityFixture ? "typed-null" : "BROKEN")} · {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    private static string[] SplitFixture(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static RhythmReadout ParseRhythm(string[] lines, string path)
    {
        if (CountHeader(lines, "── RHYTHM") != 1) throw new InvalidDataException($"rhythm report header is missing or duplicated: {path}");
        int noOpportunity = lines.Count(static line => line == "  (never consulted — the arm was off)");
        if (noOpportunity == 1)
        {
            if (lines.Length != 2) throw new InvalidDataException("rhythm no-opportunity report has extra lines");
            return new RhythmReadout(false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }
        if (noOpportunity > 1) throw new InvalidDataException("rhythm no-opportunity marker is duplicated");
        Match census = MatchUnique(lines, Census, "rhythm census");
        long day = CountValue(census, "day", "rhythm day"), dream = CountValue(census, "dream", "rhythm dream"), aestivation = CountValue(census, "aestivation", "rhythm aestivation"), total = CountValue(census, "total", "rhythm total");
        if (total <= 0 || checked(day + dream + aestivation) != total) throw new InvalidDataException("rhythm phase census does not close");
        ValidatePercent(census, "dayPct", "rhythm day percentage"); ValidatePercent(census, "dreamPct", "rhythm dream percentage"); ValidatePercent(census, "aestivationPct", "rhythm aestivation percentage");
        Match policy = MatchUnique(lines, PolicyOutcomes, "rhythm policy outcomes");
        long policyTotal = CountValue(policy, "total", "rhythm policy total"), dayPolicy = CountValue(policy, "day", "rhythm day policy outcomes"), dreamPolicy = CountValue(policy, "dream", "rhythm dream policy outcomes"), aestivationPolicy = CountValue(policy, "aestivation", "rhythm aestivation policy outcomes");
        if (checked(dayPolicy + dreamPolicy + aestivationPolicy) != policyTotal) throw new InvalidDataException("rhythm policy outcome census does not close");
        double productivity = Finite(policy, "productivity", "rhythm productivity"), magnitude = Finite(policy, "magnitude", "rhythm magnitude");
        Match outcomeCredit = MatchUnique(lines, ReplayOutcomeCredit, "rhythm dream vesting");
        long resolved = CountValue(outcomeCredit, "resolved", "rhythm resolved cohorts"), productive = CountValue(outcomeCredit, "productive", "rhythm productive cohorts");
        if (productive > resolved) throw new InvalidDataException("rhythm productive cohorts exceed resolved cohorts");
        string yieldText = outcomeCredit.Groups["yield"].Value.Trim();
        double yield = yieldText == "— (no cohort matured)" ? double.NaN : Finite(yieldText, "rhythm yield");
        if ((!double.IsNaN(yield) && (yield < 0 || yield > 1)) || (resolved == 0 && !double.IsNaN(yield)) || (resolved > 0 && double.IsNaN(yield))) throw new InvalidDataException("rhythm yield does not match cohort census");
        Match window = MatchUnique(lines, Window, "rhythm window");
        double dreamFraction = Finite(window, "dreamFrac", "rhythm dream fraction"), residual = Finite(window, "residual", "rhythm residual"), worth = Finite(window, "worth", "rhythm worth");
        if (dreamFraction is < 0 or > 1 || residual is < 0 or > 1) throw new InvalidDataException("rhythm fraction/residual is outside [0,1]");
        Match bootstrap = MatchUnique(lines, Bootstrap, "rhythm bootstrap");
        double epsilon = Finite(bootstrap, "eps", "rhythm final epsilon");
        if (epsilon is < 0 or > 1) throw new InvalidDataException("rhythm final epsilon is outside [0,1]");
        return new RhythmReadout(true, day, dream, aestivation, total, dreamFraction, residual, worth, epsilon, policyTotal, dayPolicy, dreamPolicy, aestivationPolicy, productivity, magnitude, resolved, productive, yield);
    }

    private static HomeostatReadout ParseHomeostat(string[] lines, string path)
    {
        if (CountHeader(lines, "── HOMEOSTAT") != 1) throw new InvalidDataException($"homeostat report header is missing or duplicated: {path}");
        Match closes = MatchUnique(lines, Closes, "homeostat closes");
        long closeCount = CountValue(closes, "closes", "homeostat closes"), wasted = CountValue(closes, "wasted", "homeostat wasted closes"), reversals = CountValue(closes, "reversals", "homeostat reversals");
        if (wasted > closeCount) throw new InvalidDataException("homeostat wasted closes exceed closes");
        Match grammar = MatchUnique(lines, Grammar, "homeostat grammar");
        long valid = CountValue(grammar, "valid", "homeostat valid grammar executions"), paid = CountValue(grammar, "paid", "homeostat paid grammar outcomes"), grammarOutcomes = CountValue(grammar, "outcomes", "homeostat grammar outcomes");
        if (paid > valid || grammarOutcomes > valid || paid > grammarOutcomes) throw new InvalidDataException("homeostat grammar counters do not close");
        Match conservation = MatchUnique(lines, Conservation, "homeostat decision conservation");
        long decisionOutcomes = CountValue(conservation, "outcomes", "homeostat decision outcomes"), decisions = CountValue(conservation, "decisions", "homeostat decisions"), unresolved = CountValue(conservation, "unresolved", "homeostat unresolved decisions");
        if (decisionOutcomes > decisions || checked(decisionOutcomes + unresolved) != decisions) throw new InvalidDataException("homeostat decision accounting does not close");
        return new HomeostatReadout(closeCount, wasted, reversals, valid, paid, grammarOutcomes, decisionOutcomes, decisions, unresolved);
    }

    private readonly record struct PolicyEvidence(
        int BoundaryReceipts,
        long PaidCloseDelta,
        long BaselinePaidCloseDelta,
        long ForcedDivergentNullPaidCloseDelta,
        long ReflexPaidCloseDelta,
        long GrammarExecutions,
        long BaselineGrammarExecutions,
        long ForcedDivergentNullGrammarExecutions,
        long ReflexGrammarExecutions,
        long ForcedDivergentNullExecutions,
        long ReflexAdaptationTransitions,
        bool ForcedDivergentNullBehaviorExecuted,
        bool HasBoundaryOpportunity);

    private static PolicyEvidence ReadPolicyBoundaryEvidence(Tape tape)
    {
        int receipts = 0;
        long paid = 0, baselinePaid = 0, nullPaid = 0, reflexPaid = 0;
        long executions = 0, baselineExecutions = 0, nullExecutions = 0, reflexExecutions = 0;
        long nullBehaviorExecutions = 0, reflexAdaptations = 0;
        bool allNullBehaviorExecuted = true;
        HashSet<string> digests = new(StringComparer.Ordinal);
        foreach (TapeEventView view in tape.GetEventViews())
        {
            if (!tape.Resolve(view.Id, out byte[] bytes) || !bytes.AsSpan().StartsWith(BoundaryPrefix)) continue;
            if (!PolicyBoundaryTapeVerifier.TryRead(bytes, HomeostatPolicyBoundaryDomain.Instance,
                    out PolicyBoundaryForkReceipt receipt, out CortexPolicyID policy)
                || !policy.Equals(Homeostat.PolicyID)) throw new InvalidDataException("policy-boundary tape packet is malformed");
            receipt.Validate(HomeostatPolicyBoundaryDomain.Instance);
            string digest = PolicyBoundaryObligation.ComputeReceiptDigest(in receipt);
            if (!digests.Add(digest)) throw new InvalidDataException("duplicate policy-boundary tape receipt");
            receipts++;
            if (receipt.Arms.Length == 0) throw new InvalidDataException("policy-boundary tape receipt has no arms");
            foreach (PolicyBoundaryArmReceipt arm in receipt.Arms)
            {
                switch (arm.Arm)
                {
                    case PolicyBoundaryArms.Baseline:
                        baselinePaid = checked(baselinePaid + arm.PaidCloseDelta);
                        baselineExecutions = checked(baselineExecutions + arm.GrammarExecutionsDelta);
                        break;
                    case PolicyBoundaryArms.Candidate:
                        paid = checked(paid + arm.PaidCloseDelta);
                        executions = checked(executions + arm.GrammarExecutionsDelta);
                        break;
                    case PolicyBoundaryArms.ForcedDivergentNull:
                        nullPaid = checked(nullPaid + arm.PaidCloseDelta);
                        nullExecutions = checked(nullExecutions + arm.GrammarExecutionsDelta);
                        if (arm.BehaviorallyExecuted) nullBehaviorExecutions++;
                        break;
                    case PolicyBoundaryArms.ReflexFrozenControl:
                        reflexPaid = checked(reflexPaid + arm.PaidCloseDelta);
                        reflexExecutions = checked(reflexExecutions + arm.GrammarExecutionsDelta);
                        reflexAdaptations = checked(reflexAdaptations + arm.TrialAdaptationTransitions);
                        break;
                }
            }
            allNullBehaviorExecuted &= receipt.ForcedNullBehaviorExecuted;
        }
        return new PolicyEvidence(receipts, paid, baselinePaid, nullPaid, reflexPaid, executions, baselineExecutions,
            nullExecutions, reflexExecutions, nullBehaviorExecutions, reflexAdaptations, receipts > 0 && allNullBehaviorExecuted, receipts > 0);
    }

    private static int CountHeader(string[] lines, string prefix) => lines.Count(line => line.StartsWith(prefix, StringComparison.Ordinal));
    private static Match MatchUnique(string[] lines, Regex regex, string label)
    {
        Match? found = null;
        foreach (string line in lines)
        {
            Match match = regex.Match(line);
            if (!match.Success) continue;
            if (found is not null) throw new InvalidDataException($"{label} is duplicated or ambiguous");
            found = match;
        }
        return found ?? throw new InvalidDataException($"{label} is missing");
    }

    private static long CountValue(Match match, string group, string label)
        => long.TryParse(match.Groups[group].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long value)
            ? value : throw new InvalidDataException($"{label} is not a finite nonnegative integer");

    private static double Finite(Match match, string group, string label) => Finite(match.Groups[group].Value, label);
    private static double Finite(string text, string label)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value)
            ? value : throw new InvalidDataException($"{label} is not finite");

    private static void ValidatePercent(Match match, string group, string label)
    {
        double value = Finite(match.Groups[group].Value.Trim().TrimEnd('%').Trim(), label);
        if (value is < 0 or > 100) throw new InvalidDataException($"{label} is outside [0,100]");
    }

    private static bool Rejects(Action action)
    {
        try { action(); return false; }
        catch (Exception error) when (error is InvalidDataException or OverflowException) { return true; }
    }
}
