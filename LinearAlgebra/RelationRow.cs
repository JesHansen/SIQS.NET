using System.Collections.Immutable;

namespace LinearAlgebra;

/// <summary>
/// One row of the filtered GF(2) matrix: the strictly ascending column indices whose parity bit is
/// set. An empty row (<see cref="Empty"/>) is already the zero vector.
/// </summary>
public readonly record struct RelationRow
{
    public static RelationRow Empty { get; } = new(ImmutableArray<int>.Empty);

    public RelationRow(ImmutableArray<int> columns)
        => Columns = columns.IsDefault ? ImmutableArray<int>.Empty : columns;

    public RelationRow(params int[] columns)
        : this(ImmutableArray.Create(columns))
    {
    }

    public static RelationRow FromColumns(IEnumerable<int> columns) => new(ImmutableArray.CreateRange(columns));

    public ImmutableArray<int> Columns { get; }

    public int Count => Columns.Length;
}
