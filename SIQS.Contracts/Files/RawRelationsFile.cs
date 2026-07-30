using System.Globalization;
using System.Numerics;
using System.Text;
using SIQS.Contracts.Text;

namespace SIQS.Contracts.Files;

/// <summary>Metadata header shared by <c>relations_*.txt</c> and <c>partials_*.txt</c>.</summary>
public sealed record RawRelationsMetadata(
    BigInteger TargetN,
    BigInteger Multiplier,
    BigInteger ScaledN,
    long FactorBaseBound,
    long LargePrimeBound,
    long? LargePrime2Bound = null);

/// <summary>A parsed raw relations or partials file: its format string, metadata, and relations.</summary>
public sealed record RawRelationsDocument
{
    public RawRelationsDocument(string Format, RawRelationsMetadata Metadata, IReadOnlyList<RawRelationRecord> Relations)
    {
        this.Format = Format;
        this.Metadata = Metadata;
        this.Relations = Array.AsReadOnly(Relations.ToArray());
    }

    public string Format { get; }
    public RawRelationsMetadata Metadata { get; }
    public IReadOnlyList<RawRelationRecord> Relations { get; }
}

/// <summary>
/// Reads and writes the sieving phase's raw output files: <c>relations_*.txt</c>
/// (<see cref="FileFormats.RawRelationsV1"/>) and <c>partials_*.txt</c>
/// (<see cref="FileFormats.RawPartialsV1"/>). The two files share the same column layout and
/// differ only by the format string and the relation <c>kind</c>.
/// </summary>
public static class RawRelationsFile
{
    private const string ColumnsValue =
        "relation_id,kind,poly_id,a,b,c,x,t,sign,factor_exponents,parity_columns,large_prime";
    private const string ColumnsValueV2 =
        "relation_id,kind,poly_id,a,b,c,x,t,sign,factor_exponents,parity_columns,large_primes";

