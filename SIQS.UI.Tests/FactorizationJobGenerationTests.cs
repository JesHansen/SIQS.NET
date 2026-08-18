using System.Collections.Concurrent;
using System.Numerics;
using SIQS.Contracts;
using SIQS.Pipeline;
using SIQS.UI.Services;

namespace SIQS.UI.Tests;

public sealed class FactorizationJobGenerationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "siqs-generation-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Delayed_callback_from_previous_job_cannot_mutate_new_job()
    {
        var pipeline = new ControlledPipeline();
        var first = pipeline.Enqueue();
        var second = pipeline.Enqueue();
        await using var service = new FactorizationJobService(pipeline, new RunsDirectory(_root));

        service.Start(new FactorizationRequest(91));
        await first.Invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        first.Report(SiqsPhase.FactorBase, "first-active");
        first.Complete(JobStatus.CompletedNoFactor);
        await service.Completion!.WaitAsync(TimeSpan.FromSeconds(5));

        service.Start(new FactorizationRequest(143));
        await second.Invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        second.Report(SiqsPhase.FactorBase, "second-active");
        var beforeStale = service.Current;

        first.Report(SiqsPhase.Sieving, "stale-first-event");

        Assert.Same(beforeStale, service.Current);
        Assert.Equal(new[] { "second-active" }, service.Events.Snapshot().Select(value => value.Message));

        second.Complete(JobStatus.CompletedNoFactor);
        await service.Completion!.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Late_event_from_terminal_generation_is_ignored_without_notification()
    {
        var pipeline = new ControlledPipeline();
        var run = pipeline.Enqueue();
        await using var service = new FactorizationJobService(pipeline, new RunsDirectory(_root));
        var changes = 0;
        service.Changed += () => Interlocked.Increment(ref changes);

        service.Start(new FactorizationRequest(91));
        await run.Invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        run.Complete(JobStatus.CompletedNoFactor);
        await service.Completion!.WaitAsync(TimeSpan.FromSeconds(5));
        var terminal = service.Current;
        var changesAtTerminal = Volatile.Read(ref changes);

        run.Report(SiqsPhase.Sieving, "late-running-event");

        Assert.Same(terminal, service.Current);
        Assert.Equal(changesAtTerminal, Volatile.Read(ref changes));
        Assert.DoesNotContain(service.Events.Snapshot(), value => value.Message == "late-running-event");
    }

    [Fact]
    public async Task Failed_generation_can_restart_and_cannot_report_into_replacement()
    {
        var pipeline = new ControlledPipeline();
        var failed = pipeline.Enqueue();
        var replacement = pipeline.Enqueue();
        await using var service = new FactorizationJobService(pipeline, new RunsDirectory(_root));

        service.Start(new FactorizationRequest(91));
        await failed.Invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        failed.Fail(new InvalidOperationException("controlled failure"));
        await service.Completion!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(JobStatus.Failed, service.Current!.Status);

        service.Start(new FactorizationRequest(143));
        await replacement.Invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        failed.Report(SiqsPhase.Filtering, "stale-after-failure");

        Assert.Equal(JobStatus.Running, service.Current!.Status);
        Assert.DoesNotContain(service.Events.Snapshot(), value => value.Message == "stale-after-failure");

        replacement.Complete(JobStatus.CompletedNoFactor);
        await service.Completion!.WaitAsync(TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class ControlledPipeline : ISiqsPipeline
    {
        private readonly ConcurrentQueue<RunSlot> _runs = new();

        public RunSlot Enqueue()
        {
            var run = new RunSlot();
            _runs.Enqueue(run);
            return run;
        }

        public FactorizationRequest NormalizeAndValidate(FactorizationRequest request) => request;

        public async Task<FactorizationJobResult> RunAsync(
            FactorizationRequest request,
            IProgress<SiqsProgressEvent>? progress,
            CancellationToken cancellationToken,
            string? requestedJobId = null)
        {
            if (!_runs.TryDequeue(out var run))
            {
                throw new InvalidOperationException("No controlled run was queued.");
            }

            run.Attach(progress!, requestedJobId!, request.TargetN);
            return await run.Result.Task.WaitAsync(cancellationToken);
        }

        public Task<FactorizationJobResult> ResumeAsync(
            string jobDirectory,
            FactorizationRequest? overrides,
            IProgress<SiqsProgressEvent>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public IReadOnlyList<ArtifactDescriptor> GetExpectedArtifacts() => Array.Empty<ArtifactDescriptor>();
        public JobState LoadJob(string jobDirectory) => throw new NotSupportedException();
    }

    private sealed class RunSlot
    {
        private IProgress<SiqsProgressEvent>? _progress;
        private string? _jobId;
        private BigInteger _target;

        public TaskCompletionSource Invoked { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<FactorizationJobResult> Result { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Attach(IProgress<SiqsProgressEvent> progress, string jobId, BigInteger target)
        {
            _progress = progress;
            _jobId = jobId;
            _target = target;
            Invoked.TrySetResult();
        }

        public void Report(SiqsPhase phase, string message)
            => _progress!.Report(new SiqsProgressEvent(
                DateTimeOffset.UtcNow,
                _jobId,
                phase,
                ProgressLevel.Info,
                message,
                null,
                new Dictionary<string, string>(),
                null));

        public void Complete(JobStatus status)
            => Result.TrySetResult(new FactorizationJobResult(
                _jobId!, status, _target, false, Array.Empty<BigInteger>(), 0,
                Array.Empty<string>(), Array.Empty<PhaseSummary>(), null));

        public void Fail(Exception exception) => Result.TrySetException(exception);
    }
}
