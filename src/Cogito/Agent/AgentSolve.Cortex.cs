namespace Cogito;

using System.Globalization;
using System.Text;
using Cogito.Grammar;
using Cogito.Induct;
using Ronmamon;

internal sealed record CortexLocCurriculum : CortexCurriculumConfig
{
    private const string Prefix = "loc:";

    public int WorkloadCount { get; init; }

    internal override string Token => Prefix + Math.Max(1, WorkloadCount).ToString(CultureInfo.InvariantCulture);

    public static bool TryParseWorkloadCount(string token, out int workloadCount)
    {
        workloadCount = 0;
        return token.StartsWith(Prefix, StringComparison.Ordinal)
            && int.TryParse(token.AsSpan(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out workloadCount)
            && workloadCount > 0;
    }
}

public static partial class AgentSolve
{
    private readonly record struct LocReplayWalk(string World, string Source, TapeEventID[] EventIDs, NoveltyBands Band, double Coverage, int Step);
    private readonly record struct LocCommitChoice(CommitCalibrationChoice Choice, bool Executed);

    private readonly record struct LocTransferReceipt(
        int Pass, string RunDir, bool Shuffled, int Solved, int Total, int Commits, int CorrectCommits,
        int CommittedActions, int DeepCorrect, int DeepTotal, double MeanZ, int KZ, double CalibrationError)
    {
        public string Render()
        {
            double successAtCommit = Commits > 0 ? 100.0 * CorrectCommits / Commits : 0;
            double actions = Commits > 0 ? (double)CommittedActions / Commits : 0;
            double deep = DeepTotal > 0 ? 100.0 * DeepCorrect / DeepTotal : 0;
            return $"P{Pass} {(Shuffled ? "shuffled" : "routed")} · solved {Solved}/{Total} · success@commit {successAtCommit:F1}%"
                 + $" · actions {actions:F2} · deep {DeepCorrect}/{DeepTotal} ({deep:F1}%) · meanz {MeanZ:F3}/KZ {KZ}"
                 + $" · calibration-error {CalibrationError:F3} · run={Path.GetFileName(RunDir)}";
        }
    }

    private sealed class LocHeldoutExperiment
    {
        private readonly SolveOpts _opt;
        private readonly List<string> _heldoutDirs;
        private readonly List<LocTransferReceipt> _receipts = new();

        public LocHeldoutExperiment(SolveOpts opt, List<string> heldoutDirs)
        {
            _opt = opt;
            _heldoutDirs = heldoutDirs;
        }

        public void RunFork(Cortex source, int pass)
        {
            byte[] image = source.CaptureForkSnapshot();
            string lineage = Path.GetFileName(source.CurrentRun.Dir) + $"_heldout_pass{pass}";
            Cogito.Run forkRun = Cogito.Run.New(lineage);
            using (FileStream log = new(forkRun.PathOf("tape.spanlog"), FileMode.Create, FileAccess.Write, FileShare.Read))
                source.CopyTapeLogTo(log);
            string sourceCurve = source.CurrentRun.PathOf("curve.tsv");
            if (File.Exists(sourceCurve)) File.Copy(sourceCurve, forkRun.PathOf("curve.tsv"));
            Checkpoint.Save(forkRun, image);

            LocCurriculum curriculum = new(_heldoutDirs, _opt);
            Cortex child = new(BuildSolveConfig(curriculum, _opt, ComputeSolveStepBudget(_heldoutDirs.Count, _opt), lineage));
            int horizon = source.Step + 1 + ComputeSolveStepBudget(_heldoutDirs.Count, _opt);
            int exitCode = child.Resume(forkRun.Dir, horizon, forkCurriculum: true);
            if (exitCode != 0) throw new InvalidOperationException($"held-out fork {lineage} exited {exitCode}");
            _receipts.Add(curriculum.CreateTransferReceipt(pass, forkRun.Dir, child.Grammar));
            source.Journal.Consolidation(source.Step, $"heldout-fork · pass={pass} · worlds={_heldoutDirs.Count} · run={Path.GetFileName(forkRun.Dir)}");
        }

        public void SavePlan(CkptWriter writer)
        {
            writer.I32(_heldoutDirs.Count);
            foreach (string dir in _heldoutDirs) writer.Str(dir);
            writer.I32(_receipts.Count);
            foreach (LocTransferReceipt receipt in _receipts)
            {
                writer.I32(receipt.Pass); writer.Str(receipt.RunDir); writer.Bool(receipt.Shuffled);
                writer.I32(receipt.Solved); writer.I32(receipt.Total); writer.I32(receipt.Commits); writer.I32(receipt.CorrectCommits);
                writer.I32(receipt.CommittedActions); writer.I32(receipt.DeepCorrect); writer.I32(receipt.DeepTotal);
                writer.F64(receipt.MeanZ); writer.I32(receipt.KZ); writer.F64(receipt.CalibrationError);
            }
        }

        public static LocHeldoutExperiment LoadPlan(CkptReader reader, SolveOpts options)
        {
            int heldoutCount = reader.I32();
            List<string> heldoutDirs = new(heldoutCount);
            for (int i = 0; i < heldoutCount; i++) heldoutDirs.Add(reader.Str());
            LocHeldoutExperiment experiment = new(options, heldoutDirs);
            int receiptCount = reader.I32();
            for (int i = 0; i < receiptCount; i++)
            {
                experiment._receipts.Add(new LocTransferReceipt(
                    reader.I32(), reader.Str(), reader.Bool(), reader.I32(), reader.I32(), reader.I32(), reader.I32(),
                    reader.I32(), reader.I32(), reader.I32(), reader.F64(), reader.I32(), reader.F64()));
            }
            return experiment;
        }

        public string RenderRuns()
        {
            if (_receipts.Count == 0) return "held-out forks pending";
            string[] lines = new string[_receipts.Count];
            for (int i = 0; i < _receipts.Count; i++) lines[i] = _receipts[i].Render();
            return string.Join("\n  HELDOUT · ", lines);
        }
    }

    private sealed class LocCurriculum : ICurriculum
    {
        private const string InputSnapshotFile = "loc_inputs.ron";
        private List<string> _dirs;
        private ulong _dirsFingerprint;
        private SolveOpts _opt;
        private int _passSize;
        private LocHeldoutExperiment? _heldoutExperiment;
        private readonly bool _adoptCheckpointRuntime;
        private int _checkpointWorkloadCount;
        private LocEpisode? _episode;
        private int _next;
        private int _completed;
        private int _firstN;
        private int _firstOk;
        private int _returnN;
        private int _returnOk;
        private int _commits;
        private int _correctCommits;
        private int _committedActions;
        private int _deepTargetTotal;
        private int _deepTargetCorrect;
        private int _totalOutcomeCredited;
        private StreamWriter? _curve;
        private StreamWriter? _rankings;
        private readonly HashSet<string> _seenRepos = new(StringComparer.Ordinal);
        private readonly List<bool> _results = new();
        private readonly CommitCalibrationHomeostat _gate = new();
        private readonly SolveDiagnostics _diag = new();
        private MeshHomeostat _diet;
        private readonly Queue<double> _meanzWindow = new();
        private readonly List<LocReplayWalk> _replayJournal = new();
        private double _lastMeanz = double.NaN;
        private double _lastThrottle = 1.0;
        private double _lastDrift = double.NaN;
        private int _replayedWalkEvents;
        private int _replayNights;
        private int _pendingHeldoutPass;
        private long _curveLength = -1;
        private long _rankingsLength = -1;

        public LocCurriculum(List<string> dirs, SolveOpts opt, int passSize = 0, LocHeldoutExperiment? heldoutExperiment = null)
        {
            _dirs = dirs;
            _dirsFingerprint = ComputeDirsFingerprint(dirs);
            _opt = opt;
            _passSize = passSize;
            _heldoutExperiment = heldoutExperiment;
            _diet = new MeshHomeostat(opt.MeshFloor, opt.MeshGain);
        }

        public LocCurriculum(int checkpointWorkloadCount, SolveOpts opt)
        {
            _dirs = new List<string>();
            _opt = opt;
            _adoptCheckpointRuntime = true;
            _checkpointWorkloadCount = Math.Max(1, checkpointWorkloadCount);
            _diet = new MeshHomeostat(opt.MeshFloor, opt.MeshGain);
        }

        public LocEpisode? Episode => _episode;

        public SolveOpts Options => _opt;

        public CommitCalibrationHomeostat Gate => _gate;

