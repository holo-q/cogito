namespace Cogito;

using System.Text;
using Cogito.Induct;

public enum RecursionTraceSpecies
{
    Walk,
    WeftExecution,
    EmlAction,
    Discovery,
    All,
}

public readonly record struct RecursionTowerRow(
    RecursionTraceSpecies Species,
    int PrefixPercent,
    int Events,
    int Bytes,
    int Rules,
    int Towers,
    int MaxHeight,
    long DeepestSpan,
    int CampaignTowers,
    string HeightHistogram);

public static class RecursionTowerCensus
{
    private static readonly int[] PrefixPercents = [25, 50, 75, 100];

    public static List<RecursionTowerRow> Measure(Tape tape, Journal journal, string? journalLogPath = null)
    {
        Dictionary<RecursionTraceSpecies, List<byte[]>> events = new();
        foreach (RecursionTraceSpecies species in Enum.GetValues<RecursionTraceSpecies>()) events[species] = new List<byte[]>();

        HashSet<long> weftIDs = ReadWeftIDs(journal, journalLogPath);
        List<TapeEventView> views = new(tape.GetEventViews());
        views.Sort(static (left, right) => left.Id.Value.CompareTo(right.Id.Value));
        foreach (TapeEventView view in views)
        {
            if (!tape.Resolve(view.Id, out byte[] bytes))
                throw new InvalidDataException($"tower census could not resolve {view.Id}");
            events[RecursionTraceSpecies.All].Add(bytes);
            if (view.Source.StartsWith("walk:", StringComparison.Ordinal) || view.Source is "WALK" or "SUCCESS-WALK")
                events[RecursionTraceSpecies.Walk].Add(bytes);
            if (weftIDs.Contains(view.Id.Value)) events[RecursionTraceSpecies.WeftExecution].Add(bytes);
            if (IsActionPacket(bytes)) events[RecursionTraceSpecies.EmlAction].Add(bytes);
            if (IsDiscoveryEvent(view.Source, bytes)) events[RecursionTraceSpecies.Discovery].Add(bytes);
        }

        byte[] campaign = BuildCampaignCorpus(events[RecursionTraceSpecies.Discovery]);
        int campaignTowers = campaign.Length == 0
            ? 0
            : CountSlots.Summarize(CountSlots.Scan(Engine.Induce(campaign).Result.Rules, 256)).Towers;

        List<RecursionTowerRow> rows = new();
        foreach (RecursionTraceSpecies species in Enum.GetValues<RecursionTraceSpecies>())
        {
            List<byte[]> speciesEvents = events[species];
            foreach (int percent in PrefixPercents)
            {
                int take = speciesEvents.Count == 0 ? 0 : Math.Max(1, (speciesEvents.Count * percent + 99) / 100);
                byte[] corpus = BuildCorpus(speciesEvents, take);
                RePairResult grammar = Engine.Induce(corpus).Result;
                CountSlots.Census census = CountSlots.Summarize(CountSlots.Scan(grammar.Rules, grammar.AlphabetSize));
                rows.Add(new RecursionTowerRow(
                    species,
                    percent,
                    take,
                    corpus.Length,
                    grammar.Rules.Length,
                    census.Towers,
                    census.MaxHeight,
                    census.DeepestSpan,
                    species == RecursionTraceSpecies.Discovery ? campaignTowers : 0,
                    RenderHistogram(census.HeightHistogram)));
            }
        }
        return rows;
    }

    public static string RenderTsv(List<RecursionTowerRow> rows)
    {
        StringBuilder output = new("species\tprefix_pct\tevents\tbytes\trules\ttowers\tmax_height\tdeepest_span\tcampaign_towers\theight_histogram\n");
        foreach (RecursionTowerRow row in rows)
        {
            output.Append(row.Species).Append('\t').Append(row.PrefixPercent).Append('\t')
                .Append(row.Events).Append('\t').Append(row.Bytes).Append('\t').Append(row.Rules).Append('\t')
                .Append(row.Towers).Append('\t').Append(row.MaxHeight).Append('\t').Append(row.DeepestSpan).Append('\t')
                .Append(row.CampaignTowers).Append('\t').Append(row.HeightHistogram).AppendLine();
        }
        return output.ToString();
    }

    private static HashSet<long> ReadWeftIDs(Journal journal, string? journalLogPath)
    {
        HashSet<long> ids = new();
        foreach (string line in journal.EnumerateAllLines(journalLogPath))
        {
            string[] fields = line.Split('\t');
            if (fields.Length < 3 || fields[1] != "weft" || fields[2].Length < 2 || fields[2][0] != 's') continue;
            if (long.TryParse(fields[2].AsSpan(1), out long id)) ids.Add(id);
        }
        return ids;
    }

