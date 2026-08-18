using SIQS.Contracts;
using SIQS.Contracts.Files;
using LinearAlgebra;

namespace SIQS.Pipeline;

internal sealed class LinearAlgebraPhaseRunner
{
    public Task<PhaseResult> RunAsync(PhaseContext context) => Task.Run(() =>
    {
        var meta = MatrixMetaFile.Parse(PhaseArtifactStore.Read(context, "matrix_meta.txt"));
        var rows = FilteredMatrixFile.Parse(PhaseArtifactStore.Read(context, "filtered_matrix.txt"))
            .OrderBy(r => r.RowId).ToArray();
        var maxDependencies = context.Request.LinearAlgebra.MaxDependencies ?? BlockLanczos.DefaultMaxDependencies;
        var linalgParallelism = context.Request.LinearAlgebra.Parallelism ?? BlockLanczosOptions.DefaultParallelism;
        var linalgOptions = new BlockLanczosOptions(
            BlockLanczosOptions.DefaultPostLanczosRows,
            BlockLanczosOptions.DefaultMinPostLanczosDimension,
            linalgParallelism);
        var relationRows = rows.Select(r => RelationRow.FromColumns(r.Columns)).ToArray();
        var zeroRowCount = relationRows.Count(r => r.Count == 0);
        var nonZeroRowCount = relationRows.Length - zeroRowCount;
        if (nonZeroRowCount < meta.ColumnCount)
        {
            throw new UnderdeterminedMatrixException(nonZeroRowCount, meta.ColumnCount);
        }

        var solve = BlockLanczos.Solve(
            relationRows,
            meta.ColumnCount,
            maxDependencies,
            linalgOptions,
            PhaseArtifactStore.LinearAlgebraProgressBridge.For(context),
            context.CancellationToken);
        if (solve.Dependencies.Count == 0)
        {
            throw new InvalidOperationException($"Block Lanczos recovered no verified dependencies after {solve.LanczosRuns} run(s).");
        }

        var deps = solve.Dependencies
            .Select((dependency, id) => new DependencyRecord(
                id, dependency.RowIds.ToArray(), dependency.RowIds.Select(r => rows[r].RelationId).ToArray()))
            .ToArray();
        var doc = new DependenciesDocument(meta.TargetN, meta.Multiplier, meta.ScaledN, meta.RowCount, meta.ColumnCount, deps);
        PhaseArtifactStore.Write(context, "dependencies.txt", DependenciesFile.Write(doc));
        return PhaseResult.Completed(SiqsPhase.LinearAlgebra, new[] { "dependencies.txt" },
            new Dictionary<string, string>
            {
                ["solver"] = solve.Solver,
                ["lanczos_runs"] = solve.LanczosRuns.ToString(),
                ["lanczos_dependencies"] = solve.LanczosDependencies.ToString(),
                ["lanczos_candidates_extracted"] = solve.LanczosCandidatesExtracted.ToString(),
                ["lanczos_stop_reason"] = solve.StopReason,
                ["pivots"] = solve.Pivots.ToString(),
                ["dependencies"] = deps.Length.ToString(),
                ["linalg_parallelism_requested"] = linalgParallelism.ToString(),
                ["linalg_parallelism"] = linalgOptions.EffectiveParallelism.ToString(),
                ["zero_rows"] = zeroRowCount.ToString(),
                ["nonzero_rows"] = nonZeroRowCount.ToString(),
                ["nonzero_row_surplus"] = (nonZeroRowCount - meta.ColumnCount).ToString(),
            });
    }, context.CancellationToken);
}