        public SolveDiagnostics Diagnostics => _diag;

        public int TotalOutcomeCredited => _totalOutcomeCredited;

        public int RecallHitGold { get; private set; }

        public void AppendProbeSamples(List<byte[]> samples)
        {
            if (_dirs.Count == 0)
            {
                samples.Add("loc-agent-v1!\n"u8.ToArray());
                return;
            }
            foreach (string dir in _dirs)
            {
                string queryPath = Path.Combine(dir, "query.txt");
                samples.Add(File.Exists(queryPath) ? File.ReadAllBytes(queryPath) : Encoding.UTF8.GetBytes(Path.GetFileName(dir) + "\n"));
            }
        }

        public void Seed(Tape tape, Journal journal)
        {
            byte[] anchor = Encoding.UTF8.GetBytes("LOC-RUNTIME\n");
            TapeEventID anchorID = tape.Append(anchor, "corpus", Provenances.Real);
            journal.Ingest(0, anchorID, "corpus", anchor);
            if (!_opt.Pretrain) return;
            string corpus = AgentTrace.SynthesizeCorpus(_dirs, null, _opt.AnswerLeakFree);
            int n = 0;
            foreach (string line in corpus.Split('\n'))
            {
                if (line.Length == 0) continue;
                byte[] bytes = Encoding.UTF8.GetBytes(line);
                TapeEventID eventID = tape.Append(bytes, "corpus", Provenances.Real);
                journal.Ingest(0, eventID, "corpus", bytes);
                n++;
            }
            Console.WriteLine($"solve · pretrained on {n} corpus spans ({corpus.Length}B) · tool-use in-distribution from step 0");
        }

        public IntakeStep Draw(RePairResult grammar, Tape tape, Journal journal, int step, int batch)
        {
            if (_episode is not null || _next >= _dirs.Count) return new IntakeStep(0, false, 0);
            string dir = _dirs[_next];
            string instance = Path.GetFileName(dir);
            string repo = ParseRepo(instance);
            bool isReturn = !_seenRepos.Add(repo);
            string query = File.ReadAllText(Path.Combine(dir, "query.txt")).Trim();
            List<Tool.SiteRow> sites = Tool.LoadSites(Path.Combine(dir, "sites.jsonl"));
            Tool.AgentWorld world = new(sites);
            string goldPath = Path.Combine(dir, "gold.json");
            string gold = LoadGoldFile(goldPath);
            int goldStart = LoadGoldStartLine(goldPath);
            List<string> terms = QueryTerms.Rank(query, sites);
            HashSet<string> paths = new(world.Paths, StringComparer.Ordinal);
            CortexProcedure procedure = CreateProcedure(grammar, terms, instance, journal, step);
            LocEpisode episode = new(_next, dir, instance, repo, isReturn, query, gold, goldStart, sites, world, terms, paths, procedure);
            _episode = episode;
            AppendContact(tape, journal, step, episode);
            _next++;
            return new IntakeStep(1, false, 0);
        }

        public bool Drained => _next >= _dirs.Count && _episode is null;

        public bool Exhausted => Drained;

        public int IngestedCount => _completed;

        public int WorkloadCount => _dirs.Count > 0 ? _dirs.Count : _checkpointWorkloadCount;

        public int MixEvery { get; set; }

        public void Mix(Cortex cortex, RePairResult grammar, Tape tape, Journal journal, int step, double affirmCut) { }

        public int StreakResets => 0;

        public void RegisterPolicies(Cortex cortex)
            => cortex.RegisterPolicy(CommitCalibrationHomeostat.PolicySchema);

        public void SaveState(CkptWriter w)
        {
            if (_episode is not null) throw new InvalidOperationException("cannot checkpoint LOC during an active episode");
            _curve?.Flush();
            _rankings?.Flush();
            if (_curve is not null) _curveLength = _curve.BaseStream.Length;
            if (_rankings is not null) _rankingsLength = _rankings.BaseStream.Length;

            w.I32(_dirs.Count);
            foreach (string dir in _dirs) w.Str(dir);
            w.U64(_dirsFingerprint);
            SaveRuntimeOptions(w, _opt);
            w.I32(_passSize);
            w.Bool(_heldoutExperiment is not null);
            _heldoutExperiment?.SavePlan(w);
            _gate.Save(w);
            _diag.Save(w);
            _diet.Save(w);
            w.I32(_meanzWindow.Count);
            foreach (double meanz in _meanzWindow) w.F64(meanz);
            w.F64(_lastMeanz); w.F64(_lastThrottle); w.F64(_lastDrift);
            w.I32(_replayJournal.Count);
            foreach (LocReplayWalk walk in _replayJournal)
            {
                w.Str(walk.World); w.Str(walk.Source); w.I32(walk.EventIDs.Length);
                foreach (TapeEventID eventID in walk.EventIDs) w.I64(eventID.Value);
                w.I32((int)walk.Band); w.F64(walk.Coverage); w.I32(walk.Step);
            }
            w.I32(_totalOutcomeCredited); w.I32(RecallHitGold); w.I32(_replayedWalkEvents); w.I32(_replayNights);

            w.I32(_next); w.I32(_completed); w.I32(_firstN); w.I32(_firstOk); w.I32(_returnN); w.I32(_returnOk);
            w.I32(_commits); w.I32(_correctCommits); w.I32(_committedActions); w.I32(_deepTargetTotal); w.I32(_deepTargetCorrect);
            List<string> repos = _seenRepos.OrderBy(static repo => repo, StringComparer.Ordinal).ToList();
            w.I32(repos.Count);
            foreach (string repo in repos) w.Str(repo);
            w.I32(_results.Count);
            foreach (bool result in _results) w.Bool(result);
            w.I32(_pendingHeldoutPass);
            w.I64(_curveLength); w.I64(_rankingsLength);
        }

        public void LoadState(CkptReader r)
        {
            int dirCount = r.I32();
            List<string> checkpointDirs = new(dirCount);
            for (int i = 0; i < dirCount; i++) checkpointDirs.Add(r.Str());
            ulong checkpointFingerprint = r.U64();
            ulong rebuiltFingerprint = ComputeDirsFingerprint(checkpointDirs);
            if (checkpointFingerprint != rebuiltFingerprint)
                throw new InvalidDataException("LOC curriculum inputs changed since the checkpoint; the runtime stream cannot resume byte-identically");
            SolveOpts checkpointOptions = LoadRuntimeOptions(r);
            int checkpointPassSize = r.I32();
            LocHeldoutExperiment? checkpointExperiment = r.Bool()
                ? LocHeldoutExperiment.LoadPlan(r, checkpointOptions)
                : null;
            bool sameCurriculum = _adoptCheckpointRuntime || checkpointFingerprint == _dirsFingerprint;
            if (_adoptCheckpointRuntime)
            {
                _dirs = checkpointDirs;
                _dirsFingerprint = checkpointFingerprint;
                _opt = checkpointOptions;
                _passSize = checkpointPassSize;
                _heldoutExperiment = checkpointExperiment;
                _checkpointWorkloadCount = checkpointDirs.Count;
                _diet = new MeshHomeostat(_opt.MeshFloor, _opt.MeshGain);
            }
            else if (sameCurriculum)
            {
                _opt = checkpointOptions;
                _passSize = checkpointPassSize;
                _heldoutExperiment = checkpointExperiment;
                _diet = new MeshHomeostat(_opt.MeshFloor, _opt.MeshGain);
            }
            _gate.Load(r);
            _diag.Load(r);
            _diet.Load(r);
            _meanzWindow.Clear();
            int meanzCount = r.I32();
            for (int i = 0; i < meanzCount; i++) _meanzWindow.Enqueue(r.F64());
            _lastMeanz = r.F64(); _lastThrottle = r.F64(); _lastDrift = r.F64();
            _replayJournal.Clear();
            int replayCount = r.I32();
            for (int i = 0; i < replayCount; i++)
            {
                string world = r.Str();
                string source = r.Str();
                int eventCount = r.I32();
                TapeEventID[] eventIDs = new TapeEventID[eventCount];
                for (int j = 0; j < eventCount; j++) eventIDs[j] = new TapeEventID(r.I64());
                NoveltyBands band = (NoveltyBands)r.I32();
                double coverage = r.F64();
                int step = r.I32();
                _replayJournal.Add(new LocReplayWalk(world, source, eventIDs, band, coverage, step));
            }
            _totalOutcomeCredited = r.I32();
            int recallHitGold = r.I32();
            _replayedWalkEvents = r.I32(); _replayNights = r.I32();

            int next = r.I32();
            int completed = r.I32();
            int firstN = r.I32();
            int firstOk = r.I32();
            int returnN = r.I32();
            int returnOk = r.I32();
            int commits = r.I32();
            int correctCommits = r.I32();
            int committedActions = r.I32();
            int deepTargetTotal = r.I32();
            int deepTargetCorrect = r.I32();
            int repoCount = r.I32();
            List<string> repos = new(repoCount);
            for (int i = 0; i < repoCount; i++) repos.Add(r.Str());
            int resultCount = r.I32();
            List<bool> results = new(resultCount);
            for (int i = 0; i < resultCount; i++) results.Add(r.Bool());
            int pendingHeldoutPass = r.I32();
            long curveLength = r.I64();
            long rankingsLength = r.I64();
            if (!sameCurriculum) return;

            _next = next; _completed = completed; _firstN = firstN; _firstOk = firstOk; _returnN = returnN; _returnOk = returnOk;
            _commits = commits; _correctCommits = correctCommits; _committedActions = committedActions;
            _deepTargetTotal = deepTargetTotal; _deepTargetCorrect = deepTargetCorrect;
            RecallHitGold = recallHitGold;
            _seenRepos.Clear();
            foreach (string repo in repos) _seenRepos.Add(repo);
            _results.Clear();
            _results.AddRange(results);
            _pendingHeldoutPass = pendingHeldoutPass;
            _curveLength = curveLength;
            _rankingsLength = rankingsLength;
        }

