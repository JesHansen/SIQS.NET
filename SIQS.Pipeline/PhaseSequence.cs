using SIQS.Contracts;

namespace SIQS.Pipeline;

/// <summary>Single source of truth for phase order and phase-index operations.</summary>
internal static class PhaseSequence
{
    public static readonly SiqsPhase[] All =
    {
        SiqsPhase.FactorBase,
        SiqsPhase.Sieving,
        SiqsPhase.Filtering,
        SiqsPhase.LinearAlgebra,
        SiqsPhase.SquareRoot,
    };

    public static int IndexOf(SiqsPhase phase)
    {
        var index = Array.IndexOf(All, phase);
        return index >= 0
            ? index
            : throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown pipeline phase.");
    }

    public static void EnsureStateOrder(JobState state)
    {
        if (state.PhaseStates.Count == 0)
        {
            foreach (var phase in All)
            {
                state.PhaseStates.Add(new PhaseState { Phase = phase, Status = PhaseStatus.Pending });
            }

            return;
        }

        if (state.PhaseStates.Count != All.Length)
        {
            throw new InvalidOperationException(
                $"job.json has {state.PhaseStates.Count} phase states, expected {All.Length}.");
        }

        for (var i = 0; i < All.Length; i++)
        {
            if (state.PhaseStates[i].Phase != All[i])
            {
                throw new InvalidOperationException(
                    $"job.json phase state {i} is {state.PhaseStates[i].Phase}, expected {All[i]}.");
            }
        }
    }
}
