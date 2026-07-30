using LinearAlgebra;

namespace LinearAlgebra.Tests;

public class BlockLanczosTests
{
    private static void AssertAllDependenciesXorToZero(SolveResult result, IReadOnlyList<RelationRow> rows)
    {
        Assert.NotEmpty(result.Dependencies);
        foreach (var dep in result.Dependencies)
        {
            var acc = new HashSet<int>();
            foreach (var rowId in dep.RowIds)
            {
                foreach (var col in rows[rowId].Columns)
                {
                    if (!acc.Add(col))
                    {
                        acc.Remove(col);
                    }
                }
            }

            Assert.Empty(acc);
        }
    }

    [Fact]
    public void Solves_three_row_cycle()
    {
        var rows = new[] { new RelationRow(0, 2), new RelationRow(1, 2), new RelationRow(0, 1) };

        var result = BlockLanczos.Solve(rows, columnCount: 3);

        AssertAllDependenciesXorToZero(result, rows);
        Assert.Equal("block-lanczos", result.Solver);
        Assert.True(result.LanczosRuns > 0);
        Assert.True(result.LanczosDependencies > 0);
    }

    [Fact]
    public void Respects_max_dependencies()
    {
        var rows = new[] { RelationRow.Empty, RelationRow.Empty, RelationRow.Empty };

        var result = BlockLanczos.Solve(rows, columnCount: 1, maxDependencies: 2);

        Assert.Equal(2, result.Dependencies.Count);
    }

    [Fact]
    public void Zero_rows_are_emitted_as_free_dependencies_before_lanczos()
    {
        var rows = Enumerable.Repeat(RelationRow.Empty, 1)
            .Concat(new[] { new RelationRow(0, 2), new RelationRow(1, 2), new RelationRow(0, 1) })
            .ToArray();

        var result = BlockLanczos.Solve(rows, columnCount: 3, maxDependencies: 2);

        Assert.Equal(2, result.Dependencies.Count);
        Assert.Equal(new[] { 0 }, result.Dependencies[0].RowIds);
        Assert.Contains(result.Dependencies, dep => dep.RowIds.SequenceEqual(new[] { 1, 2, 3 }));
        Assert.True(result.LanczosRuns > 0);
        Assert.Equal(1, result.LanczosDependencies);
    }

    [Fact]
    public void Zero_rows_respect_the_total_dependency_budget()
    {
        var rows = Enumerable.Repeat(RelationRow.Empty, 4).ToArray();

        var result = BlockLanczos.Solve(rows, columnCount: 0, maxDependencies: 2);

        Assert.Equal(2, result.Dependencies.Count);
        Assert.All(result.Dependencies, dependency => Assert.Single(dependency.RowIds));
        Assert.Equal(0, result.LanczosRuns);
        Assert.Equal(0, result.LanczosDependencies);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    public void Solve_is_deterministic_across_parallelism_levels(int parallelism)
    {
        var rows = Enumerable.Range(0, 8)
            .SelectMany(_ => new[] { new RelationRow(0, 2), new RelationRow(1, 2), new RelationRow(0, 1) })
            .ToArray();
        var sequential = BlockLanczos.Solve(
            rows,
            columnCount: 3,
            options: new BlockLanczosOptions(
                BlockLanczosOptions.DefaultPostLanczosRows,
                BlockLanczosOptions.DefaultMinPostLanczosDimension,
                Parallelism: 1));

        var actual = BlockLanczos.Solve(
            rows,
            columnCount: 3,
            options: new BlockLanczosOptions(
                BlockLanczosOptions.DefaultPostLanczosRows,
                BlockLanczosOptions.DefaultMinPostLanczosDimension,
                parallelism));

        Assert.Equal(
            sequential.Dependencies.Select(d => d.RowIds.ToArray()).ToArray(),
            actual.Dependencies.Select(d => d.RowIds.ToArray()).ToArray());
    }

    [Fact]
    public void Solve_is_deterministic_when_parallel_vector_dot_is_used()
    {
        var rows = Enumerable.Range(0, 3_000)
            .SelectMany(_ => new[] { new RelationRow(0, 2), new RelationRow(1, 2), new RelationRow(0, 1) })
            .ToArray();
        var sequential = BlockLanczos.Solve(
            rows,
            columnCount: 3,
            options: new BlockLanczosOptions(
                BlockLanczosOptions.DefaultPostLanczosRows,
                BlockLanczosOptions.DefaultMinPostLanczosDimension,
                Parallelism: 1));

        var actual = BlockLanczos.Solve(
            rows,
            columnCount: 3,
            options: new BlockLanczosOptions(
                BlockLanczosOptions.DefaultPostLanczosRows,
                BlockLanczosOptions.DefaultMinPostLanczosDimension,
                Parallelism: 8));

        Assert.Equal(
            sequential.Dependencies.Select(d => d.RowIds.ToArray()).ToArray(),
            actual.Dependencies.Select(d => d.RowIds.ToArray()).ToArray());
    }
}
