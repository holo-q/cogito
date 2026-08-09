namespace Cogito;

/// One verification pass's policy evidence — the parsed trial/readout journals,
/// the decision receipt + journal lines, and the run Tape — loaded lazily ONCE
/// and threaded through the trial, readout, and decision verifiers so an
/// adjudication chain stops re-parsing the same TSVs and re-decoding the same
/// tape per verifier. Verdicts are identical to the directory entry points; the
/// bundle only deduplicates loads. Never reuse a bundle across verification
/// passes: files may change between passes, so every pass builds its own.
internal sealed class CortexPolicyOccurrenceCheckBundle : IDisposable
{
    private readonly Tape? _callerTape;
    private Tape? _ownedTape;
    private List<CortexPolicyTrialQuotaDecision>? _trialFunding;
    private List<CortexPolicyTrialCompletion>? _trialSettlements;
    private List<CortexPolicyReadoutQuotaDecision>? _readoutFunding;
    private List<CortexPolicyTrialCompletion>? _readoutSettlements;
    private List<CortexPolicyReadoutAllocation>? _readoutAllocations;
    private bool _readoutAllocationsLoaded;
    private string[]? _decisionReceiptLines;
    private string[]? _journalLines;

    internal CortexPolicyOccurrenceCheckBundle(string runDirectory, Tape? tape = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);
        RunDirectory = runDirectory;
        _callerTape = tape;
    }

    internal string RunDirectory { get; }

    /// A caller-provided tape is trusted as this run's decoded tape and never
    /// disposed here; absent one, the run's tape is decoded once and owned.
    internal Tape Tape => _callerTape ?? (_ownedTape ??= Checkpoint.LoadTape(RunDirectory));

    internal List<CortexPolicyTrialQuotaDecision> TrialFundingDecisions
        => _trialFunding ??= CortexPolicyTrialJournalVerifier.ReadFundingDecisions(
            RequireFile("policy_trial_funding.journal.tsv", "policy funding journal is missing"));

    internal List<CortexPolicyTrialCompletion> TrialCompletions
        => _trialSettlements ??= CortexPolicyTrialJournalVerifier.ReadSettlements(
            RequireFile("policy_trial_settlements.journal.tsv", "policy settlement journal is missing"));

    internal List<CortexPolicyReadoutQuotaDecision> ReadoutFundingDecisions
        => _readoutFunding ??= CortexPolicyTrialJournalVerifier.ReadReadoutFundingDecisions(
            RequireFile("policy_readout_funding.journal.tsv", "policy readout funding journal is missing"));

    internal List<CortexPolicyTrialCompletion> ReadoutCompletions
        => _readoutSettlements ??= CortexPolicyTrialJournalVerifier.ReadSettlements(
            RequireFile("policy_readout_settlements.journal.tsv", "policy readout settlement journal is missing"));

    /// Null when the allocation journal is absent — the readout verifier owns
    /// the verdict for that absence.
    internal List<CortexPolicyReadoutAllocation>? ReadoutAllocations
    {
        get
        {
            if (!_readoutAllocationsLoaded)
            {
                string path = Path.Combine(RunDirectory, "policy_readout_allocations.journal.tsv");
                _readoutAllocations = File.Exists(path) ? CortexPolicyTrialJournalVerifier.ReadReadoutAllocations(path) : null;
                _readoutAllocationsLoaded = true;
            }
            return _readoutAllocations;
        }
    }

    internal string[] DecisionReceiptLines
        => _decisionReceiptLines ??= File.ReadAllLines(
            RequireFile(CortexPolicyDecisionReadoutVerifier.ReceiptFile, "policy decision readout receipt is missing"));

    internal string[] JournalLines
        => _journalLines ??= File.ReadAllLines(RequireFile("journal.log", "journal is missing"));

    private string RequireFile(string file, string missingMessage)
    {
        string path = Path.Combine(RunDirectory, file);
        if (!File.Exists(path)) throw new FileNotFoundException(missingMessage, path);
        return path;
    }

    public void Dispose()
    {
        _ownedTape?.Dispose();
        _ownedTape = null;
    }
}
