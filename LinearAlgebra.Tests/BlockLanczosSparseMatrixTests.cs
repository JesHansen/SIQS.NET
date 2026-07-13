using LinearAlgebra;

namespace LinearAlgebra.Tests;

public class BlockLanczosSparseMatrixTests
{
    [Fact]
    public void Builds_lanczos_columns_from_relation_rows()
    {
        var rows = new[] { new RelationRow(0, 2), new RelationRow(1, 2), new RelationRow(0, 1) };

        var matrix = BlockLanczosSparseMatrix.FromRelationRows(rows, columnCount: 3);

        Assert.Equal(3, matrix.RelationCount);
        Assert.Equal(3, matrix.OriginalParityColumnCount);
        Assert.Equal(3, matrix.SparseParityColumnCount);
        Assert.Empty(matrix.DenseParityColumns);
        Assert.Equal(new[] { 0, 2 }, matrix.SparseColumnsForRelation(0).ToArray());
        Assert.Equal(new[] { 1, 2 }, matrix.SparseColumnsForRelation(1).ToArray());
        Assert.Equal(new[] { 0, 1 }, matrix.SparseColumnsForRelation(2).ToArray());
    }

    [Fact]
    public void Randomized_dense_MultiplySymmetric_supports_in_place_output()
    {
        var rows = RandomRows(seed: 0x2C91, rowCount: 200, columnCount: 150, weight: 10);
        var options = new BlockLanczosOptions(PostLanczosRows: 16, MinPostLanczosDimension: 1, Parallelism: 1);
        var matrix = BlockLanczosSparseMatrix.FromRelationRows(rows, columnCount: 150, options);
        var random = new Random(0x7788);

        for (var trial = 0; trial < 20; trial++)
        {
            var input = RandomWords(random, rows.Count);
            var expected = new ulong[input.Length];
            var actual = input.ToArray();

            matrix.MultiplySymmetric(input, expected, new ulong[matrix.SparseParityColumnCount]);
            matrix.MultiplySymmetric(actual, actual, new ulong[matrix.SparseParityColumnCount]);

            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Randomized_operations_match_full_matrix_oracles_with_dense_rows()
    {
        var rows = RandomRows(seed: 0x5821, rowCount: 96, columnCount: 80, weight: 9);
        var options = new BlockLanczosOptions(PostLanczosRows: 12, MinPostLanczosDimension: 1, Parallelism: 1);
        var matrix = BlockLanczosSparseMatrix.FromRelationRows(rows, columnCount: 80, options);
        var random = new Random(0x41C0);

        var x = RandomWords(random, rows.Count);
        var sparseY = RandomWords(random, matrix.SparseParityColumnCount);

        var actualSparse = new ulong[matrix.SparseParityColumnCount];
        matrix.MultiplyA(x, actualSparse);
        Assert.Equal(SparseProduct(rows, matrix, x), actualSparse);

        var actualTranspose = new ulong[rows.Count];
        matrix.MultiplyTranspose(sparseY, actualTranspose);
        Assert.Equal(SparseTransposeProduct(rows, matrix, sparseY), actualTranspose);

        var actualSymmetric = new ulong[rows.Count];
        matrix.MultiplySymmetric(x, actualSymmetric, new ulong[matrix.SparseParityColumnCount]);
        Assert.Equal(FullSymmetricProduct(rows, columnCount: 80, x), actualSymmetric);

        var actualDense = new ulong[matrix.DenseParityColumns.Count];
        matrix.MultiplyDenseRows(x, actualDense);
        Assert.Equal(DenseProduct(rows, matrix, x), actualDense);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    public void Parallel_operations_are_bit_identical_to_sequential(int parallelism)
    {
        var rows = RandomRows(seed: 0x7321, rowCount: 240, columnCount: 170, weight: 11);
        var random = new Random(0x1077);
        var x = RandomWords(random, rows.Count);
        var sequential = BlockLanczosSparseMatrix.FromRelationRows(
            rows,
            columnCount: 170,
            new BlockLanczosOptions(PostLanczosRows: 16, MinPostLanczosDimension: 1, Parallelism: 1));
        var parallel = BlockLanczosSparseMatrix.FromRelationRows(
            rows,
            columnCount: 170,
            new BlockLanczosOptions(PostLanczosRows: 16, MinPostLanczosDimension: 1, Parallelism: parallelism));

        var expectedA = new ulong[sequential.SparseParityColumnCount];
        var actualA = new ulong[parallel.SparseParityColumnCount];
        sequential.MultiplyA(x, expectedA);
        parallel.MultiplyA(x, actualA);
        Assert.Equal(expectedA, actualA);

        var y = RandomWords(random, sequential.SparseParityColumnCount);
        var expectedTranspose = new ulong[rows.Count];
        var actualTranspose = new ulong[rows.Count];
        sequential.MultiplyTranspose(y, expectedTranspose);
        parallel.MultiplyTranspose(y, actualTranspose);
        Assert.Equal(expectedTranspose, actualTranspose);

        var expectedSymmetric = new ulong[rows.Count];
        var actualSymmetric = new ulong[rows.Count];
        sequential.MultiplySymmetric(x, expectedSymmetric, new ulong[sequential.SparseParityColumnCount]);
        parallel.MultiplySymmetric(x, actualSymmetric, new ulong[parallel.SparseParityColumnCount]);
        Assert.Equal(expectedSymmetric, actualSymmetric);

        var expectedAlias = x.ToArray();
        var actualAlias = x.ToArray();
        sequential.MultiplySymmetric(expectedAlias, expectedAlias, new ulong[sequential.SparseParityColumnCount]);
        parallel.MultiplySymmetric(actualAlias, actualAlias, new ulong[parallel.SparseParityColumnCount]);
        Assert.Equal(expectedAlias, actualAlias);

        var expectedDense = new ulong[sequential.DenseParityColumns.Count];
        var actualDense = new ulong[parallel.DenseParityColumns.Count];
        sequential.MultiplyDenseRows(x, expectedDense);
        parallel.MultiplyDenseRows(x, actualDense);
        Assert.Equal(expectedDense, actualDense);
    }

    [Fact]
    public void MultiplyA_uses_relation_rows_as_lanczos_columns()
    {
        var rows = new[] { new RelationRow(0, 2), new RelationRow(1, 2), new RelationRow(0, 1) };
        var matrix = BlockLanczosSparseMatrix.FromRelationRows(rows, columnCount: 3);
        var x = new ulong[] { 0b0011, 0b0101, 0b1001 };
        var y = new ulong[3];

        matrix.MultiplyA(x, y);

        Assert.Equal(x[0] ^ x[2], y[0]);
        Assert.Equal(x[1] ^ x[2], y[1]);
        Assert.Equal(x[0] ^ x[1], y[2]);
    }

    [Fact]
    public void MultiplyTranspose_returns_one_word_per_relation_row()
    {
        var rows = new[] { new RelationRow(0, 2), new RelationRow(1, 2), new RelationRow(0, 1) };
        var matrix = BlockLanczosSparseMatrix.FromRelationRows(rows, columnCount: 3);
        var y = new ulong[] { 0b0011, 0b0101, 0b1001 };
        var x = new ulong[3];

        matrix.MultiplyTranspose(y, x);

        Assert.Equal(y[0] ^ y[2], x[0]);
        Assert.Equal(y[1] ^ y[2], x[1]);
        Assert.Equal(y[0] ^ y[1], x[2]);
    }

    [Fact]
    public void MultiplySymmetric_matches_transpose_times_A()
    {
        var rows = new[] { new RelationRow(0, 2), new RelationRow(1, 2), new RelationRow(0, 1) };
        var matrix = BlockLanczosSparseMatrix.FromRelationRows(rows, columnCount: 3);
        var x = new ulong[] { 0x0102030405060708, 0x1020304050607080, 0x1111222233334444 };
        var expectedRows = new ulong[3];
        var expected = new ulong[3];
        var actual = new ulong[3];

        matrix.MultiplyA(x, expectedRows);
        matrix.MultiplyTranspose(expectedRows, expected);
        matrix.MultiplySymmetric(x, actual);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void MultiplySymmetric_accepts_reusable_scratch_buffer()
    {
        var rows = new[] { new RelationRow(0, 2), new RelationRow(1, 2), new RelationRow(0, 1) };
        var matrix = BlockLanczosSparseMatrix.FromRelationRows(rows, columnCount: 3);
        var x = new ulong[] { 0x0102030405060708, 0x1020304050607080, 0x1111222233334444 };
        var expected = new ulong[3];
        var actual = new ulong[3];
        var scratch = new ulong[matrix.SparseParityColumnCount];

        matrix.MultiplySymmetric(x, expected);
        matrix.MultiplySymmetric(x, actual, scratch);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void MultiplySymmetric_includes_deferred_dense_columns_and_supports_in_place_output()
    {
        var rows = new[]
        {
            new RelationRow(0, 1, 3),
            new RelationRow(0, 1),
            new RelationRow(0, 2),
            new RelationRow(3),
        };
        var options = new BlockLanczosOptions(PostLanczosRows: 2, MinPostLanczosDimension: 0);
        var matrix = BlockLanczosSparseMatrix.FromRelationRows(rows, columnCount: 4, options);
        var expected = FullSymmetricProduct(rows, columnCount: 4, new ulong[]
        {
            0x0102030405060708,
            0x1020304050607080,
            0x1111222233334444,
            0xABCDEF0123456789,
        });
        var actual = new ulong[]
        {
            0x0102030405060708,
            0x1020304050607080,
            0x1111222233334444,
            0xABCDEF0123456789,
        };
        var scratch = new ulong[matrix.SparseParityColumnCount];

        matrix.MultiplySymmetric(actual, actual, scratch);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void MultiplySymmetric_rejects_wrong_scratch_length()
    {
        var rows = new[] { new RelationRow(0, 2), new RelationRow(1, 2), new RelationRow(0, 1) };
        var matrix = BlockLanczosSparseMatrix.FromRelationRows(rows, columnCount: 3);
        var x = new ulong[3];
        var z = new ulong[3];
        var scratch = new ulong[matrix.SparseParityColumnCount + 1];

        Assert.Throws<ArgumentException>(() => matrix.MultiplySymmetric(x, z, scratch));
    }

    [Fact]
    public void Selects_densest_parity_columns_for_post_lanczos_handling()
    {
        var rows = new[]
        {
            new RelationRow(0, 1, 3),
            new RelationRow(0, 1),
            new RelationRow(0, 2),
            new RelationRow(3),
        };
        var options = new BlockLanczosOptions(PostLanczosRows: 2, MinPostLanczosDimension: 0);

        var matrix = BlockLanczosSparseMatrix.FromRelationRows(rows, columnCount: 4, options);

        Assert.Equal(new[] { 0, 1 }, matrix.DenseParityColumns);
        Assert.Equal(2, matrix.SparseParityColumnCount);
        Assert.Equal(new[] { 1 }, matrix.SparseColumnsForRelation(0).ToArray()); // original column 3
        Assert.Empty(matrix.SparseColumnsForRelation(1).ToArray());
        Assert.Equal(new[] { 0 }, matrix.SparseColumnsForRelation(2).ToArray()); // original column 2
        Assert.Equal(new[] { 1 }, matrix.SparseColumnsForRelation(3).ToArray()); // original column 3
        Assert.Equal(new ulong[] { 0b11, 0b11, 0b01, 0b00 }, matrix.DenseRowMasks);
    }

    [Fact]
    public void Dense_row_parity_is_checked_against_candidate_dependency()
    {
        var rows = new[]
        {
            new RelationRow(0, 1),
            new RelationRow(0),
            new RelationRow(1),
        };
        var options = new BlockLanczosOptions(PostLanczosRows: 2, MinPostLanczosDimension: 0);
        var matrix = BlockLanczosSparseMatrix.FromRelationRows(rows, columnCount: 2, options);

        Assert.True(matrix.DenseRowsAreZero(new[] { 0, 1, 2 }));
        Assert.False(matrix.DenseRowsAreZero(new[] { 0, 1 }));
    }

    [Fact]
    public void MultiplyDenseRows_returns_deferred_parity_row_products()
    {
        var rows = new[]
        {
            new RelationRow(0, 1),
            new RelationRow(0),
            new RelationRow(1),
        };
        var options = new BlockLanczosOptions(PostLanczosRows: 2, MinPostLanczosDimension: 0);
        var matrix = BlockLanczosSparseMatrix.FromRelationRows(rows, columnCount: 2, options);
        var x = new ulong[] { 0b0011, 0b0101, 0b1001 };
        var y = new ulong[2];

        matrix.MultiplyDenseRows(x, y);

        Assert.Equal(x[0] ^ x[1], y[0]);
        Assert.Equal(x[0] ^ x[2], y[1]);
    }

    [Fact]
    public void Rejects_post_lanczos_rows_that_leave_no_expected_output_columns()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new BlockLanczosOptions(PostLanczosRows: 64, MinPostLanczosDimension: 0));

        Assert.Equal("PostLanczosRows", ex.ParamName);
    }

    private static ulong[] FullSymmetricProduct(IReadOnlyList<RelationRow> rows, int columnCount, IReadOnlyList<ulong> x)
    {
        var ax = new ulong[columnCount];
        for (var relation = 0; relation < rows.Count; relation++)
        {
            foreach (var column in rows[relation].Columns)
            {
                ax[column] ^= x[relation];
            }
        }

        var result = new ulong[rows.Count];
        for (var relation = 0; relation < rows.Count; relation++)
        {
            foreach (var column in rows[relation].Columns)
            {
                result[relation] ^= ax[column];
            }
        }

        return result;
    }

    private static ulong[] SparseProduct(
        IReadOnlyList<RelationRow> rows,
        BlockLanczosSparseMatrix matrix,
        IReadOnlyList<ulong> x)
    {
        var sparseIndexByOriginalColumn = SparseIndexByOriginalColumn(matrix);
        var result = new ulong[matrix.SparseParityColumnCount];
        for (var relation = 0; relation < rows.Count; relation++)
        {
            foreach (var column in rows[relation].Columns)
            {
                var sparse = sparseIndexByOriginalColumn[column];
                if (sparse >= 0)
                {
                    result[sparse] ^= x[relation];
                }
            }
        }

        return result;
    }

    private static ulong[] SparseTransposeProduct(
        IReadOnlyList<RelationRow> rows,
        BlockLanczosSparseMatrix matrix,
        IReadOnlyList<ulong> y)
    {
        var sparseIndexByOriginalColumn = SparseIndexByOriginalColumn(matrix);
        var result = new ulong[rows.Count];
        for (var relation = 0; relation < rows.Count; relation++)
        {
            ulong accum = 0;
            foreach (var column in rows[relation].Columns)
            {
                var sparse = sparseIndexByOriginalColumn[column];
                if (sparse >= 0)
                {
                    accum ^= y[sparse];
                }
            }

            result[relation] = accum;
        }

        return result;
    }

    private static ulong[] DenseProduct(
        IReadOnlyList<RelationRow> rows,
        BlockLanczosSparseMatrix matrix,
        IReadOnlyList<ulong> x)
    {
        var denseIndexByOriginalColumn = DenseIndexByOriginalColumn(matrix);
        var result = new ulong[matrix.DenseParityColumns.Count];
        for (var relation = 0; relation < rows.Count; relation++)
        {
            foreach (var column in rows[relation].Columns)
            {
                if (denseIndexByOriginalColumn.TryGetValue(column, out var denseIndex))
                {
                    result[denseIndex] ^= x[relation];
                }
            }
        }

        return result;
    }

    private static int[] SparseIndexByOriginalColumn(BlockLanczosSparseMatrix matrix)
    {
        var dense = DenseIndexByOriginalColumn(matrix);
        var sparse = new int[matrix.OriginalParityColumnCount];
        Array.Fill(sparse, -1);
        var next = 0;
        for (var column = 0; column < sparse.Length; column++)
        {
            if (!dense.ContainsKey(column))
            {
                sparse[column] = next++;
            }
        }

        return sparse;
    }

    private static Dictionary<int, int> DenseIndexByOriginalColumn(BlockLanczosSparseMatrix matrix)
    {
        var dense = new Dictionary<int, int>();
        for (var i = 0; i < matrix.DenseParityColumns.Count; i++)
        {
            dense.Add(matrix.DenseParityColumns[i], i);
        }

        return dense;
    }

    private static IReadOnlyList<RelationRow> RandomRows(int seed, int rowCount, int columnCount, int weight)
    {
        var random = new Random(seed);
        var rows = new RelationRow[rowCount];
        for (var row = 0; row < rows.Length; row++)
        {
            var columns = new SortedSet<int>();
            while (columns.Count < weight)
            {
                columns.Add(random.Next(columnCount));
            }

            rows[row] = RelationRow.FromColumns(columns);
        }

        return rows;
    }

    private static ulong[] RandomWords(Random random, int count)
    {
        var values = new ulong[count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = ((ulong)random.NextInt64() << 1) ^ (ulong)random.Next(2);
        }

        return values;
    }
}
