using System.Collections.ObjectModel;
using SIQS.Contracts;
using SIQS.Pipeline;

namespace SIQS.UI.Services;

/// <summary>Immutable UI projection of one phase.</summary>
public sealed record PhaseSnapshot
{
    public PhaseSnapshot(
        SiqsPhase phase,
        PhaseStatus status = PhaseStatus.Pending,
        string? message = null,
        double? percent = null,
        double? elapsedSeconds = null,
        IReadOnlyDictionary<string, string>? counters = null)
    {
        Phase = phase;
        Status = status;
        Message = message;
        Percent = percent;
        ElapsedSeconds = elapsedSeconds;
        Counters = counters is null
            ? new ReadOnlyDictionary<string, string>(new Dictionary<string, string>())
            : new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(counters));
    }

    public SiqsPhase Phase { get; init; }
    public PhaseStatus Status { get; init; }
    public string? Message { get; init; }
    public double? Percent { get; init; }
    public double? ElapsedSeconds { get; init; }
    public IReadOnlyDictionary<string, string> Counters { get; init; }
}

/// <summary>Immutable state exposed to UI components.</summary>
public sealed record FactorizationJobSnapshot(
    string JobId,
    string TargetN,
    string JobDirectory,
    JobStatus Status,
    IReadOnlyList<PhaseSnapshot> Phases,
    FactorizationJobResult? Result = null,
    string? Error = null)
{
    public bool IsRunning => Status is JobStatus.Created or JobStatus.Running or JobStatus.Canceling;
}

/// <summary>Reduces progress events into a new immutable UI snapshot.</summary>
public static class JobProgressReducer
{
    public static FactorizationJobSnapshot Apply(
        FactorizationJobSnapshot snapshot,
        SiqsProgressEvent progress)
    {
        var phases = snapshot.Phases.ToArray();
        var index = Array.FindIndex(phases, p => p.Phase == progress.Phase);
        if (index < 0)
        {
            return snapshot;
        }

        for (var i = 0; i < index; i++)
        {
            if (phases[i].Status == PhaseStatus.Pending)
            {
                phases[i] = phases[i] with { Status = PhaseStatus.Completed };
            }
        }

        var elapsed = phases[index].ElapsedSeconds;
        if (progress.Counters.ContainsKey(CounterKeys.ElapsedSeconds))
        {
            elapsed = CounterFormat.ReadSeconds(progress.Counters, CounterKeys.ElapsedSeconds, elapsed ?? 0.0);
        }
        phases[index] = phases[index] with
        {
            Status = progress.Message == "phase completed" ? PhaseStatus.Completed : PhaseStatus.Running,
            Message = progress.Message,
            Percent = progress.Percent,
            Counters = progress.Counters,
            ElapsedSeconds = elapsed,
        };
        return snapshot with { Phases = Array.AsReadOnly(phases) };
    }

    public static FactorizationJobSnapshot ApplyResult(
        FactorizationJobSnapshot snapshot,
        FactorizationJobResult result)
    {
        var phases = snapshot.Phases.ToArray();
        foreach (var summary in result.PhaseSummaries)
        {
            var index = Array.FindIndex(phases, p => p.Phase == summary.Phase);
            if (index >= 0)
            {
                phases[index] = phases[index] with
                {
                    Status = summary.Status,
                    Counters = summary.Counters,
                    ElapsedSeconds = summary.ElapsedSeconds,
                };
            }
        }

        return snapshot with { Status = result.Status, Result = result, Phases = Array.AsReadOnly(phases) };
    }
}
