namespace SIQS.Pipeline;

/// <summary>Owns legal phase lifecycle transitions and historical attempt bookkeeping.</summary>
internal static class PhaseStateMachine
{
    public static void Begin(PhaseState phaseState)
    {
        phaseState.Attempt = Math.Max(phaseState.Attempt, phaseState.Attempts.Count) + 1;
        phaseState.Status = SIQS.Contracts.PhaseStatus.Running;
        phaseState.StartedUtc = JobTimestamp.Now();
        phaseState.CompletedUtc = null;
        phaseState.ElapsedSeconds = null;
        phaseState.CurrentOperation = null;
        phaseState.Percent = null;
        phaseState.Counters = new Dictionary<string, string>();
        phaseState.Artifacts = new List<string>();
        phaseState.Error = null;
    }

    public static void RecordAttempt(PhaseState phaseState)
    {
        if (phaseState.Attempt <= 0)
        {
            return;
        }

        phaseState.Attempts.Add(new PhaseAttemptState
        {
            Attempt = phaseState.Attempt,
            Status = phaseState.Status,
            StartedUtc = phaseState.StartedUtc,
            CompletedUtc = phaseState.CompletedUtc,
            ElapsedSeconds = phaseState.ElapsedSeconds,
            Counters = new Dictionary<string, string>(phaseState.Counters),
            Artifacts = phaseState.Artifacts.ToList(),
            Error = phaseState.Error,
        });
    }

    public static void Reset(PhaseState phaseState)
    {
        phaseState.Status = SIQS.Contracts.PhaseStatus.Pending;
        phaseState.StartedUtc = null;
        phaseState.CompletedUtc = null;
        phaseState.ElapsedSeconds = null;
        phaseState.CurrentOperation = null;
        phaseState.Percent = null;
        phaseState.Counters = new Dictionary<string, string>();
        phaseState.Artifacts = new List<string>();
        phaseState.Error = null;
    }

    public static void Complete(PhaseState phaseState, double elapsedSeconds, PhaseResult result)
    {
        phaseState.Status = SIQS.Contracts.PhaseStatus.Completed;
        phaseState.CompletedUtc = JobTimestamp.Now();
        phaseState.ElapsedSeconds = elapsedSeconds;
        phaseState.Error = null;
        phaseState.Counters = new Dictionary<string, string>(result.Counters);
        phaseState.Artifacts = result.Artifacts.ToList();
    }

    public static void Fail(PhaseState phaseState, string message, IReadOnlyDictionary<string, string>? counters = null)
    {
        phaseState.Status = SIQS.Contracts.PhaseStatus.Failed;
        phaseState.CompletedUtc = JobTimestamp.Now();
        phaseState.Error = message;
        if (counters is not null)
        {
            phaseState.Counters = new Dictionary<string, string>(counters);
        }
    }

    public static void Cancel(PhaseState phaseState)
    {
        phaseState.Status = SIQS.Contracts.PhaseStatus.Canceled;
        phaseState.CompletedUtc = JobTimestamp.Now();
    }

    public static void Skip(PhaseState phaseState) => phaseState.Status = SIQS.Contracts.PhaseStatus.Skipped;
}
