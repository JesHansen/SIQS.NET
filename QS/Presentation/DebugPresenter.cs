using System.Numerics;
using SIQS.Contracts;
using SIQS.Pipeline;

namespace QS.Presentation;

/// <summary>
/// Verbose, diagnostic presentation used with <c>--debug</c>: the full per-phase counter dump and
/// CPU breakdowns produced by <see cref="ConsoleReporter"/>, plus the plain outcome and job lines.
/// This is the historical output, preserved verbatim for troubleshooting and benchmarking.
/// </summary>
internal sealed class DebugPresenter : IRunPresenter
{
    public void ShowTarget(BigInteger target)
    {
        Console.WriteLine($"  N = {target}  ({target.ToString().Length} digits)");
        Console.WriteLine();
    }

    public Task<FactorizationJobResult> RunAsync(
        Func<IProgress<SiqsProgressEvent>, Task<FactorizationJobResult>> execute)
        => execute(new ConsoleReporter());

    public void ShowOutcome(FactorizationCommandResult execution)
    {
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

        Console.WriteLine($"  job {result.JobId} ({execution.Elapsed.TotalSeconds:F1}s) artifacts: {execution.ResolvedArtifactDirectory}");
    }
}
