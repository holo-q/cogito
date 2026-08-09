namespace Cogito;

using System.Globalization;
using System.Text;
using Cogito.Exec;
using Cogito.Grammar;
using Cogito.Induct;

/// Standing execution-witness channel for the mesh. VM traces land on the shared Tape for Pearl to witness, while
/// this barrier-free trace loom keeps the execution-form reads live during the run.
public sealed class WeftChannel : IDisposable
{
    private const string ValueFile = "weft_value.tsv";
    private const string DepthFile = "weft_depth.tsv";
    private const string TowerFile = "weft_towers.tsv";

    /// The shuffled-null depth read rebuilds a fresh loom over EVERY accumulated trace, so running it
    /// per publication is O(steps²) over the run. Default cadence recomputes it every Nth publication
    /// and re-emits the last read between refreshes; 1 restores the per-publication read exactly.
    public const int DefaultNullDepthEvery = 8;

    private const string ValueHeader = "step\trule\tdepth\tspan\tbreadth\thome\tload\ttrace_load\tjournal_body\tjournal_calls\tjournal_leaf\tvalue\tsources\texpansion";
    private const string DepthHeader = "step\trules\tcalled\treflected\trate\tslope\tnull_rules\tnull_called\tnull_reflected\tnull_rate\tnull_slope\tnull_collapsed";
    private const string TowerHeader = "step\trules\ttowers\tmax_height\tdeepest_span\thistogram\tknots_parked";

    private readonly Run _run;
    private readonly int _blockBudget;
    private readonly IWeftProgramCatalog? _programCatalog;
    private readonly int _nullDepthEvery;
    private readonly Loom _loom = new(256, RePair.NoBarrier, wScale: 1);
    private readonly List<WeftTrace> _traces = new();
    // ── THE INCREMENTAL CENSUS ──  per-rule cumulative occurrence/journal accumulators. The old
    // Analyze re-expanded every rule and byte-scanned EVERY accumulated trace per publication —
    // O(steps²) as _traces grows monotonically. This trace loom is barrier-free and never Rebases,
    // so rules are append-only with immutable patterns (an expansion is fixed at rule birth) and
    // traces are append-only: each publication folds only (new rules × all traces) + (old rules ×
    // Δ traces), leaving every cumulative sum identical to the full re-scan.
    private readonly List<RuleCensus> _census = new();
    private int _censusTraces;
    private int _publications;
    private DepthRead _lastNull;
    private StreamWriter _valueW = null!;
    private StreamWriter _depthW = null!;
    private StreamWriter _towerW = null!;
    private bool _dirty;
    private string _latestDepth = "no execution trace grammar yet";
    private string _latestTowers = "no tower census yet";
    private string _latestValue = "no value rows yet";
    private int _replayedAuthored;
    private int _replayedInduced;

    private WeftChannel(Run run, int blockBudget, IWeftProgramCatalog? programCatalog, int nullDepthEvery)
    {
        _run = run;
        _blockBudget = blockBudget;
        _programCatalog = programCatalog;
        _nullDepthEvery = Math.Max(1, nullDepthEvery);
    }

    public static WeftChannel Open(Run run, int blockBudget, Journal journal, Tape tape, int nextStep, bool resume,
        IWeftProgramCatalog? programCatalog = null, int nullDepthEvery = DefaultNullDepthEvery)
    {
        WeftChannel channel = new(run, blockBudget, programCatalog, nullDepthEvery);
        if (resume) channel.Rebuild(journal, tape, nextStep);
        channel.PrepareArtifact(ValueFile, ValueHeader, nextStep, resume);
        channel.PrepareArtifact(DepthFile, DepthHeader, nextStep, resume);
        channel.PrepareArtifact(TowerFile, TowerHeader, nextStep, resume);
        channel._valueW = run.Appender(ValueFile);
        channel._depthW = run.Appender(DepthFile);
        channel._towerW = run.Appender(TowerFile);
        return channel;
    }

