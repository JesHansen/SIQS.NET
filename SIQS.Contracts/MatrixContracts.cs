using System.Numerics;

namespace SIQS.Contracts;

/// <summary>Header metadata for the filtered matrix handed to linear algebra.</summary>
public sealed record MatrixMetadata(
    BigInteger TargetN,
    BigInteger Multiplier,
    BigInteger ScaledN,
    int RowCount,
    int ColumnCount,
    int FactorBaseCount,
    int SignColumn,
    string MatrixFile,
    string RelationsFile);

/// <summary>One sparse GF(2) matrix row: the ascending list of columns set to 1.</summary>
public sealed record SparseMatrixRowRecord : IEquatable<SparseMatrixRowRecord>
{
    public SparseMatrixRowRecord(int RowId, string RelationId, IReadOnlyList<int> Columns)
    {
        this.RowId = RowId;
        this.RelationId = RelationId;
        this.Columns = Array.AsReadOnly(Columns.ToArray());
    }

    public int RowId { get; }
    public string RelationId { get; }
    public IReadOnlyList<int> Columns { get; }

    public bool Equals(SparseMatrixRowRecord? other)
        => other is not null
            && RowId == other.RowId
            && RelationId == other.RelationId
            && StructuralEquality.SequenceEqual(Columns, other.Columns);

    public override int GetHashCode() => HashCode.Combine(RowId, RelationId, Columns.Count);
}

/// <summary>A nullspace dependency: a set of relation rows whose parity vectors XOR to zero.</summary>
public sealed record DependencyRecord : IEquatable<DependencyRecord>
{
    public DependencyRecord(int DependencyId, IReadOnlyList<int> RowIds, IReadOnlyList<string> RelationIds)
    {
        this.DependencyId = DependencyId;
        this.RowIds = Array.AsReadOnly(RowIds.ToArray());
        this.RelationIds = Array.AsReadOnly(RelationIds.ToArray());
    }

    public int DependencyId { get; }
    public IReadOnlyList<int> RowIds { get; }
    public IReadOnlyList<string> RelationIds { get; }

    public bool Equals(DependencyRecord? other)
        => other is not null
            && DependencyId == other.DependencyId
            && StructuralEquality.SequenceEqual(RowIds, other.RowIds)
            && StructuralEquality.SequenceEqual(RelationIds, other.RelationIds);

    public override int GetHashCode() => HashCode.Combine(DependencyId, RowIds.Count, RelationIds.Count);
}
