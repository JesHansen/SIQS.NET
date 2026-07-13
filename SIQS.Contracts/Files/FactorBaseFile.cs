using System.Globalization;
using System.Numerics;
using System.Text;
using SIQS.Contracts.Text;

namespace SIQS.Contracts.Files;

/// <summary>A parsed <c>factor_base.txt</c>: its metadata header plus the ordered prime entries.</summary>
public sealed record FactorBaseDocument
{
    public FactorBaseDocument(FactorBaseMetadata Metadata, IReadOnlyList<FactorBaseEntry> Entries)
    {
        this.Metadata = Metadata;
        _entries = Array.AsReadOnly(Entries.ToArray());
    }

    public FactorBaseMetadata Metadata { get; }
    private IReadOnlyList<FactorBaseEntry> _entries;
    public IReadOnlyList<FactorBaseEntry> Entries
    {
        get => _entries;
        init => _entries = Array.AsReadOnly(value.ToArray());
    }
}

/// <summary>
/// Reads and writes <c>factor_base.txt</c> (<see cref="FileFormats.FactorBaseV1"/>). This is the
/// shared contract: the Factorbase phase writes it, while Sieving, SquareRoot and the pipeline
/// read it, so the serializer lives in the common contracts project. Output uses LF line endings
/// and base-10 decimal integers for deterministic, byte-stable files.
/// </summary>
public static class FactorBaseFile
{
    private const string ColumnsValue = "index,prime,root1,root2,logp";

    /// <summary>Serializes a document to its full UTF-8 text form.</summary>
    public static string Write(FactorBaseDocument document)
    {
        var meta = document.Metadata;
        var sb = new StringBuilder();
        sb.Append(MetadataFormat.Comment("format", FileFormats.FactorBaseV1)).Append('\n');
        sb.Append(MetadataFormat.Comment("target_n", meta.TargetN.ToString(CultureInfo.InvariantCulture))).Append('\n');
        sb.Append(MetadataFormat.Comment("multiplier", meta.Multiplier.ToString(CultureInfo.InvariantCulture))).Append('\n');
        sb.Append(MetadataFormat.Comment("scaled_n", meta.ScaledN.ToString(CultureInfo.InvariantCulture))).Append('\n');
        sb.Append(MetadataFormat.Comment("bound", meta.Bound.ToString(CultureInfo.InvariantCulture))).Append('\n');
        sb.Append(MetadataFormat.Comment("log_scale", meta.LogScale.ToString("R", CultureInfo.InvariantCulture))).Append('\n');
        sb.Append(MetadataFormat.Comment("columns", ColumnsValue)).Append('\n');
        sb.Append(ColumnsValue).Append('\n');

        foreach (var e in document.Entries.OrderBy(e => e.Prime))
        {
            sb.Append(Csv.WriteLine(new[]
            {
                e.Index.ToString(CultureInfo.InvariantCulture),
                e.Prime.ToString(CultureInfo.InvariantCulture),
                e.Root1.ToString(CultureInfo.InvariantCulture),
                e.Root2.ToString(CultureInfo.InvariantCulture),
                e.LogP.ToString(CultureInfo.InvariantCulture),
            })).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>Parses full <c>factor_base.txt</c> text into a document.</summary>
    public static FactorBaseDocument Parse(string text)
    {
        var lines = text.Split('\n');
        var meta = MetadataFormat.ParseAll(lines);

        var format = Require(meta, "format");
        if (format != FileFormats.FactorBaseV1)
        {
            throw new FormatException($"Unexpected factor base format '{format}'.");
        }

        var metadata = new FactorBaseMetadata(
            TargetN: BigInteger.Parse(Require(meta, "target_n"), CultureInfo.InvariantCulture),
            Multiplier: BigInteger.Parse(Require(meta, "multiplier"), CultureInfo.InvariantCulture),
            ScaledN: BigInteger.Parse(Require(meta, "scaled_n"), CultureInfo.InvariantCulture),
            Bound: long.Parse(Require(meta, "bound"), CultureInfo.InvariantCulture),
            LogScale: double.Parse(Require(meta, "log_scale"), CultureInfo.InvariantCulture));

        var entries = new List<FactorBaseEntry>();
        foreach (var line in lines)
        {
            if (line.Length == 0 || MetadataFormat.IsComment(line))
            {
                continue;
            }

            var fields = Csv.ParseLine(line);
            if (fields.Count == 0 || fields[0] == "index")
            {
                continue; // header row
            }

            entries.Add(new FactorBaseEntry(
                Index: int.Parse(fields[0], CultureInfo.InvariantCulture),
                Prime: long.Parse(fields[1], CultureInfo.InvariantCulture),
                Root1: long.Parse(fields[2], CultureInfo.InvariantCulture),
                Root2: long.Parse(fields[3], CultureInfo.InvariantCulture),
                LogP: int.Parse(fields[4], CultureInfo.InvariantCulture)));
        }

        return new FactorBaseDocument(metadata, entries);
    }

    private static string Require(IReadOnlyDictionary<string, string> meta, string key)
        => meta.TryGetValue(key, out var value)
            ? value
            : throw new FormatException($"factor_base.txt is missing required metadata key '{key}'.");
}
