using System.Numerics;
using SIQS.Contracts;
using SIQS.Contracts.Files;
using SIQS.Pipeline;

namespace SIQS.Pipeline.Tests;

/// <summary>
/// A matrix with fewer nonzero rows than columns can still carry free zero-row dependencies. These
/// tests run the real linear-algebra and square-root phases on a hand-checked N = 77 fixture.
/// </summary>
public sealed class ZeroRowDependencyTests : IDisposable
{
    private static readonly BigInteger N = 77;

    // 9^2 - 77 = 2^2: gcd(9 - 2, 77) = 7 splits N.
    private static readonly ZeroRow Splitting = new(9, new Dictionary<int, int> { [1] = 2 });

    // 39^2 - 77 = 38^2 = 2^2 * 19^2: gcd(39 - 38, 77) = 1 is trivial.
    private static readonly ZeroRow Trivial = new(39, new Dictionary<int, int> { [1] = 2, [2] = 2 });

    private readonly string _directory = Directory.CreateTempSubdirectory("siqs-zero-row-").FullName;

    [Fact]
    public async Task Underdetermined_matrix_tries_its_zero_row_dependency_and_finds_the_factor()
    {
        WriteFixture(_directory, [Splitting]);
        var executor = new RealPhaseExecutor();
        var context = new PhaseContext("zero-row", _directory, new FactorizationRequest(N), null, CancellationToken.None);

        var linearAlgebra = await executor.RunLinearAlgebraAsync(context);
        var squareRoot = await executor.RunSquareRootAsync(context);

        var dependency = Assert.Single(DependenciesFile.Parse(
            File.ReadAllText(Path.Combine(_directory, "dependencies.txt"))).Dependencies);
        Assert.Equal([0], dependency.RowIds);
        Assert.Equal("1", linearAlgebra.Counters[CounterKeys.UnderdeterminedDeficit]);
        Assert.Equal(new PhaseFactorOutcome(7, 11), squareRoot.Factor);
    }

    [Fact]
    public async Task Zero_row_dependencies_respect_the_dependency_budget()
    {
        WriteFixture(_directory, [Trivial, Splitting]);
        var request = new FactorizationRequest(N) { LinearAlgebra = new LinearAlgebraRunOptions { MaxDependencies = 1 } };

        await new RealPhaseExecutor().RunLinearAlgebraAsync(
            new PhaseContext("zero-row", _directory, request, null, CancellationToken.None));

        var dependency = Assert.Single(DependenciesFile.Parse(
            File.ReadAllText(Path.Combine(_directory, "dependencies.txt"))).Dependencies);
        Assert.Equal([0], dependency.RowIds);
    }

    [Fact]
    public async Task Underdetermined_matrix_without_zero_rows_still_requests_a_top_up()
    {
        WriteFixture(_directory, []);

        await Assert.ThrowsAsync<UnderdeterminedMatrixException>(() => new RealPhaseExecutor().RunLinearAlgebraAsync(
            new PhaseContext("zero-row", _directory, new FactorizationRequest(N), null, CancellationToken.None)));
    }

    [Fact]
    public async Task Trivial_zero_row_dependencies_top_up_instead_of_completing()
    {
        var executor = new FixtureExecutor([Trivial], [Splitting]);
        var pipeline = new SiqsPipeline(executor);

        var result = await pipeline.RunAsync(
            new FactorizationRequest(N) { RunDirectory = _directory }, null, CancellationToken.None);

        Assert.Equal(JobStatus.CompletedFactorFound, result.Status);
        Assert.Equal(2, executor.FilteringRounds);
        Assert.Single(pipeline.LoadJob(_directory).TopUpRounds);
    }

    [Fact]
    public async Task Only_trivial_zero_row_dependencies_never_report_a_finished_factorization()
    {
        var pipeline = new SiqsPipeline(new FixtureExecutor([Trivial]));

        var result = await pipeline.RunAsync(
            new FactorizationRequest(N) { RunDirectory = _directory }, null, CancellationToken.None);

        Assert.Equal(JobStatus.Failed, result.Status);
        Assert.Equal(TopUpCoordinator.MaxRounds, pipeline.LoadJob(_directory).TopUpRounds.Count);
    }

    /// <summary>
    /// Writes a five-column matrix: the given zero rows first, then four independent nonzero rows,
    /// so nonzero rows (4) are one short of the column count (5).
    /// </summary>
    private static void WriteFixture(string directory, IReadOnlyList<ZeroRow> zeroRows)
    {
        File.WriteAllText(Path.Combine(directory, "factor_base.txt"), FactorBaseFile.Write(new FactorBaseDocument(
            new FactorBaseMetadata(N, 1, N, 1000, 10.0),
            [new FactorBaseEntry(1, 2, 1, 1, 1), new FactorBaseEntry(2, 19, 1, 18, 4)])));
        int[][] nonZeroColumns = [[1, 2, 3, 4], [0, 2, 3, 4], [0, 1, 3, 4], [0, 1, 2, 4]];
        var relations = zeroRows
            .Select(row => new FilteredRelationRecord(
                "", RelationKind.Full, ["R"], row.T, 1, row.Exponents, [], null))
            .Concat(nonZeroColumns.Select(columns => new FilteredRelationRecord(
                "", RelationKind.Full, ["R"], 2, 1, new Dictionary<int, int> { [1] = 1 }, columns, null)))
            .Select((relation, index) => relation with
            {
                RelationId = $"F{index:D8}",
                SourceRelationIds = [$"R{index:D8}"],
            })
            .ToArray();
        File.WriteAllText(Path.Combine(directory, "relations_filtered.txt"),
            FilteredRelationsFile.Write(new FilteredRelationsDocument(N, 1, N, relations)));
        File.WriteAllText(Path.Combine(directory, "filtered_matrix.txt"), FilteredMatrixFile.Write(
            relations.Select((relation, index) => new SparseMatrixRowRecord(index, relation.RelationId, relation.ParityColumns))
                .ToArray()));
        File.WriteAllText(Path.Combine(directory, "matrix_meta.txt"), MatrixMetaFile.Write(new MatrixMetadata(
            N, 1, N, relations.Length, 5, 2, 0, "filtered_matrix.txt", "relations_filtered.txt")));
    }

    private sealed record ZeroRow(BigInteger T, IReadOnlyDictionary<int, int> Exponents);

    /// <summary>Real linear algebra and square root over a scripted filtering fixture per round.</summary>
    private sealed class FixtureExecutor(params IReadOnlyList<ZeroRow>[] rounds) : IPhaseExecutor
    {
        private readonly FakePhaseExecutor _fake = new();
        private readonly RealPhaseExecutor _real = new();

        public int FilteringRounds { get; private set; }

        public Task<PhaseResult> RunFactorBaseAsync(PhaseContext context) => _fake.RunFactorBaseAsync(context);
        public Task<PhaseResult> RunSievingAsync(PhaseContext context) => _fake.RunSievingAsync(context);
        public Task<PhaseResult> RunLinearAlgebraAsync(PhaseContext context) => _real.RunLinearAlgebraAsync(context);
        public Task<PhaseResult> RunSquareRootAsync(PhaseContext context) => _real.RunSquareRootAsync(context);

        public Task<PhaseResult> RunFilteringAsync(PhaseContext context)
        {
            WriteFixture(context.JobDirectory, rounds[Math.Min(FilteringRounds++, rounds.Length - 1)]);
            return Task.FromResult(PhaseResult.Completed(SiqsPhase.Filtering,
                ["relations_filtered.txt", "filtered_matrix.txt", "matrix_meta.txt"],
                new Dictionary<string, string>()));
        }
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
