using System.Globalization;
using System.Numerics;
using System.Text;
using SIQS.Contracts.Text;

namespace SIQS.Contracts.Files;

/// <summary>A parsed <c>relations_filtered.txt</c>: shared metadata plus the filtered relations.</summary>
public sealed record FilteredRelationsDocument
{
    public FilteredRelationsDocument(BigInteger TargetN, BigInteger Multiplier, BigInteger ScaledN, IReadOnlyList<FilteredRelationRecord> Relations)
    {
        this.TargetN = TargetN;
        this.Multiplier = Multiplier;
        this.ScaledN = ScaledN;
        this.Relations = Array.AsReadOnly(Relations.ToArray());
    }

    public BigInteger TargetN { get; }
    public BigInteger Multiplier { get; }
    public BigInteger ScaledN { get; }
    public IReadOnlyList<FilteredRelationRecord> Relations { get; }
}

/// <summary>
/// Reads and writes <c>relations_filtered.txt</c> (<see cref="FileFormats.FilteredRelationsV1"/>),
/// produced by Filtering and read by the SquareRoot phase. The <c>large_prime</c> column is empty
/// for ordinary full relations and required for combined partials.
/// </summary>
public static class FilteredRelationsFile
{
    private const string ColumnsValue =
        "relation_id,kind,source_relation_ids,t,sign,exponents,parity_columns,large_prime";
    private const string ColumnsValueV2 =
        "relation_id,kind,source_relation_ids,t,sign,exponents,parity_columns,large_primes";

    public static string Write(FilteredRelationsDocument document, string format = FileFormats.FilteredRelationsV1)
    {
        if (format is not (FileFormats.FilteredRelationsV1 or FileFormats.FilteredRelationsV2))
        {
            throw new ArgumentException($"Unexpected filtered relations format '{format}'.", nameof(format));
        }

        var isV2 = format == FileFormats.FilteredRelationsV2;
        var sb = new StringBuilder();
        sb.Append(MetadataFormat.Comment("format", format)).Append('\n');
        sb.Append(MetadataFormat.Comment("target_n", Dec(document.TargetN))).Append('\n');
        sb.Append(MetadataFormat.Comment("multiplier", Dec(document.Multiplier))).Append('\n');
        sb.Append(MetadataFormat.Comment("scaled_n", Dec(document.ScaledN))).Append('\n');
        sb.Append(MetadataFormat.Comment("columns", isV2 ? ColumnsValueV2 : ColumnsValue)).Append('\n');
        sb.Append(isV2 ? ColumnsValueV2 : ColumnsValue).Append('\n');

        foreach (var r in document.Relations)
        {
            sb.Append(Csv.WriteLine(new[]
            {
                r.RelationId,
                SiqsTokens.ToToken(r.Kind),
                string.Join(' ', r.SourceRelationIds),
                Dec(r.T),
                r.Sign.ToString(CultureInfo.InvariantCulture),
                ExponentMapFormat.Write(r.Exponents),
                IntegerListFormat.WriteInts(r.ParityColumns),
                isV2 ? WriteBigIntegers(r.LargePrimes)
                    : r.LargePrime.HasValue ? Dec(r.LargePrime.Value) : string.Empty,
            })).Append('\n');
        }

        return sb.ToString();
    }

    public static FilteredRelationsDocument Parse(string text)
    {
        var lines = text.Split('\n');
        var meta = MetadataFormat.ParseAll(lines);

        var format = Require(meta, "format");
        if (format is not (FileFormats.FilteredRelationsV1 or FileFormats.FilteredRelationsV2))
        {
            throw new FormatException($"Unexpected filtered relations format '{format}'.");
        }

        var isV2 = format == FileFormats.FilteredRelationsV2;
        var relations = new List<FilteredRelationRecord>();
        foreach (var line in lines)
        {
            if (line.Length == 0 || MetadataFormat.IsComment(line))
            {
                continue;
            }

            var f = Csv.ParseLine(line);
            if (f.Count == 0 || f[0] == "relation_id")
            {
                continue;
            }

            var largePrimes = isV2
                ? ParseBigIntegers(f.Count > 7 ? f[7] : string.Empty)
                : f.Count > 7 && f[7].Length > 0
                    ? new[] { BigInteger.Parse(f[7], CultureInfo.InvariantCulture) }
                    : Array.Empty<BigInteger>();

            relations.Add(new FilteredRelationRecord(
                RelationId: f[0],
                Kind: SiqsTokens.Parse<RelationKind>(f[1]),
                SourceRelationIds: f[2].Length == 0 ? Array.Empty<string>() : f[2].Split(' ', StringSplitOptions.RemoveEmptyEntries),
                T: BigInteger.Parse(f[3], CultureInfo.InvariantCulture),
                Sign: int.Parse(f[4], CultureInfo.InvariantCulture),
                Exponents: ExponentMapFormat.Parse(f[5]),
                ParityColumns: IntegerListFormat.ParseInts(f[6]),
                LargePrime: !isV2 && largePrimes.Length == 1 ? largePrimes[0] : null)
            {
                LargePrimes = largePrimes,
            });
        }

        return new FilteredRelationsDocument(
            BigInteger.Parse(Require(meta, "target_n"), CultureInfo.InvariantCulture),
            BigInteger.Parse(Require(meta, "multiplier"), CultureInfo.InvariantCulture),
            BigInteger.Parse(Require(meta, "scaled_n"), CultureInfo.InvariantCulture),
            relations);
    }

    private static string Dec(BigInteger value) => value.ToString(CultureInfo.InvariantCulture);

    private static string WriteBigIntegers(IReadOnlyList<BigInteger> values)
        => string.Join(' ', values.Select(Dec));

    private static BigInteger[] ParseBigIntegers(string text)
        => text.Length == 0
            ? Array.Empty<BigInteger>()
            : text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(v => BigInteger.Parse(v, CultureInfo.InvariantCulture))
                .ToArray();

    private static string Require(IReadOnlyDictionary<string, string> meta, string key)
        => meta.TryGetValue(key, out var value)
            ? value
            : throw new FormatException($"relations_filtered.txt is missing required metadata key '{key}'.");
}
