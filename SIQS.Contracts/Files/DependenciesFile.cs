using System.Globalization;
using System.Numerics;
using System.Text;
using SIQS.Contracts.Text;

namespace SIQS.Contracts.Files;

/// <summary>A parsed <c>dependencies.txt</c>: metadata plus the nullspace dependencies.</summary>
public sealed record DependenciesDocument
{
    public DependenciesDocument(BigInteger TargetN, BigInteger Multiplier, BigInteger ScaledN, int RowCount, int ColumnCount, IReadOnlyList<DependencyRecord> Dependencies)
    {
        this.TargetN = TargetN;
        this.Multiplier = Multiplier;
        this.ScaledN = ScaledN;
        this.RowCount = RowCount;
        this.ColumnCount = ColumnCount;
        this.Dependencies = Array.AsReadOnly(Dependencies.ToArray());
    }

    public BigInteger TargetN { get; }
    public BigInteger Multiplier { get; }
    public BigInteger ScaledN { get; }
    public int RowCount { get; }
    public int ColumnCount { get; }
    public IReadOnlyList<DependencyRecord> Dependencies { get; }
}

/// <summary>
/// Reads and writes <c>dependencies.txt</c> (<see cref="FileFormats.DependenciesV1"/>), produced
/// by linear algebra and consumed by the SquareRoot phase. Each dependency lists the original
/// zero-based matrix row ids and the matching filtered relation ids.
/// </summary>
public static class DependenciesFile
{
    private const string ColumnsValue = "dependency_id,row_ids,relation_ids";

    public static string Write(DependenciesDocument document)
    {
        var sb = new StringBuilder();
        sb.Append(MetadataFormat.Comment("format", FileFormats.DependenciesV1)).Append('\n');
        sb.Append(MetadataFormat.Comment("target_n", Dec(document.TargetN))).Append('\n');
        sb.Append(MetadataFormat.Comment("multiplier", Dec(document.Multiplier))).Append('\n');
        sb.Append(MetadataFormat.Comment("scaled_n", Dec(document.ScaledN))).Append('\n');
        sb.Append(MetadataFormat.Comment("row_count", document.RowCount.ToString(CultureInfo.InvariantCulture))).Append('\n');
        sb.Append(MetadataFormat.Comment("column_count", document.ColumnCount.ToString(CultureInfo.InvariantCulture))).Append('\n');
        sb.Append(MetadataFormat.Comment("dependency_count", document.Dependencies.Count.ToString(CultureInfo.InvariantCulture))).Append('\n');
        sb.Append(MetadataFormat.Comment("columns", ColumnsValue)).Append('\n');
        sb.Append(ColumnsValue).Append('\n');

        foreach (var d in document.Dependencies)
        {
            sb.Append(Csv.WriteLine(new[]
            {
                d.DependencyId.ToString(CultureInfo.InvariantCulture),
                IntegerListFormat.WriteInts(d.RowIds),
                string.Join(' ', d.RelationIds),
            })).Append('\n');
        }

        return sb.ToString();
    }

    public static DependenciesDocument Parse(string text)
    {
        var lines = text.Split('\n');
        var meta = MetadataFormat.ParseAll(lines);

        var format = Require(meta, "format");
        if (format != FileFormats.DependenciesV1)
        {
            throw new FormatException($"Unexpected dependencies format '{format}'.");
        }

        var deps = new List<DependencyRecord>();
        foreach (var line in lines)
        {
            if (line.Length == 0 || MetadataFormat.IsComment(line))
            {
                continue;
            }

            var f = Csv.ParseLine(line);
            if (f.Count == 0 || f[0] == "dependency_id")
            {
                continue;
            }

            deps.Add(new DependencyRecord(
                DependencyId: int.Parse(f[0], CultureInfo.InvariantCulture),
                RowIds: IntegerListFormat.ParseInts(f[1]),
                RelationIds: f.Count > 2 && f[2].Length > 0
                    ? f[2].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    : Array.Empty<string>()));
        }

        return new DependenciesDocument(
            BigInteger.Parse(Require(meta, "target_n"), CultureInfo.InvariantCulture),
            BigInteger.Parse(Require(meta, "multiplier"), CultureInfo.InvariantCulture),
            BigInteger.Parse(Require(meta, "scaled_n"), CultureInfo.InvariantCulture),
            int.Parse(Require(meta, "row_count"), CultureInfo.InvariantCulture),
            int.Parse(Require(meta, "column_count"), CultureInfo.InvariantCulture),
            deps);
    }

    private static string Dec(BigInteger value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Require(IReadOnlyDictionary<string, string> meta, string key)
        => meta.TryGetValue(key, out var value)
            ? value
            : throw new FormatException($"dependencies.txt is missing required metadata key '{key}'.");
}