        private static void SaveRuntimeOptions(CkptWriter writer, in SolveOpts options)
        {
            writer.I32(options.Looks); writer.I32(options.LooksCap); writer.I32(options.Len); writer.I32(options.Sweeps);
            writer.U64(options.Seed); writer.I32(options.Limit); writer.Bool(options.Pretrain); writer.Bool(options.MeshHomeo);
            writer.I32(options.SiteBudget); writer.Bool(options.ConfidenceTrace);
            writer.Bool(options.ExplainRank); writer.F64(options.MeshFloor); writer.F64(options.MeshGain); writer.I32(options.MixSpans);
            writer.I32(options.Passes); writer.Bool(options.Interleave); writer.I32(options.CheckpointEvery);
            writer.Bool(options.AnswerLeakFree); writer.Bool(options.ShuffleBindings); writer.I32(options.Heldout); writer.I32(options.Revisited);
        }

        private static SolveOpts LoadRuntimeOptions(CkptReader reader)
            => new(
                Looks: reader.I32(), LooksCap: reader.I32(), Len: reader.I32(), Sweeps: reader.I32(),
                Seed: reader.U64(), Limit: reader.I32(), Pretrain: reader.Bool(), MeshHomeo: reader.Bool(),
                SiteBudget: reader.I32(), ConfidenceTrace: reader.Bool(),
                ExplainRank: reader.Bool(), MeshFloor: reader.F64(), MeshGain: reader.F64(), MixSpans: reader.I32(),
                Passes: reader.I32(), Interleave: reader.Bool(), CheckpointEvery: reader.I32(),
                AnswerLeakFree: reader.Bool(), ShuffleBindings: reader.Bool(),
                Heldout: reader.I32(), Revisited: reader.I32());

        public void PrepareResumeArtifacts(Run run)
        {
            if (_dirs.Count > 0)
            {
                LocRONInputs inputs = new();
                inputs.directories.AddRange(_dirs);
                byte[] bytes = RonSerializer.SerializeToUtf8(in inputs);
                File.WriteAllBytes(run.PathOf(InputSnapshotFile), bytes);
            }
            if (_curveLength >= 0 && File.Exists(run.PathOf("loc_curve.tsv"))) run.TruncateCurve("loc_curve.tsv", _curveLength);
            if (_rankingsLength >= 0 && File.Exists(run.PathOf("rankings.jsonl"))) run.Truncate("rankings.jsonl", _rankingsLength);
        }

        public static List<string> LoadInputDirectories(string runDir)
        {
            string path = Path.Combine(runDir, InputSnapshotFile);
            if (!File.Exists(path)) return new List<string>();
            LocRONInputs inputs = RonSerializer.Deserialize<LocRONInputs>(File.ReadAllBytes(path));
            return inputs.directories;
        }

        public void Abstain(Cortex cortex, LocEpisode episode)
        {
            if (_episode != episode || episode.Completed) return;
            CommitCalibrationRead read = _gate.Read(0.0);
            _gate.ObserveAbstain(episode.Looks, read.Coverage);
            SolveResult result = new(episode.Gold, "", false, false, episode.ReachedGold, episode.RecalledGold, episode.Looks,
                0.0, _gate.CalibrationError, 0.0, 0.0, 0, 0, -1, 0,
                read.Coverage, read.Band, read.Floor, false, 0);
            episode.Completed = true;
            Complete(cortex, episode, result, vested: 0);
        }

        public void Complete(Cortex cortex, LocEpisode episode, SolveResult result, int vested)
        {
            if (_episode != episode) return;
            ResolveCommitChoices(cortex, episode, result);
            EnsureWriters(cortex.CurrentRun);
            _results.Add(result.Correct);
            _totalOutcomeCredited += vested;
            if (result.Committed)
            {
                _commits++;
                _committedActions += result.Looks;
                if (result.Correct) _correctCommits++;
                if (episode.IsReturn)
                {
                    _returnN++;
                    if (result.Correct) _returnOk++;
                }
                else
                {
                    _firstN++;
                    if (result.Correct) _firstOk++;
                }
            }
            if (ClassifyTargetDepthBand(episode) == "deep")
            {
                _deepTargetTotal++;
                if (result.Correct) _deepTargetCorrect++;
            }
            if (result.RecalledGold) RecallHitGold++;
            _rankings!.WriteLine(EmitJson(episode.Index, episode.Instance, episode.Repo, result, episode.IsReturn, _gate));
            _curve!.WriteLine($"{episode.Index}\t{episode.Instance}\t{episode.Repo}\t{(result.Committed ? 1 : 0)}\t{(result.Correct ? 1 : 0)}\t{(result.ReachedGold ? 1 : 0)}\t{(result.RecalledGold ? 1 : 0)}\t{result.Looks}\t{result.Confidence:F6}\t{result.CalibrationError:F6}\t{result.NoveltyCoverage:F6}\t{CommitCalibrationHomeostat.LabelOf(result.NoveltyBand)}\t{result.NoveltyFloor:F6}\t{_gate.SuccessAtCommit:F6}\t{_gate.AbstentionRate:F6}\t{(episode.IsReturn ? 1 : 0)}");
            Console.WriteLine($"   {episode.Index,-4} {Truncate(episode.Instance, 30),-30} {Truncate(result.Gold, 34),-34} {Truncate(result.Committed ? result.Answer : "ABSTAIN", 34),-34} {result.Looks,4} {result.Confidence,5:F2} {100.0 * _gate.SuccessAtCommit,5:F1}% {100.0 * _gate.AbstentionRate,5:F1}%  {(result.ReachedGold ? "gold" : "  . "),-4} {(episode.IsReturn ? "<-" : " ")}");
            SenseCriticality(cortex);
            _episode = null;
            _completed++;
            cortex.EndEpisode();
            if (_passSize > 0 && _completed % _passSize == 0 && _heldoutExperiment is not null)
            {
                _pendingHeldoutPass = _completed / _passSize;
                if (_completed < _dirs.Count) cortex.RequestConsolidationPhase();
            }
        }

        private static void ResolveCommitChoices(Cortex cortex, LocEpisode episode, SolveResult result)
        {
            for (int i = 0; i < episode.CommitChoices.Count; i++)
            {
                LocCommitChoice pending = episode.CommitChoices[i];
                bool selectedCommit = pending.Choice.Action == CommitCalibrationActions.Commit;
                bool invariantClean = pending.Executed && (!selectedCommit || result.Committed);
                CommitCalibrationChoice choice = pending.Choice;
                CommitCalibrationHomeostat.Resolve(cortex, in choice, result.Committed, result.Correct,
                    result.Looks, invariantClean);
            }
            episode.CommitChoices.Clear();
        }

