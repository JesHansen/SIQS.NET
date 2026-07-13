using System.Collections.Immutable;
using LinearAlgebra;

namespace LinearAlgebra.Tests;

/// <summary>
/// Compares every retained sequential and parallel multiply against a naive, test-only GF(2)
/// reference derived from the matrix's public structure (<see cref="BlockLanczosSparseMatrix.SparseColumnsForRelation"/>
/// and <see cref="BlockLanczosSparseMatrix.DenseRowMasks"/>). Covers random dense matrices, the empty
/// matrix, the dense-column path, and a malformed (non-ascending) row.
/// </summary>
public class BlockLanczosReferenceEquivalenceTests
{
    [Theory]
    [InlineData(0x11u, 300, 220, 8, 0)]     // no dense columns
    [InlineData(0x22u, 300, 220, 8, 12)]    // dense-column path
    [InlineData(0x33u, 64, 40, 5, 6)]       // small, dense columns
    public void Sequential_and_parallel_match_reference(uint seed, int rowCount, int columnCount, int weight, int postLanczosRows)
    {
        var rows = RandomRows(seed, rowCount, columnCount, weight);
        var reference = new Gf2Reference(rows, columnCount, postLanczosRows);

        var sequential = Build(rows, columnCount, postLanczosRows, parallelism: 1);
        var parallel = Build(rows, columnCount, postLanczosRows, parallelism: 4);

        var x = RandomBlock(seed ^ 0xABCD, sequential.RelationCount);
        var ySparse = RandomBlock(seed ^ 0x1234, sequential.SparseParityColumnCount);

        foreach (var matrix in new[] { sequential, parallel })
        {
            Assert.Equal(reference.SparseColumnCount, matrix.SparseParityColumnCount);
            Assert.Equal(reference.DenseColumnCount, matrix.DenseParityColumns.Count);

            var a = new ulong[matrix.SparseParityColumnCount];
            matrix.MultiplyA(x, a);
            Assert.Equal(reference.MultiplyA(x), a);

            var t = new ulong[matrix.RelationCount];
            matrix.MultiplyTranspose(ySparse, t);
            Assert.Equal(reference.MultiplyTranspose(ySparse), t);

            var dense = new ulong[matrix.DenseParityColumns.Count];
            matrix.MultiplyDenseRows(x, dense);
            Assert.Equal(reference.MultiplyDenseRows(x), dense);

            var sym = new ulong[matrix.RelationCount];
            matrix.MultiplySymmetric(x, sym, new ulong[matrix.SparseParityColumnCount]);
            Assert.Equal(reference.MultiplySymmetric(x), sym);
        }
    }

    [Fact]
    public void Empty_matrix_matches_reference()
    {
        var rows = Array.Empty<RelationRow>();
        var matrix = BlockLanczosSparseMatrix.FromRelationRows(rows, columnCount: 0);

        Assert.Equal(0, matrix.RelationCount);
        Assert.Equal(0, matrix.SparseParityColumnCount);
        Assert.Empty(matrix.DenseParityColumns);

        var y = Array.Empty<ulong>();
        matrix.MultiplyA(Array.Empty<ulong>(), y); // no-op, no throw
        Assert.Empty(y);
    }

    [Fact]
    public void Malformed_non_ascending_row_is_rejected()
    {
        var rows = new[] { new RelationRow(ImmutableArray.Create(2, 1)) };
        Assert.Throws<ArgumentException>(() => BlockLanczosSparseMatrix.FromRelationRows(rows, columnCount: 3));
    }

    private static BlockLanczosSparseMatrix Build(IReadOnlyList<RelationRow> rows, int columnCount, int postLanczosRows, int parallelism)
    {
        var options = new BlockLanczosOptions(postLanczosRows, MinPostLanczosDimension: 0, Parallelism: parallelism);
        return BlockLanczosSparseMatrix.FromRelationRows(rows, columnCount, options);
    }

    private static RelationRow[] RandomRows(uint seed, int rowCount, int columnCount, int weight)
    {
        var rows = new RelationRow[rowCount];
        var state = seed;
        for (var r = 0; r < rowCount; r++)
        {
            var columns = new SortedSet<int>();
            while (columns.Count < weight)
            {
                columns.Add((int)(Next(ref state) % (uint)columnCount));
            }

            rows[r] = new RelationRow(ImmutableArray.CreateRange(columns));
        }

        return rows;
    }

