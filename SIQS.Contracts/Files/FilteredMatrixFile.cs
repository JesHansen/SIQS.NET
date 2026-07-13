using System.Globalization;
using System.Text;
using SIQS.Contracts.Text;

namespace SIQS.Contracts.Files;

/// <summary>
/// Reads and writes <c>filtered_matrix.txt</c> (<see cref="FileFormats.FilteredMatrixV1"/>): the
/// sparse GF(2) parity matrix consumed by linear algebra. Each row lists the ascending column
/// indexes whose value is 1; an empty column list is an already-zero row.
/// </summary>
public static class FilteredMatrixFile
{
    private const string ColumnsValue = "row_id,relation_id,columns";

    public static string Write(IReadOnlyList<SparseMatrixRowRecord> rows)
    {
        var sb = new StringBuilder();
        sb.Append(MetadataFormat.Comment("format", FileFormats.FilteredMatrixV1)).Append('\n');
        sb.Append(MetadataFormat.Comment("columns", ColumnsValue)).Append('\n');
        sb.Append(ColumnsValue).Append('\n');

        foreach (var row in rows)
        {
            sb.Append(Csv.WriteLine(new[]
            {
                row.RowId.ToString(CultureInfo.InvariantCulture),
                row.RelationId,
                IntegerListFormat.WriteInts(row.Columns),
            })).Append('\n');
        }

        return sb.ToString();
    }

    public static IReadOnlyList<SparseMatrixRowRecord> Parse(string text)
    {
        var lines = text.Split('\n');
        var meta = MetadataFormat.ParseAll(lines);
        if (!meta.TryGetValue("format", out var format) || format != FileFormats.FilteredMatrixV1)
        {
            throw new FormatException($"Unexpected filtered matrix format '{(meta.GetValueOrDefault("format"))}'.");
        }

        var rows = new List<SparseMatrixRowRecord>();
        foreach (var line in lines)
        {
            if (line.Length == 0 || MetadataFormat.IsComment(line))
            {
                continue;
            }

            var f = Csv.ParseLine(line);
            if (f.Count == 0 || f[0] == "row_id")
            {
                continue;
            }

            rows.Add(new SparseMatrixRowRecord(
                RowId: int.Parse(f[0], CultureInfo.InvariantCulture),
                RelationId: f[1],
                Columns: f.Count > 2 ? IntegerListFormat.ParseInts(f[2]) : Array.Empty<int>()));
        }

        return rows;
    }
}
