using System.Numerics;
using SIQS.Contracts;
using SIQS.Pipeline;
using SIQS.UI.Services;

namespace SIQS.UI.Tests;

public sealed class FactorizationJobLifetimeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "siqs-local-lifetime-tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(SiqsPhase.FactorBase)]
    [InlineData(SiqsPhase.Sieving)]
    [InlineData(SiqsPhase.Filtering)]
    [InlineData(SiqsPhase.LinearAlgebra)]
    [InlineData(SiqsPhase.SquareRoot)]
    public async Task Stop_joins_a_job_in_each_pipeline_phase_and_rejects_restart(SiqsPhase phase)
    {
        var pipeline = new BlockingPipeline(phase);
        await using var service = new FactorizationJobService(pipeline, new RunsDirectory(_root));
        service.Start(new FactorizationRequest(91));
        await pipeline.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await service.StopAsync(CancellationToken.None);

        Assert.True(service.Completion!.IsCompleted);
        Assert.Equal(JobStatus.Canceled, service.Current!.Status);
        Assert.Throws<InvalidOperationException>(() => service.Start(new FactorizationRequest(91)));
        await service.StopAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class BlockingPipeline(SiqsPhase phase) : ISiqsPipeline
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FactorizationRequest NormalizeAndValidate(FactorizationRequest request) => request;

        public async Task<FactorizationJobResult> RunAsync(
            FactorizationRequest request,
            IProgress<SiqsProgressEvent>? progress,
            CancellationToken cancellationToken,
            string? requestedJobId = null)
        {
            progress?.Report(new SiqsProgressEvent(
                DateTimeOffset.UtcNow,
                requestedJobId,
                phase,
                ProgressLevel.Info,
                "test phase running",
                null,
                new Dictionary<string, string>(),
                null));
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }

            return new FactorizationJobResult(
                requestedJobId!,
                JobStatus.Canceled,
                request.TargetN,
                false,
                Array.Empty<BigInteger>(),
                0,
                Array.Empty<string>(),
                Array.Empty<PhaseSummary>(),
                null);
        }

        public Task<FactorizationJobResult> ResumeAsync(
            string jobDirectory,
            FactorizationRequest? overrides,
            IProgress<SiqsProgressEvent>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public IReadOnlyList<ArtifactDescriptor> GetExpectedArtifacts() => Array.Empty<ArtifactDescriptor>();
        public JobState LoadJob(string jobDirectory) => throw new NotSupportedException();
    }
}
