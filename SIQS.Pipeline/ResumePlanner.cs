using SIQS.Contracts;

namespace SIQS.Pipeline;

/// <summary>Why a resume restarts at <see cref="ResumePlan.PhaseIndex"/>.</summary>
internal enum ResumeReason
{
    /// <summary>Every phase completed and validated; there is nothing to rerun.</summary>
    Complete,

    /// <summary>The phase never completed; its progress so far has not been rejected.</summary>
    Incomplete,

    /// <summary>The phase completed but its persisted output failed validation.</summary>
    InvalidArtifacts,
}

/// <summary>Where a resume restarts, why, and the validation issues that forced it.</summary>
internal sealed record ResumePlan(int PhaseIndex, ResumeReason Reason, IReadOnlyList<ValidationIssue> Issues)
{
    /// <summary>
    /// Sieve output is reused only when sieving was merely interrupted. Output that failed validation
    /// is discarded together with its A-index checkpoint, so the rerun regenerates it consistently.
    /// </summary>
    public bool KeepsSievingArtifacts
        => PhaseIndex == PhaseSequence.IndexOf(SiqsPhase.Sieving) && Reason == ResumeReason.Incomplete;
}

/// <summary>Chooses the first incomplete or invalid phase for a resumable job.</summary>
internal sealed class ResumePlanner
{
    public ResumePlan FindResumePoint(string directory, JobState state, FactorizationRequest request)
    {
        for (var i = 0; i < PhaseSequence.All.Length; i++)
        {
            if (state.PhaseStates[i].Status != PhaseStatus.Completed)
            {
                return new ResumePlan(i, ResumeReason.Incomplete, []);
            }

            var validation = ArtifactValidator.Validate(PhaseSequence.All[i], directory, request, result: null);
            if (!validation.IsValid)
            {
                return new ResumePlan(i, ResumeReason.InvalidArtifacts, validation.Issues);
            }
        }

        return new ResumePlan(PhaseSequence.All.Length, ResumeReason.Complete, []);
    }
}
