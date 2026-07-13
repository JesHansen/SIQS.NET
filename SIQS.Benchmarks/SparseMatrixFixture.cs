using System.Collections.Immutable;
using LinearAlgebra;

namespace SIQS.Benchmarks;

/// <summary>
/// Deterministic sparse GF(2) matrix fixtures for the Block Lanczos kernel benchmarks. The row
/// generator is a fixed SplitMix64 stream, so a given (rows, columns, weight, seed) always produces
/// the same matrix — results are comparable across runs and machines.
/// </summary>
internal static class SparseMatrixFixture
{
    public static IReadOnlyList<RelationRow> BuildRows(int rowCount, int columnCount, int rowWeight, ulong seed)
    {
        var rows = new RelationRow[rowCount];
        var state = seed;
        for (var r = 0; r < rowCount; r++)
        {
            var columns = new SortedSet<int>();
            // Each row draws rowWeight distinct columns; retry on collision keeps the weight exact.
            while (columns.Count < rowWeight)
            {
                columns.Add((int)(NextRandom(ref state) % (ulong)columnCount));
            }

            rows[r] = new RelationRow(ImmutableArray.CreateRange(columns));
        }

        return rows;
    }

    public static ulong[] BuildBlock(int length, ulong seed)
    {
        var block = new ulong[length];
        var state = seed;
        for (var i = 0; i < length; i++)
        {
            block[i] = NextRandom(ref state);
        }

        return block;
    }

    private static ulong NextRandom(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        var z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
