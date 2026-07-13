using SIQS.Contracts;

namespace SIQS.Pipeline;

/// <summary>Chooses the first incomplete or invalid phase for a resumable job.</summary>
internal sealed class ResumePlanner
{
    public int FindResumePoint(string directory, JobState state, FactorizationRequest request)
    {
        for (var i = 0; i < PhaseSequence.All.Length; i++)
        {
            if (state.PhaseStates[i].Status != PhaseStatus.Completed)
            {
                return i;
            }

            if (!ArtifactValidator.Validate(PhaseSequence.All[i], directory, request, result: null).IsValid)
            {
                return i;
            }
        }

        return PhaseSequence.All.Length;
    }
}
