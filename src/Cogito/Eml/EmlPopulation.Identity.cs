namespace Cogito;

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

internal readonly record struct CohortID(string Value)
{
    public override string ToString() => Value;
}

internal readonly record struct MindID(string Value)
{
    public override string ToString() => Value;
}

internal readonly record struct MindLineageID(string Value)
{
    public override string ToString() => Value;
}

internal readonly record struct CheckpointID(string Value)
{
    public override string ToString() => Value;
}

internal readonly record struct EmlEvaluatorID(string Value)
{
    public override string ToString() => Value;
}

internal readonly record struct EmlLawClassID(string Value)
{
    public override string ToString() => Value;
}

internal readonly record struct ImportPackageID(string Value)
{
    public override string ToString() => Value;
}

internal enum EmlMindKinds
{
    Founder,
    Clone,
}

internal readonly record struct EmlMindIdentity(
    MindID Mind,
    MindLineageID Lineage,
    EmlMindKinds Kind,
    ulong SearchSeed,
    CheckpointID InitialCheckpoint);

internal sealed class EmlCohortManifest
{
    private const int FounderCount = 3;
    private const int MindCount = 4;

    private EmlCohortManifest(
        CohortID cohort,
        EmlEvaluatorID evaluator,
        string configurationDigest,
        string intakeDigest,
        List<EmlMindIdentity> minds)
    {
        Cohort = cohort;
        Evaluator = evaluator;
        ConfigurationDigest = configurationDigest;
        IntakeDigest = intakeDigest;
        Minds = minds;
        Validate();
    }

    public CohortID Cohort { get; }
    public EmlEvaluatorID Evaluator { get; }
    public string ConfigurationDigest { get; }
    public string IntakeDigest { get; }
    public IReadOnlyList<EmlMindIdentity> Minds { get; }

    public static EmlCohortManifest Create(
        EmlEvaluatorID evaluator,
        string configurationDigest,
        string intakeDigest,
        ReadOnlySpan<ulong> founderSeeds,
        ReadOnlySpan<CheckpointID> founderCheckpoints)
    {
        if (founderSeeds.Length != FounderCount || founderCheckpoints.Length != FounderCount)
            throw new ArgumentException("an EML population requires exactly three founders");
        EmlPopulationHash cohortHash = new("cogito/eml/cohort/v1");
        cohortHash.Append(evaluator.Value);
        cohortHash.Append(configurationDigest);
        cohortHash.Append(intakeDigest);
        for (int i = 0; i < FounderCount; i++)
        {
            cohortHash.Append(founderSeeds[i]);
            cohortHash.Append(founderCheckpoints[i].Value);
        }
        CohortID cohort = new(cohortHash.Finish());
        List<EmlMindIdentity> minds = new(MindCount);
        for (int i = 0; i < FounderCount; i++)
        {
            MindLineageID lineage = CreateLineage(
                evaluator,
                configurationDigest,
                intakeDigest,
                founderSeeds[i],
                founderCheckpoints[i]);
            minds.Add(new EmlMindIdentity(
                CreateMind(cohort, i),
                lineage,
                EmlMindKinds.Founder,
                founderSeeds[i],
                founderCheckpoints[i]));
        }
        EmlMindIdentity founder = minds[0];
        minds.Add(new EmlMindIdentity(
            CreateMind(cohort, FounderCount),
            founder.Lineage,
            EmlMindKinds.Clone,
            founder.SearchSeed,
            founder.InitialCheckpoint));
        return new EmlCohortManifest(cohort, evaluator, configurationDigest, intakeDigest, minds);
    }

    internal static EmlCohortManifest Restore(
        CohortID cohort,
        EmlEvaluatorID evaluator,
        string configurationDigest,
        string intakeDigest,
        List<EmlMindIdentity> minds)
        => new(cohort, evaluator, configurationDigest, intakeDigest, new List<EmlMindIdentity>(minds));

    private static MindID CreateMind(CohortID cohort, int slot)
    {
        EmlPopulationHash hash = new("cogito/eml/mind/v1");
        hash.Append(cohort.Value);
        hash.Append(slot);
        return new MindID(hash.Finish());
    }

