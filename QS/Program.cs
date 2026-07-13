using QS;
using SIQS.Contracts;

const string Usage = "usage: qs <number> [options] or qs --resume <run-dir> [options]";

try
{
    if (args.Any(argument => argument.Equals("--help", StringComparison.OrdinalIgnoreCase)
        || argument.Equals("-h", StringComparison.OrdinalIgnoreCase)))
    {
        Console.WriteLine(Usage);
        return 0;
    }

    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
    var command = new FactorizationCommandHandler().ExecuteAsync(
        CliArguments.Parse(args), new ConsoleReporter(), cancellation.Token);
    var execution = await command;
    var result = execution.Result;
    Console.WriteLine();
    if (execution.TrialSieve)
    {
        TrialSieveReporter.Write(result);
    }
    else if (result.Status is JobStatus.CompletedFactorFound or JobStatus.CompletedTrivialFactor)
    {
        Console.WriteLine($"  {execution.Target} = {string.Join(" * ", result.Factors)}");
    }
    else if (result.Status == JobStatus.CompletedNoFactor)
    {
        Console.WriteLine($"  No non-trivial factor found ({result.AttemptedDependencies} dependencies attempted).");
    }
    else if (result.Status == JobStatus.Canceled)
    {
        Console.WriteLine("  Canceled.");
    }
    else
    {
        Console.WriteLine($"  Failed: {result.ErrorSummary}");
    }

    var artifactDirectory = execution.ArtifactDirectory.Length == 0
        ? Path.Combine("runs", result.JobId)
        : execution.ArtifactDirectory;
    Console.WriteLine($"  job {result.JobId} ({execution.Elapsed.TotalSeconds:F1}s) artifacts: {artifactDirectory}");
    return execution.TrialSieve ? 0
        : result.Status is JobStatus.CompletedFactorFound or JobStatus.CompletedTrivialFactor ? 0
        : result.Status == JobStatus.CompletedNoFactor ? 2 : 1;
}
catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException
    or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    Console.Error.WriteLine(Usage);
    return 1;
}
