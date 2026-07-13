using LinearAlgebra;

namespace LinearAlgebra.Tests;

public class BlockLanczosCandidateCombinerTests
{
    [Fact]
    public void Emits_null_candidates_from_second_half_of_reduced_block()
    {
        var x = new ulong[2];
        var v = new ulong[] { 0b10, 0b10 };
        var ax = new ulong[65];
        for (var i = 0; i < 64; i++)
        {
            ax[i] = 1UL << i;
        }

        var av = new ulong[65];
        av[64] = 1UL;

        var combined = BlockLanczosCandidateCombiner.Combine(
            columnCount: 2,
            rowCount: 65,
            x,
            v,
            ax,
            av);

        var deps = BlockLanczosDependencyExtractor.ExtractVerified(
            new[] { new RelationRow(0), new RelationRow(0) },
            columnCount: 1,
            combined,
            maxDependencies: 64);

        var dep = Assert.Single(deps);
        Assert.Equal(new[] { 0, 1 }, dep.RowIds);
    }

    [Fact]
    public void Combines_sparse_null_candidates_to_satisfy_deferred_dense_rows()
    {
        var x = new ulong[]
        {
            0b01,
            0b11,
            0b10,
        };
        var v = new ulong[3];
        var ax = new ulong[]
        {
            0b00, // sparse row already vanishes for both candidate columns
            0b11, // deferred dense row requires candidate 0 xor candidate 1
        };
        var av = new ulong[2];

        var combined = BlockLanczosCandidateCombiner.Combine(
            columnCount: 3,
            rowCount: 2,
            x,
            v,
            ax,
            av);

        var deps = BlockLanczosDependencyExtractor.ExtractVerified(
            new[] { new RelationRow(0), RelationRow.Empty, new RelationRow(0) },
            columnCount: 1,
            combined,
            maxDependencies: 64);

        var dep = Assert.Single(deps);
        Assert.Equal(new[] { 0, 2 }, dep.RowIds);
    }
}
