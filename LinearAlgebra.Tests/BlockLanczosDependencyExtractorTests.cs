using LinearAlgebra;

namespace LinearAlgebra.Tests;

public class BlockLanczosDependencyExtractorTests
{
    [Fact]
    public void Extracts_verified_dependencies_from_candidate_block_columns()
    {
        var rows = new[] { new RelationRow(0, 2), new RelationRow(1, 2), new RelationRow(0, 1) };
        var candidates = new ulong[]
        {
            0b001,
            0b001,
            0b001,
        };

        var deps = BlockLanczosDependencyExtractor.ExtractVerified(rows, columnCount: 3, candidates, maxDependencies: 64);

        var dep = Assert.Single(deps);
        Assert.Equal(new[] { 0, 1, 2 }, dep.RowIds);
    }

    [Fact]
    public void Rejects_candidate_columns_that_do_not_xor_to_zero()
    {
        var rows = new[] { new RelationRow(0, 2), new RelationRow(1, 2), new RelationRow(0, 1) };
        var candidates = new ulong[]
        {
            0b001,
            0b001,
            0b000,
        };

        var deps = BlockLanczosDependencyExtractor.ExtractVerified(rows, columnCount: 3, candidates, maxDependencies: 64);

        Assert.Empty(deps);
    }

    [Fact]
    public void De_duplicates_and_respects_max_dependencies()
    {
        var rows = new[] { RelationRow.Empty, RelationRow.Empty, RelationRow.Empty };
        var candidates = new ulong[]
        {
            0b001 | 0b100,
            0b010,
            0b001 | 0b100,
        };

        var deps = BlockLanczosDependencyExtractor.ExtractVerified(rows, columnCount: 1, candidates, maxDependencies: 2);

        Assert.Equal(2, deps.Count);
        Assert.Equal(new[] { 0, 2 }, deps[0].RowIds);
        Assert.Equal(new[] { 1 }, deps[1].RowIds);
    }
}
