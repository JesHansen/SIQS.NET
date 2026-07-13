using SIQS.Contracts;

namespace SIQS.Pipeline;

/// <summary>Owns persisted job-status transitions and final-factor updates.</summary>
internal static class JobStateMachine
{
    public static void Start(JobState state)
    {
        state.Status = JobStatus.Running;
        state.StartedUtc ??= Now();
        state.CompletedUtc = null;
        state.ErrorSummary = null;
    }

    public static void Canceling(JobState state) => state.Status = JobStatus.Canceling;

    public static void Canceled(JobState state)
    {
        state.Status = JobStatus.Canceled;
        state.CompletedUtc = Now();
    }

    public static void Failed(JobState state, SiqsPhase phase, string message, string? exceptionType)
    {
        state.ErrorSummary = new ErrorSummary { Phase = phase, Message = message, ExceptionType = exceptionType };
        state.Status = JobStatus.Failed;
        state.CompletedUtc = Now();
    }

    public static void Completed(JobState state, JobStatus status, PhaseFactorOutcome? factor)
    {
        if (factor is not null)
        {
            state.FinalFactors = [factor.Factor1.ToString(), factor.Factor2.ToString()];
        }

        state.Status = status;
        state.CompletedUtc = Now();
    }

    private static string Now() => JobTimestamp.Now();
}
