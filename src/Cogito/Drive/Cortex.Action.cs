namespace Cogito;

using System.Text;
using Cogito.Induct;
using Cogito.Grammar;
public readonly record struct CortexAction(CortexTool Tool, string Raw)
{
    public static readonly CortexAction None = new(CortexTool.None, "");
}

public readonly record struct CortexActionArgument(string Slot, string Value, Blur.SlotSources Source);

public readonly record struct CortexObservation(string Text, bool Terminal)
{
    public static readonly CortexObservation Empty = new("", false);
}

public readonly record struct CortexObservationField(string Slot, string Value, Blur.SlotSources Source);

public enum CortexActionAdmissionPhases : byte
{
    Request,
    Execution,
}

public enum CortexActionAdmissionDecisionSpecies : byte
{
    Admitted,
    Denied,
}

public readonly record struct CortexActionAdmissionDecision(
    CortexActionAdmissionDecisionSpecies Species,
    string Reason)
{
    public bool Admitted => Species == CortexActionAdmissionDecisionSpecies.Admitted;

    public static CortexActionAdmissionDecision Admit(string reason = "policy-default")
        => new(CortexActionAdmissionDecisionSpecies.Admitted, RequireReason(reason));

    public static CortexActionAdmissionDecision Deny(string reason)
        => new(CortexActionAdmissionDecisionSpecies.Denied, RequireReason(reason));

    public void Validate()
    {
        if (!Enum.IsDefined(Species) || string.IsNullOrWhiteSpace(Reason))
            throw new InvalidDataException("action admission decision is malformed");
    }

    private static string RequireReason(string reason)
        => string.IsNullOrWhiteSpace(reason) ? throw new ArgumentException("action admission reason is required", nameof(reason)) : reason;
}

