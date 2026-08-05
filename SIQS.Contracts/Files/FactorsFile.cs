using System.Globalization;
using System.Numerics;
using System.Text;
using SIQS.Contracts.Text;

namespace SIQS.Contracts.Files;

/// <summary>A parsed <c>factors.txt</c>: shared metadata plus one result row per attempt.</summary>
public sealed record FactorsDocument
{
    public FactorsDocument(BigInteger TargetN, BigInteger Multiplier, BigInteger ScaledN, int DependencyCount, IReadOnlyList<FactorResultRecord> Results)
    {
        this.TargetN = TargetN;
        this.Multiplier = Multiplier;
        this.ScaledN = ScaledN;
        this.DependencyCount = DependencyCount;
        this.Results = Array.AsReadOnly(Results.ToArray());
    }

    public BigInteger TargetN { get; }
    public BigInteger Multiplier { get; }
    public BigInteger ScaledN { get; }

    /// <summary>
    /// The number of dependencies <em>attempted</em> (i.e. <c>Results.Count</c>), not the number
    /// available in <c>dependencies.txt</c>. With the SquareRoot phase's default
    /// <c>ContinueAfterFactor = false</c>, the run stops at the first factor found, so this is
    /// normally smaller than the total dependency count.
    /// </summary>
    public int DependencyCount { get; }
    public IReadOnlyList<FactorResultRecord> Results { get; }
}

/// <summary>
/// Reads and writes <c>factors.txt</c> (<see cref="FileFormats.FactorsV1"/>). Both the Factorbase
/// precheck path and the SquareRoot phase emit this file, and the pipeline reads it for the final
/// result, so the serializer lives in the shared contracts project.
/// </summary>
public static class FactorsFile
{
    private const string ColumnsValue = "dependency_id,status,gcd_minus,gcd_plus,factor1,factor2,reason,factor1_composite,factor2_composite";

    /// <summary>Serializes a factors document to its full UTF-8 text form.</summary>
    public static string Write(FactorsDocument document)
    {
        var sb = new StringBuilder();
        sb.Append(MetadataFormat.Comment("format", FileFormats.FactorsV1)).Append('\n');
        sb.Append(MetadataFormat.Comment("target_n", Dec(document.TargetN))).Append('\n');
        sb.Append(MetadataFormat.Comment("multiplier", Dec(document.Multiplier))).Append('\n');
        sb.Append(MetadataFormat.Comment("scaled_n", Dec(document.ScaledN))).Append('\n');
        sb.Append(MetadataFormat.Comment("dependency_count", document.DependencyCount.ToString(CultureInfo.InvariantCulture))).Append('\n');
        sb.Append(MetadataFormat.Comment("columns", ColumnsValue)).Append('\n');
        sb.Append(ColumnsValue).Append('\n');

        foreach (var r in document.Results)
        {
            sb.Append(Csv.WriteLine(new[]
            {
                r.DependencyId,
                SiqsTokens.ToToken(r.Status),
                OptionalDec(r.GcdMinus),
                OptionalDec(r.GcdPlus),
                OptionalDec(r.Factor1),
                OptionalDec(r.Factor2),
                r.Reason ?? string.Empty,
                OptionalBool(r.Factor1IsComposite),
                OptionalBool(r.Factor2IsComposite),
            })).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>Parses full <c>factors.txt</c> text into a document.</summary>
    public static FactorsDocument Parse(string text)
    {
        var lines = text.Split('\n');
        var meta = MetadataFormat.ParseAll(lines);

        var format = Require(meta, "format");
        if (format != FileFormats.FactorsV1)
        {
            throw new FormatException($"Unexpected factors format '{format}'.");
        }

        var results = new List<FactorResultRecord>();
        foreach (var line in lines)
        {
            if (line.Length == 0 || MetadataFormat.IsComment(line))
            {
                continue;
            }

            var fields = Csv.ParseLine(line);
            if (fields.Count == 0 || fields[0] == "dependency_id")
            {
                continue; // header row
            }

            results.Add(new FactorResultRecord(
                DependencyId: fields[0],
                Status: SiqsTokens.Parse<FactorizationStatus>(fields[1]),
                GcdMinus: OptionalParse(fields[2]),
                GcdPlus: OptionalParse(fields[3]),
                Factor1: OptionalParse(fields[4]),
                Factor2: OptionalParse(fields[5]),
                Reason: fields.Count > 6 && fields[6].Length > 0 ? fields[6] : null,
                Factor1IsComposite: fields.Count > 7 ? OptionalBoolParse(fields[7]) : null,
                Factor2IsComposite: fields.Count > 8 ? OptionalBoolParse(fields[8]) : null));
        }

        return new FactorsDocument(
            TargetN: BigInteger.Parse(Require(meta, "target_n"), CultureInfo.InvariantCulture),
            Multiplier: BigInteger.Parse(Require(meta, "multiplier"), CultureInfo.InvariantCulture),
            ScaledN: BigInteger.Parse(Require(meta, "scaled_n"), CultureInfo.InvariantCulture),
            DependencyCount: int.Parse(Require(meta, "dependency_count"), CultureInfo.InvariantCulture),
            Results: results);
    }

    private static string Dec(BigInteger value) => value.ToString(CultureInfo.InvariantCulture);

    private static string OptionalDec(BigInteger? value)
        => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;

    private static BigInteger? OptionalParse(string field)
        => field.Length == 0 ? null : BigInteger.Parse(field, CultureInfo.InvariantCulture);

    private static string OptionalBool(bool? value)
        => value.HasValue ? CounterFormat.Bool(value.Value) : string.Empty;

    private static bool? OptionalBoolParse(string field)
        => field.Length == 0 ? null : field == "true";

    private static string Require(IReadOnlyDictionary<string, string> meta, string key)
        => meta.TryGetValue(key, out var value)
            ? value
            : throw new FormatException($"factors.txt is missing required metadata key '{key}'.");
}
