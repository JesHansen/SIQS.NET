using System.Collections.Immutable;

namespace LinearAlgebra;

/// <summary>
/// A nullspace dependency: the set of matrix row ids whose GF(2) exponent vectors XOR to the zero
/// vector. Row ids are stored in ascending order and dependencies are compared by value, so equal
/// row-id sets are equal dependencies.
/// </summary>
public readonly record struct Dependency
{
    public Dependency(ImmutableArray<int> rowIds) => RowIds = rowIds;

    public ImmutableArray<int> RowIds { get; }

    public int Count => RowIds.Length;

    public bool Equals(Dependency other) => RowIds.AsSpan().SequenceEqual(other.RowIds.AsSpan());

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var rowId in RowIds)
        {
            hash.Add(rowId);
        }

        return hash.ToHashCode();
    }
}