        // classified from the episode's in-memory sites + gold — the per-completion sites.jsonl + gold.json re-parse
        // read back exactly what Draw had already loaded.
        private static string ClassifyTargetDepthBand(LocEpisode episode)
        {
            List<Tool.SiteRow> sites = episode.Sites;
            if (sites.Count == 0 || episode.Gold.Length == 0) return "none";
            int start = episode.GoldStart;
            int index = sites.FindIndex(site => site.Path == episode.Gold && (start < 0 || site.Start == start));
            if (index < 0) index = sites.FindIndex(site => site.Path == episode.Gold);
            if (index < 0) return "none";
            int third = Math.Max(1, sites.Count / 3);
            return index < third ? "shallow" : index < 2 * third ? "mid" : "deep";
        }

        private static int LoadGoldStartLine(string goldPath)
        {
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(goldPath));
            if (document.RootElement.TryGetProperty("functions", out System.Text.Json.JsonElement functions)
                && functions.ValueKind == System.Text.Json.JsonValueKind.Array
                && functions.GetArrayLength() > 0
                && functions[0].TryGetProperty("start_line", out System.Text.Json.JsonElement start)
                && start.TryGetInt32(out int line))
                return line;
            return -1;
        }

        public void RunPendingHeldoutFork(Cortex cortex)
        {
            if (_pendingHeldoutPass <= 0 || _heldoutExperiment is null) return;
            int pass = _pendingHeldoutPass;
            _heldoutExperiment.RunFork(cortex, pass);
            _pendingHeldoutPass = 0;
        }

        public void CloseRunWriters()
        {
            _curve?.Dispose();
            _curve = null;
            _rankings?.Dispose();
            _rankings = null;
        }

        public LocTransferReceipt CreateTransferReceipt(int pass, string runDir, RePairResult grammar)
        {
            int solved = 0;
            foreach (bool correct in _results) if (correct) solved++;
            Engine.RenormStat criticality = Engine.RenormStats(grammar);
            return new LocTransferReceipt(pass, runDir, _opt.ShuffleBindings, solved, _results.Count, _commits, _correctCommits,
                _committedActions, _deepTargetCorrect, _deepTargetTotal, criticality.MeanZ, criticality.KZ, _gate.CalibrationError);
        }

        public void AdmitSuccessfulWalk(Cortex cortex, LocEpisode episode)
        {
            if (episode.WalkEvents.Count == 0) return;
            Engine.GrammarCover? affirmCover = cortex.Grammar.Rules is { Length: > 0 }
                ? cortex.GrammarCover ?? new Engine.GrammarCover(cortex.Grammar.Rules)
                : null;
            double affirmCut = DeriveWalkAffirmCut();
            string source = "walk:" + episode.Instance;
            byte[] prefix = Encoding.UTF8.GetBytes("SUCCESS-WALK " + episode.Instance + "\n");
            List<TapeEventID> ids = new(episode.WalkEvents.Count);
            int copied = 0;
            int skipped = 0;
            for (int i = 0; i < episode.WalkEvents.Count; i++)
            {
                byte[] eventBytes = episode.WalkEvents[i];
                byte[] framed = new byte[prefix.Length + eventBytes.Length];
                Buffer.BlockCopy(prefix, 0, framed, 0, prefix.Length);
                Buffer.BlockCopy(eventBytes, 0, framed, prefix.Length, eventBytes.Length);
                ReadOnlySpan<byte> procedureBody = ExtractWalkProcedureBody(eventBytes);
                CortexTapeAdmissionChoice admission = cortex.ChooseTapeAdmission(
                    affirmCover, procedureBody, framed.Length, Provenances.Real, affirmCut);
                if (admission.Action == CortexTapeAdmissionActions.Reject)
                {
                    cortex.CompleteTapeAdmission(in admission, appended: false);
                    skipped++;
                    if (i < episode.WalkEventIDs.Count && episode.WalkEventIDs[i] is TapeEventID existingID)
                        ids.Add(existingID);
                    continue;
                }
                ids.Add(cortex.AppendEvidence(framed, source));
                cortex.CompleteTapeAdmission(in admission, appended: true);
                copied++;
            }
            if (skipped > 0)
                cortex.Journal.Consolidation(cortex.Step, $"walk-diet · source={source} · copied={copied} · affirmSkipped={skipped} · affirmCut={affirmCut:F3}");
            if (ids.Count > 0)
                _replayJournal.Add(new LocReplayWalk(episode.Repo, source, ids.ToArray(), episode.CommitNoveltyBand, episode.CommitNoveltyCoverage, cortex.Step));
        }

        public void RunNightDiet(Cortex cortex)
        {
            int rawBudget = _opt.MixSpans;
            int budget = _opt.MeshHomeo ? _diet.Apply(rawBudget) : rawBudget;
            _replayNights++;
            if (rawBudget <= 0 || budget <= 0 || _replayJournal.Count == 0)
            {
                cortex.Journal.Consolidation(cortex.Step,
                    $"walk-diet · raw={rawBudget} · budget={budget} · mounted=0 · worlds=0 · candidates={_replayJournal.Count} · throttle={Throttle:F3}");
                return;
            }

            List<LocReplayWalk> selected = SelectReplayWalks(budget);
            int mounted = 0;
            int worlds = 0;
            foreach (LocReplayWalk walk in selected)
            {
                bool mountedWorld = false;
                foreach (TapeEventID eventID in walk.EventIDs)
                {
                    if (!cortex.Tape.Resolve(eventID, out byte[] bytes)) continue;
                    cortex.AppendEvidence(bytes, walk.Source);
                    mounted++;
                    mountedWorld = true;
                }
                if (mountedWorld) worlds++;
                if (mounted >= budget) break;
            }
            _replayedWalkEvents += mounted;
            cortex.Journal.Consolidation(cortex.Step,
                $"walk-diet · raw={rawBudget} · budget={budget} · mounted={mounted} · worlds={worlds} · candidates={_replayJournal.Count} · throttle={Throttle:F3} · total={_replayedWalkEvents} · nights={_replayNights}");
        }

        private void SenseCriticality(Cortex cortex)
        {
            if (!_opt.MeshHomeo || cortex.Grammar.Compressed is not { Length: > 0 }) return;
            Engine.RenormStat stat = Engine.RenormStats(cortex.Grammar);
            double meanz = stat.MeanZ;
            if (!double.IsNaN(meanz))
            {
                _meanzWindow.Enqueue(meanz);
                while (_meanzWindow.Count > 12) _meanzWindow.Dequeue();
            }
            double drift = Reads.Slope(_meanzWindow);
            _lastMeanz = meanz;
            _lastDrift = drift;
            _lastThrottle = _diet.Sense(meanz, drift);
            _diet.SenseSelection(meanz, stat.KZ, _gate.Commits > 0, _gate.CalibrationError);
        }

        private double DeriveWalkAffirmCut()
            => _opt.MeshHomeo && Throttle < 1.0
                ? Math.Clamp(Throttle, Radula.ExactAffirmCut, 1.0)
                : Radula.ExactAffirmCut;

        private double Throttle => _opt.MeshHomeo ? _lastThrottle : 1.0;

        private List<LocReplayWalk> SelectReplayWalks(int budget)
        {
            Dictionary<string, LocReplayWalk> byWorld = new(StringComparer.Ordinal);
            foreach (LocReplayWalk walk in _replayJournal)
            {
                if (walk.EventIDs.Length == 0) continue;
                if (!byWorld.TryGetValue(walk.World, out LocReplayWalk incumbent) || ReplayWalkBeats(walk, incumbent))
                    byWorld[walk.World] = walk;
            }

            List<LocReplayWalk> ranked = byWorld.Values.ToList();
            ranked.Sort(CompareReplayMembership);
            List<LocReplayWalk> selected = new();
            int used = 0;
            foreach (LocReplayWalk walk in ranked)
            {
                if (used + walk.EventIDs.Length > budget) continue;
                selected.Add(walk);
                used += walk.EventIDs.Length;
                if (used >= budget) break;
            }
            selected.Sort(CompareReplayMountOrder);
            return selected;
        }

        private static bool ReplayWalkBeats(LocReplayWalk candidate, LocReplayWalk incumbent)
            => candidate.Step != incumbent.Step
                ? candidate.Step > incumbent.Step
                : string.CompareOrdinal(candidate.Source, incumbent.Source) < 0;

        private static int CompareReplayMembership(LocReplayWalk left, LocReplayWalk right)
        {
            int step = right.Step.CompareTo(left.Step);
            return step != 0 ? step : string.CompareOrdinal(left.Source, right.Source);
        }

