using System.Numerics;
using Filtering;
using LinearAlgebra;
using SIQS.Contracts;
using SIQS.Contracts.Files;
using SquareRoot;

namespace SIQS.Pipeline.Tests;

/// <summary>
/// Drives a tiny hand-checkable fixture (N = 77) through the Filtering → LinearAlgebra →
/// SquareRoot phases, verifying the core file contracts compose end to end and yield 77 = 7 × 11.
/// The main instructions now use a larger generated C29 example; this test remains intentionally
/// small so failures are easy to inspect.
/// </summary>
public class EndToEndExampleTests
{
    private const string FactorBaseText =
        "# format=siqs-factor-base-v1\n" +
        "# target_n=77\n# multiplier=1\n# scaled_n=77\n# bound=2\n# log_scale=255\n" +
        "# columns=index,prime,root1,root2,logp\n" +
        "index,prime,root1,root2,logp\n" +
        "1,2,0,0,255\n";

    private const string RelationsText =
        "# format=siqs-raw-relations-v1\n" +
        "# target_n=77\n# multiplier=1\n# scaled_n=77\n# factor_base_bound=2\n# large_prime_bound=128\n" +
        "# columns=relation_id,kind,poly_id,a,b,c,x,t,sign,factor_exponents,parity_columns,large_prime\n" +
        "relation_id,kind,poly_id,a,b,c,x,t,sign,factor_exponents,parity_columns,large_prime\n" +
        "R00000000,full,P00000000,1,0,-77,9,9,1,1:2,,\n";

    [Fact]
    public void Fixture_composes_through_filtering_linalg_and_square_root()
    {
        var factorBase = FactorBaseFile.Parse(FactorBaseText);
        var rawRelations = RawRelationsFile.Parse(RelationsText);

        // Filtering: the single full relation has an empty parity vector (exponent 2 is even),
        // so it survives singleton pruning as an already-zero row.
        var filtering = FilteringEngine.Run(factorBase, rawRelations.Relations, Array.Empty<RawRelationRecord>());
        var filteredRelation = Assert.Single(filtering.Relations.Relations);
        Assert.Equal("F00000000", filteredRelation.RelationId);
        Assert.Empty(filteredRelation.ParityColumns);
        Assert.Equal(0, filtering.Meta.ColumnCount); // no active parity columns after compaction

        // Linear algebra: the zero row is immediately a dependency.
        var solve = BlockLanczos.Solve(
            filtering.Matrix.Select(r => RelationRow.FromColumns(r.Columns)).ToArray(), filtering.Meta.ColumnCount);
        var dependency = Assert.Single(solve.Dependencies);
        Assert.Equal(new[] { 0 }, dependency.RowIds);

        var dependencies = new DependenciesDocument(77, 1, 77, filtering.Meta.RowCount, filtering.Meta.ColumnCount,
            new[] { new DependencyRecord(0, new[] { 0 }, new[] { "F00000000" }) });

        // Square root: X = 9, Y = 2 -> gcd(7,77)=7, gcd(11,77)=11.
        var result = SquareRootEngine.Run(factorBase, filtering.Relations, dependencies);
        Assert.Equal(new BigInteger(7), result.Factor1);
        Assert.Equal(new BigInteger(11), result.Factor2);

        var row = Assert.Single(result.Factors.Results);
        Assert.Equal(FactorizationStatus.FactorFound, row.Status);
        Assert.Equal(new BigInteger(7), row.GcdMinus);
        Assert.Equal(new BigInteger(11), row.GcdPlus);
    }
}
