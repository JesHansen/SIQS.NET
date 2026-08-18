using System.Numerics;
using SIQS.Contracts;
using SIQS.Contracts.Files;
using SIQS.Pipeline;

namespace SIQS.Pipeline.Tests;

/// <summary>
/// Test double for <see cref="IPhaseExecutor"/> that records call order and writes minimal valid
/// artifacts so orchestration (validation, job state, flow control) can be tested without running
/// SIQS work. Behavior is configurable via the flags.
/// </summary>
internal sealed class FakePhaseExecutor : IPhaseExecutor
{
    public List<SiqsPhase> Calls { get; } = new();
    public List<FactorizationRequest> SievingRequests { get; } = new();
    public List<FactorizationRequest> LinearAlgebraRequests { get; } = new();
    public Queue<(int NonZeroRows, int Columns)> FilteringShapes { get; } = new();
    public Queue<(int NonZeroRows, int Columns)> LinearAlgebraDeficits { get; } = new();
    public bool EarlyFactor { get; init; }
    public bool EarlyPrime { get; init; }
    public bool SquareRootFindsFactor { get; init; } = true;
    public SiqsPhase? FailAt { get; init; }
    public SiqsPhase? CancelAt { get; set; }

    public Task<PhaseResult> RunFactorBaseAsync(PhaseContext c)
    {
        Calls.Add(SiqsPhase.FactorBase);
        CancelIfRequested(SiqsPhase.FactorBase);
        if (FailAt == SiqsPhase.FactorBase)
        {
            return Task.FromResult(PhaseResult.Failed(SiqsPhase.FactorBase, "boom"));
        }

        var n = c.Request.TargetN;
        if (EarlyPrime)
        {
            var factors = new FactorsDocument(n, 1, n, 0,
            [
                new FactorResultRecord(
                    "precheck", FactorizationStatus.InputPrime, null, null, null, null, "input_is_prime"),
            ]);
            File.WriteAllText(Path.Combine(c.JobDirectory, "factors.txt"), FactorsFile.Write(factors));
            return Task.FromResult(PhaseResult.Completed(SiqsPhase.FactorBase, new[] { "factors.txt" },
                new Dictionary<string, string> { [CounterKeys.InputIsPrime] = CounterFormat.Bool(true) }));
        }

        if (EarlyFactor)
        {
            var factors = new FactorsDocument(n, 1, n, 0, new[]
            {
                new FactorResultRecord("precheck", FactorizationStatus.FactorFound, null, null, 2, n / 2, "even_target"),
            });
            File.WriteAllText(Path.Combine(c.JobDirectory, "factors.txt"), FactorsFile.Write(factors));
            return Task.FromResult(PhaseResult.Completed(SiqsPhase.FactorBase, new[] { "factors.txt" },
                new Dictionary<string, string>(), new PhaseFactorOutcome(2, n / 2)));
        }

        var rootModuloTwo = (long)(n & 1);
        var doc = new FactorBaseDocument(new FactorBaseMetadata(n, 1, n, 1000, 10.0),
            new[] { new FactorBaseEntry(1, 2, rootModuloTwo, rootModuloTwo, 5) });
        File.WriteAllText(Path.Combine(c.JobDirectory, "factor_base.txt"), FactorBaseFile.Write(doc));
        return Task.FromResult(PhaseResult.Completed(SiqsPhase.FactorBase, new[] { "factor_base.txt" }, new Dictionary<string, string>()));
    }

    public Task<PhaseResult> RunSievingAsync(PhaseContext c)
    {
        Calls.Add(SiqsPhase.Sieving);
        CancelIfRequested(SiqsPhase.Sieving);
        SievingRequests.Add(c.Request);
        if (FailAt == SiqsPhase.Sieving)
        {
            return Task.FromResult(PhaseResult.Failed(SiqsPhase.Sieving, "boom"));
        }

        var n = c.Request.TargetN;
        var meta = new RawRelationsMetadata(n, 1, n, 1000, 64000);
        File.WriteAllText(Path.Combine(c.JobDirectory, "relations_0000.txt"),
            RawRelationsFile.Write(new RawRelationsDocument(FileFormats.RawRelationsV1, meta, Array.Empty<RawRelationRecord>())));
        var relationTarget = c.Request.Sieving.RelationTarget ?? 1000;
        return Task.FromResult(PhaseResult.Completed(SiqsPhase.Sieving, new[] { "relations_0000.txt" },
            new Dictionary<string, string>
            {
                ["relations_needed"] = relationTarget.ToString(),
                ["polynomials"] = "100",
            }));
    }

