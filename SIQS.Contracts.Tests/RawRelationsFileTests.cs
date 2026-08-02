using System.Numerics;
using SIQS.Contracts.Files;

namespace SIQS.Contracts.Tests;

public class RawRelationsFileTests
{
    private static RawRelationsMetadata Meta() => new(
        TargetN: 77, Multiplier: 1, ScaledN: 77, FactorBaseBound: 2, LargePrimeBound: 128);

    [Theory]
    [InlineData("relations_0000.txt", "relations", 0)]
    [InlineData("partials_9999.txt", "partials", 9999)]
    [InlineData("relations_10000.txt", "relations", 10000)]
    public void Parses_canonical_raw_batch_file_names(string fileName, string prefix, int expectedIndex)
    {
        Assert.True(RawBatchFileName.TryParse(fileName, prefix, out var batch));
        Assert.Equal(expectedIndex, batch.Index);
        Assert.Equal(fileName, batch.FileName);
    }

    [Theory]
    [InlineData("relations_000.txt")]
    [InlineData("relations_00000.txt")]
    [InlineData("relations_10000.tmp")]
    [InlineData("relations_abcd.txt")]
    [InlineData("partials_10000.txt")]
    public void Rejects_noncanonical_raw_batch_file_names(string fileName)
    {
        Assert.False(RawBatchFileName.TryParse(fileName, "relations", out _));
    }

    [Fact]
    public void Writes_full_relation_with_example_shape()
    {
        var doc = new RawRelationsDocument(
            FileFormats.RawRelationsV1, Meta(),
            new[]
            {
                new RawRelationRecord(
                    "R00000000", RelationKind.Full, "P00000000",
                    A: 1, B: 0, C: -77, X: 9, T: 9, Sign: 1,
                    FactorExponents: new Dictionary<int, int> { [1] = 2 },
                    ParityColumns: Array.Empty<int>(),
                    LargePrime: null),
            });

        var lines = RawRelationsFile.Write(doc).Split('\n');
        Assert.Equal("# format=siqs-raw-relations-v1", lines[0]);
        Assert.Equal("relation_id,kind,poly_id,a,b,c,x,t,sign,factor_exponents,parity_columns,large_prime", lines[7]);
        Assert.Equal("R00000000,full,P00000000,1,0,-77,9,9,1,1:2,,", lines[8]);
    }

    [Fact]
    public void Writes_partial_relation_with_large_prime()
    {
        var doc = new RawRelationsDocument(
            FileFormats.RawPartialsV1, Meta(),
            new[]
            {
                new RawRelationRecord(
                    "R00000001", RelationKind.Partial, "P00000012",
                    A: 103, B: 45, C: -890, X: 17, T: 864, Sign: -1,
                    FactorExponents: new Dictionary<int, int> { [0] = 1, [1] = 3, [8] = 1 },
                    ParityColumns: new[] { 0, 1, 8 },
                    LargePrime: 20000003),
            });

        var lines = RawRelationsFile.Write(doc).Split('\n');
        Assert.Equal("# format=siqs-raw-partials-v1", lines[0]);
        Assert.Equal("R00000001,partial,P00000012,103,45,-890,17,864,-1,0:1 1:3 8:1,0 1 8,20000003", lines[8]);
    }

    [Fact]
    public void Writes_v2_partial_relation_with_two_large_primes()
    {
        var doc = new RawRelationsDocument(
            FileFormats.RawPartialsV2, Meta() with { LargePrime2Bound = 512 },
            new[]
            {
                new RawRelationRecord(
                    "R00000001", RelationKind.Partial, "P00000012",
                    A: 103, B: 45, C: -890, X: 17, T: 864, Sign: -1,
                    FactorExponents: new Dictionary<int, int> { [0] = 1, [1] = 3, [8] = 1 },
                    ParityColumns: new[] { 0, 1, 8 },
                    LargePrime: null)
                {
                    LargePrimes = new BigInteger[] { 20000003, 20000033 },
                },
            });

        var text = RawRelationsFile.Write(doc);
        var lines = text.Split('\n');
        Assert.Equal("# format=siqs-raw-partials-v2", lines[0]);
        Assert.Equal("# large_prime2_bound=512", lines[6]);
        Assert.Equal("relation_id,kind,poly_id,a,b,c,x,t,sign,factor_exponents,parity_columns,large_primes", lines[8]);
        Assert.Equal("R00000001,partial,P00000012,103,45,-890,17,864,-1,0:1 1:3 8:1,0 1 8,20000003 20000033", lines[9]);

        var parsed = RawRelationsFile.Parse(text);
        Assert.Equal(doc.Format, parsed.Format);
        Assert.Equal(new BigInteger[] { 20000003, 20000033 }, parsed.Relations[0].LargePrimes);
        Assert.Null(parsed.Relations[0].LargePrime);
    }

