using System.Collections.Immutable;

namespace LinearAlgebra;

/// <summary>Behaviour attached to <see cref="Dependency"/> values.</summary>
public static class DependencyExtensions
{
    /// <summary>
    /// Verifies the dependency against the original filtered matrix: XORing the selected rows must
    /// yield the all-zero parity vector.
    /// </summary>
    public static bool VerifiesAgainst(
        this Dependency dependency,
        IReadOnlyList<RelationRow> rows,
        int columnCount)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentOutOfRangeException.ThrowIfNegative(columnCount);

        var parity = new HashSet<int>();
        foreach (var rowId in dependency.RowIds)
        {
            foreach (var column in rows[rowId].Columns)
            {
                if ((uint)column >= (uint)columnCount)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(rows), $"Column {column} is out of range [0, {columnCount}).");
                }

                if (!parity.Add(column))
                {
                    parity.Remove(column);
                }
            }
        }

        return parity.Count == 0;
    }

    /// <summary>Remaps a dependency's row ids through a lookup, e.g. filtered rows to original rows.</summary>
    public static Dependency MapThrough(this Dependency dependency, IReadOnlyList<int> rowIdLookup)
    {
        ArgumentNullException.ThrowIfNull(rowIdLookup);

        var mapped = ImmutableArray.CreateBuilder<int>(dependency.Count);
        foreach (var rowId in dependency.RowIds)
        {
            mapped.Add(rowIdLookup[rowId]);
        }

        return new Dependency(mapped.MoveToImmutable());
    }
}