    public Task<PhaseResult> RunFilteringAsync(PhaseContext c)
    {
        Calls.Add(SiqsPhase.Filtering);
        CancelIfRequested(SiqsPhase.Filtering);
        if (FailAt == SiqsPhase.Filtering)
        {
            return Task.FromResult(PhaseResult.Failed(SiqsPhase.Filtering, "boom"));
        }

        var n = c.Request.TargetN;
        var (nonZeroRows, columns) = FilteringShapes.Count > 0
            ? FilteringShapes.Dequeue()
            : (300, 2);
        var filteredRelations = Enumerable.Range(0, nonZeroRows)
            .Select(i => new FilteredRelationRecord(
                $"F{i:D8}",
                RelationKind.Full,
                new[] { $"R{i:D8}" },
                T: 2,
                Sign: 1,
                Exponents: new Dictionary<int, int> { [1] = 2 },
                ParityColumns: Array.Empty<int>(),
                LargePrime: null))
            .ToArray();
        var matrix = filteredRelations
            .Select((r, i) => new SparseMatrixRowRecord(
                i, r.RelationId, columns > 0 ? new[] { i % columns } : Array.Empty<int>()))
            .ToArray();
        File.WriteAllText(Path.Combine(c.JobDirectory, "relations_filtered.txt"),
            FilteredRelationsFile.Write(new FilteredRelationsDocument(n, 1, n, filteredRelations)));
        File.WriteAllText(Path.Combine(c.JobDirectory, "filtered_matrix.txt"),
            FilteredMatrixFile.Write(matrix));
        File.WriteAllText(Path.Combine(c.JobDirectory, "matrix_meta.txt"),
            MatrixMetaFile.Write(new MatrixMetadata(n, 1, n, nonZeroRows, columns, columns, 0, "filtered_matrix.txt", "relations_filtered.txt")));
        return Task.FromResult(PhaseResult.Completed(SiqsPhase.Filtering,
            new[] { "relations_filtered.txt", "filtered_matrix.txt", "matrix_meta.txt" },
            new Dictionary<string, string>
            {
                ["nonzero_rows"] = nonZeroRows.ToString(),
                ["columns"] = columns.ToString(),
                ["nonzero_row_surplus"] = (nonZeroRows - columns).ToString(),
            }));
    }

    public Task<PhaseResult> RunLinearAlgebraAsync(PhaseContext c)
    {
        Calls.Add(SiqsPhase.LinearAlgebra);
        CancelIfRequested(SiqsPhase.LinearAlgebra);
        LinearAlgebraRequests.Add(c.Request);
        if (FailAt == SiqsPhase.LinearAlgebra)
        {
            return Task.FromResult(PhaseResult.Failed(SiqsPhase.LinearAlgebra, "boom"));
        }

        if (LinearAlgebraDeficits.Count > 0)
        {
            var (nonZeroRows, columns) = LinearAlgebraDeficits.Dequeue();
            throw new UnderdeterminedMatrixException(nonZeroRows, columns);
        }

        var n = c.Request.TargetN;
        var meta = MatrixMetaFile.Parse(File.ReadAllText(Path.Combine(c.JobDirectory, "matrix_meta.txt")));
        var dependencyRows = meta.ColumnCount == 0
            ? new[] { 0 }
            : new[] { 0, meta.ColumnCount };
        var dependencyRelations = dependencyRows.Select(row => $"F{row:D8}").ToArray();
        File.WriteAllText(Path.Combine(c.JobDirectory, "dependencies.txt"),
            DependenciesFile.Write(new DependenciesDocument(
                n, 1, n, meta.RowCount, meta.ColumnCount,
                new[] { new DependencyRecord(0, dependencyRows, dependencyRelations) })));
        return Task.FromResult(PhaseResult.Completed(SiqsPhase.LinearAlgebra, new[] { "dependencies.txt" }, new Dictionary<string, string>()));
    }

    public Task<PhaseResult> RunSquareRootAsync(PhaseContext c)
    {
        Calls.Add(SiqsPhase.SquareRoot);
        CancelIfRequested(SiqsPhase.SquareRoot);
        if (FailAt == SiqsPhase.SquareRoot)
        {
            return Task.FromResult(PhaseResult.Failed(SiqsPhase.SquareRoot, "boom"));
        }

        var n = c.Request.TargetN;
        PhaseFactorOutcome? factor = SquareRootFindsFactor ? new PhaseFactorOutcome(7, n / 7) : null;
        var status = SquareRootFindsFactor ? FactorizationStatus.FactorFound : FactorizationStatus.NoFactor;
        var row = new FactorResultRecord("0", status, 7, n / 7,
            SquareRootFindsFactor ? 7 : null, SquareRootFindsFactor ? n / 7 : null, SquareRootFindsFactor ? null : "no_factor");
        File.WriteAllText(Path.Combine(c.JobDirectory, "factors.txt"),
            FactorsFile.Write(new FactorsDocument(n, 1, n, 1, new[] { row })));
        return Task.FromResult(PhaseResult.Completed(SiqsPhase.SquareRoot, new[] { "factors.txt" },
            new Dictionary<string, string> { ["dependencies_attempted"] = "1" }, factor));
    }

    private void CancelIfRequested(SiqsPhase phase)
    {
        if (CancelAt == phase)
        {
            throw new OperationCanceledException($"Canceled during {phase} test work.");
        }
    }
}