        private static int CompareReplayMountOrder(LocReplayWalk left, LocReplayWalk right)
        {
            int world = string.CompareOrdinal(left.World, right.World);
            return world != 0 ? world : string.CompareOrdinal(left.Source, right.Source);
        }

        private static ReadOnlySpan<byte> ExtractWalkProcedureBody(ReadOnlySpan<byte> bytes)
        {
            bytes = StripLinePrefix(bytes, "SUCCESS-WALK "u8);
            return StripLinePrefix(bytes, "WALK "u8);
        }

        private static ReadOnlySpan<byte> StripLinePrefix(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> prefix)
        {
            if (!bytes.StartsWith(prefix)) return bytes;
            int newline = bytes.IndexOf((byte)'\n');
            return newline < 0 ? ReadOnlySpan<byte>.Empty : bytes[(newline + 1)..];
        }

        private static ulong ComputeDirsFingerprint(List<string> dirs)
        {
            ulong hash = 14695981039346656037UL;
            string[] files = ["query.txt", "sites.jsonl", "gold.json"];
            foreach (string dir in dirs)
            {
                hash = FoldFingerprint(hash, Path.GetFullPath(dir));
                foreach (string file in files)
                {
                    string path = Path.Combine(dir, file);
                    hash = FoldFingerprint(hash, file);
                    byte[] bytes = File.ReadAllBytes(path);
                    hash ^= (ulong)bytes.Length;
                    hash *= 1099511628211UL;
                    hash ^= Simhash.Fnv64(bytes);
                    hash *= 1099511628211UL;
                }
            }
            return hash;
        }

        private static ulong FoldFingerprint(ulong hash, string text)
        {
            foreach (byte value in Encoding.UTF8.GetBytes(text))
            {
                hash ^= value;
                hash *= 1099511628211UL;
            }
            return hash;
        }

        public string Report()
        {
            double firstAcc = _firstN > 0 ? 100.0 * _firstOk / _firstN : 0;
            double returnAcc = _returnN > 0 ? 100.0 * _returnOk / _returnN : 0;
            StringBuilder sb = new();
            sb.AppendLine("── LOC VERDICT ──");
            sb.AppendLine(_gate.Line());
            sb.AppendLine($"  RETURN-COMMIT · first-visit {_firstOk}/{_firstN} ({firstAcc:F1}%) -> repo-return {_returnOk}/{_returnN} ({returnAcc:F1}%) · lift {(returnAcc - firstAcc >= 0 ? "+" : "")}{returnAcc - firstAcc:F1} pts over committed actions");
            sb.AppendLine($"  LOC-CORTEX · episodes {_completed}/{_dirs.Count} · Σvested {_totalOutcomeCredited} · recall surfaced gold on {RecallHitGold}/{Math.Max(1, _results.Count)}");
            sb.AppendLine($"  WALK-DIET · {_replayJournal.Count} admitted trajectories · {_replayedWalkEvents} events remounted across {_replayNights} aestivations · throttle {Throttle:F3}");
            if (_opt.MeshHomeo)
                sb.AppendLine($"  CRITICALITY · meanz {(double.IsNaN(_lastMeanz) ? "n/a" : _lastMeanz.ToString("F3", CultureInfo.InvariantCulture))} · drift {(double.IsNaN(_lastDrift) ? "n/a" : _lastDrift.ToString("F5", CultureInfo.InvariantCulture))} · {_diet.Line()}");
            if (_heldoutExperiment is not null) sb.AppendLine("  HELDOUT · " + _heldoutExperiment.RenderRuns());
            sb.AppendLine(_diag.ActionabilityLine());
            return sb.ToString();
        }

        private void EnsureWriters(Run run)
        {
            if (_curve is not null) return;
            _curve = run.CurveAppender("loc_curve.tsv");
            if (new FileInfo(run.PathOf("loc_curve.tsv")).Length == 0)
                _curve.WriteLine("idx\tinstance\trepo\tcommitted\tcorrect\treached\trecalled\tactions\tconfidence\tcalibration_error\tnovelty_coverage\tnovelty_band\tnovelty_floor\tsuccess_at_commit\tabstention_rate\treturn");
            _rankings = run.Appender("rankings.jsonl");
        }

        private void AppendContact(Tape tape, Journal journal, int step, LocEpisode episode)
        {
            byte[] queryBytes = Encoding.UTF8.GetBytes("QUERY: " + FormatTapeText(FormatOneLine(episode.Query), episode.Gold, _opt.AnswerLeakFree));
            TapeEventID queryID = tape.Append(queryBytes, "corpus", Provenances.Real);
            journal.Ingest(step, queryID, "corpus", queryBytes);
            int budget = _opt.SiteBudget;
            int taken = 0;
            foreach (Tool.SiteRow site in episode.Sites)
            {
                if (site.Path != episode.Gold) continue;
                byte[] bytes = Encoding.UTF8.GetBytes(FormatTapeText(site.Text, episode.Gold, _opt.AnswerLeakFree));
                TapeEventID eventID = tape.Append(bytes, "corpus", Provenances.Real);
                journal.Ingest(step, eventID, "corpus", bytes);
                taken++;
                if (taken >= budget) return;
            }
            int stride = Math.Max(1, episode.Sites.Count / Math.Max(1, budget - taken));
            for (int i = 0; i < episode.Sites.Count && taken < budget; i += stride)
            {
                if (episode.Sites[i].Path == episode.Gold) continue;
                byte[] bytes = Encoding.UTF8.GetBytes(FormatTapeText(episode.Sites[i].Text, episode.Gold, _opt.AnswerLeakFree));
                TapeEventID eventID = tape.Append(bytes, "corpus", Provenances.Real);
                journal.Ingest(step, eventID, "corpus", bytes);
                taken++;
            }
        }

        private CortexProcedure CreateProcedure(RePairResult grammar, List<string> terms, string instance, Journal journal, int step)
        {
            if (terms.Count == 0 || !CanTransferProcedure(grammar)) return CortexProcedure.Disabled;
            string grammarPrior = PickGrammarPriorFiller(grammar, terms);
            HashSet<string> seen = new(StringComparer.Ordinal);
            List<string> stimuli = new(terms.Count);
            foreach (string term in terms)
            {
                if (term.Length > 0 && seen.Add(term)) stimuli.Add(term);
            }
            if (stimuli.Count == 0) return CortexProcedure.Disabled;

            int stepsPerStimulus = _opt.ShuffleBindings ? 2 : 3;
            CortexProcedureStep[] steps = new CortexProcedureStep[stimuli.Count * stepsPerStimulus];
            int next = 0;
            for (int i = 0; i < stimuli.Count; i++)
            {
                steps[next++] = _opt.ShuffleBindings
                    ? new CortexProcedureStep("grep", "grep_anchor", "grammar", Blur.SlotSources.GrammarPrior, ConsumeInput: false)
                    : new CortexProcedureStep("grep", "grep_anchor", "stimulus", Blur.SlotSources.StimulusRead);
                steps[next++] = _opt.ShuffleBindings
                    ? new CortexProcedureStep("read", "descend_target", "stimulus", Blur.SlotSources.StimulusRead)
                    : new CortexProcedureStep("read", "descend_target", "top_hit", Blur.SlotSources.PriorObservation);
                if (!_opt.ShuffleBindings)
                    steps[next++] = new CortexProcedureStep("answer", "answer_path", "witness", Blur.SlotSources.PriorObservation);
            }

            CortexProcedure procedure = new(steps);
            foreach (string stimulus in stimuli)
                procedure.AddInput(new CortexProcedureInput("stimulus", Blur.SlotSources.StimulusRead, stimulus));
            procedure.AddInput(new CortexProcedureInput("grammar", Blur.SlotSources.GrammarPrior, grammarPrior));
            journal.Consolidation(step,
                $"procedure-program · world={instance} · {(_opt.ShuffleBindings ? "SHUFFLED" : "ROUTED")} · " +
                $"grep<-{Blur.SourceLabel(_opt.ShuffleBindings ? Blur.SlotSources.GrammarPrior : Blur.SlotSources.StimulusRead)} · " +
                $"descend<-{Blur.SourceLabel(_opt.ShuffleBindings ? Blur.SlotSources.StimulusRead : Blur.SlotSources.PriorObservation)}");
            return procedure;
        }

