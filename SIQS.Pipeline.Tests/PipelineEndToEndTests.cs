using System.Numerics;
using SIQS.Contracts;
using SIQS.Pipeline;

namespace SIQS.Pipeline.Tests;

public class PipelineEndToEndTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "siqs-e2e", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public async Task Factors_a_small_composite_through_real_phases()
    {
        var pipeline = new SiqsPipeline(); // real executor
        var request = new FactorizationRequest(BigInteger.Parse("1022117")) // 1009 * 1013
        {
            RunDirectory = _dir,
            FactorBase = new FactorBaseRunOptions { Bound = 1000, Multiplier = 1 },
            Sieving = new SievingRunOptions
            {
                HalfInterval = 20_000,
                APrimeCount = 2,
                APrimeWindowSize = 24,
                ErrorMargin = 20,
                RelationTarget = 150,
                PolynomialCount = 200_000,
            },
        };

        var captured = new List<SiqsProgressEvent>();
        var result = await pipeline.RunAsync(request, new SynchronousProgress(captured), CancellationToken.None);

        Assert.Equal(JobStatus.CompletedFactorFound, result.Status);
        Assert.True(result.FactorFound);
        Assert.Equal(new BigInteger(1022117), result.Factors[0] * result.Factors[1]);
        Assert.Contains(new BigInteger(1009), result.Factors);
        Assert.Contains(new BigInteger(1013), result.Factors);
        Assert.Contains(captured, e =>
            e.Phase == SiqsPhase.LinearAlgebra
            && e.Message == "run-start"
            && e.Counters.TryGetValue("target_dimensions", out var target)
            && int.Parse(target) > 0);
        Assert.Contains(captured, e =>
            e.Phase == SiqsPhase.LinearAlgebra
            && e.Counters.ContainsKey("dimensions_solved"));

        // All expected artifacts exist on disk.
        foreach (var name in new[] { "factor_base.txt", "relations_0000.txt", "matrix_meta.txt",
                     "filtered_matrix.txt", "relations_filtered.txt", "dependencies.txt", "factors.txt", "job.json", "events.log" })
        {
            Assert.True(File.Exists(Path.Combine(_dir, name)), $"missing {name}");
        }

        var eventsLog = await File.ReadAllTextAsync(Path.Combine(_dir, "events.log"));
        Assert.Contains("\"phase\":\"linear_algebra\"", eventsLog);
        Assert.Contains("\"message\":\"run-start\"", eventsLog);
    }

    [Fact]
    public async Task Trivial_factor_short_circuits_through_real_phases()
    {
        var pipeline = new SiqsPipeline();
        var request = new FactorizationRequest(BigInteger.Parse("1000000")) { RunDirectory = _dir }; // even

        var result = await pipeline.RunAsync(request, null, CancellationToken.None);

        Assert.Equal(JobStatus.CompletedTrivialFactor, result.Status);
        Assert.Equal(new BigInteger(2), result.Factors[0]);
        Assert.True(File.Exists(Path.Combine(_dir, "factors.txt")));
        Assert.False(File.Exists(Path.Combine(_dir, "factor_base.txt")));
    }

    private sealed class SynchronousProgress : IProgress<SiqsProgressEvent>
    {
        private readonly List<SiqsProgressEvent> _sink;

        public SynchronousProgress(List<SiqsProgressEvent> sink) => _sink = sink;

        public void Report(SiqsProgressEvent value) => _sink.Add(value);
    }
}