    private static bool IsActionPacket(byte[] bytes)
    {
        ReadOnlySpan<byte> packet = bytes;
        return packet.StartsWith("ACTION "u8) || packet.StartsWith("EPISODE "u8) && packet.IndexOf("\nACTION "u8) >= 0;
    }

    private static bool IsDiscoveryEvent(string source, byte[] bytes)
        => source.StartsWith("eml", StringComparison.Ordinal)
        || bytes.AsSpan().StartsWith("LAW\t"u8);

    private static byte[] BuildCorpus(List<byte[]> events, int take)
    {
        int length = 0;
        for (int i = 0; i < take; i++) length = checked(length + events[i].Length + 1);
        byte[] corpus = new byte[length];
        int cursor = 0;
        for (int i = 0; i < take; i++)
        {
            events[i].CopyTo(corpus, cursor);
            cursor += events[i].Length;
            corpus[cursor++] = (byte)'\n';
        }
        return corpus;
    }

    private static byte[] BuildCampaignCorpus(List<byte[]> events)
    {
        List<byte[]> tokens = new();
        foreach (byte[] bytes in events)
        {
            string packet = Encoding.UTF8.GetString(bytes);
            string? token = packet switch
            {
                string text when text.Contains("ACTION counterexample", StringComparison.Ordinal) => "stress",
                string text when text.Contains("ACTION mutate", StringComparison.Ordinal) => "mutate",
                string text when text.Contains("ACTION compare", StringComparison.Ordinal) => "compare",
                string text when text.StartsWith("LAW\t", StringComparison.Ordinal) => "admit",
                _ => "delta",
            };
            tokens.Add(Encoding.ASCII.GetBytes(token));
        }
        return BuildCorpus(tokens, tokens.Count);
    }

    private static string RenderHistogram(int[] histogram)
    {
        StringBuilder output = new();
        for (int height = 0; height < histogram.Length; height++)
        {
            if (histogram[height] == 0) continue;
            if (output.Length > 0) output.Append(',');
            output.Append(height).Append(':').Append(histogram[height]);
        }
        return output.ToString();
    }
}

public sealed partial class Cortex
{
    public static int ScanRecursionTowers(string runReference, string? outputPath = null)
    {
        string? runDirectory = Cogito.Run.Resolve(runReference);
        if (runDirectory is null || !File.Exists(Path.Combine(runDirectory, Checkpoint.FileName)))
        {
            Console.Error.WriteLine($"  no {Checkpoint.FileName} under '{runReference}' — tower census requires a durable run");
            return 1;
        }

        CortexRunConfig sourceConfig = Checkpoint.PeekConfig(runDirectory);
        Cortex runtime = CreateCheckpointRuntime(sourceConfig);
        CortexRunConfig config = runtime.MountedCurriculum is null
            ? sourceConfig
            : sourceConfig with { RuntimeCurriculum = runtime.MountedCurriculum };

        using World world = new(config);
        string spanLog = Path.Combine(runDirectory, "tape.spanlog");
        if (world.Loom is not null && config.Shed && File.Exists(spanLog))
            world.Tape.MountLog(new FileStream(spanLog, FileMode.Open, FileAccess.Read));

        Checkpoint.Load(runDirectory, world.CorpusBytes, world.Pool.Count, world.Fc?.Families ?? 0,
            world.Tape, world.Journal, world.Curriculum, world.Reads, world.SelfStream,
            world.Controller, world.Metabolism, world.Memory, world.Homeo, world.Loom, world.Rhythm, runtime);

        List<RecursionTowerRow> rows = RecursionTowerCensus.Measure(world.Tape, world.Journal, Path.Combine(runDirectory, "journal.log"));
        string destination = outputPath ?? Path.Combine(runDirectory, "recursion_towers.tsv");
        File.WriteAllText(destination, RecursionTowerCensus.RenderTsv(rows));
        byte[] ron = RecursionRONCodec.EncodeTowerRows(rows);
        List<RecursionTowerRow> decoded = RecursionRONCodec.DecodeTowerRows(ron);
        if (decoded.Count != rows.Count) throw new InvalidDataException("tower census RON round-trip changed the row count");
        for (int i = 0; i < rows.Count; i++)
            if (decoded[i] != rows[i]) throw new InvalidDataException($"tower census RON round-trip changed row {i}");
        string ronPath = Path.ChangeExtension(destination, ".ron");
        File.WriteAllBytes(ronPath, ron);
        Console.WriteLine($"  tower census · {rows.Count} prefix rows → {destination} + {ronPath}");
        return 0;
    }
}