        private static bool CanTransferProcedure(RePairResult grammar)
        {
            if (grammar.Rules is not { Length: > 0 } rules) return false;
            (int[]?[] Mates, int Classes, Blur.SlotSources?[] Sources) slots =
                Blur.DetectRuleSlots(rules, grammar.AlphabetSize, Pearl.ReflectFloorBytes, minContext: 3);
            if (slots.Classes == 0) return false;
            bool stimulus = false;
            bool observation = false;
            foreach (Blur.SlotSources? source in slots.Sources)
            {
                stimulus |= source == Blur.SlotSources.StimulusRead;
                observation |= source == Blur.SlotSources.PriorObservation;
                if (stimulus && observation) return true;
            }
            return false;
        }

        private static string PickGrammarPriorFiller(RePairResult grammar, List<string> stimulusTerms)
        {
            HashSet<string> forbidden = new(stimulusTerms, StringComparer.Ordinal);
            Dictionary<string, int> counts = new(StringComparer.Ordinal);
            if (grammar.Rules is { Length: > 0 } rules)
            {
                int limit = Math.Min(96, rules.Length);
                for (int i = 0; i < limit; i++)
                {
                    string[] tokens = Blur.TokensOf(Reconstruct.Expand(rules, rules[i].Pattern));
                    foreach (string token in tokens)
                    {
                        if (token.Length < 3 || forbidden.Contains(token) || Blur.SourceToken(token) != Blur.SlotSources.Unknown)
                            continue;
                        counts[token] = counts.GetValueOrDefault(token) + 1;
                    }
                }
            }
            string best = "grammar_prior";
            int bestCount = -1;
            foreach ((string token, int count) in counts)
            {
                if (count > bestCount || count == bestCount && string.CompareOrdinal(token, best) < 0)
                {
                    best = token;
                    bestCount = count;
                }
            }
            return best;
        }
    }

    private sealed class LocEpisode
    {
        public readonly int Index;
        public readonly string Dir;
        public readonly string Instance;
        public readonly string Repo;
        public readonly bool IsReturn;
        public readonly string Query;
        public readonly string Gold;
        public readonly int GoldStart;                                // gold fn's start_line (−1 = none) — the depth-band classifier reads it without re-parsing gold.json
        public readonly List<Tool.SiteRow> Sites;
        public readonly Tool.AgentWorld World;
        public readonly List<string> Terms;
        public readonly PathVotes Votes;
        public readonly HashSet<string> SearchedTerms = new(StringComparer.Ordinal);
        public readonly List<byte[]> WalkEvents = new();
        public readonly List<TapeEventID?> WalkEventIDs = new();
        public readonly CortexProcedure Procedure;
        public readonly List<LocCommitChoice> CommitChoices = new();
        public int Looks;
        public bool ReachedGold;
        public bool RecalledGold;
        public bool Completed;
        public double CommitConfidence;
        public double CommitNoveltyCoverage;
        public double CommitNoveltyFloor;
        public NoveltyBands CommitNoveltyBand = NoveltyBands.Edge;

        public LocEpisode(int index, string dir, string instance, string repo, bool isReturn, string query, string gold, int goldStart, List<Tool.SiteRow> sites, Tool.AgentWorld world, List<string> terms, HashSet<string> paths, CortexProcedure procedure)
        {
            Index = index;
            Dir = dir;
            Instance = instance;
            Repo = repo;
            IsReturn = isReturn;
            Query = query;
            Gold = gold;
            GoldStart = goldStart;
            Sites = sites;
            World = world;
            Terms = terms;
            Votes = new PathVotes(paths);
            Procedure = procedure;
        }
    }

    private sealed class LocActionPolicy : CortexActionPolicy
    {
        private readonly LocCurriculum _loc;
        private readonly Engine.MarkovModel _generationModel = new();
        private readonly List<string> _hitPaths = new(16);
        private readonly List<CortexActionArgument> _parsedProcedureArguments = new(1);

        public LocActionPolicy(LocCurriculum loc) => _loc = loc;

        public override void OnStepStart(Cortex cortex, int step)
        {
            LocEpisode? episode = _loc.Episode;
            if (episode is null || episode.Completed) return;
            BeginCurrentEpisode(cortex, episode);
            if (episode.Looks >= _loc.Options.LooksCap) _loc.Abstain(cortex, episode);
        }

        public override void OnActionBatchEnd(Cortex cortex)
        {
            LocEpisode? episode = _loc.Episode;
            if (episode is not null && !episode.Completed) _loc.Abstain(cortex, episode);
        }

        public override bool TryChooseAction(Cortex cortex, List<CortexActionArgument> arguments, out CortexAction action)
        {
            action = CortexAction.None;
            LocEpisode? episode = _loc.Episode;
            if (episode is null || episode.Completed || episode.Looks >= _loc.Options.LooksCap) return false;
            BeginCurrentEpisode(cortex, episode);

            Engine.GrammarCover? cover = cortex.Grammar.Rules is { Length: > 0 }
                ? cortex.GrammarCover ?? new Engine.GrammarCover(cortex.Grammar.Rules)
                : null;
            if (episode.Procedure.Enabled)
                return TryChooseProcedureAction(cortex, episode, cover, arguments, out action);

            if (episode.Votes.TryCandidate(cover, out PathVotes.CommitCandidate candidate))
            {
                CommitCalibrationChoice choice = _loc.Gate.Choose(cortex, candidate.Confidence, candidate.NoveltyCoverage, episode.Looks);
                CommitCalibrationRead read = choice.Read;
                if (_loc.Options.ConfidenceTrace) episode.Votes.TraceCandidate(candidate, episode.Instance, read);
                if (choice.Action == CommitCalibrationActions.Commit)
                {
                    bool built = TryBuildAction(cortex, $"answer {candidate.Path}", "answer_path", Blur.SlotSources.PriorObservation, arguments, out action);
                    episode.CommitChoices.Add(new LocCommitChoice(choice, built));
                    if (built)
                    {
                        episode.CommitConfidence = candidate.Confidence;
                        episode.CommitNoveltyCoverage = read.Coverage;
                        episode.CommitNoveltyFloor = read.Floor;
                        episode.CommitNoveltyBand = read.Band;
                        return true;
                    }
                }
                else episode.CommitChoices.Add(new LocCommitChoice(choice, Executed: true));
            }

            if (episode.Looks == 0 && episode.Terms.Count > 0)
            {
                string query = string.Join(' ', episode.Terms.Take(4));
                return TryBuildAction(cortex, $"recall {query}", "recall_query", Blur.SlotSources.StimulusRead, arguments, out action);
            }

            if (episode.Looks == 0)
            {
                string query = ExtractQueryFallback(episode.Query);
                if (query.Length > 0)
                    return TryBuildAction(cortex, $"recall {query}", "recall_query", Blur.SlotSources.StimulusRead, arguments, out action);
            }

            if (episode.Looks == 1 && episode.Terms.Count == 0)
                return TryBuildAction(cortex, "ls src", "descend_target", Blur.SlotSources.GrammarPrior, arguments, out action);

            if (episode.Looks > 0 && episode.SearchedTerms.Count < episode.Terms.Count && episode.Looks < Math.Max(3, episode.Terms.Count))
            {
                string term = "";
                foreach (string candidateTerm in episode.Terms)
                {
                    if (episode.SearchedTerms.Contains(candidateTerm)) continue;
                    term = candidateTerm;
                    break;
                }
                if (term.Length > 0)
                {
                    episode.SearchedTerms.Add(term);
                    return TryBuildAction(cortex, $"grep {term}", "grep_anchor", Blur.SlotSources.StimulusRead, arguments, out action);
                }
            }

            if (episode.Looks > 1 && episode.Votes.TryCandidate(cover, out PathVotes.CommitCandidate openCandidate))
                return TryBuildAction(cortex, $"open {openCandidate.Path}", "descend_target", Blur.SlotSources.PriorObservation, arguments, out action);

            return TryGenerateAction(cortex, episode, arguments, out action);
        }

