using SIQS.Contracts;

namespace SIQS.Pipeline;

/// <summary>The durable job summary serialized to <c>job.json</c>.</summary>
public sealed class JobState
{
    public string JobId { get; set; } = string.Empty;
    public string TargetN { get; set; } = string.Empty;
    public JobStatus Status { get; set; }
    public string? CreatedUtc { get; set; }
    public string? StartedUtc { get; set; }
    public string? CompletedUtc { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();
    public List<PhaseState> PhaseStates { get; set; } = new();
    public List<string> ArtifactPaths { get; set; } = new();
    public List<string> FinalFactors { get; set; } = new();
    public List<TopUpRoundState> TopUpRounds { get; set; } = new();
    public ErrorSummary? ErrorSummary { get; set; }
}