    public void Step(int step, Tape tape, Journal journal, IReadOnlyList<Neuron> nodes)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            string source = nodes[i].Name;
            WeftProgram program = WeftDiet.Pick(i, step, _blockBudget, source);
            ExecResult exec = new TapeVm(program.Rules).Run(program.Start, program.Fuel);
            if (exec.Trace.Length == 0) continue;
            TapeEventID sid = TapePacketCreator.AppendWeftExecution(tape, journal, step, source, in program, in exec);
            AddTrace(step, sid, source, program, exec.Trace, exec);
        }
        if (_dirty) Publish(step);
    }

    public string Report()
        => $"value\t{_latestValue}\n"
         + $"depth\t{_latestDepth}\n"
         + $"towers\t{_latestTowers}\n"
         + $"replay\tinduced {_replayedInduced} · authored {_replayedAuthored}\n";

    public WeftReplayReceipt ReadReplayReceipt() => new(_replayedInduced, _replayedAuthored);

    public void Dispose()
    {
        _valueW?.Dispose();
        _depthW?.Dispose();
        _towerW?.Dispose();
        _loom.Dispose();
    }

    private void Rebuild(Journal journal, Tape tape, int nextStep)
    {
        foreach (string line in journal.ResidentLines)
        {
            if (!TryParseWeft(line, out WeftEntry entry)) continue;
            if (entry.Step >= nextStep) continue;
            if (!tape.Resolve(entry.Sid, out byte[] trace)) throw new InvalidDataException($"weft replay: {entry.Sid} not on the tape");
            WeftProgram program = ResolveProgram(entry.Program, entry.Source);
            int fuel = entry.ReadFuel(trace.Length, program.Fuel);
            ExecResult exec = new TapeVm(program.Rules).Run(program.Start, fuel);
            AddTrace(entry.Step, entry.Sid, entry.Source, program, trace, exec);
        }
        if (_dirty)
        {
            _loom.Pump();
            _dirty = false;
        }
    }

    private WeftProgram ResolveProgram(string name, string source)
    {
        if (_programCatalog is not null && _programCatalog.TryGetProgram(name, out WeftProgram induced))
        {
            _replayedInduced++;
            return induced;
        }
        WeftProgram authored = WeftDiet.GetByName(name, source, _blockBudget);
        _replayedAuthored++;
        return authored;
    }

    private void AddTrace(int step, TapeEventID sid, string source, WeftProgram program, byte[] trace, ExecResult exec)
    {
        _loom.SpliceEvent(trace, sid.Value, weight: 1, trailingBarriers: 0);
        _traces.Add(new WeftTrace(step, sid, source, program.Name, trace, FiredLoads(program, exec)));
        _dirty = true;
    }

    private static RuleLoad[] FiredLoads(WeftProgram program, ExecResult exec)
    {
        List<RuleLoad> rows = new();
        for (int r = 0; r < program.Rules.Length; r++)
        {
            long calls = exec.FuelJournal.Calls[r], body = exec.FuelJournal.BodyFuel[r], leaf = exec.FuelJournal.LeafFuel[r];
            if ((calls | body | leaf) == 0) continue;
            byte[] ruleBody = program.DirectRuleBodies[r];
            if (ruleBody.Length == 0) continue;
            rows.Add(new RuleLoad(ruleBody, program.RuleNames[r], calls, body, leaf));
        }
        return rows.ToArray();
    }

    private void Publish(int step)
    {
        _loom.Pump();
        RePairResult grammar = _loom.Result();
        UpdateCensus(in grammar);
        RuleFact[] facts = CensusFacts(in grammar);
        int valueRows = 0;
        foreach (RuleFact f in facts)
        {
            if (f.Load <= 0) continue;
            valueRows++;
            _valueW.WriteLine(string.Join('\t',
                step.ToString(CultureInfo.InvariantCulture),
                $"N{Symbol.FirstNonterminal + (uint)f.Rule}",
                f.Depth.ToString(CultureInfo.InvariantCulture),
                f.Span.ToString(CultureInfo.InvariantCulture),
                f.Breadth.ToString(CultureInfo.InvariantCulture),
                Fmt(f.Home),
                f.Load.ToString(CultureInfo.InvariantCulture),
                f.TraceLoad.ToString(CultureInfo.InvariantCulture),
                f.JournalBody.ToString(CultureInfo.InvariantCulture),
                f.JournalCalls.ToString(CultureInfo.InvariantCulture),
                f.JournalLeaf.ToString(CultureInfo.InvariantCulture),
                Fmt(f.Value),
                SourceMap(f.SourceCounts),
                Vis(f.Expansion, 64)));
        }

        DepthRead real = DepthRead.Of(facts);
        // The shuffled null rides its cadence (DefaultNullDepthEvery): recompute on absolute-step
        // boundaries (resume-stable — a resumed run refreshes on the same steps a straight-through
        // run does), re-emit the last read between refreshes — the collapse verdict then compares
        // against the freshest null on record rather than a per-step rebuild of the whole history.
        if (_publications == 0 || step % _nullDepthEvery == 0) _lastNull = NullDepthRead(step);
        _publications++;
        DepthRead nul = _lastNull;
        bool nullCollapsed = nul.Reflected < real.Reflected;
        _depthW.WriteLine(string.Join('\t',
            step.ToString(CultureInfo.InvariantCulture),
            grammar.Rules.Length.ToString(CultureInfo.InvariantCulture),
            real.Called.ToString(CultureInfo.InvariantCulture),
            real.Reflected.ToString(CultureInfo.InvariantCulture),
            Fmt(real.Rate),
            Fmt(real.Slope),
            nul.Rules.ToString(CultureInfo.InvariantCulture),
            nul.Called.ToString(CultureInfo.InvariantCulture),
            nul.Reflected.ToString(CultureInfo.InvariantCulture),
            Fmt(nul.Rate),
            Fmt(nul.Slope),
            nullCollapsed ? "yes" : "no"));

        List<CountSlots.Tower> towers = CountSlots.Scan(grammar.Rules, grammar.AlphabetSize);
        CountSlots.Census census = CountSlots.Summarize(towers);
        bool parked = census.Towers == 0;
        _towerW.WriteLine(string.Join('\t',
            step.ToString(CultureInfo.InvariantCulture),
            grammar.Rules.Length.ToString(CultureInfo.InvariantCulture),
            census.Towers.ToString(CultureInfo.InvariantCulture),
            census.MaxHeight.ToString(CultureInfo.InvariantCulture),
            census.DeepestSpan.ToString(CultureInfo.InvariantCulture),
            Histogram(census.HeightHistogram),
            parked ? "yes" : "no"));

        _latestValue = $"{valueRows} live rule rows · {grammar.Rules.Length} trace rules";
        _latestDepth = $"called {real.Called}, reflected {real.Reflected}, slope {Fmt(real.Slope)} · shuffled null {nul.Reflected}/{nul.Called}, collapsed {(nullCollapsed ? "yes" : "no")}";
        _latestTowers = parked ? "no towers, knots parked" : $"{census.Towers} towers · max height {census.MaxHeight} · deepest {census.DeepestSpan}B";
        _dirty = false;
    }

    private DepthRead NullDepthRead(int step)
    {
        if (_traces.Count == 0) return new DepthRead(0, 0, 0, double.NaN, double.NaN, 0);
        using Loom loom = new(256, RePair.NoBarrier, wScale: 1);
        List<WeftTrace> shuffled = new(_traces.Count);
        for (int i = 0; i < _traces.Count; i++)
        {
            WeftTrace t = _traces[i];
            byte[] bytes = Engine.Shuffled(t.Trace, ((ulong)(step + 1) << 32) ^ (uint)(i + 1));
            loom.SpliceEvent(bytes, i, weight: 1, trailingBarriers: 0);
            shuffled.Add(t with { Trace = bytes, Loads = [] });
        }
        loom.Pump();
        RePairResult grammar = loom.Result();
        DepthRead read = DepthRead.Of(Analyze(grammar, shuffled));
        return read with { Rules = grammar.Rules.Length };
    }

    private void UpdateCensus(in RePairResult grammar)
    {
        // Defensive only: this barrier-free loom never Rebases, so rules can only append. A shrink
        // would mean the append-only premise broke — fall back to a full re-fold rather than serve
        // stale sums.
        if (grammar.Rules.Length < _census.Count)
        {
            _census.Clear();
            _censusTraces = 0;
        }
        int oldRules = _census.Count, oldTraces = _censusTraces;
        for (int r = oldRules; r < grammar.Rules.Length; r++)
            _census.Add(new RuleCensus(Reconstruct.Expand(grammar.Rules, [new Symbol(Symbol.FirstNonterminal + (uint)r)])));
        for (int r = 0; r < oldRules; r++)
            for (int t = oldTraces; t < _traces.Count; t++) FoldTrace(_census[r], _traces[t]);
        for (int r = oldRules; r < _census.Count; r++)
            for (int t = 0; t < _traces.Count; t++) FoldTrace(_census[r], _traces[t]);
        _censusTraces = _traces.Count;
    }

    private static void FoldTrace(RuleCensus census, in WeftTrace trace)
    {
        byte[] expansion = census.Expansion;
        int occ = CountOccurrences(trace.Trace, expansion);
        if (occ > 0)
        {
            census.Counts[trace.Source] = census.Counts.GetValueOrDefault(trace.Source) + occ;
            census.TraceLoad += (long)occ * expansion.Length;
        }
        foreach (RuleLoad load in trace.Loads)
        {
            if (!load.Body.AsSpan().SequenceEqual(expansion)) continue;
            census.JournalCalls += load.Calls;
            census.JournalBody += load.BodyFuel;
            census.JournalLeaf += load.LeafFuel;
        }
    }

    private RuleFact[] CensusFacts(in RePairResult grammar)
    {
        (int[] depth, int[] span) = Engine.RuleDepthSpan(grammar);
        RuleFact[] facts = new RuleFact[_census.Count];
        for (int r = 0; r < facts.Length; r++)
        {
            RuleCensus census = _census[r];
            long loadValue = census.JournalBody > 0 ? census.JournalBody : census.TraceLoad;
            facts[r] = new RuleFact(r, depth[r], span[r], census.Counts.Count, Home(census.Counts), loadValue,
                census.TraceLoad, census.JournalBody, census.JournalCalls, census.JournalLeaf, census.Counts,
                census.Expansion);
        }
        return facts;
    }

    /// The full re-scan census — retained for the shuffled-null read, whose traces are re-shuffled per
    /// refresh and so can never be folded incrementally.
    private static RuleFact[] Analyze(in RePairResult grammar, IReadOnlyList<WeftTrace> traces)
    {
        int n = grammar.Rules.Length;
        (int[] depth, int[] span) = Engine.RuleDepthSpan(grammar);
        RuleFact[] facts = new RuleFact[n];
        for (int r = 0; r < n; r++)
        {
            byte[] expansion = Reconstruct.Expand(grammar.Rules, [new Symbol(Symbol.FirstNonterminal + (uint)r)]);
            SortedDictionary<string, long> counts = new(StringComparer.Ordinal);
            long traceLoad = 0, journalBody = 0, journalCalls = 0, journalLeaf = 0;
            foreach (WeftTrace t in traces)
            {
                int occ = CountOccurrences(t.Trace, expansion);
                if (occ > 0)
                {
                    counts[t.Source] = counts.GetValueOrDefault(t.Source) + occ;
                    traceLoad += (long)occ * expansion.Length;
                }
                foreach (RuleLoad load in t.Loads)
                {
                    if (!load.Body.AsSpan().SequenceEqual(expansion)) continue;
                    journalCalls += load.Calls;
                    journalBody += load.BodyFuel;
                    journalLeaf += load.LeafFuel;
                }
            }

            long loadValue = journalBody > 0 ? journalBody : traceLoad;
            double home = Home(counts);
            facts[r] = new RuleFact(r, depth[r], span[r], counts.Count, home, loadValue, traceLoad, journalBody,
                journalCalls, journalLeaf, counts, expansion);
        }
        return facts;
    }

    private static int CountOccurrences(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length) return 0;
        int n = 0;
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
            if (haystack.Slice(i, needle.Length).SequenceEqual(needle)) n++;
        return n;
    }

    private static double Home(SortedDictionary<string, long> counts)
    {
        if (counts.Count <= 1) return 0;
        long total = 0, max = 0;
        foreach (long c in counts.Values) { total += c; if (c > max) max = c; }
        return max - (double)total / counts.Count;
    }

    private void PrepareArtifact(string file, string header, int nextStep, bool resume)
    {
        string path = _run.PathOf(file);
        if (!resume || !File.Exists(path))
        {
            File.WriteAllText(path, header + "\n");
            return;
        }

        StringBuilder sb = new(header.Length + 1);
        sb.Append(header).Append('\n');
        foreach (string row in File.ReadLines(path).Skip(1))
        {
            int tab = row.IndexOf('\t');
            if (tab <= 0 || !int.TryParse(row.AsSpan(0, tab), NumberStyles.None, CultureInfo.InvariantCulture, out int step)) continue;
            if (step < nextStep) sb.Append(row).Append('\n');
        }
        File.WriteAllText(path, sb.ToString());
    }

    private static bool TryParseWeft(string line, out WeftEntry entry)
    {
        entry = default;
        string[] f = line.Split('\t');
        if (f.Length < 5 || f[1] != "weft") return false;
        if (!int.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out int step)) return false;
        if (!long.TryParse(f[2].AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out long sid)) return false;
        int fuelLeft = -1;
        long calls = -1;
        if (f.Length >= 9)
        {
            if (!TryParseField(f[6], "fuelLeft=", out long parsedFuelLeft) || parsedFuelLeft < 0 || parsedFuelLeft > int.MaxValue) return false;
            if (!TryParseField(f[8], "calls=", out calls) || calls < 0) return false;
            fuelLeft = (int)parsedFuelLeft;
        }
        entry = new WeftEntry(step, new TapeEventID(sid), f[3], f[4], fuelLeft, calls);
        return true;
    }

    private static bool TryParseField(string field, string prefix, out long value)
    {
        value = 0;
        return field.StartsWith(prefix, StringComparison.Ordinal)
            && long.TryParse(field.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static string SourceMap(SortedDictionary<string, long> counts)
    {
        if (counts.Count == 0) return "-";
        StringBuilder sb = new();
        foreach ((string src, long n) in counts)
        {
            if (sb.Length > 0) sb.Append(',');
            sb.Append(src).Append(':').Append(n.ToString(CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private static string Histogram(int[] hist)
    {
        if (hist.Length <= 1) return "-";
        StringBuilder sb = new();
        for (int i = 1; i < hist.Length; i++)
        {
            if (hist[i] == 0) continue;
            if (sb.Length > 0) sb.Append(',');
            sb.Append(i).Append(':').Append(hist[i]);
        }
        return sb.Length == 0 ? "-" : sb.ToString();
    }

    private static string Vis(ReadOnlySpan<byte> bytes, int max)
    {
        int n = Math.Min(bytes.Length, max);
        StringBuilder sb = new(n + 1);
        for (int i = 0; i < n; i++)
        {
            byte b = bytes[i];
            sb.Append(Bios.TryOpcode(b, out _) ? (char)b : '.');
        }
        if (bytes.Length > max) sb.Append("...");
        return sb.ToString();
    }

    private static string Fmt(double d)
        => double.IsNaN(d) ? "nan" : d.ToString("0.######", CultureInfo.InvariantCulture);

    private readonly record struct WeftEntry(int Step, TapeEventID Sid, string Source, string Program, int FuelLeft, long Calls)
    {
        public int ReadFuel(int traceLength, int fallback)
        {
            if (FuelLeft < 0 || Calls < 0) return fallback;
            long fuel = checked(traceLength + Calls + FuelLeft);
            if (fuel <= 0 || fuel > int.MaxValue) throw new InvalidDataException($"weft replay Fuel {fuel} exceeds Int32");
            return (int)fuel;
        }
    }
    private readonly record struct RuleLoad(byte[] Body, string Name, long Calls, long BodyFuel, long LeafFuel);

    /// One rule's cumulative census row — expansion fixed at rule birth (append-only loom), sums
    /// grown per Δ trace by FoldTrace.
    private sealed class RuleCensus(byte[] expansion)
    {
        public byte[] Expansion { get; } = expansion;
        public SortedDictionary<string, long> Counts { get; } = new(StringComparer.Ordinal);
        public long TraceLoad;
        public long JournalBody;
        public long JournalCalls;
        public long JournalLeaf;
    }
    private readonly record struct WeftTrace(int Step, TapeEventID Sid, string Source, string Program, byte[] Trace, RuleLoad[] Loads);

    private readonly record struct RuleFact(int Rule, int Depth, int Span, int Breadth, double Home, long Load,
        long TraceLoad, long JournalBody, long JournalCalls, long JournalLeaf, SortedDictionary<string, long> SourceCounts,
        byte[] Expansion)
    {
        public double Value => Breadth * Home * Load;
        public bool Called => Load > 0 && Span >= Pearl.ReflectFloorBytes;
        public bool Reflected => Called && Breadth >= 2;
    }

    private readonly record struct DepthRead(int Called, int Reflected, int Rules, double Rate, double Slope, int PopulatedDepths)
    {
        public static DepthRead Of(IReadOnlyList<RuleFact> facts)
        {
            SortedDictionary<int, (int Called, int Reflected)> byDepth = new();
            int called = 0, reflected = 0;
            foreach (RuleFact f in facts)
            {
                if (!f.Called) continue;
                called++;
                if (f.Reflected) reflected++;
                (int Called, int Reflected) row = byDepth.GetValueOrDefault(f.Depth);
                row.Called++;
                if (f.Reflected) row.Reflected++;
                byDepth[f.Depth] = row;
            }

            List<(double Depth, double Rate)> bins = new();
            foreach ((int d, (int Called, int Reflected) row) in byDepth)
                if (row.Called > 0) bins.Add((d, (double)row.Reflected / row.Called));
            double rate = called == 0 ? double.NaN : (double)reflected / called;
            return new DepthRead(called, reflected, facts.Count, rate, FitSlope(bins), bins.Count);
        }

        private static double FitSlope(IReadOnlyList<(double Depth, double Rate)> bins)
        {
            int n = bins.Count;
            if (n < 2) return double.NaN;
            double sx = 0, sy = 0, sxx = 0, sxy = 0;
            foreach ((double x, double y) in bins) { sx += x; sy += y; sxx += x * x; sxy += x * y; }
            double den = n * sxx - sx * sx;
            return den == 0 ? double.NaN : (n * sxy - sx * sy) / den;
        }
    }
}

public readonly record struct WeftReplayReceipt(int InducedPrograms, int AuthoredPrograms);
