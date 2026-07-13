using System.Diagnostics;
using System.Globalization;
using LinearAlgebra;
using SIQS.Contracts;
using SIQS.Contracts.Files;

namespace QS_LinAlg;

/// <summary>Loads, validates, solves, and writes a sparse linear-algebra command.</summary>
internal sealed class LinearAlgebraCommandHandler
{
    public LinearAlgebraCommandResult Execute(CliArguments cli, IProgress<BlockLanczosProgress> progress)
    {
        var metaPath = cli.GetOptional("meta") ?? "matrix_meta.txt";
        var matrixPath = cli.GetOptional("matrix") ?? "filtered_matrix.txt";
        var relationsPath = cli.GetOptional("relations") ?? "relations_filtered.txt";
        var outPath = cli.GetOptional("out") ?? "dependencies.txt";
        var telemetryPath = cli.GetOptional("telemetry")
            ?? Path.Combine(Path.GetDirectoryName(outPath) ?? ".", "linalg_telemetry.txt");
        var maxDependencies = cli.GetOptionalInt("max-dependencies") ?? BlockLanczos.DefaultMaxDependencies;
        var requestedParallelism = cli.GetOptionalInt("linalg-parallelism") ?? BlockLanczosOptions.DefaultParallelism;
        if (requestedParallelism < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedParallelism), "Linear algebra parallelism must be zero or positive.");
        }

        var options = new BlockLanczosOptions(
            BlockLanczosOptions.DefaultPostLanczosRows,
            BlockLanczosOptions.DefaultMinPostLanczosDimension,
            requestedParallelism);
        var total = Stopwatch.StartNew();
        var watch = Stopwatch.StartNew();
        var meta = MatrixMetaFile.Parse(File.ReadAllText(metaPath));
        var metaMs = watch.ElapsedMilliseconds;
        watch.Restart();
        var matrix = FilteredMatrixFile.Parse(File.ReadAllText(matrixPath));
        var matrixMs = watch.ElapsedMilliseconds;
        watch.Restart();
        var relations = FilteredRelationsFile.Parse(File.ReadAllText(relationsPath));
        var relationsMs = watch.ElapsedMilliseconds;
        watch.Restart();
        Validate(meta, matrix, relations);
        var validationMs = watch.ElapsedMilliseconds;

        var ordered = matrix.OrderBy(row => row.RowId).ToArray();
        var rows = ordered.Select(row => RelationRow.FromColumns(row.Columns)).ToArray();
        var stats = MatrixStats(rows);
        var nonZeroSurplus = stats.NonZeroRows - meta.ColumnCount;
        var solveWatch = Stopwatch.StartNew();
        var solve = BlockLanczos.Solve(rows, meta.ColumnCount, maxDependencies, options, progress);
        solveWatch.Stop();
        if (solve.Dependencies.Count == 0)
        {
            throw new InvalidOperationException($"Block Lanczos recovered no verified dependencies after {solve.LanczosRuns} run(s).");
        }

        var dependencies = solve.Dependencies.Select((dependency, id) => new DependencyRecord(
            id, dependency.RowIds.ToArray(), dependency.RowIds.Select(row => ordered[row].RelationId).ToArray())).ToArray();
        var zeroRowDependencies = dependencies.Count(d => d.RowIds.Count == 1 && rows[d.RowIds[0]].Count == 0);
        var dependencyStats = DependencyStats(dependencies.Select(d => d.RowIds.Count).ToArray());
        var doc = new DependenciesDocument(meta.TargetN, meta.Multiplier, meta.ScaledN, meta.RowCount, meta.ColumnCount, dependencies);
        CreateParentDirectory(outPath);
        CreateParentDirectory(telemetryPath);
        watch.Restart();
        File.WriteAllText(outPath, DependenciesFile.Write(doc));
        var writeMs = watch.ElapsedMilliseconds;
        total.Stop();

        var telemetry = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["solver"] = solve.Solver,
            ["max_dependencies"] = maxDependencies.ToString(CultureInfo.InvariantCulture),
            ["linalg_parallelism_requested"] = requestedParallelism.ToString(CultureInfo.InvariantCulture),
            ["linalg_parallelism"] = options.EffectiveParallelism.ToString(CultureInfo.InvariantCulture),
            ["rows"] = meta.RowCount.ToString(CultureInfo.InvariantCulture),
            ["columns"] = meta.ColumnCount.ToString(CultureInfo.InvariantCulture),
            ["zero_rows"] = stats.ZeroRows.ToString(CultureInfo.InvariantCulture),
            ["nonzero_rows"] = stats.NonZeroRows.ToString(CultureInfo.InvariantCulture),
            ["nonzero_row_surplus"] = nonZeroSurplus.ToString(CultureInfo.InvariantCulture),
            ["total_row_weight"] = stats.TotalRowWeight.ToString(CultureInfo.InvariantCulture),
            ["avg_row_weight"] = stats.AverageRowWeight.ToString("0.###", CultureInfo.InvariantCulture),
            ["max_row_weight"] = stats.MaxRowWeight.ToString(CultureInfo.InvariantCulture),
            ["row_weight_p50"] = stats.RowWeightP50.ToString(CultureInfo.InvariantCulture),
            ["row_weight_p90"] = stats.RowWeightP90.ToString(CultureInfo.InvariantCulture),
            ["row_weight_p99"] = stats.RowWeightP99.ToString(CultureInfo.InvariantCulture),
            ["pivots"] = solve.Pivots.ToString(CultureInfo.InvariantCulture),
            ["dependencies"] = dependencies.Length.ToString(CultureInfo.InvariantCulture),
            ["lanczos_runs"] = solve.LanczosRuns.ToString(CultureInfo.InvariantCulture),
            ["lanczos_dependencies"] = solve.LanczosDependencies.ToString(CultureInfo.InvariantCulture),
            ["lanczos_run_ms"] = Join(solve.LanczosRunMilliseconds),
            ["lanczos_run_dimensions"] = Join(solve.LanczosRunDimensions),
            ["zero_row_dependencies"] = zeroRowDependencies.ToString(CultureInfo.InvariantCulture),
            ["zero_row_dependencies_omitted"] = Math.Max(0, stats.ZeroRows - zeroRowDependencies).ToString(CultureInfo.InvariantCulture),
            ["dependency_rows_min"] = dependencyStats.Min.ToString(CultureInfo.InvariantCulture),
            ["dependency_rows_avg"] = dependencyStats.Average.ToString("0.###", CultureInfo.InvariantCulture),
            ["dependency_rows_max"] = dependencyStats.Max.ToString(CultureInfo.InvariantCulture),
            ["load_meta_ms"] = metaMs.ToString(CultureInfo.InvariantCulture),
            ["load_matrix_ms"] = matrixMs.ToString(CultureInfo.InvariantCulture),
            ["load_relations_ms"] = relationsMs.ToString(CultureInfo.InvariantCulture),
            ["validate_ms"] = validationMs.ToString(CultureInfo.InvariantCulture),
            ["solve_ms"] = solveWatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture),
            ["write_ms"] = writeMs.ToString(CultureInfo.InvariantCulture),
            ["total_ms"] = total.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture),
        };
        File.WriteAllLines(telemetryPath, telemetry.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"));
        return new LinearAlgebraCommandResult(meta, solve, dependencies, stats, outPath, telemetryPath, nonZeroSurplus);
    }

    private static string Join<T>(IReadOnlyList<T>? values)
        => values is null || values.Count == 0 ? string.Empty : string.Join(",", values);

    private static void Validate(MatrixMetadata meta, IReadOnlyList<SparseMatrixRowRecord> matrix, FilteredRelationsDocument relations)
    {
        if (matrix.Count != meta.RowCount)
        {
            throw new FormatException($"matrix_meta row_count {meta.RowCount} does not match matrix file ({matrix.Count} rows).");
        }

        var ordered = matrix.OrderBy(row => row.RowId).ToArray();
        for (var i = 0; i < ordered.Length; i++)
        {
            if (ordered[i].RowId != i)
            {
                throw new FormatException("Matrix row ids must be contiguous starting at 0.");
            }
        }

        var relationIds = relations.Relations.Select(relation => relation.RelationId).ToHashSet(StringComparer.Ordinal);
        foreach (var row in ordered)
        {
            if (!relationIds.Contains(row.RelationId))
            {
                throw new FormatException($"Matrix row {row.RowId} references unknown relation id '{row.RelationId}'.");
            }

            if (row.Columns.Any(column => column < 0 || column >= meta.ColumnCount))
            {
                throw new FormatException($"Matrix row {row.RowId} references a column outside [0, {meta.ColumnCount}).");
            }
        }
    }

    private static MatrixTelemetry MatrixStats(IReadOnlyList<RelationRow> rows)
    {
        var weights = rows.Select(row => row.Count).Order().ToArray();
        var zero = weights.Count(weight => weight == 0);
        var total = weights.Sum(weight => (long)weight);
        return new MatrixTelemetry(zero, rows.Count - zero, total, rows.Count == 0 ? 0 : (double)total / rows.Count,
            weights.Length == 0 ? 0 : weights[^1], Percentile(weights, .50), Percentile(weights, .90), Percentile(weights, .99));
    }

    private static DependencyTelemetry DependencyStats(IReadOnlyList<int> values)
        => values.Count == 0 ? new(0, 0, 0) : new(values.Min(), values.Average(), values.Max());

    private static int Percentile(IReadOnlyList<int> values, double percentile)
    {
        if (values.Count == 0) return 0;
        return values[Math.Clamp((int)Math.Ceiling(values.Count * percentile) - 1, 0, values.Count - 1)];
    }

    private static void CreateParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
    }
}

internal sealed record LinearAlgebraCommandResult(
    MatrixMetadata Metadata,
    SolveResult Solve,
    IReadOnlyList<DependencyRecord> Dependencies,
    MatrixTelemetry Stats,
    string OutputPath,
    string TelemetryPath,
    int NonZeroSurplus);

internal sealed record MatrixTelemetry(int ZeroRows, int NonZeroRows, long TotalRowWeight, double AverageRowWeight, int MaxRowWeight, int RowWeightP50, int RowWeightP90, int RowWeightP99);
internal sealed record DependencyTelemetry(int Min, double Average, int Max);