public readonly record struct CortexActionAdmissionReceipt(
    int Step,
    CortexActionAdmissionPhases Phase,
    string Tool,
    string Source,
    string ActionRequestSHA256,
    string ExecutionSHA256,
    CortexActionAdmissionDecisionSpecies Decision,
    string Reason,
    string ReceiptSHA256)
{
    public void Validate()
    {
        if (Step < 0 || !Enum.IsDefined(Phase) || !Enum.IsDefined(Decision)
            || string.IsNullOrWhiteSpace(Tool) || string.IsNullOrWhiteSpace(Source)
            || !IsSHA256(ActionRequestSHA256) || string.IsNullOrWhiteSpace(Reason)
            || !IsSHA256(ReceiptSHA256)
            || (Phase == CortexActionAdmissionPhases.Request
                ? ExecutionSHA256.Length != 0
                : !IsSHA256(ExecutionSHA256)))
            throw new InvalidDataException("action admission receipt is malformed");
    }

    private static bool IsSHA256(string? value)
        => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

public abstract class CortexTool
{
    public static readonly CortexTool None = new NoTool();

    public abstract string Name { get; }

    /// Terminal tools close the current world episode.  The shared grammar action
    /// selector uses this marker to keep non-terminal exploration and committed
    /// answers on the same generated-call path without knowing a tool's domain type.
    public virtual bool IsTerminal => false;

    public abstract bool TryParseAction(string line, List<CortexActionArgument> arguments, out CortexAction action);

    public abstract CortexObservation Act(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
        List<CortexObservationField> fields);

    private sealed class NoTool : CortexTool
    {
        public override string Name => "noop";

        public override bool TryParseAction(string line, List<CortexActionArgument> arguments, out CortexAction action)
        {
            action = CortexAction.None;
            return false;
        }

        public override CortexObservation Act(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
            List<CortexObservationField> fields)
            => new("[no-op]\n", false);
    }
}

public abstract class CortexActionPolicy
{
    /// Generate complete tool calls from the currently published Cortex grammar and
    /// parse them through the mounted tool registry.  The query and every admitted
    /// observation are already on that grammar's tape; this seam deliberately has no
    /// domain choreography or path knowledge.
    protected static bool TryGenerateAction(Cortex cortex, Engine.MarkovModel generationModel,
        int length, int sweeps, ulong seed, int salt, List<CortexActionArgument> arguments,
        out CortexAction action, bool allowTerminal = false)
    {
        action = CortexAction.None;
        RePairResult grammar = cortex.Grammar;
        int compressedLength = grammar.Compressed?.Length ?? 0;
        int generationLength = Math.Max(1, length);
        int generationSweeps = Math.Max(1, sweeps);
        for (int attempt = 0; attempt < 12; attempt++)
        {
            ulong attemptSeed = seed + (ulong)(cortex.Step * 131 + salt * 97 + attempt) * 0x9E3779B97F4A7C15UL;
            byte[] generated;
            if (compressedLength >= 8 && attempt < 8)
            {
                int seedIndex = Math.Max(1, compressedLength - 1 - (attempt * 3) % Math.Max(1, Math.Min(compressedLength - 1, 24)));
                generated = generationModel.GenerateFrom(in grammar, generationLength, attemptSeed, seedIndex);
            }
            else
            {
                generated = generationModel.GenerateMCMC(in grammar, generationLength, generationSweeps, attemptSeed);
            }

            string text = Encoding.UTF8.GetString(generated);
            foreach (string raw in text.Split('\n'))
            {
                arguments.Clear();
                if (!cortex.TryParseAction(raw, arguments, out CortexAction parsed) || arguments.Count == 0)
                    continue;
                if ((!allowTerminal && parsed.Tool.IsTerminal) || arguments[0].Value.Length == 0)
                    continue;
                action = parsed;
                return true;
            }
        }
        arguments.Clear();
        return false;
    }

    public virtual void OnRunStart(Cortex cortex) { }

    public virtual void OnRunEnd(Cortex cortex) { }

    public virtual void OnStepStart(Cortex cortex, int step) { }

    public virtual void OnStepCompleted(Cortex cortex, int step) { }

    public abstract bool TryChooseAction(Cortex cortex, List<CortexActionArgument> arguments, out CortexAction action);

    public virtual string GetSource(Cortex cortex, CortexAction action) => "node0";

    public virtual TapeEventRoles ActionExecutionRoles(Cortex cortex, CortexAction action)
        => TapeEventRoles.GrammarInput;

    public virtual string FormatTapeValue(Cortex cortex, string value) => value;

    public virtual bool ShouldRouteActionArgument(Cortex cortex, CortexAction action, CortexActionArgument argument)
        => true;

    public virtual bool ShouldRouteObservationField(Cortex cortex, CortexAction action, CortexObservationField field)
        => true;

    public virtual void AppendDomainEvents(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
        CortexObservation observation, List<CortexObservationField> fields, List<TapeEventID> eventIDs) { }

    public virtual void OnActionExecutionAdmission(Cortex cortex, CortexAction action,
        in CortexActionAdmissionDecision decision) { }

    public virtual CortexActionAdmissionDecision EvaluateActionRequestAdmission(Cortex cortex, CortexAction action,
        List<CortexActionArgument> arguments)
        => CortexActionAdmissionDecision.Admit();

    public virtual CortexActionAdmissionDecision EvaluateActionExecutionAdmission(Cortex cortex, CortexAction action,
        List<CortexActionArgument> arguments,
        CortexObservation observation, List<CortexObservationField> fields)
        => CortexActionAdmissionDecision.Admit();

    public virtual void OnObservation(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
        CortexObservation observation, List<CortexObservationField> fields, byte[] executionBytes,
        List<TapeEventID> eventIDs) { }

    public virtual bool HarvestsAfterBatch => false;

    /// External observations that become the next action's world state cannot wait
    /// for the ordinary corpus installRevision stride.
    public virtual bool InstallsRevisionAfterBatch => false;

    public virtual void OnActionBatchEnd(Cortex cortex) { }
}

public abstract class CortexReward
{
    public virtual void OnRunStart(Cortex cortex) { }

    public virtual void OnRunEnd(Cortex cortex) { }

    public virtual void OnStepStart(Cortex cortex, int step) { }

    public virtual void OnStepCompleted(Cortex cortex, int step) { }

    public virtual void OnTapeEvent(Cortex cortex, TapeEventID eventID) { }

    public virtual void OnAction(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments) { }

    public virtual void OnObservation(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
        CortexObservation observation, List<CortexObservationField> fields, List<TapeEventID> eventIDs) { }

    public virtual void OnActionHarvest(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
        CortexObservation observation, List<CortexObservationField> fields) { }

    public virtual void OnActionBatchEnd(Cortex cortex) { }

    public virtual void OnEpisodeStart(Cortex cortex, string episodeID) { }

    public virtual void OnEpisodeEnd(Cortex cortex, string episodeID) { }

    public virtual void OnConsolidationPhase(Cortex cortex, int step) { }
}

public sealed partial class Cortex
{
    private Tape? _runtimeTape;
    private Journal? _runtimeJournal;
    private Run? _runtimeRun;
    private Run? _checkpointAuthorityRun;
    private Homeostat? _runtimeHomeostat;
    private ICurriculum? _runtimeCurriculum;
    private RePairResult _runtimeGrammar;
    private InstallRevision? _runtimeInstallRevision;
    private GrammarShape? _runtimeShape;
    private Engine.GrammarCover? _runtimeGrammarCover;
    private int _runtimeStep;
    private double _runtimeReplayRatio;
    private Action? _flushRuntimeOutputs;
    private CortexExecutionWindow _runtimeExecutionWindow;
    private string _episodeID = "";
    private Func<byte[]>? _captureForkSnapshot;
    private bool _forkSnapshotAvailable;
    private Func<CortexForkSeed>? _materializeCompletedStepForkSeed;
    private bool _completedStepForkAvailable;
    private CortexForkSeed? _coldForkSeed;
    private bool _coldForkAvailable;
    private bool _autonomicSpawningEnabled = true;
    private CortexForkRailRoles _forkRailRole;
    private bool _consolidationPhaseRequested;
    private bool _stopRequested;
    private readonly List<CortexActionArgument> _actionArguments = new(4);
    private readonly List<CortexObservationField> _observationFields = new(8);

    public Tape Tape => _runtimeTape ?? throw new InvalidOperationException("Cortex tape is available only during Run().");

    public Journal Journal => _runtimeJournal ?? throw new InvalidOperationException("Cortex journal is available only during Run().");

    public Run CurrentRun => _runtimeRun ?? throw new InvalidOperationException("Cortex run is available only during Run().");

    public Homeostat Homeostat => _runtimeHomeostat ?? throw new InvalidOperationException("Cortex Homeostat is available only during Run() and after a completed run.");

    public ICurriculum ActiveCurriculum => _runtimeCurriculum ?? throw new InvalidOperationException("Cortex curriculum is available only during Run().");

    public RePairResult Grammar => _runtimeGrammar;

    /// The immutable snapshot at the last grammar installRevision boundary. Legacy
    /// consumers continue reading Grammar; analysis consumers can subscribe to
    /// the typed installRevision without rebuilding a view themselves.
    public InstallRevision? InstallRevision => _runtimeInstallRevision;

    /// The single grammar-analysis plane for the live installRevision. Reads, intake
    /// policies, and action affirmation all bind to this owner instead of rebuilding
    /// the same expansion basis from the Re-Pair result independently.
    public GrammarShape? GrammarShape => _runtimeShape;

    public Engine.GrammarCover? GrammarCover => _runtimeGrammarCover;

    public int Step => _runtimeStep;

    public CortexExecutionWindow ExecutionWindow => _runtimeExecutionWindow;

    public string EpisodeID => _episodeID;

    public IReadOnlyList<CortexTool> Tools => _tools;

    public IReadOnlyList<CortexActionPolicy> ActionPolicies => _actionPolicies;

    public IReadOnlyList<CortexReward> Rewards => _rewards;

    public byte[] CaptureForkSnapshot()
    {
        if (!_forkSnapshotAvailable || _captureForkSnapshot is null)
            throw new InvalidOperationException("A fork snapshot is available only at the post-action-batch boundary.");
        return _captureForkSnapshot();
    }

    public CortexForkSeed MaterializeCompletedStepForkSeed()
    {
        if (!_completedStepForkAvailable || _materializeCompletedStepForkSeed is null)
            throw new InvalidOperationException("A completed-step fork seed is available only at the OnStepCompleted boundary.");
        return _materializeCompletedStepForkSeed();
    }

    public CortexForkSeed MaterializeColdForkSeed()
    {
        if (!_coldForkAvailable || _coldForkSeed is null)
            throw new InvalidOperationException("A cold fork seed is available only at the pre-step runtime boundary.");
        _coldForkAvailable = false;
        return _coldForkSeed;
    }

    /// Runtime-only recursion guard. The matched-fork runner checks the spawning Cortex at its entry boundary;
    /// matched-fork children read false so an experiment cannot recursively propose another experiment.
    public bool AllowsAutonomicSpawning => _autonomicSpawningEnabled;

    internal CortexForkRailRoles ForkRailRole => _forkRailRole;

    /// Calibration is the only non-autonomic rail that earns policy-boundary authority; evaluation mounts that
    /// authority and trial arms execute it, but neither may orchestrate another boundary assay.
    internal bool AllowsPolicyBoundaryAssay
        => AllowsAutonomicSpawning || ForkRailRole == CortexForkRailRoles.Calibration;

    public void CopyTapeLogTo(Stream target) => Tape.CopyLogTo(target);

    public (byte[] Bytes, long Cursor) CopyExcursionLog()
    {
        if (_runtimeRun is null) throw new InvalidOperationException("excursion log is available only while a Cortex runtime is active");
        _flushRuntimeOutputs?.Invoke();
        string path = _runtimeRun.PathOf("excursions.txt");
        if (!File.Exists(path)) throw new InvalidDataException("runtime excursion log is missing");
        byte[] bytes = File.ReadAllBytes(path);
        int newline = Array.IndexOf(bytes, (byte)'\n');
        if (newline < 0 || !System.Text.Encoding.UTF8.GetString(bytes, 0, newline).TrimEnd('\r').Equals("step\ttoken", StringComparison.Ordinal))
            throw new InvalidDataException("runtime excursion log header is malformed");
        long cursor = 0;
        for (int i = newline + 1; i < bytes.Length; i++) if (bytes[i] == (byte)'\n') cursor++;
        return (bytes, cursor);
    }

    public void RequestConsolidationPhase() => _consolidationPhaseRequested = true;

    public void RequestStop() => _stopRequested = true;

    public void BeginEpisode(string episodeID)
    {
        _episodeID = episodeID;
        foreach (CortexReward reward in _rewards) reward.OnEpisodeStart(this, episodeID);
    }

    public void EndEpisode()
    {
        string ended = _episodeID;
        _episodeID = "";
        if (ended.Length == 0) return;
        foreach (CortexReward reward in _rewards) reward.OnEpisodeEnd(this, ended);
    }

    public TapeEventID AppendEvidence(byte[] bytes, string source)
    {
        TapeEventID eventID = Tape.Append(bytes, source, Provenances.Real);
        Journal.Ingest(Step, eventID, source, bytes);
        NotifyTapeEvent(eventID);
        return eventID;
    }

    public TapeEventID AppendExecution(byte[] bytes, string source, TapeEventRoles roles = TapeEventRoles.GrammarInput)
    {
        TapeEventID eventID = Tape.Append(bytes, source, Provenances.Execution, roles);
        Journal.RecordExecution(Step, eventID, source, bytes);
        NotifyTapeEvent(eventID);
        return eventID;
    }

    public int CorroborateCurrentGrammar(bool crossReflect = true, int? wScale = null)
    {
        PearlAudit audit = Pearl.Audit(Tape, _runtimeGrammar, wScale ?? _config.Learning.EvidenceWeightScale, crossReflect);
        return Pearl.Corroborate(audit, Tape, Journal, Step);
    }

    public bool TryParseAction(string line, List<CortexActionArgument> arguments, out CortexAction action)
    {
        foreach (CortexTool tool in _tools)
        {
            arguments.Clear();
            if (tool.TryParseAction(line, arguments, out action)) return true;
        }
        arguments.Clear();
        action = CortexAction.None;
        return false;
    }

    internal List<CortexActionArgument> GetActionArguments() => _actionArguments;

    internal List<CortexObservationField> GetObservationFields() => _observationFields;

    public TTool? FindTool<TTool>() where TTool : CortexTool
    {
        foreach (CortexTool tool in _tools)
        {
            if (tool is TTool matched) return matched;
        }
        return null;
    }

    internal bool CanAppendReplay()
        => _runtimeReplayRatio <= 0 || Tape.ComputeUnreflectedHeadroom(_runtimeReplayRatio) > 0;

    internal void BindRuntime(Run run, Tape tape, Journal journal, Homeostat homeostat, double dreamRatio, Action? flushRuntimeOutputs = null)
    {
        _runtimeRun = run;
        _runtimeTape = tape;
        _runtimeJournal = journal;
        BindLoopLineage(tape, journal);
        RestoreLoopClosureFolds(run.Dir);
        _runtimeHomeostat = homeostat;
        _runtimeReplayRatio = dreamRatio;
        _flushRuntimeOutputs = flushRuntimeOutputs;
        if (_policyReadoutJournalRewritePending) RewritePolicyReadoutJournalFiles();
        RestorePolicyTrialQuota();
        EnsurePolicyJournalFiles();
        RestorePolicyOccurrenceCheckReceipts();
        ValidateDeferredPolicyTrialAuthority();
    }

    internal void BindCheckpointRuntime(Run run, Tape tape, Journal journal, Homeostat homeostat, double dreamRatio)
    {
        _runtimeRun = run;
        _runtimeTape = tape;
        _runtimeJournal = journal;
        _runtimeHomeostat = homeostat;
        _runtimeReplayRatio = dreamRatio;
        try { ValidateDeferredPolicyTrialAuthority(); }
        catch
        {
            UnbindCheckpointRuntime();
            throw;
        }
    }

    internal void BindCheckpointAuthority(Run run)
    {
        _checkpointAuthorityRun = run;
    }

    internal void UnbindCheckpointRuntime()
    {
        _runtimeRun = null;
        _checkpointAuthorityRun = null;
        _runtimeTape = null;
        _runtimeJournal = null;
        _runtimeHomeostat = null;
        _runtimeReplayRatio = 0;
    }

    /// Attach only the existing parent run identity for a durable fork
    /// continuation. The continuation does not drive this Cortex; the fork
    /// runner needs the parent directory to bind child seed-load receipts.
    internal void AttachForkParentRun(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (_runtimeRun is not null)
            throw new InvalidOperationException("cannot attach a fork parent while a Cortex runtime is active");
        _runtimeRun = run;
    }

    internal void BindRuntimeCurriculum(ICurriculum? curriculum)
    {
        _runtimeCurriculum = curriculum;
        if (curriculum is not null && _runtimeShape is not null)
            curriculum.BindGrammarShape(_runtimeShape);
    }

    internal void BindRuntimeStep(int step, in RePairResult grammar)
    {
        _runtimeStep = step;
        _runtimeGrammar = grammar;
        AllocatePolicyReadoutUnits(step);
    }

    internal void BindRuntimeExecutionWindow(in CortexExecutionWindow window)
        => _runtimeExecutionWindow = window;

    internal void BindRuntimeGrammar(in RePairResult grammar)
        => _runtimeGrammar = grammar;

    internal void SwapGrammar(in InstallRevision installRevision, bool advancePolicies = true)
    {
        InstallRevision boundInstallRevision = installRevision;
        if (boundInstallRevision.FoldProvenance is null
            && TryGetLoopClosureFold(boundInstallRevision.Revision, out GrammarFoldProvenanceReceipt persistedFold))
            boundInstallRevision = new InstallRevision(boundInstallRevision.Snapshot, boundInstallRevision.Delta, persistedFold);
        if (_runtimeInstallRevision is { } prior
            && prior.Revision == boundInstallRevision.Revision
            && (!prior.Snapshot.Matches(boundInstallRevision.Snapshot)
                || !OverlayMatches(prior.Overlay, boundInstallRevision.Overlay)))
            throw new InvalidDataException($"runtime grammar installRevision revision {boundInstallRevision.Revision} carries different content");
        if (_runtimeShape is null)
        {
            _runtimeShape = GrammarShape.BuildFromSnapshot(boundInstallRevision.Snapshot);
            _runtimeGrammarCover = new Engine.GrammarCover(_runtimeShape);
        }
        else
        {
            _runtimeShape.Apply(boundInstallRevision);
        }
        _runtimeInstallRevision = boundInstallRevision;
        if (advancePolicies) AdvancePolicyInstallRevision(in boundInstallRevision);
        else ObservePolicyInstallRevision(in boundInstallRevision);
        _runtimeCurriculum?.BindGrammarShape(_runtimeShape);
    }

    private static bool OverlayMatches(GrammarOverlay? left, GrammarOverlay? right)
        => left is null ? right is null : right is not null && left.ContentEquals(right);

    private void ObservePolicyInstallRevision(in InstallRevision installRevision)
    {
        foreach (PolicyState state in _policies.Values)
            state.ObservedInstallRevision = installRevision.Revision;
    }

    internal void BindRuntimeSnapshot(Func<byte[]>? capture)
    {
        _captureForkSnapshot = capture;
        if (capture is null) _forkSnapshotAvailable = false;
    }

    internal void SetRuntimeForkBoundary(bool available)
        => _forkSnapshotAvailable = available;

    internal void BindCompletedStepForkSeed(Func<CortexForkSeed>? materialize)
    {
        _materializeCompletedStepForkSeed = materialize;
        if (materialize is null) _completedStepForkAvailable = false;
    }

    internal void BindColdForkSeed(CortexForkSeed? seed)
    {
        _coldForkSeed = seed;
        _coldForkAvailable = seed is not null;
    }

    internal void SetCompletedStepForkBoundary(bool available)
        => _completedStepForkAvailable = available;

    internal void SetColdForkBoundary(bool available)
        => _coldForkAvailable = available;

    internal void DisableAutonomicSpawning()
        => _autonomicSpawningEnabled = false;

    internal void BindForkRailRole(CortexForkRailRoles role)
    {
        if (role == CortexForkRailRoles.Unknown) return;
        if (_forkRailRole != CortexForkRailRoles.Unknown && _forkRailRole != role)
            throw new InvalidOperationException($"fork runtime rail role changed from {_forkRailRole} to {role}");
        _forkRailRole = role;
    }

    internal bool ConsumeConsolidationPhaseRequest()
    {
        bool requested = _consolidationPhaseRequested;
        _consolidationPhaseRequested = false;
        return requested;
    }

    internal bool ConsumeStopRequest()
    {
        bool requested = _stopRequested;
        _stopRequested = false;
        return requested;
    }

    private void NotifyTapeEvent(TapeEventID eventID)
    {
        foreach (CortexReward reward in _rewards) reward.OnTapeEvent(this, eventID);
    }
}
