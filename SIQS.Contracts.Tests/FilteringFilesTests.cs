using System.Numerics;
using SIQS.Contracts.Files;

namespace SIQS.Contracts.Tests;

public class FilteredRelationsFileTests
{
    [Fact]
    public void Writes_full_and_combined_partial_rows()
    {
        var doc = new FilteredRelationsDocument(
            TargetN: 77, Multiplier: 1, ScaledN: 77,
            Relations: new[]
            {
                new FilteredRelationRecord("F00000000", RelationKind.Full, new[] { "R00000000" },
                    T: 9, Sign: 1, Exponents: new Dictionary<int, int> { [1] = 2 },
                    ParityColumns: Array.Empty<int>(), LargePrime: null),
                new FilteredRelationRecord("F00000001", RelationKind.CombinedPartial,
                    new[] { "R00000022", "R00000087" },
                    T: 987654321, Sign: -1,
                    Exponents: new Dictionary<int, int> { [0] = 1, [2] = 4, [7] = 2 },
                    ParityColumns: new[] { 0 }, LargePrime: 101),
            });

        var lines = FilteredRelationsFile.Write(doc).Split('\n');
        Assert.Equal("# format=siqs-filtered-relations-v1", lines[0]);
        Assert.Equal("relation_id,kind,source_relation_ids,t,sign,exponents,parity_columns,large_prime", lines[5]);
        Assert.Equal("F00000000,full,R00000000,9,1,1:2,,", lines[6]);
        Assert.Equal("F00000001,combined_partial,R00000022 R00000087,987654321,-1,0:1 2:4 7:2,0,101", lines[7]);
    }

    [Fact]
    public void Round_trips()
    {
        var doc = new FilteredRelationsDocument(77, 1, 77, new[]
        {
            new FilteredRelationRecord("F00000000", RelationKind.Full, new[] { "R0", "R1" },
                123, -1, new Dictionary<int, int> { [0] = 1, [4] = 1, [9] = 3 }, new[] { 0, 4, 9 }, null),
        });
        var parsed = FilteredRelationsFile.Parse(FilteredRelationsFile.Write(doc));
        Assert.Equal(doc.TargetN, parsed.TargetN);
        Assert.Equal(doc.Relations, parsed.Relations);
    }

    [Fact]
    public void Writes_v2_combined_partial_with_large_prime_list()
    {
        var doc = new FilteredRelationsDocument(77, 1, 77, new[]
        {
            new FilteredRelationRecord("F00000000", RelationKind.CombinedPartial, new[] { "R0", "R1", "R2" },
                123, -1, new Dictionary<int, int> { [0] = 1, [4] = 2 }, new[] { 0 },
                LargePrime: null)
            {
                LargePrimes = new BigInteger[] { 101, 103, 107 },
            },
        });

        var text = FilteredRelationsFile.Write(doc, FileFormats.FilteredRelationsV2);
        var lines = text.Split('\n');

        Assert.Equal("# format=siqs-filtered-relations-v2", lines[0]);
        Assert.Equal("relation_id,kind,source_relation_ids,t,sign,exponents,parity_columns,large_primes", lines[5]);
        Assert.Equal("F00000000,combined_partial,R0 R1 R2,123,-1,0:1 4:2,0,101 103 107", lines[6]);

        var parsed = FilteredRelationsFile.Parse(text);
        Assert.Equal(new BigInteger[] { 101, 103, 107 }, parsed.Relations[0].LargePrimes);
    }
}

public class FilteredMatrixFileTests
{
    [Fact]
    public void Writes_rows_with_empty_and_nonempty_columns()
    {
        var rows = new[]
        {
            new SparseMatrixRowRecord(0, "F00000000", new[] { 4, 9 }),
            new SparseMatrixRowRecord(1, "F00000001", Array.Empty<int>()),
        };
        var lines = FilteredMatrixFile.Write(rows).Split('\n');
        Assert.Equal("# format=siqs-filtered-matrix-v1", lines[0]);
        Assert.Equal("row_id,relation_id,columns", lines[2]);
        Assert.Equal("0,F00000000,4 9", lines[3]);
        Assert.Equal("1,F00000001,", lines[4]);
    }

    [Fact]
    public void Round_trips()
    {
        var rows = new[]
        {
            new SparseMatrixRowRecord(0, "F0", new[] { 0, 3, 9, 27 }),
            new SparseMatrixRowRecord(1, "F1", Array.Empty<int>()),
        };
        var parsed = FilteredMatrixFile.Parse(FilteredMatrixFile.Write(rows));
        Assert.Equal((IReadOnlyList<SparseMatrixRowRecord>)rows, parsed);
    }
}

public class MatrixMetaFileTests
{
    [Fact]
    public void Writes_comment_format_then_bare_key_values()
    {
        var meta = new MatrixMetadata(77, 1, 77, RowCount: 1, ColumnCount: 2, FactorBaseCount: 1,
            SignColumn: 0, MatrixFile: "filtered_matrix.txt", RelationsFile: "relations_filtered.txt");
        var lines = MatrixMetaFile.Write(meta).Split('\n');
        Assert.Equal("# format=siqs-matrix-meta-v1", lines[0]);
        Assert.Equal("target_n=77", lines[1]);
        Assert.Equal("row_count=1", lines[4]);
        Assert.Equal("column_count=2", lines[5]);
        Assert.Equal("sign_column=0", lines[7]);
        Assert.Equal("matrix_file=filtered_matrix.txt", lines[8]);
    }

    [Fact]
    public void Round_trips()
    {
        var meta = new MatrixMetadata(1000003, 3, 3000009, 120, 91, 90, 0, "filtered_matrix.txt", "relations_filtered.txt");
        Assert.Equal(meta, MatrixMetaFile.Parse(MatrixMetaFile.Write(meta)));
    }
}