        private bool TryChooseProcedureAction(Cortex cortex, LocEpisode episode, Engine.GrammarCover? cover,
            List<CortexActionArgument> arguments, out CortexAction action)
        {
            action = CortexAction.None;
            while (true)
            {
                CortexProcedureTransitions transition = episode.Procedure.ReadNext(arguments, out string tool);
                if (transition == CortexProcedureTransitions.Skip)
                {
                    episode.Procedure.AdvanceNext(transition);
                    continue;
                }
                if (transition is CortexProcedureTransitions.Abstain or CortexProcedureTransitions.Blocked or CortexProcedureTransitions.Complete)
                {
                    if (transition == CortexProcedureTransitions.Abstain) episode.Procedure.AdvanceNext(transition);
                    _loc.Abstain(cortex, episode);
                    return false;
                }

                if (tool == "answer")
                {
                    if (!episode.Votes.TryCandidate(cover, out PathVotes.CommitCandidate candidate))
                    {
                        _loc.Abstain(cortex, episode);
                        return false;
                    }
                    CommitCalibrationChoice choice = _loc.Gate.Choose(cortex, 1.0, candidate.NoveltyCoverage, episode.Looks);
                    CommitCalibrationRead read = choice.Read;
                    if (_loc.Options.ConfidenceTrace) episode.Votes.TraceCandidate(candidate, episode.Instance, read);
                    string corroboration = arguments.Count > 0 ? arguments[0].Value : "";
                    bool built = candidate.Path == corroboration
                        && choice.Action == CommitCalibrationActions.Commit
                        && TryBuildProcedureAction(cortex, tool, arguments, out action);
                    episode.CommitChoices.Add(new LocCommitChoice(choice, built));
                    if (!built)
                    {
                        _loc.Abstain(cortex, episode);
                        return false;
                    }
                    episode.Procedure.AdvanceNext(CortexProcedureTransitions.Execute);
                    SetCommitRead(episode, confidence: 1.0, read);
                    return true;
                }

                if (!TryBuildProcedureAction(cortex, tool, arguments, out action))
                {
                    _loc.Abstain(cortex, episode);
                    return false;
                }
                episode.Procedure.AdvanceNext(CortexProcedureTransitions.Execute);
                return true;
            }
        }

        private static void SetCommitRead(LocEpisode episode, double confidence, CommitCalibrationRead read)
        {
            episode.CommitConfidence = confidence;
            episode.CommitNoveltyCoverage = read.Coverage;
            episode.CommitNoveltyFloor = read.Floor;
            episode.CommitNoveltyBand = read.Band;
        }

        private static void BeginCurrentEpisode(Cortex cortex, LocEpisode episode)
        {
            if (cortex.EpisodeID == episode.Instance) return;
            if (cortex.EpisodeID.Length > 0) cortex.EndEpisode();
            cortex.BeginEpisode(episode.Instance);
        }

        private static string ExtractQueryFallback(string query)
        {
            List<string> terms = new();
            int i = 0;
            while (i < query.Length && terms.Count < 4)
            {
                if (!char.IsLetterOrDigit(query[i]) && query[i] != '_')
                {
                    i++;
                    continue;
                }
                int start = i;
                while (i < query.Length && (char.IsLetterOrDigit(query[i]) || query[i] == '_')) i++;
                string token = query[start..i];
                if (token.Length >= 4) terms.Add(token);
            }
            return string.Join(' ', terms);
        }

        public override string FormatTapeValue(Cortex cortex, string value)
            => _loc.Episode is { } episode
                ? FormatTapeText(value, episode.Gold, _loc.Options.AnswerLeakFree)
                : value;

        public override void OnObservation(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
            CortexObservation observation, List<CortexObservationField> fields, byte[] executionBytes,
            List<TapeEventID> eventIDs)
        {
            LocEpisode? episode = _loc.Episode;
            if (episode is null || episode.Completed) return;
            TapeEventID? executionID = null;
            foreach (TapeEventID eventID in eventIDs)
            {
                if (cortex.Tape.ProvenanceOf(eventID) == Provenances.Execution)
                {
                    executionID = eventID;
                    break;
                }
            }
            episode.WalkEvents.Add(executionBytes);
            episode.WalkEventIDs.Add(executionID);
            foreach (TapeEventID eventID in eventIDs)
            {
                if (cortex.Tape.ProvenanceOf(eventID) == Provenances.Execution) continue;
                if (!cortex.Tape.Resolve(eventID, out byte[] bytes)) continue;
                episode.WalkEvents.Add(bytes);
                episode.WalkEventIDs.Add(eventID);
            }
            LocTool? tool = action.Tool as LocTool;
            Tool.ToolVerbs verb = tool?.Verb ?? Tool.ToolVerbs.Noop;
            CollectObservationPaths(fields, _hitPaths);
            bool hitGold = episode.Gold.Length > 0 && _hitPaths.Contains(episode.Gold);
            episode.ReachedGold |= hitGold;
            if (verb == Tool.ToolVerbs.Recall && hitGold) episode.RecalledGold = true;
            CortexActionArgument argument = arguments.Count > 0 ? arguments[0] : default;
            int termRank = argument.Source == Blur.SlotSources.StimulusRead ? episode.Terms.IndexOf(argument.Value) : -1;
            episode.Votes.Tally(_hitPaths, verb, observation.Text, argument.Value, termRank);
            for (int i = 0; episode.Procedure.Enabled && verb == Tool.ToolVerbs.Grep && i < fields.Count; i++)
            {
                CortexObservationField field = fields[i];
                if (field.Slot != "top_hit") continue;
                episode.Procedure.AddInput(new CortexProcedureInput("top_hit", field.Source, field.Value));
                episode.Procedure.AddInput(new CortexProcedureInput("witness", field.Source, field.Value));
            }
            episode.Looks++;
        }

        private bool TryBuildProcedureAction(Cortex cortex, string tool,
            List<CortexActionArgument> arguments, out CortexAction action)
        {
            if (arguments.Count == 0)
            {
                action = CortexAction.None;
                return false;
            }
            _parsedProcedureArguments.Clear();
            if (!cortex.TryParseAction(tool + " " + arguments[0].Value, _parsedProcedureArguments, out action) ||
                _parsedProcedureArguments.Count == 0)
            {
                action = CortexAction.None;
                return false;
            }
            return true;
        }

        private static bool TryBuildAction(Cortex cortex, string line, string slot, Blur.SlotSources source,
            List<CortexActionArgument> arguments, out CortexAction action)
        {
            if (!cortex.TryParseAction(line, arguments, out CortexAction parsed) || arguments.Count == 0)
            {
                action = CortexAction.None;
                return false;
            }
            CortexActionArgument parsedArgument = arguments[0];
            arguments.Clear();
            arguments.Add(new CortexActionArgument(slot, parsedArgument.Value, source));
            action = parsed;
            return true;
        }

        private bool TryGenerateAction(Cortex cortex, LocEpisode episode, List<CortexActionArgument> arguments,
            out CortexAction action)
        {
            if (!TryGenerateAction(cortex, _generationModel, _loc.Options.Len, _loc.Options.Sweeps,
                _loc.Options.Seed, episode.Looks, arguments, out action))
                return false;

            CortexActionArgument parsedArgument = arguments[0];
            arguments[0] = parsedArgument with
            {
                Slot = action.Tool is GrepTool ? "grep_anchor" :
                       action.Tool is ReadTool or OpenTool ? "descend_target" :
                       action.Tool is RecallTool or IndexTool ? "memory_query" : "generated_arg",
                Source = action.Tool is RecallTool or IndexTool ? Blur.SlotSources.PriorObservation : Blur.SlotSources.GrammarPrior,
            };
            return true;
        }

        private static void CollectObservationPaths(List<CortexObservationField> fields, List<string> paths)
        {
            paths.Clear();
            foreach (CortexObservationField field in fields)
            {
                if (field.Slot is "top_hit" or "hit_path") paths.Add(field.Value);
            }
        }
    }

    private sealed class FilePathAnswerReward : CortexReward
    {
        private readonly LocCurriculum _loc;
        private LocEpisode? _pending;
        private bool _pendingCorrect;
        private string _pendingResolved = "";

        public FilePathAnswerReward(LocCurriculum loc) => _loc = loc;

        public override void OnRunStart(Cortex cortex)
        {
            _loc.PrepareResumeArtifacts(cortex.CurrentRun);
        }

        public override void OnObservation(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
            CortexObservation observation, List<CortexObservationField> fields, List<TapeEventID> eventIDs)
        {
            LocEpisode? episode = _loc.Episode;
            if (episode is null || episode.Completed || !observation.Terminal) return;
            string answer = FindObservationValue(fields, "answer_path");
            string resolved = episode.World.ResolveAnswer(answer);
            bool correct = resolved.Length > 0 && resolved == episode.Gold;
            _pending = episode;
            _pendingCorrect = correct;
            _pendingResolved = resolved;
            if (correct) _loc.AdmitSuccessfulWalk(cortex, episode);
            if (episode.Gold.Length > 0) AppendGoldCorroboration(cortex, episode);
        }

