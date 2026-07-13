using System.Collections.Immutable;

namespace LinearAlgebra;

/// <summary>
/// Turns a packed Block Lanczos candidate block into verified, de-duplicated <see cref="Dependency"/>
/// values. Each of the 64 candidate columns is a bitset over relation rows; a column survives only if
/// its rows XOR to zero over the original filtered matrix.
/// </summary>
public static class BlockLanczosDependencyExtractor
{
    private const int CandidateColumns = 64;

    public static IReadOnlyList<Dependency> ExtractVerified(
        IReadOnlyList<RelationRow> rows,
        int columnCount,
        IReadOnlyList<ulong> candidateBlock,
        int maxDependencies)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(candidateBlock);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDependencies);
        if (candidateBlock.Count != rows.Count)
        {
            throw new ArgumentException(
                "Candidate block length must match the relation row count.", nameof(candidateBlock));
        }

        var seen = new HashSet<Dependency>();
        var verified = new List<Dependency>();
        for (var column = 0; column < CandidateColumns && verified.Count < maxDependencies; column++)
        {
            var dependency = new Dependency(RowIdsInColumn(candidateBlock, column));
            if (dependency.Count != 0 &&
                dependency.VerifiesAgainst(rows, columnCount) &&
                seen.Add(dependency))
            {
                verified.Add(dependency);
            }
        }

        return verified;
    }

    private static ImmutableArray<int> RowIdsInColumn(IReadOnlyList<ulong> candidateBlock, int column)
    {
        var mask = 1UL << column;
        var rowIds = ImmutableArray.CreateBuilder<int>();
        for (var row = 0; row < candidateBlock.Count; row++)
        {
            if ((candidateBlock[row] & mask) != 0)
            {
                rowIds.Add(row);
            }
        }

        return rowIds.DrainToImmutable();
    }
}
