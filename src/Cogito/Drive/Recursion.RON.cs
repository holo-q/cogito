namespace Cogito;

using Ronmamon;

public static class RecursionRONCodec
{
    private const int SchemaVersion = 1;

    public static byte[] EncodeTowerRows(List<RecursionTowerRow> rows)
    {
        RecursionRONTowerCensus document = new() { schemaVersion = SchemaVersion };
        for (int i = 0; i < rows.Count; i++)
        {
            RecursionTowerRow row = rows[i];
            document.rows.Add(new RecursionRONTowerRow
            {
                species = row.Species,
                prefixPercent = row.PrefixPercent,
                events = row.Events,
                bytes = row.Bytes,
                rules = row.Rules,
                towers = row.Towers,
                maxHeight = row.MaxHeight,
                deepestSpan = row.DeepestSpan,
                campaignTowers = row.CampaignTowers,
                heightHistogram = row.HeightHistogram,
            });
        }
        return RonSerializer.SerializeToUtf8(in document);
    }

    public static List<RecursionTowerRow> DecodeTowerRows(ReadOnlySpan<byte> bytes)
    {
        RecursionRONTowerCensus document = RonSerializer.Deserialize<RecursionRONTowerCensus>(bytes);
        if (document.schemaVersion != SchemaVersion)
            throw new InvalidDataException(
                $"unsupported recursion tower RON schema {document.schemaVersion}; expected {SchemaVersion}");
        List<RecursionTowerRow> rows = new(document.rows.Count);
        for (int i = 0; i < document.rows.Count; i++)
        {
            RecursionRONTowerRow row = document.rows[i];
            if (row.prefixPercent is < 0 or > 100
                || row.events < 0
                || row.bytes < 0
                || row.rules < 0
                || row.towers < 0
                || row.maxHeight < 0
                || row.deepestSpan < 0
                || row.campaignTowers < 0)
                throw new InvalidDataException("recursion tower RON contains an invalid negative or prefix value");
            rows.Add(new RecursionTowerRow(
                row.species,
                row.prefixPercent,
                row.events,
                row.bytes,
                row.rules,
                row.towers,
                row.maxHeight,
                row.deepestSpan,
                row.campaignTowers,
                row.heightHistogram ?? ""));
        }
        return rows;
    }
}

[RonObject]
internal partial class RecursionRONTowerCensus
{
    public int schemaVersion;
    public List<RecursionRONTowerRow> rows = new();
}

[RonObject]
internal partial class RecursionRONTowerRow
{
    public RecursionTraceSpecies species;
    public int prefixPercent;
    public int events;
    public int bytes;
    public int rules;
    public int towers;
    public int maxHeight;
    public long deepestSpan;
    public int campaignTowers;
    public string heightHistogram = "";
}
