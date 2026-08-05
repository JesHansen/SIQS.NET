using System.Globalization;
using LinearAlgebra;
using QS_LinAlg;
using SIQS.Contracts.Cli;

const string Usage = "usage: qs-linalg --meta matrix_meta.txt --matrix filtered_matrix.txt --relations relations_filtered.txt --out dependencies.txt [--telemetry linalg_telemetry.txt] [--max-dependencies N] [--linalg-parallelism N] [--linalg-seed N]";

try
{
    if (CommandLine.IsHelpRequested(args))
    {
        Console.WriteLine(Usage);
        return 0;
    }

    var result = new LinearAlgebraCommandHandler().Execute(
        CommandLine.Parse(args, CommandLineSyntax.Strict), new ConsoleLanczosProgress());
    var stats = result.Stats;
    var percent = result.Metadata.ColumnCount > 0 ? 100.0 * result.NonZeroSurplus / result.Metadata.ColumnCount : 0.0;
    Console.WriteLine($"{ConsoleTelemetry.Prefix("[linear algebra]")}matrix={result.Metadata.RowCount}x{result.Metadata.ColumnCount}, nonzero={stats.NonZeroRows}, zero={stats.ZeroRows}, surplus={result.NonZeroSurplus} ({percent:0.###}%), solver={result.Solve.Solver}");
    Console.WriteLine($"{ConsoleTelemetry.Prefix("[linalg result]")}pivots={result.Solve.Pivots}, dependencies={result.Dependencies.Count}, lanczos-runs={result.Solve.LanczosRuns}, lanczos-dependencies={result.Solve.LanczosDependencies}");
    Console.WriteLine($"{ConsoleTelemetry.Prefix("[matrix weights]")}total={stats.TotalRowWeight}, avg={stats.AverageRowWeight:0.###}, max={stats.MaxRowWeight}, p50={stats.RowWeightP50}, p90={stats.RowWeightP90}, p99={stats.RowWeightP99}");
    Console.WriteLine($"{ConsoleTelemetry.Prefix("[write]")}{result.OutputPath}");
    Console.WriteLine($"{ConsoleTelemetry.Prefix("[write]")}{result.TelemetryPath}");
    return 0;
}
catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException or IOException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    Console.Error.WriteLine(Usage);
    return 1;
}

internal sealed class ConsoleLanczosProgress : IProgress<BlockLanczosProgress>
{
    public void Report(BlockLanczosProgress value)
    {
        var elapsed = value.Elapsed.TotalHours >= 1.0
            ? value.Elapsed.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : value.Elapsed.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        Console.Error.WriteLine($"{ConsoleTelemetry.Prefix("[linalg]")}{value.Stage} run={value.Run} iteration={value.Iteration} dimensions={value.DimensionsSolved}/{value.TargetDimensions} elapsed={elapsed}");
    }
}

internal static class ConsoleTelemetry
{
    public static string Prefix(string label) => $"  {label.PadRight(17)}";
}