    [Fact]
    public void Round_trips()
    {
        var doc = new RawRelationsDocument(
            FileFormats.RawRelationsV1, Meta(),
            new[]
            {
                new RawRelationRecord("R00000000", RelationKind.Full, "P00000000",
                    1, 0, -77, 9, 9, 1, new Dictionary<int, int> { [1] = 2 }, Array.Empty<int>(), null),
                new RawRelationRecord("R00000002", RelationKind.Full, "P00000003",
                    103, -7, 890, -14, 555, -1,
                    new Dictionary<int, int> { [0] = 1, [4] = 1, [12] = 1 }, new[] { 0, 4, 12 }, null),
            });

        var parsed = RawRelationsFile.Parse(RawRelationsFile.Write(doc));
        Assert.Equal(doc.Format, parsed.Format);
        Assert.Equal(doc.Metadata, parsed.Metadata);
        Assert.Equal(doc.Relations, parsed.Relations);
    }

    [Fact]
    public void Reads_metadata_without_parsing_rows()
    {
        var text = RawRelationsFile.Write(new RawRelationsDocument(
            FileFormats.RawPartialsV2, Meta() with { LargePrime2Bound = 512 },
            new[]
            {
                new RawRelationRecord(
                    "R00000000", RelationKind.Partial, "P00000000",
                    A: 1, B: 0, C: -77, X: 9, T: 9, Sign: 1,
                    FactorExponents: new Dictionary<int, int> { [1] = 1 },
                    ParityColumns: new[] { 1 },
                    LargePrime: null)
                {
                    LargePrimes = new BigInteger[] { 101, 103 },
                },
            }));

        using var reader = new StringReader(text);
        var metadata = RawRelationsFile.ReadMetadata(reader);

        Assert.Equal(Meta() with { LargePrime2Bound = 512 }, metadata);
    }

    [Fact]
    public void Parse_rejects_unknown_format()
    {
        Assert.Throws<FormatException>(() => RawRelationsFile.Parse("# format=siqs-bogus-v1\n"));
    }

    private static RawRelationsDocument PartialsDoc(int count)
    {
        var records = Enumerable.Range(0, count)
            .Select(i => new RawRelationRecord(
                $"R{i:D8}", RelationKind.Partial, $"P{i:D8}",
                A: 103, B: 45, C: -890, X: 17 + i, T: 864 + i, Sign: 1,
                FactorExponents: new Dictionary<int, int> { [1] = 1, [2 + i % 3] = 1 },
                ParityColumns: new[] { 1, 2 + i % 3 }.OrderBy(c => c).ToArray(),
                LargePrime: null)
            {
                LargePrimes = new BigInteger[] { 20000003 + 2 * i, 20000033 },
            })
            .ToArray();
        return new RawRelationsDocument(FileFormats.RawPartialsV2, Meta() with { LargePrime2Bound = 512 }, records);
    }

    [Fact]
    public void Enumerates_records_with_data_row_ordinals()
    {
        var doc = PartialsDoc(4);
        using var reader = new StringReader(RawRelationsFile.Write(doc));

        var streamed = RawRelationsFile.EnumerateWithOrdinals(reader).ToArray();

        Assert.Equal(new[] { 0, 1, 2, 3 }, streamed.Select(x => x.Ordinal));
        Assert.Equal(doc.Relations, streamed.Select(x => x.Record));
    }

    [Fact]
    public void Parses_only_requested_ordinals()
    {
        var doc = PartialsDoc(6);
        using var reader = new StringReader(RawRelationsFile.Write(doc));

        var selected = RawRelationsFile.ParseRecordsAt(reader, new[] { 1, 4, 5 }).ToArray();

        Assert.Equal(new[] { 1, 4, 5 }, selected.Select(x => x.Ordinal));
        Assert.Equal(new[] { doc.Relations[1], doc.Relations[4], doc.Relations[5] }, selected.Select(x => x.Record));
    }

    [Fact]
    public void Parses_no_records_for_empty_ordinal_set()
    {
        using var reader = new StringReader(RawRelationsFile.Write(PartialsDoc(2)));
        Assert.Empty(RawRelationsFile.ParseRecordsAt(reader, Array.Empty<int>()));
    }

    [Fact]
    public void Rejects_descending_ordinals()
    {
        using var reader = new StringReader(RawRelationsFile.Write(PartialsDoc(3)));
        Assert.Throws<ArgumentException>(() => RawRelationsFile.ParseRecordsAt(reader, new[] { 2, 0 }).ToArray());
    }
}
