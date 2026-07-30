using System.Numerics;
using SIQS.Contracts;
using SIQS.Pipeline;

namespace QS.Presentation;

/// <summary>
/// Minimal presentation used with <c>--quiet</c> for embedding in a pipeline: on success it writes a
/// single line with the factor product to stdout and nothing else. Non-success outcomes stay off
/// stdout (so consumers never parse noise) and report a terse reason on stderr; the exit code carries
/// the real signal.
/// </summary>
internal sealed class QuietPresenter : IRunPresenter
{
    public void ShowTarget(BigInteger target)
    {
    }

    public Task<FactorizationJobResult> RunAsync(
        Func<IProgress<SiqsProgressEvent>, Task<FactorizationJobResult>> execute)
        => execute(new NullProgress());

    public void ShowOutcome(FactorizationCommandResult execution)
    {
        var result = execution.Result;
        if (!execution.TrialSieve
            && result.Status is JobStatus.CompletedFactorFound or JobStatus.CompletedTrivialFactor)
        {
            Console.WriteLine(string.Join(" * ", result.Factors));
            return;
        }

        var reason = result.Status switch
        {
            JobStatus.CompletedNoFactor => "no non-trivial factor found",
            JobStatus.CompletedPrime => "input is prime",
            JobStatus.Canceled => "canceled",
            _ when execution.TrialSieve => "trial sieve produced no factors",
            _ => result.ErrorSummary ?? "factorization failed",
        };
        Console.Error.WriteLine($"qs: {reason}");
    }

    private sealed class NullProgress : IProgress<SiqsProgressEvent>
    {
        public void Report(SiqsProgressEvent value)
        {
        }
    }
}
