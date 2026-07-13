using System.Globalization;
using System.Numerics;
using System.Text;
using SIQS.Contracts.Text;

namespace SIQS.Contracts.Files;

/// <summary>
/// Reads and writes <c>matrix_meta.txt</c> (<see cref="FileFormats.MatrixMetaV1"/>). The format is
/// a single commented format line followed by bare <c>key=value</c> rows. Produced by Filtering
/// and read by linear algebra (and the pipeline) to size the matrix.
/// </summary>
public static class MatrixMetaFile
{
    public static string Write(MatrixMetadata meta)
    {
        var sb = new StringBuilder();
        sb.Append(MetadataFormat.Comment("format", FileFormats.MatrixMetaV1)).Append('\n');
        sb.Append(MetadataFormat.KeyValue("target_n", Dec(meta.TargetN))).Append('\n');
        sb.Append(MetadataFormat.KeyValue("multiplier", Dec(meta.Multiplier))).Append('\n');
        sb.Append(MetadataFormat.KeyValue("scaled_n", Dec(meta.ScaledN))).Append('\n');
        sb.Append(MetadataFormat.KeyValue("row_count", meta.RowCount.ToString(CultureInfo.InvariantCulture))).Append('\n');
        sb.Append(MetadataFormat.KeyValue("column_count", meta.ColumnCount.ToString(CultureInfo.InvariantCulture))).Append('\n');
        sb.Append(MetadataFormat.KeyValue("factor_base_count", meta.FactorBaseCount.ToString(CultureInfo.InvariantCulture))).Append('\n');
        sb.Append(MetadataFormat.KeyValue("sign_column", meta.SignColumn.ToString(CultureInfo.InvariantCulture))).Append('\n');
        sb.Append(MetadataFormat.KeyValue("matrix_file", meta.MatrixFile)).Append('\n');
        sb.Append(MetadataFormat.KeyValue("relations_file", meta.RelationsFile)).Append('\n');
        return sb.ToString();
    }

    public static MatrixMetadata Parse(string text)
    {
        var meta = MetadataFormat.ParseAll(text.Split('\n'));
        if (!meta.TryGetValue("format", out var format) || format != FileFormats.MatrixMetaV1)
        {
            throw new FormatException($"Unexpected matrix meta format '{meta.GetValueOrDefault("format")}'.");
        }

        return new MatrixMetadata(
            TargetN: BigInteger.Parse(Require(meta, "target_n"), CultureInfo.InvariantCulture),
            Multiplier: BigInteger.Parse(Require(meta, "multiplier"), CultureInfo.InvariantCulture),
            ScaledN: BigInteger.Parse(Require(meta, "scaled_n"), CultureInfo.InvariantCulture),
            RowCount: int.Parse(Require(meta, "row_count"), CultureInfo.InvariantCulture),
            ColumnCount: int.Parse(Require(meta, "column_count"), CultureInfo.InvariantCulture),
            FactorBaseCount: int.Parse(Require(meta, "factor_base_count"), CultureInfo.InvariantCulture),
            SignColumn: int.Parse(Require(meta, "sign_column"), CultureInfo.InvariantCulture),
            MatrixFile: Require(meta, "matrix_file"),
            RelationsFile: Require(meta, "relations_file"));
    }

    private static string Dec(BigInteger value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Require(IReadOnlyDictionary<string, string> meta, string key)
        => meta.TryGetValue(key, out var value)
            ? value
            : throw new FormatException($"matrix_meta.txt is missing required metadata key '{key}'.");
}