    /// <summary>Serializes a raw relations/partials document to its full UTF-8 text form.</summary>
    public static string Write(RawRelationsDocument document)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Write(writer, document);
        return writer.ToString();
    }

    public static void Write(TextWriter writer, RawRelationsDocument document)
    {
        if (document.Format is not (FileFormats.RawRelationsV1 or FileFormats.RawPartialsV1
            or FileFormats.RawRelationsV2 or FileFormats.RawPartialsV2))
        {
            throw new ArgumentException($"Unexpected raw relations format '{document.Format}'.", nameof(document));
        }

        var isV2 = document.Format is FileFormats.RawRelationsV2 or FileFormats.RawPartialsV2;
        var m = document.Metadata;
        WriteLine(writer, MetadataFormat.Comment("format", document.Format));
        WriteLine(writer, MetadataFormat.Comment("target_n", Dec(m.TargetN)));
        WriteLine(writer, MetadataFormat.Comment("multiplier", Dec(m.Multiplier)));
        WriteLine(writer, MetadataFormat.Comment("scaled_n", Dec(m.ScaledN)));
        WriteLine(writer, MetadataFormat.Comment("factor_base_bound", m.FactorBaseBound.ToString(CultureInfo.InvariantCulture)));
        WriteLine(writer, MetadataFormat.Comment("large_prime_bound", m.LargePrimeBound.ToString(CultureInfo.InvariantCulture)));
        if (isV2)
        {
            WriteLine(writer, MetadataFormat.Comment("large_prime2_bound",
                (m.LargePrime2Bound ?? m.LargePrimeBound).ToString(CultureInfo.InvariantCulture)));
        }

        WriteLine(writer, MetadataFormat.Comment("columns", isV2 ? ColumnsValueV2 : ColumnsValue));
        WriteLine(writer, isV2 ? ColumnsValueV2 : ColumnsValue);

        foreach (var r in document.Relations)
        {
            WriteLine(writer, Csv.WriteLine(new[]
            {
                r.RelationId,
                SiqsTokens.ToToken(r.Kind),
                r.PolyId,
                Dec(r.A),
                Dec(r.B),
                Dec(r.C),
                r.X.ToString(CultureInfo.InvariantCulture),
                Dec(r.T),
                r.Sign.ToString(CultureInfo.InvariantCulture),
                ExponentMapFormat.Write(r.FactorExponents),
                IntegerListFormat.WriteInts(r.ParityColumns),
                isV2 ? WriteBigIntegers(r.LargePrimes)
                    : r.LargePrime.HasValue ? Dec(r.LargePrime.Value) : string.Empty,
            }));
        }
    }

    /// <summary>Parses full raw relations/partials text into a document.</summary>
    public static RawRelationsDocument Parse(string text)
    {
        using var reader = new StringReader(text);
        return Parse(reader);
    }

    /// <summary>Parses full raw relations/partials text from a reader into a document.</summary>
    public static RawRelationsDocument Parse(TextReader reader)
    {
        var meta = new Dictionary<string, string>(StringComparer.Ordinal);
        var relations = new List<RawRelationRecord>();
        string? format = null;
        bool? isV2 = null;
        RawRelationsMetadata? metadata = null;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
            {
                continue;
            }

            if (MetadataFormat.TryParse(line, out var key, out var value))
            {
                meta[key] = value;
                continue;
            }

            if (IsHeaderRow(line))
            {
                continue;
            }

            EnsureHeader(meta, ref format, ref isV2, ref metadata);
            relations.Add(ParseRecord(line, isV2!.Value));
        }

        EnsureHeader(meta, ref format, ref isV2, ref metadata);
        return new RawRelationsDocument(format!, metadata!, relations);
    }

    /// <summary>
    /// Streams records with their zero-based data-row ordinals without materializing the whole
    /// document. Ordinals count data rows only (metadata comments and the header row are excluded),
    /// so an ordinal addresses the same record across repeated reads of an unchanged file.
    /// </summary>
    public static IEnumerable<(int Ordinal, RawRelationRecord Record)> EnumerateWithOrdinals(TextReader reader)
        => EnumerateCore(reader, shouldParse: null);

    /// <summary>
    /// Parses only the data rows at the given ascending ordinals, skipping other rows with a cheap
    /// line classification (no CSV or numeric parsing). Reading stops after the last requested
    /// ordinal. See <see cref="EnumerateWithOrdinals"/> for the ordinal definition.
    /// </summary>
    public static IEnumerable<(int Ordinal, RawRelationRecord Record)> ParseRecordsAt(
        TextReader reader, IEnumerable<int> ascendingOrdinals)
    {
        using var wanted = ascendingOrdinals.GetEnumerator();
        if (!wanted.MoveNext())
        {
            yield break;
        }

        var next = wanted.Current;
        foreach (var (ordinal, record) in EnumerateCore(reader, ordinal => ordinal == next))
        {
            if (ordinal != next)
            {
                continue;
            }

            yield return (ordinal, record);
            do
            {
                if (!wanted.MoveNext())
                {
                    yield break;
                }

                if (wanted.Current < next)
                {
                    throw new ArgumentException("Ordinals must be ascending.", nameof(ascendingOrdinals));
                }
            }
            while (wanted.Current == next);
            next = wanted.Current;
        }
    }

    private static IEnumerable<(int Ordinal, RawRelationRecord Record)> EnumerateCore(
        TextReader reader, Func<int, bool>? shouldParse)
    {
        var meta = new Dictionary<string, string>(StringComparer.Ordinal);
        string? format = null;
        bool? isV2 = null;
        RawRelationsMetadata? metadata = null;
        var ordinal = 0;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
            {
                continue;
            }

            if (MetadataFormat.TryParse(line, out var key, out var value))
            {
                meta[key] = value;
                continue;
            }

            if (IsHeaderRow(line))
            {
                continue;
            }

            EnsureHeader(meta, ref format, ref isV2, ref metadata);
            if (shouldParse is null || shouldParse(ordinal))
            {
                yield return (ordinal, ParseRecord(line, isV2!.Value));
            }

            ordinal++;
        }
    }

    private static bool IsHeaderRow(string line)
        => line.StartsWith("relation_id", StringComparison.Ordinal)
            && (line.Length == "relation_id".Length || line["relation_id".Length] == ',');

    private static RawRelationRecord ParseRecord(string line, bool isV2)
    {
        if (line.Contains('"'))
        {
            return ParseQuotedRecord(Csv.ParseLine(line), isV2);
        }

        var span = line.AsSpan();
        Span<Range> f = stackalloc Range[13];
        var fieldCount = span.Split(f, ',');
        if (fieldCount < 11)
        {
            throw new FormatException($"Raw relation row has {fieldCount} fields; at least 11 are required.");
        }

        var largePrimes = isV2
            ? ParseBigIntegers(fieldCount > 11 ? span[f[11]] : default)
            : fieldCount > 11 && !span[f[11]].IsEmpty
                ? new[] { BigInteger.Parse(span[f[11]], CultureInfo.InvariantCulture) }
                : Array.Empty<BigInteger>();

        return new RawRelationRecord(
            RelationId: line[f[0]],
            Kind: SiqsTokens.Parse<RelationKind>(span[f[1]]),
            PolyId: line[f[2]],
            A: BigInteger.Parse(span[f[3]], CultureInfo.InvariantCulture),
            B: BigInteger.Parse(span[f[4]], CultureInfo.InvariantCulture),
            C: BigInteger.Parse(span[f[5]], CultureInfo.InvariantCulture),
            X: long.Parse(span[f[6]], CultureInfo.InvariantCulture),
            T: BigInteger.Parse(span[f[7]], CultureInfo.InvariantCulture),
            Sign: int.Parse(span[f[8]], CultureInfo.InvariantCulture),
            FactorExponents: ExponentMapFormat.ParseVector(span[f[9]]),
            ParityColumns: IntegerListFormat.ParseParityColumns(span[f[10]]),
            LargePrime: !isV2 && largePrimes.Length == 1 ? largePrimes[0] : null)
        {
            LargePrimes = largePrimes,
        };
    }

    private static RawRelationRecord ParseQuotedRecord(IReadOnlyList<string> f, bool isV2)
    {
        var largePrimes = isV2
            ? ParseBigIntegers(f.Count > 11 ? f[11] : string.Empty)
            : f.Count > 11 && f[11].Length > 0
                ? new[] { BigInteger.Parse(f[11], CultureInfo.InvariantCulture) }
                : Array.Empty<BigInteger>();

        return new RawRelationRecord(
            RelationId: f[0],
            Kind: SiqsTokens.Parse<RelationKind>(f[1]),
            PolyId: f[2],
            A: BigInteger.Parse(f[3], CultureInfo.InvariantCulture),
            B: BigInteger.Parse(f[4], CultureInfo.InvariantCulture),
            C: BigInteger.Parse(f[5], CultureInfo.InvariantCulture),
            X: long.Parse(f[6], CultureInfo.InvariantCulture),
            T: BigInteger.Parse(f[7], CultureInfo.InvariantCulture),
            Sign: int.Parse(f[8], CultureInfo.InvariantCulture),
            FactorExponents: ExponentMapFormat.Parse(f[9]),
            ParityColumns: IntegerListFormat.ParseInts(f[10]),
            LargePrime: !isV2 && largePrimes.Length == 1 ? largePrimes[0] : null)
        {
            LargePrimes = largePrimes,
        };
    }

    /// <summary>Reads only the metadata header from a raw relations/partials stream.</summary>
    public static RawRelationsMetadata ReadMetadata(TextReader reader)
    {
        var meta = new Dictionary<string, string>(StringComparer.Ordinal);
        string? format = null;
        bool? isV2 = null;
        RawRelationsMetadata? metadata = null;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (MetadataFormat.TryParse(line, out var key, out var value))
            {
                meta[key] = value;
                continue;
            }

            if (line.Length == 0)
            {
                continue;
            }

            EnsureHeader(meta, ref format, ref isV2, ref metadata);
            return metadata!;
        }

        EnsureHeader(meta, ref format, ref isV2, ref metadata);
        return metadata!;
    }

    private static void EnsureHeader(
        IReadOnlyDictionary<string, string> meta,
        ref string? format,
        ref bool? isV2,
        ref RawRelationsMetadata? metadata)
    {
        if (metadata is not null)
        {
            return;
        }

        format = Require(meta, "format");
        if (format is not (FileFormats.RawRelationsV1 or FileFormats.RawPartialsV1
            or FileFormats.RawRelationsV2 or FileFormats.RawPartialsV2))
        {
            throw new FormatException($"Unexpected raw relations format '{format}'.");
        }

        isV2 = format is FileFormats.RawRelationsV2 or FileFormats.RawPartialsV2;
        metadata = new RawRelationsMetadata(
            TargetN: BigInteger.Parse(Require(meta, "target_n"), CultureInfo.InvariantCulture),
            Multiplier: BigInteger.Parse(Require(meta, "multiplier"), CultureInfo.InvariantCulture),
            ScaledN: BigInteger.Parse(Require(meta, "scaled_n"), CultureInfo.InvariantCulture),
            FactorBaseBound: long.Parse(Require(meta, "factor_base_bound"), CultureInfo.InvariantCulture),
            LargePrimeBound: long.Parse(Require(meta, "large_prime_bound"), CultureInfo.InvariantCulture),
            LargePrime2Bound: meta.TryGetValue("large_prime2_bound", out var lp2)
                ? long.Parse(lp2, CultureInfo.InvariantCulture)
                : null);
    }

    private static string Dec(BigInteger value) => value.ToString(CultureInfo.InvariantCulture);

    private static string WriteBigIntegers(IReadOnlyList<BigInteger> values)
        => string.Join(' ', values.Select(Dec));

    private static BigInteger[] ParseBigIntegers(string text)
        => ParseBigIntegers(text.AsSpan());

    private static BigInteger[] ParseBigIntegers(ReadOnlySpan<char> text)
    {
        var count = 0;
        foreach (var range in text.Split(' '))
        {
            if (!text[range].IsEmpty)
            {
                count++;
            }
        }

        if (count == 0)
        {
            return Array.Empty<BigInteger>();
        }

        var values = new BigInteger[count];
        var index = 0;
        foreach (var range in text.Split(' '))
        {
            if (!text[range].IsEmpty)
            {
                values[index++] = BigInteger.Parse(text[range], CultureInfo.InvariantCulture);
            }
        }

        return values;
    }

    private static void WriteLine(TextWriter writer, string line)
    {
        writer.Write(line);
        writer.Write('\n');
    }

    private static string Require(IReadOnlyDictionary<string, string> meta, string key)
        => meta.TryGetValue(key, out var value)
            ? value
            : throw new FormatException($"Raw relations file is missing required metadata key '{key}'.");
}
