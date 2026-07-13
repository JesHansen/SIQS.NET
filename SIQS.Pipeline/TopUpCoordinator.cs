using System.Globalization;

namespace SIQS.Pipeline;

/// <summary>Calculates bounded relation top-up plans without owning phase execution.</summary>
internal sealed class TopUpCoordinator
{
    public const int MaxRounds = 3;

    public TopUpPlan? TryPlan(JobState state, FactorizationRequest request, UnderdeterminedMatrixException failure)
    {
        if (state.TopUpRounds.Count >= MaxRounds)
        {
            return null;
        }

        var sieving = state.PhaseStates[PhaseSequence.IndexOf(SIQS.Contracts.SiqsPhase.Sieving)];
        var usable = sieving.Counters.TryGetValue("usable_relations", out var value)
            && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
        var margin = Math.Max(2_000, (int)Math.Ceiling(failure.Deficit * 0.25));
        var target = Math.Max(request.Sieving.RelationTarget ?? 0L, usable + failure.Deficit + margin);
        if (target > int.MaxValue)
        {
            throw new OverflowException($"Relation top-up target {target} exceeds the supported maximum {int.MaxValue}.");
        }

        return new TopUpPlan(
            state.TopUpRounds.Count + 1,
            failure.Deficit,
            usable,
            margin,
            (int)target,
            request with { Sieving = request.Sieving with { RelationTarget = (int)target } });
    }
}

internal sealed record TopUpPlan(
    int Round,
    int Deficit,
    long CurrentUsableRelations,
    int Margin,
    int NewRelationTarget,
    FactorizationRequest Request);