        public override void OnActionHarvest(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
            CortexObservation observation, List<CortexObservationField> fields)
        {
            LocEpisode? episode = _pending;
            if (episode is null || episode.Completed || !observation.Terminal) return;
            int vested = _pendingCorrect ? cortex.CorroborateCurrentGrammar(crossReflect: true, wScale: Math.Max(2, cortex.Config.Learning.EvidenceWeightScale)) : 0;
            if (_pendingCorrect && episode.CommitConfidence <= 0) episode.CommitConfidence = 1.0;
            _loc.Gate.ObserveCommit(episode.CommitConfidence, _pendingCorrect, episode.Looks, episode.CommitNoveltyCoverage);
            Engine.GrammarCover? cover = cortex.Grammar.Rules is { Length: > 0 }
                ? cortex.GrammarCover ?? new Engine.GrammarCover(cortex.Grammar.Rules)
                : null;
            (double coherence, double margin, double confidence, double noveltyCoverage, int candidates) = episode.Votes.Diagnose(cover);
            if (candidates > 0) _loc.Diagnostics.Record(coherence, margin, _pendingCorrect, episode.Looks);
            string answer = FindObservationValue(fields, "answer_path");
            SolveResult result = new(episode.Gold, _pendingResolved.Length > 0 ? _pendingResolved : answer, true, _pendingCorrect, episode.ReachedGold, episode.RecalledGold, episode.Looks,
                episode.CommitConfidence, _loc.Gate.CalibrationError, coherence, margin, candidates, 0, -1, 0,
                episode.CommitNoveltyCoverage, episode.CommitNoveltyBand, episode.CommitNoveltyFloor, false, 0);
            episode.Completed = true;
            _loc.Complete(cortex, episode, result, vested);
            _pending = null;
            _pendingResolved = "";
            _pendingCorrect = false;
        }

        public override void OnRunEnd(Cortex cortex)
        {
            _loc.CloseRunWriters();
            string report = _loc.Report();
            cortex.CurrentRun.Write("loc_report.txt", report);
            cortex.CurrentRun.Write("report.txt", report);
            Trace.Note(report);
        }

        public override void OnActionBatchEnd(Cortex cortex) => _loc.RunPendingHeldoutFork(cortex);

        public override void OnConsolidationPhase(Cortex cortex, int step) => _loc.RunNightDiet(cortex);

        private void AppendGoldCorroboration(Cortex cortex, LocEpisode episode)
        {
            string text = episode.World.FileText(episode.Gold);
            if (text.Length == 0) return;
            int n = 0;
            foreach (string chunk in ChunkLines(text, maxLines: 4))
            {
                cortex.AppendEvidence(Encoding.UTF8.GetBytes(FormatTapeText(chunk, episode.Gold, _loc.Options.AnswerLeakFree)), "corpus");
                n++;
                if (n >= 24) break;
            }
        }

        private static string FindObservationValue(List<CortexObservationField> fields, string slot)
        {
            foreach (CortexObservationField field in fields)
            {
                if (field.Slot == slot) return field.Value;
            }
            return "";
        }
    }

    private abstract class LocTool : CortexTool
    {
        private readonly LocCurriculum _loc;

        protected LocTool(LocCurriculum loc, Tool.ToolVerbs verb, string name)
        {
            _loc = loc;
            Verb = verb;
            Name = name;
        }

        public Tool.ToolVerbs Verb { get; }

        public override string Name { get; }

        public override bool TryParseAction(string line, List<CortexActionArgument> arguments, out CortexAction action)
        {
            Tool.ToolCall call = Tool.ToolCall.Parse(line);
            if (call.Verb != Verb || call.Arg.Length == 0)
            {
                action = CortexAction.None;
                return false;
            }
            arguments.Add(new CortexActionArgument("generated_arg", call.Arg, Blur.SlotSources.GrammarPrior));
            action = new CortexAction(this, call.Raw.Trim());
            return true;
        }

        protected LocEpisode? Episode => _loc.Episode;

        protected static string GetArgument(List<CortexActionArgument> arguments)
            => arguments.Count > 0 ? arguments[0].Value : "";

        protected static CortexObservation Convert(Tool.Observation observation, List<CortexObservationField> fields)
        {
            for (int i = 0; i < observation.HitPaths.Count; i++)
            {
                fields.Add(new CortexObservationField(i == 0 ? "top_hit" : "hit_path",
                    observation.HitPaths[i], Blur.SlotSources.PriorObservation));
            }
            if (observation.AnswerPath.Length > 0)
                fields.Add(new CortexObservationField("answer_path", observation.AnswerPath, Blur.SlotSources.PriorObservation));
            return new CortexObservation(observation.Text, observation.Answered);
        }
    }

    private sealed class GrepTool : LocTool
    {
        public GrepTool(LocCurriculum loc) : base(loc, Tool.ToolVerbs.Grep, "grep") { }

        public override CortexObservation Act(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
            List<CortexObservationField> fields)
            => Episode is { } episode ? Convert(episode.World.Act(new Tool.ToolCall(Tool.ToolVerbs.Grep, GetArgument(arguments), action.Raw)), fields) : CortexObservation.Empty;
    }

    private sealed class OpenTool : LocTool
    {
        public OpenTool(LocCurriculum loc) : base(loc, Tool.ToolVerbs.Open, "open") { }

        public override CortexObservation Act(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
            List<CortexObservationField> fields)
            => Episode is { } episode ? Convert(episode.World.Act(new Tool.ToolCall(Tool.ToolVerbs.Open, GetArgument(arguments), action.Raw)), fields) : CortexObservation.Empty;
    }

    private sealed class ReadTool : LocTool
    {
        public ReadTool(LocCurriculum loc) : base(loc, Tool.ToolVerbs.Read, "read") { }

        public override CortexObservation Act(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
            List<CortexObservationField> fields)
            => Episode is { } episode ? Convert(episode.World.Act(new Tool.ToolCall(Tool.ToolVerbs.Read, GetArgument(arguments), action.Raw)), fields) : CortexObservation.Empty;
    }

    private sealed class LsTool : LocTool
    {
        public LsTool(LocCurriculum loc) : base(loc, Tool.ToolVerbs.Ls, "ls") { }

        public override CortexObservation Act(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
            List<CortexObservationField> fields)
            => Episode is { } episode ? Convert(episode.World.Act(new Tool.ToolCall(Tool.ToolVerbs.Ls, GetArgument(arguments), action.Raw)), fields) : CortexObservation.Empty;
    }

    private sealed class AnswerTool : LocTool
    {
        public AnswerTool(LocCurriculum loc) : base(loc, Tool.ToolVerbs.Answer, "answer") { }

        public override bool IsTerminal => true;

        public override CortexObservation Act(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
            List<CortexObservationField> fields)
            => Episode is { } episode ? Convert(episode.World.Act(new Tool.ToolCall(Tool.ToolVerbs.Answer, GetArgument(arguments), action.Raw)), fields) : CortexObservation.Empty;
    }

    private sealed class RecallTool : LocTool
    {
        private Tool.MemoryWorld? _memory;

        public RecallTool(LocCurriculum loc) : base(loc, Tool.ToolVerbs.Recall, "recall") { }

        public override CortexObservation Act(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
            List<CortexObservationField> fields)
        {
            _memory ??= new Tool.MemoryWorld(cortex.Tape);
            return Convert(_memory.Act(new Tool.ToolCall(Tool.ToolVerbs.Recall, GetArgument(arguments), action.Raw)), fields);
        }
    }

    private sealed class IndexTool : LocTool
    {
        private Tool.MemoryWorld? _memory;

        public IndexTool(LocCurriculum loc) : base(loc, Tool.ToolVerbs.Index, "index") { }

        public override CortexObservation Act(Cortex cortex, CortexAction action, List<CortexActionArgument> arguments,
            List<CortexObservationField> fields)
        {
            _memory ??= new Tool.MemoryWorld(cortex.Tape);
            return Convert(_memory.Act(new Tool.ToolCall(Tool.ToolVerbs.Index, GetArgument(arguments), action.Raw)), fields);
        }
    }

    private static string FormatTapeText(string text, string answerPath, bool answerLeakFree)
        => answerLeakFree && answerPath.Length > 0 ? text.Replace(answerPath, "<answer-path>", StringComparison.Ordinal) : text;
}

[RonObject]
internal partial class LocRONInputs
{
    public List<string> directories = new();
}