    private static ulong[] RandomBlock(uint seed, int length)
    {
        var block = new ulong[length];
        var state = seed;
        for (var i = 0; i < length; i++)
        {
            block[i] = ((ulong)Next(ref state) << 32) | Next(ref state);
        }

        return block;
    }

    private static uint Next(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    /// <summary>
    /// Naive GF(2) reference built from the same dense-column selection rule as the production builder
    /// (highest weight, ties by column index), so it can be compared column-for-column.
    /// </summary>
    private sealed class Gf2Reference
    {
        private readonly List<int>[] _sparseColumnsByRow;
        private readonly ulong[] _denseMaskByRow;

        public Gf2Reference(IReadOnlyList<RelationRow> rows, int columnCount, int postLanczosRows)
        {
            var weights = new int[columnCount];
            foreach (var row in rows)
            {
                foreach (var col in row.Columns)
                {
                    weights[col]++;
                }
            }

            var denseColumns = Enumerable.Range(0, columnCount)
                .OrderByDescending(c => weights[c])
                .ThenBy(c => c)
                .Take(Math.Min(postLanczosRows, columnCount))
                .OrderBy(c => c)
                .ToArray();
            var denseIndex = new Dictionary<int, int>();
            for (var i = 0; i < denseColumns.Length; i++)
            {
                denseIndex[denseColumns[i]] = i;
            }

            var sparseIndex = new int[columnCount];
            Array.Fill(sparseIndex, -1);
            var sparseCount = 0;
            for (var c = 0; c < columnCount; c++)
            {
                if (!denseIndex.ContainsKey(c))
                {
                    sparseIndex[c] = sparseCount++;
                }
            }

            SparseColumnCount = sparseCount;
            DenseColumnCount = denseColumns.Length;
            _sparseColumnsByRow = new List<int>[rows.Count];
            _denseMaskByRow = new ulong[rows.Count];
            for (var r = 0; r < rows.Count; r++)
            {
                var list = new List<int>();
                foreach (var col in rows[r].Columns)
                {
                    if (denseIndex.TryGetValue(col, out var d))
                    {
                        _denseMaskByRow[r] |= 1UL << d;
                    }
                    else
                    {
                        list.Add(sparseIndex[col]);
                    }
                }

                _sparseColumnsByRow[r] = list;
            }
        }

        public int SparseColumnCount { get; }

        public int DenseColumnCount { get; }

        public ulong[] MultiplyA(ulong[] x)
        {
            var y = new ulong[SparseColumnCount];
            for (var r = 0; r < _sparseColumnsByRow.Length; r++)
            {
                foreach (var c in _sparseColumnsByRow[r])
                {
                    y[c] ^= x[r];
                }
            }

            return y;
        }

        public ulong[] MultiplyTranspose(ulong[] y)
        {
            var x = new ulong[_sparseColumnsByRow.Length];
            for (var r = 0; r < _sparseColumnsByRow.Length; r++)
            {
                ulong accum = 0;
                foreach (var c in _sparseColumnsByRow[r])
                {
                    accum ^= y[c];
                }

                x[r] = accum;
            }

            return x;
        }

        public ulong[] MultiplyDenseRows(ulong[] x)
        {
            var y = new ulong[DenseColumnCount];
            for (var r = 0; r < _denseMaskByRow.Length; r++)
            {
                var mask = _denseMaskByRow[r];
                while (mask != 0)
                {
                    var d = System.Numerics.BitOperations.TrailingZeroCount(mask);
                    y[d] ^= x[r];
                    mask &= mask - 1;
                }
            }

            return y;
        }

        public ulong[] MultiplySymmetric(ulong[] x)
        {
            // Full A^T A over sparse and dense columns: sparse transpose of the sparse product,
            // plus the dense transpose of the dense-forward product.
            var z = MultiplyTranspose(MultiplyA(x));
            var denseImage = MultiplyDenseRows(x);
            for (var r = 0; r < _denseMaskByRow.Length; r++)
            {
                var mask = _denseMaskByRow[r];
                ulong accum = 0;
                while (mask != 0)
                {
                    var d = System.Numerics.BitOperations.TrailingZeroCount(mask);
                    accum ^= denseImage[d];
                    mask &= mask - 1;
                }

                z[r] ^= accum;
            }

            return z;
        }
    }
}
