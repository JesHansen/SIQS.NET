using System.Numerics;
using Factorbase;
using SIQS.Contracts;

namespace Factorbase.Tests;

public sealed class FactorBaseCancellationTests
{
    [Fact]
    public void Cancellation_after_generation_starts_interrupts_prime_generation()
    {
        using var cancellation = new CancellationTokenSource();
        var progress = new CancelingProgress(cancellation);
        var options = new FactorBaseOptions(
            BigInteger.Parse("1022117"),
            Bound: 1_000_000,
            Multiplier: 1,
            AllowTinyInputTrialDivision: false);

        Assert.Throws<OperationCanceledException>(
            () => FactorBaseGenerator.Generate(options, progress, cancellation.Token));
        Assert.True(progress.GenerationStarted);
    }

    private sealed class CancelingProgress(CancellationTokenSource cancellation)
        : IProgress<SiqsProgressEvent>
    {
        public bool GenerationStarted { get; private set; }

        public void Report(SiqsProgressEvent value)
        {
            if (value.Message == "generating primes")
            {
                GenerationStarted = true;
                cancellation.Cancel();
            }
        }
    }
}
