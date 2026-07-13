using SIQS.Contracts;

namespace SIQS.Contracts.Tests;

public sealed class SparseDomainTypesTests
{
    [Fact]
    public void Sparse_vector_sorts_and_owns_dictionary_input()
    {
        var input = new Dictionary<int, int> { [4] = 1, [1] = 2, [8] = 0 };
        var vector = new SparseExponentVector(input);
        input[1] = 99;
        input[3] = 7;

        Assert.Equal(new[] { 1, 4 }, vector.ColumnsSpan.ToArray());
        Assert.Equal(new[] { 2, 1 }, vector.ValuesSpan.ToArray());
        Assert.Equal(2, vector.Count);
        Assert.True(vector.TryGetExponent(4, out var exponent));
        Assert.Equal(1, exponent);
        Assert.False(vector.TryGetExponent(8, out _));
    }

    [Fact]
    public void Sparse_vector_rejects_invalid_compact_storage()
    {
        Assert.Throws<ArgumentException>(() => new SparseExponentVector(new[] { 1 }, Array.Empty<int>()));
        Assert.Throws<ArgumentException>(() => new SparseExponentVector(new[] { 2, 1 }, new[] { 1, 1 }));
        Assert.Throws<ArgumentException>(() => new SparseExponentVector(new[] { 1, 1 }, new[] { 1, 1 }));
        Assert.Throws<ArgumentException>(() => new SparseExponentVector(new[] { 1 }, new[] { 0 }));
    }

    [Fact]
    public void Sparse_vector_has_structural_equality_and_derived_parity()
    {
        var left = new SparseExponentVector(new Dictionary<int, int> { [1] = 3, [4] = 2, [7] = -1 });
        var right = new SparseExponentVector(new Dictionary<int, int> { [7] = -1, [4] = 2, [1] = 3 });

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.Equal(new[] { 1, 7 }, left.DeriveParity().ToArray());
    }

    [Fact]
    public void Parity_set_defensively_copies_and_rejects_unsorted_or_duplicate_columns()
    {
        var input = new[] { 1, 4 };
        var columns = new ParityColumnSet(input);
        input[0] = 9;

        Assert.Equal(new[] { 1, 4 }, columns.ToArray());
        Assert.True(columns.Contains(4));
        Assert.Throws<ArgumentException>(() => new ParityColumnSet(new[] { 2, 1 }));
        Assert.Throws<ArgumentException>(() => new ParityColumnSet(new[] { 1, 1 }));
    }

    [Fact]
    public void Relation_record_owns_domain_collections()
    {
        var exponents = new Dictionary<int, int> { [1] = 2 };
        var parity = Array.Empty<int>();
        var relation = new RawRelationRecord(
            "R1", RelationKind.Full, "P1", 1, 0, -1, 1, 1, 1, exponents, parity, null);
        exponents[1] = 9;
        parity = new[] { 1 };

        Assert.Equal(2, relation.FactorExponents.GetValueOrDefault(1));
        Assert.Empty(relation.ParityColumns);
    }

    [Fact]
    public void Matrix_dependency_and_progress_contracts_own_collections()
    {
        var columns = new[] { 1, 3 };
        var rowIds = new[] { 2 };
        var relationIds = new[] { "F2" };
        var counters = new Dictionary<string, string> { ["rows"] = "1" };
        var row = new SparseMatrixRowRecord(2, "F2", columns);
        var dependency = new DependencyRecord(0, rowIds, relationIds);
        var progress = new SiqsProgressEvent(DateTimeOffset.UtcNow, null, SiqsPhase.Filtering,
            ProgressLevel.Info, "filtering", null, counters, null);

        columns[0] = 9;
        rowIds[0] = 8;
        relationIds[0] = "changed";
        counters["rows"] = "9";

        Assert.Equal(new[] { 1, 3 }, row.Columns);
        Assert.Equal(new[] { 2 }, dependency.RowIds);
        Assert.Equal(new[] { "F2" }, dependency.RelationIds);
        Assert.Equal("1", progress.Counters["rows"]);
    }
}
