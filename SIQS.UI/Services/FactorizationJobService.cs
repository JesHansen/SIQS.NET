using System.Globalization;
using System.Text.Json;
using SIQS.Contracts;
using SIQS.Pipeline;

namespace SIQS.UI.Services;

/// <summary>A summary of a job discovered on disk for the recent-jobs list.</summary>
public sealed record JobSummary(string JobId, string TargetN, JobStatus Status, string? CreatedUtc);

internal static class SiqsPhases
{
    public static readonly SiqsPhase[] Order =
    {
        SiqsPhase.FactorBase, SiqsPhase.Sieving, SiqsPhase.Filtering, SiqsPhase.LinearAlgebra, SiqsPhase.SquareRoot,
    };
}

/// <summary>
/// Singleton application service that starts factorization jobs on a background task, tracks the
/// live job view and progress buffer, and lists previously completed jobs from the runs directory.
/// Only one job runs at a time in v1. UI components subscribe to <see cref="Changed"/>.
/// </summary>
public sealed class FactorizationJobService : IAsyncDisposable
{
    private readonly ISiqsPipeline _pipeline;
    private readonly string _runsRoot;
    private readonly object _gate = new();

    private CancellationTokenSource? _cts;
    private Task? _running;
    private bool _accepting = true;
    private Task? _stopTask;
    private long _generation;
    private FactorizationJobSnapshot? _current;

    public FactorizationJobService(ISiqsPipeline pipeline, RunsDirectory runsDirectory)
    {
        _pipeline = pipeline;
        _runsRoot = runsDirectory.Path;
        Directory.CreateDirectory(_runsRoot);
    }

    public event Action? Changed;

    public FactorizationJobSnapshot? Current { get { lock (_gate) { return _current; } } }

    public ProgressEventBuffer Events { get; } = new();

    public bool IsBusy { get { lock (_gate) { return _current?.IsRunning ?? false; } } }

    public string RunsRoot => _runsRoot;

    /// <summary>The active pipeline task, exposed for hosted lifetime coordination and tests.</summary>
    public Task? Completion { get { lock (_gate) { return _running; } } }

    /// <summary>Starts a new job. Throws if a job is already running.</summary>
    public void Start(FactorizationRequest request)
    {
        lock (_gate)
        {
            if (!_accepting)
            {
                throw new InvalidOperationException("The application is shutting down and no longer accepts jobs.");
            }

            if (_current?.IsRunning ?? false)
            {
                throw new InvalidOperationException("A factorization job is already running.");
            }

            var normalized = _pipeline.NormalizeAndValidate(request);
            var jobId = $"J{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-ffff}";
            var directory = Path.Combine(_runsRoot, jobId);
            var withDir = normalized with { RunDirectory = directory };

            var generation = ++_generation;
            Events.Clear();
            _current = new FactorizationJobSnapshot(
                jobId,
                request.TargetN.ToString(),
                directory,
                JobStatus.Running,
                Array.AsReadOnly(SiqsPhases.Order.Select(phase => new PhaseSnapshot(phase)).ToArray()));

            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var cancellation = _cts;
            var view = _current;
            var progress = new JobGenerationProgress(this, jobId, generation);
            _running = Task.Run(() => ExecuteAsync(
                withDir, cancellation.Token, view, progress, generation));
        }

        RaiseChanged();
    }

    public void Cancel()
    {
        lock (_gate)
        {
            _cts?.Cancel();
        }
    }

    /// <summary>Stops submissions, cancels the active pipeline, and joins it within the host budget.</summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Task stopTask;
        lock (_gate)
        {
            _accepting = false;
            _stopTask ??= StopCoreAsync(_cts, _running);
            stopTask = _stopTask;
        }

        return stopTask.WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        lock (_gate)
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    public IReadOnlyList<JobSummary> ListRecentJobs(int max = 25)
    {
        if (!Directory.Exists(_runsRoot))
        {
            return Array.Empty<JobSummary>();
        }

        var summaries = new List<JobSummary>();
        foreach (var dir in Directory.EnumerateDirectories(_runsRoot))
        {
            try
            {
                var job = JobStore.LoadSnapshot(dir);
                summaries.Add(new JobSummary(job.JobId, job.TargetN, job.Status, job.CreatedUtc));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or FormatException or JsonException)
            {
                // Skip directories without a readable job.json.
            }
        }

        return summaries
            .OrderByDescending(s => s.CreatedUtc, StringComparer.Ordinal)
            .Take(max)
            .ToArray();
    }

    private async Task ExecuteAsync(
        FactorizationRequest request,
        CancellationToken token,
        FactorizationJobSnapshot view,
        IProgress<SiqsProgressEvent> progress,
        long generation)
    {
        try
        {
            var result = await _pipeline.RunAsync(request, progress, token, view.JobId);
            ApplyTerminal(generation, view.JobId, snapshot => JobProgressReducer.ApplyResult(snapshot, result));
        }
        catch (OperationCanceledException)
        {
            ApplyTerminal(generation, view.JobId, snapshot => snapshot with { Status = JobStatus.Canceled });
        }
        catch (Exception ex)
        {
            ApplyTerminal(generation, view.JobId,
                snapshot => snapshot with { Status = JobStatus.Failed, Error = ex.Message });
        }
    }

    private void AcceptProgress(long generation, string jobId, SiqsProgressEvent value)
    {
        var accepted = false;
        lock (_gate)
        {
            if (!IdentifiesActiveRun(generation, jobId) || !_current!.IsRunning ||
                (value.JobId is not null && !string.Equals(value.JobId, jobId, StringComparison.Ordinal)))
            {
                return;
            }

            Events.Add(value);
            _current = JobProgressReducer.Apply(_current, value);
            accepted = true;
        }

        if (accepted)
        {
            RaiseChanged();
        }
    }

    private void ApplyTerminal(
        long generation,
        string jobId,
        Func<FactorizationJobSnapshot, FactorizationJobSnapshot> apply)
    {
        lock (_gate)
        {
            if (!IdentifiesActiveRun(generation, jobId) || !_current!.IsRunning)
            {
                return;
            }

            _current = apply(_current);
        }

        RaiseChanged();
    }

    private bool IdentifiesActiveRun(long generation, string jobId)
        => generation == _generation &&
           _current is not null &&
           string.Equals(_current.JobId, jobId, StringComparison.Ordinal);

    private void RaiseChanged() => Changed?.Invoke();

    private static async Task StopCoreAsync(CancellationTokenSource? cancellation, Task? running)
    {
        cancellation?.Cancel();
        if (running is not null)
        {
            await running.ConfigureAwait(false);
        }
    }

    private sealed class JobGenerationProgress(
        FactorizationJobService owner,
        string jobId,
        long generation) : IProgress<SiqsProgressEvent>
    {
        public void Report(SiqsProgressEvent value)
            => owner.AcceptProgress(generation, jobId, value);
    }
}

/// <summary>Holds the configured runs directory path for dependency injection.</summary>
public sealed record RunsDirectory(string Path);
