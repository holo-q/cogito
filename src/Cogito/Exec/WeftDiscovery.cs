namespace Cogito.Exec;

using Cogito.Grammar;
using Cogito.Induct;

public readonly struct WeftKnotID(ulong value) : IEquatable<WeftKnotID>, IComparable<WeftKnotID>
{
    public readonly ulong Value = value;
    public int CompareTo(WeftKnotID other) => Value.CompareTo(other.Value);
    public bool Equals(WeftKnotID other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is WeftKnotID other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => $"K{Value:x16}";
}

public readonly record struct WeftTowerReproduction(
    int Height,
    long Iterations,
    int ExpectedBytes,
    int ActualBytes,
    bool ByteExact,
    bool FuelExact);

public readonly record struct WeftKnotReceipt(
    WeftKnotID ID,
    long TowerRentMbits,
    long KnotRentMbits,
    long SavingsMbits,
    int SourceBreadth,
    bool ShuffledAccepted,
    WeftTowerReproduction[] Reproductions);

public interface IWeftProgramCatalog
{
    bool TryGetProgram(string name, out WeftProgram program);
}

public sealed class WeftKnot
{
    internal WeftKnot(
        WeftKnotID id,
        byte[] body,
        int minimumStack,
        int stackDelta,
        long[] verifiedIterations,
        string[] sources,
        WeftKnotReceipt receipt)
    {
        ID = id;
        Body = body;
        MinimumStack = minimumStack;
        StackDelta = stackDelta;
        VerifiedIterations = verifiedIterations;
        Sources = sources;
        Receipt = receipt;
    }

    public WeftKnotID ID { get; }
    public byte[] Body { get; }
    public int MinimumStack { get; }
    public int StackDelta { get; }
    public long[] VerifiedIterations { get; }
    public string[] Sources { get; }
    public WeftKnotReceipt Receipt { get; }
    public long DeepestVerifiedIteration => VerifiedIterations.Length == 0 ? 0 : VerifiedIterations[^1];
    public long DeepestVerifiedSpan => checked(DeepestVerifiedIteration * Body.Length);

    public WeftProgram CreateProgram(long iterations)
    {
        if (iterations <= 0) throw new ArgumentOutOfRangeException(nameof(iterations));
        long fuelLong = checked(iterations * (Body.Length + 1L));
        if (fuelLong > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(iterations), "knot Fuel exceeds Int32");
        Symbol[] pattern = new Symbol[Body.Length + 1];
        for (int i = 0; i < Body.Length; i++) pattern[i] = Symbol.Terminal(Body[i]);
        pattern[^1] = new Symbol(Symbol.FirstNonterminal);
        GrammarRule rule = new(GrammarRule.ComputeId(pattern), pattern, new Mbits(256));
        return new WeftProgram(
            $"knot-{ID.Value:x16}-i{iterations.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            $"verified recursive body through {DeepestVerifiedIteration} iterations",
            Bios.Render(Body) + " N256(self)",
            [new Symbol(Symbol.FirstNonterminal)],
            [rule],
            (int)fuelLong,
            ["knot-body"],
            [Body]);
    }
}

/// Discovers executable recursive rules from finite Re-Pair doubling towers. The trace grammar remains a DAG;
/// admitted knots live only in this catalog and execute only through TapeVm under Fuel.
public sealed class WeftDiscovery : IWeftProgramCatalog, IDisposable
{
    private const int STATE_VERSION = 2;
    private const long SYMBOL_RENT_MBITS = 32_000;
    private const long KNOT_HEADER_MBITS = 64_000;

    private readonly Loom _loom = new(256, RePair.NoBarrier, wScale: 1);
    private readonly List<TraceEvidence> _evidence = new();
    private readonly SortedDictionary<WeftKnotID, WeftKnot> _knots = new();
    private readonly SortedDictionary<string, WeftProgram> _programs = new(StringComparer.Ordinal);
    private long _nextEvidenceID;
    private int _rejected;

    public IReadOnlyCollection<WeftKnot> Knots => _knots.Values;
    public int Rejected => _rejected;

    public WeftProgram CreateProgram(WeftKnotID id, long iterations)
    {
        if (!_knots.TryGetValue(id, out WeftKnot? knot)) throw new KeyNotFoundException($"unknown Weft knot {id}");
        WeftProgram program = knot.CreateProgram(iterations);
        if (_programs.TryGetValue(program.Name, out WeftProgram existing))
        {
            if (!WeftProgramCodec.Equals(in existing, in program))
                throw new InvalidOperationException($"Weft program name collision '{program.Name}'");
            return existing;
        }
        _programs.Add(program.Name, program);
        return program;
    }

    public bool TryGetProgram(string name, out WeftProgram program) => _programs.TryGetValue(name, out program);

    public void ObserveExecution(string source, ReadOnlySpan<byte> trace)
    {
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Weft evidence source cannot be blank", nameof(source));
        if (trace.Length == 0) return;
        byte[] bytes = trace.ToArray();
        _evidence.Add(new TraceEvidence(source, bytes));
        _loom.SpliceEvent(bytes, _nextEvidenceID++, weight: 1, trailingBarriers: 0);
    }

    public List<WeftKnotReceipt> InduceKnots()
    {
        _loom.Pump();
        RePairResult grammar = _loom.Result();
        return InduceKnots(grammar);
    }

    public List<WeftKnotReceipt> InduceKnots(in RePairResult grammar)
    {
        List<WeftKnotReceipt> admitted = new();
        List<CountSlots.Tower> towers = CountSlots.Scan(grammar.Rules, grammar.AlphabetSize);
        foreach (CountSlots.Tower tower in towers)
        {
            if (!TryInduceKnot(grammar, tower, out WeftKnot? knot))
            {
                _rejected++;
                continue;
            }
            if (knot is null) throw new InvalidOperationException("successful knot induction returned no knot");
            if (_knots.ContainsKey(knot.ID)) continue;
            _knots.Add(knot.ID, knot);
            admitted.Add(knot.Receipt);
        }
        return admitted;
    }

    public bool TryGetKnot(WeftKnotID id, out WeftKnot knot) => _knots.TryGetValue(id, out knot!);

    public void Save(CkptWriter writer)
    {
        writer.I32(STATE_VERSION);
        writer.I64(_nextEvidenceID);
        writer.I32(_rejected);
        writer.I32(_evidence.Count);
        foreach (TraceEvidence evidence in _evidence)
        {
            writer.Str(evidence.Source);
            writer.Bytes(evidence.Bytes);
        }
        writer.I32(_knots.Count);
        foreach (WeftKnot knot in _knots.Values) SaveKnot(writer, knot);
        writer.I32(_programs.Count);
        foreach (WeftProgram program in _programs.Values) WeftProgramCodec.Save(writer, in program);
    }

    public void Load(CkptReader reader)
    {
        if (_evidence.Count != 0 || _knots.Count != 0 || _programs.Count != 0) throw new InvalidOperationException("load Weft discovery state into a fresh instance");
        int version = reader.I32();
        if (version is not 1 and not STATE_VERSION) throw new InvalidDataException($"unsupported Weft discovery state {version}");
        _nextEvidenceID = reader.I64();
        _rejected = reader.I32();
        _evidence.Clear();
        int evidenceCount = ReadCount(reader, "Weft evidence");
        if (_nextEvidenceID != evidenceCount) throw new InvalidDataException($"Weft evidence cursor {_nextEvidenceID} does not match count {evidenceCount}");
        for (int i = 0; i < evidenceCount; i++)
        {
            TraceEvidence evidence = new(reader.Str(), reader.Bytes());
            _evidence.Add(evidence);
            _loom.SpliceEvent(evidence.Bytes, i, weight: 1, trailingBarriers: 0);
        }
        _knots.Clear();
        int knotCount = ReadCount(reader, "Weft knot");
        for (int i = 0; i < knotCount; i++)
        {
            WeftKnot knot = LoadKnot(reader);
            if (!_knots.TryAdd(knot.ID, knot)) throw new InvalidDataException($"duplicate Weft knot {knot.ID}");
        }
        _programs.Clear();
        if (version >= 2)
        {
            int programCount = ReadCount(reader, "Weft induced program");
            for (int i = 0; i < programCount; i++)
            {
                WeftProgram program = WeftProgramCodec.Load(reader);
                if (!TryReadProgramIdentity(program.Name, out WeftKnotID id, out long iterations)) throw new InvalidDataException($"invalid induced Weft program '{program.Name}'");
                if (!_knots.TryGetValue(id, out WeftKnot? knot)) throw new InvalidDataException($"induced Weft program '{program.Name}' references absent knot {id}");
                WeftProgram expected = knot.CreateProgram(iterations);
                if (!WeftProgramCodec.Equals(in expected, in program)) throw new InvalidDataException($"induced Weft program '{program.Name}' does not match knot {id}");
                if (!_programs.TryAdd(program.Name, program)) throw new InvalidDataException($"duplicate induced Weft program '{program.Name}'");
            }
        }
    }

    public void Dispose() => _loom.Dispose();

    private bool TryInduceKnot(in RePairResult grammar, in CountSlots.Tower tower, out WeftKnot? knot)
    {
        knot = null;
        if (tower.Height < 2) return false;
        byte[] baseBody = tower.Base.IsTerminal
            ? [(byte)tower.Base.Value]
            : Reconstruct.Expand(grammar.Rules, [tower.Base]);
        if (baseBody.Length == 0) return false;
        int period = FindPrimitivePeriod(baseBody);
        byte[] body = baseBody.AsSpan(0, period).ToArray();
        for (int i = 0; i < body.Length; i++)
        {
            if (!Bios.TryOpcode(body[i], out Opcodes opcode) || opcode == Opcodes.Cond) return false;
        }

        string[] sources = FindSources(grammar, tower);
        if (sources.Length < 2) return false;
        long baseIterations = baseBody.Length / body.Length;
        long[] iterations = new long[tower.Chain.Length];
        WeftTowerReproduction[] rows = new WeftTowerReproduction[tower.Chain.Length];
        for (int i = 0; i < tower.Chain.Length; i++)
        {
            iterations[i] = checked(baseIterations << (i + 1));
            byte[] expected = Reconstruct.Expand(grammar.Rules, [new Symbol(grammar.AlphabetSize + (uint)tower.Chain[i])]);
            ExecResult actual = ExecuteBody(body, iterations[i]);
            bool byteExact = actual.Trace.AsSpan().SequenceEqual(expected);
            bool fuelExact = actual.FuelLeft == 0 && !actual.Halted;
            rows[i] = new WeftTowerReproduction(i + 1, iterations[i], expected.Length, actual.Trace.Length, byteExact, fuelExact);
            if (!byteExact || !fuelExact) return false;
        }

        bool shuffledAccepted = VerifyShuffledCounts(body, iterations, grammar, tower);
        if (shuffledAccepted) return false;
        long towerRent = checked(tower.Height * 2L * SYMBOL_RENT_MBITS);
        long knotRent = checked(KNOT_HEADER_MBITS + body.Length * 8_000L + CountBits(iterations[^1]) * 1_000L);
        long savings = towerRent - knotRent;
        if (savings <= 0) return false;
        (int minimumStack, int stackDelta) = ReadEffect(body);
        WeftKnotID id = new(HashBody(body));
        WeftKnotReceipt receipt = new(id, towerRent, knotRent, savings, sources.Length, shuffledAccepted, rows);
        knot = new WeftKnot(id, body, minimumStack, stackDelta, iterations, sources, receipt);
        return true;
    }

    private string[] FindSources(in RePairResult grammar, in CountSlots.Tower tower)
    {
        byte[] deepest = Reconstruct.Expand(grammar.Rules, [new Symbol(grammar.AlphabetSize + (uint)tower.Chain[^1])]);
        SortedSet<string> sources = new(StringComparer.Ordinal);
        foreach (TraceEvidence evidence in _evidence)
            if (Contains(evidence.Bytes, deepest)) sources.Add(evidence.Source);
        return sources.ToArray();
    }

    private static bool VerifyShuffledCounts(byte[] body, long[] iterations, in RePairResult grammar, in CountSlots.Tower tower)
    {
        if (iterations.Length < 2) return false;
        for (int i = 0; i < iterations.Length; i++)
        {
            long shuffled = iterations[(i + 1) % iterations.Length];
            byte[] expected = Reconstruct.Expand(grammar.Rules, [new Symbol(grammar.AlphabetSize + (uint)tower.Chain[i])]);
            ExecResult actual = ExecuteBody(body, shuffled);
            if (!actual.Trace.AsSpan().SequenceEqual(expected)) return false;
        }
        return true;
    }

    private static ExecResult ExecuteBody(byte[] body, long iterations)
    {
        long fuelLong = checked(iterations * (body.Length + 1L));
        if (fuelLong > int.MaxValue) throw new OverflowException("verified knot Fuel exceeds Int32");
        Symbol[] pattern = new Symbol[body.Length + 1];
        for (int i = 0; i < body.Length; i++) pattern[i] = Symbol.Terminal(body[i]);
        pattern[^1] = new Symbol(Symbol.FirstNonterminal);
        GrammarRule rule = new(GrammarRule.ComputeId(pattern), pattern, new Mbits(256));
        return new TapeVm([rule]).Run([new Symbol(Symbol.FirstNonterminal)], (int)fuelLong);
    }

    private static (int MinimumStack, int StackDelta) ReadEffect(byte[] body)
    {
        int delta = 0;
        int minimum = 0;
        foreach (byte operation in body)
        {
            Bios.TryOpcode(operation, out Opcodes opcode);
            StackEffect effect = Bios.Effect(opcode);
            minimum = Math.Max(minimum, effect.MinReq - delta);
            delta += effect.DeltaH;
        }
        return (minimum, delta);
    }

    private static int FindPrimitivePeriod(byte[] bytes)
    {
        for (int period = 1; period <= bytes.Length; period++)
        {
            if (bytes.Length % period != 0) continue;
            bool matches = true;
            for (int i = period; i < bytes.Length; i++)
                if (bytes[i] != bytes[i % period]) { matches = false; break; }
            if (matches) return period;
        }
        return bytes.Length;
    }

    private static bool Contains(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length) return false;
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
            if (haystack.Slice(i, needle.Length).SequenceEqual(needle)) return true;
        return false;
    }

    private static int CountBits(long value)
    {
        int bits = 0;
        do { bits++; value >>= 1; } while (value > 0);
        return bits;
    }

    private static ulong HashBody(ReadOnlySpan<byte> body)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte value in body) { hash ^= value; hash *= 1099511628211UL; }
        return hash;
    }

    private static bool TryReadProgramIdentity(string name, out WeftKnotID id, out long iterations)
    {
        id = default;
        iterations = 0;
        const string prefix = "knot-";
        int iterationMarker = name.IndexOf("-i", prefix.Length, StringComparison.Ordinal);
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || iterationMarker < 0) return false;
        ReadOnlySpan<char> idSpan = name.AsSpan(prefix.Length, iterationMarker - prefix.Length);
        ReadOnlySpan<char> iterationSpan = name.AsSpan(iterationMarker + 2);
        if (!ulong.TryParse(idSpan, System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out ulong value)) return false;
        if (!long.TryParse(iterationSpan, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out iterations) || iterations <= 0) return false;
        id = new WeftKnotID(value);
        return true;
    }

    private static void SaveKnot(CkptWriter writer, WeftKnot knot)
    {
        writer.U64(knot.ID.Value);
        writer.Bytes(knot.Body);
        writer.I32(knot.MinimumStack);
        writer.I32(knot.StackDelta);
        writer.I32(knot.VerifiedIterations.Length);
        foreach (long iterations in knot.VerifiedIterations) writer.I64(iterations);
        writer.I32(knot.Sources.Length);
        foreach (string source in knot.Sources) writer.Str(source);
        writer.I64(knot.Receipt.TowerRentMbits);
        writer.I64(knot.Receipt.KnotRentMbits);
        writer.I64(knot.Receipt.SavingsMbits);
        writer.Bool(knot.Receipt.ShuffledAccepted);
        writer.I32(knot.Receipt.Reproductions.Length);
        foreach (WeftTowerReproduction row in knot.Receipt.Reproductions)
        {
            writer.I32(row.Height);
            writer.I64(row.Iterations);
            writer.I32(row.ExpectedBytes);
            writer.I32(row.ActualBytes);
            writer.Bool(row.ByteExact);
            writer.Bool(row.FuelExact);
        }
    }

    private static WeftKnot LoadKnot(CkptReader reader)
    {
        WeftKnotID id = new(reader.U64());
        byte[] body = reader.Bytes();
        int minimumStack = reader.I32();
        int stackDelta = reader.I32();
        long[] iterations = new long[ReadCount(reader, "verified iteration")];
        for (int i = 0; i < iterations.Length; i++) iterations[i] = reader.I64();
        string[] sources = new string[ReadCount(reader, "knot source")];
        for (int i = 0; i < sources.Length; i++) sources[i] = reader.Str();
        long towerRent = reader.I64();
        long knotRent = reader.I64();
        long savings = reader.I64();
        bool shuffledAccepted = reader.Bool();
        WeftTowerReproduction[] rows = new WeftTowerReproduction[ReadCount(reader, "reproduction")];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = new WeftTowerReproduction(reader.I32(), reader.I64(), reader.I32(), reader.I32(), reader.Bool(), reader.Bool());
        WeftKnotReceipt receipt = new(id, towerRent, knotRent, savings, sources.Length, shuffledAccepted, rows);
        return new WeftKnot(id, body, minimumStack, stackDelta, iterations, sources, receipt);
    }

    private static int ReadCount(CkptReader reader, string noun)
    {
        int count = reader.I32();
        if (count < 0 || count > 1_000_000) throw new InvalidDataException($"invalid {noun} count {count}");
        return count;
    }

    private readonly record struct TraceEvidence(string Source, byte[] Bytes);
}
