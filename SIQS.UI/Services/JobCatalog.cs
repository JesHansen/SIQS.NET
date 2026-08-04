using System.Text.Json;
using SIQS.Contracts;
using SIQS.Overlord;
using SIQS.Pipeline;

namespace SIQS.UI.Services;

public enum JobKind
{
    OneShot,
    Distributed,
}

/// <summary>A unified disk and live view of a factorization job.</summary>
public sealed record JobListEntry(
    string JobId,
    JobKind Kind,
    string TargetN,
    int DigitCount,
    JobStatus Status,
    bool IsLive,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? CompletedAt,
    double? TotalSeconds,
    IReadOnlyList<string> Factors);

/// <summary>Merges archived job snapshots with the local and distributed live job services.</summary>
public sealed class JobCatalog
{
    private readonly RunsDirectory _runsDirectory;
    private readonly FactorizationJobService _jobs;
    private readonly OverlordService _overlord;

    public JobCatalog(RunsDirectory runsDirectory, FactorizationJobService jobs, OverlordService overlord)
    {
        _runsDirectory = runsDirectory;
        _jobs = jobs;
        _overlord = overlord;
    }

    public IReadOnlyList<JobListEntry> List()
    {
        var entries = DiskEntries().ToDictionary(entry => entry.JobId, StringComparer.Ordinal);
        OverlayOneShot(entries);
        OverlayDistributed(entries);

        return entries.Values
            .OrderByDescending(entry => entry.IsLive)
            .ThenByDescending(entry => entry.CreatedAt)
            .ThenBy(entry => entry.JobId, StringComparer.Ordinal)
            .ToArray();
    }

    public int CompletedCount() => List().Count(entry => IsTerminal(entry.Status));

    private IEnumerable<JobListEntry> DiskEntries()
    {
        if (!Directory.Exists(_runsDirectory.Path))
        {
            yield break;
        }

        foreach (var directory in Directory.EnumerateDirectories(_runsDirectory.Path))
        {
            JobStateSnapshot snapshot;
            try
            {
                snapshot = JobStore.LoadSnapshot(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or FormatException or JsonException)
            {
                continue;
            }

            yield return FromSnapshot(snapshot);
        }
    }

    private void OverlayOneShot(IDictionary<string, JobListEntry> entries)
    {
        if (_jobs.Current is not { IsRunning: true } live)
        {
            return;
        }

        entries[live.JobId] = entries.TryGetValue(live.JobId, out var disk)
            ? disk with { Status = live.Status, IsLive = true }
            : new JobListEntry(
                live.JobId,
                JobKind.OneShot,
                live.TargetN,
                live.TargetN.Length,
                live.Status,
                true,
                null,
                null,
                null,
                Array.Empty<string>());
    }

    private void OverlayDistributed(IDictionary<string, JobListEntry> entries)
    {
        if (_overlord.Snapshot() is not { } live || !_overlord.IsBusy)
        {
            return;
        }

        entries[live.JobId] = entries.TryGetValue(live.JobId, out var disk)
            ? disk with { Status = JobStatus.Running, IsLive = true }
            : new JobListEntry(
                live.JobId,
                JobKind.Distributed,
                live.TargetN,
                live.TargetN.Length,
                JobStatus.Running,
                true,
                null,
                null,
                null,
                Array.Empty<string>());
    }

    private static JobListEntry FromSnapshot(JobStateSnapshot snapshot)
        => new(
            snapshot.JobId,
            Kind(snapshot.JobId),
            snapshot.TargetN,
            snapshot.TargetN.Length,
            snapshot.Status,
            false,
            snapshot.CreatedAt,
            snapshot.CompletedAt,
            IsTerminal(snapshot.Status) ? snapshot.PhaseStates.Sum(phase => phase.ElapsedSeconds ?? 0) : null,
            snapshot.FinalFactors);

    private static JobKind Kind(string jobId) => jobId.StartsWith('D') ? JobKind.Distributed : JobKind.OneShot;

    private static bool IsTerminal(JobStatus status) => status is JobStatus.Failed or JobStatus.Canceled
        or JobStatus.CompletedNoFactor or JobStatus.CompletedPrime
        or JobStatus.CompletedFactorFound or JobStatus.CompletedTrivialFactor;
}
