using SIQS.Contracts;

namespace SIQS.Pipeline;

/// <summary>Durable per-phase state recorded in <c>job.json</c>.</summary>
public sealed class PhaseState
{
    public SiqsPhase Phase { get; set; }
    public PhaseStatus Status { get; set; }
    public int Attempt { get; set; }
    public string? StartedUtc { get; set; }
    public string? CompletedUtc { get; set; }
    public double? ElapsedSeconds { get; set; }
    public string? CurrentOperation { get; set; }
    public double? Percent { get; set; }
    public Dictionary<string, string> Counters { get; set; } = new();
    public List<string> Artifacts { get; set; } = new();
    public string? Error { get; set; }
    public List<PhaseAttemptState> Attempts { get; set; } = new();
}

/// <summary>Historical attempt data for a phase, including resume and top-up retries.</summary>
public sealed class PhaseAttemptState
{
    public int Attempt { get; set; }
    public PhaseStatus Status { get; set; }
    public string? StartedUtc { get; set; }
    public string? CompletedUtc { get; set; }
    public double? ElapsedSeconds { get; set; }
    public Dictionary<string, string> Counters { get; set; } = new();
    public List<string> Artifacts { get; set; } = new();
    public string? Error { get; set; }
}
