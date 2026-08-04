using SIQS.Contracts;
using SIQS.Pipeline;

namespace SIQS.UI.Services;

/// <summary>A compact, display-ready projection of one SIQS pipeline phase.</summary>
public sealed record PhaseTimelineItem(
    SiqsPhase Phase,
    PhaseStatus Status,
    double? Percent,
    double? ElapsedSeconds,
    string? Detail);

/// <summary>Maps phase projections into the shared timeline representation.</summary>
public static class PhaseTimelineExtensions
{
    public static PhaseTimelineItem ToTimelineItem(this PhaseSnapshot phase)
        => new(phase.Phase, phase.Status, phase.Percent, phase.ElapsedSeconds, Detail(phase.Counters));

    public static PhaseTimelineItem ToTimelineItem(this PhaseStateSnapshot phase)
        => new(phase.Phase, phase.Status, phase.Percent, phase.ElapsedSeconds, Detail(phase.Counters));

    private static string? Detail(IReadOnlyDictionary<string, string> counters)
    {
        var selected = counters
            .Where(counter => counter.Key != CounterKeys.ElapsedSeconds)
            .Where(counter => counter.Key is "relations" or "usable" or "dependencies" || !string.IsNullOrWhiteSpace(counter.Value))
            .Take(2)
            .Select(counter => $"{counter.Key} {counter.Value}")
            .ToArray();

        return selected.Length == 0 ? null : string.Join(" · ", selected);
    }
}
