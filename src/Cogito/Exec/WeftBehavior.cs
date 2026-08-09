namespace Cogito.Exec;

using Cogito.Grammar;

public enum WeftExecutionOutcomes : byte
{
    Halted,
    FuelExhausted,
}

public enum WeftFuelBands : byte
{
    UpTo8,
    UpTo32,
    UpTo128,
    UpTo512,
    Exhausted,
}

public readonly record struct WeftBehaviorRun(
    WeftExecutionOutcomes Outcome,
    WeftNumber[] Data,
    WeftFuelBands FuelBand,
    int FuelSpent);

public sealed class WeftBehaviorCertificate : IEquatable<WeftBehaviorCertificate>
{
    internal WeftBehaviorCertificate(ulong index, WeftBehaviorRun[] runs)
    {
        Index = index;
        Runs = runs;
    }

    public ulong Index { get; }
    public WeftBehaviorRun[] Runs { get; }

    public bool Equals(WeftBehaviorCertificate? other)
    {
        if (other is null || Runs.Length != other.Runs.Length) return false;
        for (int i = 0; i < Runs.Length; i++)
        {
            WeftBehaviorRun left = Runs[i];
            WeftBehaviorRun right = other.Runs[i];
            if (left.Outcome != right.Outcome || left.FuelBand != right.FuelBand) return false;
            if (!left.Data.AsSpan().SequenceEqual(right.Data)) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is WeftBehaviorCertificate other && Equals(other);
    public override int GetHashCode() => Index.GetHashCode();
}

public sealed class WeftBehaviorClass
{
    internal WeftBehaviorClass(WeftBehaviorCertificate certificate, WeftProgram representative, int members)
    {
        Certificate = certificate;
        Representative = representative;
        Members = members;
    }

    public WeftBehaviorCertificate Certificate { get; }
    public WeftProgram Representative { get; internal set; }
    public int Members { get; internal set; }
}

/// Behavioral content-addressing for executable Weft programs. The hash is an index only; the complete canonical
/// input × Fuel vector decides equality, so a collision cannot counterfeit a behavior class.
public sealed class WeftBehaviorStore
{
    private const int STATE_VERSION = 1;

    private static readonly WeftNumber[][] CanonicalData =
    [
        [],
        [WeftNumber.Zero],
        [WeftNumber.One],
        [WeftNumber.FromInt64(-1)],
        [WeftNumber.Zero, WeftNumber.One],
        [WeftNumber.One, WeftNumber.Zero],
        [WeftNumber.FromInt64(2), WeftNumber.FromInt64(3)],
        [WeftNumber.FromInt64(-2), WeftNumber.FromInt64(3)],
        [WeftNumber.FromFloat64(0.5)],
        [WeftNumber.FromFloat64(1.5), WeftNumber.FromFloat64(-2.25)],
        [WeftNumber.One, WeftNumber.FromFloat64(1.0)],
    ];

    private static readonly int[] CanonicalFuel = [32, 128, 512];

    private readonly Dictionary<ulong, List<WeftBehaviorClass>> _classes = new();
    private int _members;

    public int ClassCount { get; private set; }
    public int MemberCount => _members;

    public IEnumerable<WeftBehaviorClass> Classes
    {
        get
        {
            foreach (ulong index in _classes.Keys.Order())
                foreach (WeftBehaviorClass behaviorClass in _classes[index])
                    yield return behaviorClass;
        }
    }

    public WeftBehaviorCertificate Certify(in WeftProgram program)
    {
        WeftBehaviorRun[] runs = new WeftBehaviorRun[CanonicalData.Length * CanonicalFuel.Length];
        TapeVm vm = new(program.Rules);
        int cursor = 0;
        for (int dataIndex = 0; dataIndex < CanonicalData.Length; dataIndex++)
        {
            for (int fuelIndex = 0; fuelIndex < CanonicalFuel.Length; fuelIndex++)
            {
                int fuel = CanonicalFuel[fuelIndex];
                ExecResult result = vm.Run(program.Start, fuel, CanonicalData[dataIndex]);
                int spent = result.FuelSpent(fuel);
                WeftExecutionOutcomes outcome = result.Halted ? WeftExecutionOutcomes.Halted : WeftExecutionOutcomes.FuelExhausted;
                runs[cursor++] = new WeftBehaviorRun(outcome, result.Data, ReadFuelBand(spent, result.Halted), spent);
            }
        }
        return new WeftBehaviorCertificate(HashRuns(runs), runs);
    }

    public bool Admit(in WeftProgram program, out WeftBehaviorClass behaviorClass)
    {
        WeftBehaviorCertificate certificate = Certify(program);
        if (!_classes.TryGetValue(certificate.Index, out List<WeftBehaviorClass>? bucket))
        {
            bucket = new List<WeftBehaviorClass>();
            _classes.Add(certificate.Index, bucket);
        }
        foreach (WeftBehaviorClass existing in bucket)
        {
            if (!existing.Certificate.Equals(certificate)) continue;
            existing.Members++;
            _members++;
            if (MeasureProgram(program) < MeasureProgram(existing.Representative)) existing.Representative = program;
            behaviorClass = existing;
            return false;
        }
        behaviorClass = new WeftBehaviorClass(certificate, program, members: 1);
        bucket.Add(behaviorClass);
        ClassCount++;
        _members++;
        return true;
    }

    public bool Contains(in WeftBehaviorCertificate certificate)
    {
        if (!_classes.TryGetValue(certificate.Index, out List<WeftBehaviorClass>? bucket)) return false;
        foreach (WeftBehaviorClass behaviorClass in bucket)
            if (behaviorClass.Certificate.Equals(certificate)) return true;
        return false;
    }

    public WeftProgram GetRepresentative(int ordinal)
    {
        if (ordinal < 0 || ordinal >= ClassCount) throw new ArgumentOutOfRangeException(nameof(ordinal));
        int cursor = 0;
        foreach (WeftBehaviorClass behaviorClass in Classes)
        {
            if (cursor++ == ordinal) return behaviorClass.Representative;
        }
        throw new InvalidOperationException("behavior class census changed during lookup");
    }

    public void Save(CkptWriter writer)
    {
        writer.I32(STATE_VERSION);
        writer.I32(ClassCount);
        writer.I32(_members);
        foreach (WeftBehaviorClass behaviorClass in Classes)
        {
            writer.U64(behaviorClass.Certificate.Index);
            writer.I32(behaviorClass.Members);
            SaveCertificate(writer, behaviorClass.Certificate);
            WeftProgramCodec.Save(writer, behaviorClass.Representative);
        }
    }

    public void Load(CkptReader reader)
    {
        int version = reader.I32();
        if (version != STATE_VERSION) throw new InvalidDataException($"unsupported Weft behavior state {version}");
        int classCount = ReadCount(reader, "behavior class");
        int members = ReadCount(reader, "behavior member");
        _classes.Clear();
        ClassCount = 0;
        _members = 0;
        for (int i = 0; i < classCount; i++)
        {
            ulong index = reader.U64();
            int classMembers = ReadCount(reader, "class member");
            WeftBehaviorCertificate certificate = LoadCertificate(reader, index);
            if (certificate.Index != HashRuns(certificate.Runs)) throw new InvalidDataException("Weft behavior certificate hash mismatch");
            WeftProgram representative = WeftProgramCodec.Load(reader);
            if (!_classes.TryGetValue(index, out List<WeftBehaviorClass>? bucket))
            {
                bucket = new List<WeftBehaviorClass>();
                _classes.Add(index, bucket);
            }
            foreach (WeftBehaviorClass existing in bucket)
                if (existing.Certificate.Equals(certificate)) throw new InvalidDataException("duplicate Weft behavior certificate");
            bucket.Add(new WeftBehaviorClass(certificate, representative, classMembers));
            ClassCount++;
            _members += classMembers;
        }
        if (_members != members) throw new InvalidDataException($"Weft behavior member census mismatch: stored {members}, loaded {_members}");
    }

    private static int MeasureProgram(in WeftProgram program)
    {
        int symbols = program.Start.Length;
        foreach (GrammarRule rule in program.Rules) symbols = checked(symbols + rule.Pattern.Length);
        return symbols;
    }

    private static WeftFuelBands ReadFuelBand(int spent, bool halted)
    {
        if (!halted) return WeftFuelBands.Exhausted;
        if (spent <= 8) return WeftFuelBands.UpTo8;
        if (spent <= 32) return WeftFuelBands.UpTo32;
        if (spent <= 128) return WeftFuelBands.UpTo128;
        return WeftFuelBands.UpTo512;
    }

    private static ulong HashRuns(WeftBehaviorRun[] runs)
    {
        ulong hash = 14695981039346656037UL;
        foreach (WeftBehaviorRun run in runs)
        {
            AddHash(ref hash, (ulong)run.Outcome);
            AddHash(ref hash, (ulong)run.FuelBand);
            AddHash(ref hash, (ulong)run.Data.Length);
            foreach (WeftNumber value in run.Data)
            {
                AddHash(ref hash, (ulong)value.Kind);
                AddHash(ref hash, value.Bits);
            }
        }
        return hash;
    }

    private static void AddHash(ref ulong hash, ulong value)
    {
        for (int shift = 0; shift < 64; shift += 8)
        {
            hash ^= (byte)(value >> shift);
            hash *= 1099511628211UL;
        }
    }

    private static void SaveCertificate(CkptWriter writer, WeftBehaviorCertificate certificate)
    {
        writer.I32(certificate.Runs.Length);
        foreach (WeftBehaviorRun run in certificate.Runs)
        {
            writer.U8((byte)run.Outcome);
            writer.U8((byte)run.FuelBand);
            writer.I32(run.FuelSpent);
            writer.I32(run.Data.Length);
            foreach (WeftNumber value in run.Data)
            {
                writer.U8((byte)value.Kind);
                writer.U64(value.Bits);
            }
        }
    }

    private static WeftBehaviorCertificate LoadCertificate(CkptReader reader, ulong index)
    {
        WeftBehaviorRun[] runs = new WeftBehaviorRun[ReadCount(reader, "behavior run")];
        for (int i = 0; i < runs.Length; i++)
        {
            WeftExecutionOutcomes outcome = (WeftExecutionOutcomes)reader.U8();
            WeftFuelBands band = (WeftFuelBands)reader.U8();
            if (!Enum.IsDefined(outcome) || !Enum.IsDefined(band)) throw new InvalidDataException("unknown Weft behavior enum value");
            int spent = reader.I32();
            WeftNumber[] data = new WeftNumber[ReadCount(reader, "behavior stack")];
            for (int d = 0; d < data.Length; d++)
            {
                WeftNumberKinds kind = (WeftNumberKinds)reader.U8();
                ulong bits = reader.U64();
                if (!WeftNumber.TryCreateCanonical(kind, bits, out data[d]))
                    throw new InvalidDataException($"non-canonical Weft {kind} value 0x{bits:x16}");
            }
            runs[i] = new WeftBehaviorRun(outcome, data, band, spent);
        }
        return new WeftBehaviorCertificate(index, runs);
    }

    private static int ReadCount(CkptReader reader, string noun)
    {
        int count = reader.I32();
        if (count < 0 || count > 1_000_000) throw new InvalidDataException($"invalid {noun} count {count}");
        return count;
    }
}

internal static class WeftProgramCodec
{
    public static bool Equals(in WeftProgram left, in WeftProgram right)
    {
        if (left.Name != right.Name || left.Note != right.Note || left.Source != right.Source || left.Fuel != right.Fuel) return false;
        if (!left.Start.AsSpan().SequenceEqual(right.Start)) return false;
        if (!left.RuleNames.AsSpan().SequenceEqual(right.RuleNames)) return false;
        if (left.Rules.Length != right.Rules.Length || left.DirectRuleBodies.Length != right.DirectRuleBodies.Length) return false;
        for (int i = 0; i < left.Rules.Length; i++)
        {
            if (left.Rules[i].Kind != right.Rules[i].Kind) return false;
            if (!left.Rules[i].Pattern.AsSpan().SequenceEqual(right.Rules[i].Pattern)) return false;
        }
        for (int i = 0; i < left.DirectRuleBodies.Length; i++)
            if (!left.DirectRuleBodies[i].AsSpan().SequenceEqual(right.DirectRuleBodies[i])) return false;
        return true;
    }

    public static void Save(CkptWriter writer, in WeftProgram program)
    {
        writer.Str(program.Name);
        writer.Str(program.Note);
        writer.Str(program.Source);
        writer.I32(program.Fuel);
        writer.I32(program.Start.Length);
        foreach (Symbol symbol in program.Start) writer.U32(symbol.Value);
        writer.I32(program.Rules.Length);
        foreach (GrammarRule rule in program.Rules)
        {
            if (rule.Kind != RuleBodyKind.Expansion) throw new InvalidDataException("Weft programs persist executable expansion rules only");
            writer.I32(rule.Pattern.Length);
            foreach (Symbol symbol in rule.Pattern) writer.U32(symbol.Value);
            writer.I64(rule.Cost.Value);
        }
        writer.I32(program.RuleNames.Length);
        foreach (string name in program.RuleNames) writer.Str(name);
    }

    public static WeftProgram Load(CkptReader reader)
    {
        string name = reader.Str();
        string note = reader.Str();
        string source = reader.Str();
        int fuel = reader.I32();
        if (fuel <= 0) throw new InvalidDataException($"invalid Weft Fuel {fuel}");
        Symbol[] start = new Symbol[ReadCount(reader, "start symbol")];
        for (int i = 0; i < start.Length; i++) start[i] = new Symbol(reader.U32());
        GrammarRule[] rules = new GrammarRule[ReadCount(reader, "program rule")];
        byte[][] directBodies = new byte[rules.Length][];
        for (int i = 0; i < rules.Length; i++)
        {
            Symbol[] pattern = new Symbol[ReadCount(reader, "rule symbol")];
            List<byte> direct = new(pattern.Length);
            for (int p = 0; p < pattern.Length; p++)
            {
                pattern[p] = new Symbol(reader.U32());
                if (pattern[p].IsTerminal) direct.Add((byte)pattern[p].Value);
            }
            rules[i] = new GrammarRule(GrammarRule.ComputeId(pattern), pattern, new Mbits(reader.I64()));
            directBodies[i] = direct.ToArray();
        }
        string[] ruleNames = new string[ReadCount(reader, "rule name")];
        if (ruleNames.Length != rules.Length) throw new InvalidDataException("Weft rule-name count does not match rule count");
        for (int i = 0; i < ruleNames.Length; i++) ruleNames[i] = reader.Str();
        return new WeftProgram(name, note, source, start, rules, fuel, ruleNames, directBodies);
    }

    private static int ReadCount(CkptReader reader, string noun)
    {
        int count = reader.I32();
        if (count < 0 || count > 1_000_000) throw new InvalidDataException($"invalid {noun} count {count}");
        return count;
    }
}
