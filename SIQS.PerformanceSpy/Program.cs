using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using CompositeGenerator;
using SIQS.Contracts;
using SIQS.Pipeline;
using SIQS.PerformanceSpy;

const string Usage =
    "usage: dotnet run --project SIQS.PerformanceSpy -c Release -- " +
    "[--start-digits N] [--end-digits N] [--target-seconds-per-size S] " +
    "[--max-composites-per-size N] [--regression-tolerance R] [--output PATH] [--scratch DIR]";

try
{
    if (args.Any(a => a is "--help" or "-h"))
    {
        Console.WriteLine(Usage);
        return 0;
    }

    var options = PerformanceSpyOptions.Parse(args);
    var cases = new List<PerformanceCase>();
    var pipeline = new SiqsPipeline();
    var scratchRoot = Path.GetFullPath(options.ScratchDirectory);

    Directory.CreateDirectory(scratchRoot);

    Console.WriteLine("Generating local SIQS performance spy baseline");
    Console.WriteLine($"{Prefix("[digits]")}C{options.StartDigits}..C{options.EndDigits}");
    Console.WriteLine($"{Prefix("[calibration]")}{options.TargetSecondsPerSize.ToString("F3", CultureInfo.InvariantCulture)}s per digit size");
    Console.WriteLine($"{Prefix("[output]")}{Path.GetFullPath(options.OutputPath)}");
    Console.WriteLine();

    for (var digits = options.StartDigits; digits <= options.EndDigits; digits++)
    {
        var composites = new List<BigInteger>();
        var total = TimeSpan.Zero;

        while (composites.Count < options.MaxCompositesPerSize &&
               (composites.Count == 0 || total.TotalSeconds < options.TargetSecondsPerSize))
        {
            var composite = SemiprimeGenerator.Generate(digits);
            var elapsed = await FactorAsync(pipeline, composite, scratchRoot, digits, composites.Count);
            composites.Add(composite);
            total += elapsed;
        }

        var average = total.TotalMilliseconds / composites.Count;
        cases.Add(new PerformanceCase(digits, composites.ToArray(), average));
        Console.WriteLine(
            $"{Prefix($"[C{digits}]")}{composites.Count} composite(s), {total.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)}s total, " +
            $"{average.ToString("F3", CultureInfo.InvariantCulture)} ms/composite");
    }

    WriteGeneratedTest(options, cases);

    Console.WriteLine();
    Console.WriteLine($"{Prefix("[write]")}generated performance spy test");
    Console.WriteLine($"{Prefix("[run]")}dotnet test --project SIQS.Pipeline.Tests/SIQS.Pipeline.Tests.csproj -c Release --no-restore -- --filter-class SIQS.Pipeline.Tests.GeneratedPerformanceSpyTests");

    return 0;
}
catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    Console.Error.WriteLine(Usage);
    return 1;
}

static async Task<TimeSpan> FactorAsync(SiqsPipeline pipeline, BigInteger composite, string scratchRoot, int digits, int index)
{
    var runDirectory = Path.Combine(
        scratchRoot,
        $"calibrate-C{digits}-{index:D4}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfffffff}");

    var stopwatch = Stopwatch.StartNew();
    var result = await pipeline.RunAsync(
        new FactorizationRequest(composite, RunDirectory: runDirectory),
        progress: null,
        CancellationToken.None);
    stopwatch.Stop();

    if (result.Status is not (JobStatus.CompletedFactorFound or JobStatus.CompletedTrivialFactor) || !result.FactorFound)
    {
        throw new InvalidOperationException($"Calibration failed to factor C{digits} composite {composite}: {result.ErrorSummary ?? result.Status.ToString()}.");
    }

    return stopwatch.Elapsed;
}

static void WriteGeneratedTest(PerformanceSpyOptions options, IReadOnlyList<PerformanceCase> cases)
{
    var outputPath = Path.GetFullPath(options.OutputPath);
    var outputDirectory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrWhiteSpace(outputDirectory))
    {
        Directory.CreateDirectory(outputDirectory);
    }

    File.WriteAllText(outputPath, PerformanceSpyTestSource.Generate(options, cases), Encoding.UTF8);
}

static string Prefix(string label) => $"  {label.PadRight(17)}";

internal sealed record PerformanceCase(int Digits, BigInteger[] Composites, double BaselineAverageMilliseconds);