    private static MindLineageID CreateLineage(
        EmlEvaluatorID evaluator,
        string configurationDigest,
        string intakeDigest,
        ulong seed,
        CheckpointID checkpoint)
    {
        EmlPopulationHash hash = new("cogito/eml/lineage/v1");
        hash.Append(evaluator.Value);
        hash.Append(configurationDigest);
        hash.Append(intakeDigest);
        hash.Append(seed);
        hash.Append(checkpoint.Value);
        return new MindLineageID(hash.Finish());
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(Cohort.Value)
            || string.IsNullOrWhiteSpace(Evaluator.Value)
            || string.IsNullOrWhiteSpace(ConfigurationDigest)
            || string.IsNullOrWhiteSpace(IntakeDigest)
            || Minds.Count != MindCount)
            throw new InvalidDataException("EML cohort identity is incomplete");
        HashSet<MindID> mindIDs = new();
        int founders = 0;
        int clones = 0;
        for (int i = 0; i < Minds.Count; i++)
        {
            EmlMindIdentity mind = Minds[i];
            if (!mindIDs.Add(mind.Mind)
                || string.IsNullOrWhiteSpace(mind.Lineage.Value)
                || string.IsNullOrWhiteSpace(mind.InitialCheckpoint.Value))
                throw new InvalidDataException("EML cohort repeats or omits a mind identity");
            if (mind.Kind == EmlMindKinds.Founder) founders++;
            else if (mind.Kind == EmlMindKinds.Clone) clones++;
            else throw new InvalidDataException("EML cohort carries an unknown mind kind");
        }
        if (founders != FounderCount || clones != 1)
            throw new InvalidDataException("EML cohort must contain three founders and one clone");
        EmlPopulationHash cohortHash = new("cogito/eml/cohort/v1");
        cohortHash.Append(Evaluator.Value);
        cohortHash.Append(ConfigurationDigest);
        cohortHash.Append(IntakeDigest);
        for (int i = 0; i < FounderCount; i++)
        {
            EmlMindIdentity mind = Minds[i];
            if (mind.Kind != EmlMindKinds.Founder
                || mind.Mind != CreateMind(Cohort, i)
                || mind.Lineage != CreateLineage(
                    Evaluator,
                    ConfigurationDigest,
                    IntakeDigest,
                    mind.SearchSeed,
                    mind.InitialCheckpoint))
                throw new InvalidDataException("EML founder identity does not match its genesis inputs");
            cohortHash.Append(mind.SearchSeed);
            cohortHash.Append(mind.InitialCheckpoint.Value);
        }
        string expectedCohort = cohortHash.Finish();
        cohortHash.Dispose();
        if (!string.Equals(Cohort.Value, expectedCohort, StringComparison.Ordinal))
            throw new InvalidDataException("EML cohort identity does not match its genesis inputs");
        EmlMindIdentity founder = Minds[0];
        EmlMindIdentity clone = Minds[3];
        if (clone.Kind != EmlMindKinds.Clone
            || clone.Mind != CreateMind(Cohort, FounderCount)
            || clone.Lineage != founder.Lineage
            || clone.SearchSeed != founder.SearchSeed
            || clone.InitialCheckpoint != founder.InitialCheckpoint)
            throw new InvalidDataException("EML clone must inherit the first founder's complete genesis identity");
    }
}

internal sealed class EmlPopulationHash : IDisposable
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private bool _finished;

    public EmlPopulationHash(string domain)
    {
        Append(domain);
    }

    public void Append(string value)
    {
        if (_finished) throw new InvalidOperationException("population hash is already finalized");
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        _hash.AppendData(length);
        _hash.AppendData(bytes);
    }

    public void Append(int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        _hash.AppendData(bytes);
    }

    public void Append(long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        _hash.AppendData(bytes);
    }

    public void Append(ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        _hash.AppendData(bytes);
    }

    public string Finish()
    {
        if (_finished) throw new InvalidOperationException("population hash is already finalized");
        _finished = true;
        Span<byte> digest = stackalloc byte[32];
        _hash.GetHashAndReset(digest);
        return Convert.ToHexStringLower(digest);
    }

    public void Dispose() => _hash.Dispose();
}
