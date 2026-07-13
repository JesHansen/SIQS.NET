using System.Collections.Concurrent;
using System.Globalization;
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
public sealed class FactorizationJobService
{
    private readonly ISiqsPipeline _pipeline;
    private readonly string _runsRoot;
    private readonly object _gate = new();

    private CancellationTokenSource? _cts;
    private Task? _running;

    public FactorizationJobService(ISiqsPipeline pipeline, RunsDirectory runsDirectory)
    {
        _pipeline = pipeline;
        _runsRoot = runsDirectory.Path;
        Directory.CreateDirectory(_runsRoot);
    }

    public event Action? Changed;

    public FactorizationJobSnapshot? Current { get; private set; }

    public ProgressEventBuffer Events { get; } = new();

    public bool IsBusy => Current?.IsRunning ?? false;

    public string RunsRoot => _runsRoot;

    /// <summary>Starts a new job. Throws if a job is already running.</summary>
    public void Start(FactorizationRequest request)
    {
        lock (_gate)
        {
            if (IsBusy)
            {
                throw new InvalidOperationException("A factorization job is already running.");
            }

            var normalized = _pipeline.NormalizeAndValidate(request);
            var jobId = $"J{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-ffff}";
            var directory = Path.Combine(_runsRoot, jobId);
            var withDir = normalized with { RunDirectory = directory };

            Events.Clear();
            Current = new FactorizationJobSnapshot(
                jobId,
                request.TargetN.ToString(),
                directory,
                JobStatus.Running,
                Array.AsReadOnly(SiqsPhases.Order.Select(phase => new PhaseSnapshot(phase)).ToArray()));

            _cts = new CancellationTokenSource();
            _running = Task.Run(() => ExecuteAsync(withDir, _cts.Token));
        }

        RaiseChanged();
    }

    public void Cancel() => _cts?.Cancel();

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
            catch
            {
                // Skip directories without a readable job.json.
            }
        }

        return summaries
            .OrderByDescending(s => s.CreatedUtc, StringComparer.Ordinal)
            .Take(max)
            .ToArray();
    }

    private async Task ExecuteAsync(FactorizationRequest request, CancellationToken token)
    {
        var view = Current!;
        var progress = new Progress<SiqsProgressEvent>(OnProgress);

        try
        {
            var result = await _pipeline.RunAsync(request, progress, token);
            Current = JobProgressReducer.ApplyResult(view, result);
        }
        catch (Exception ex)
        {
            Current = view with { Status = JobStatus.Failed, Error = ex.Message };
        }
        finally
        {
            RaiseChanged();
        }
    }

    private void OnProgress(SiqsProgressEvent value)
    {
        Events.Add(value);

        var view = Current;
        if (view is null)
        {
            return;
        }
        Current = JobProgressReducer.Apply(view, value);
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke();
}

/// <summary>Holds the configured runs directory path for dependency injection.</summary>
public sealed record RunsDirectory(string Path);
