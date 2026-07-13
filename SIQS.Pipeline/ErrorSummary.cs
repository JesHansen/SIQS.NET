using SIQS.Contracts;

namespace SIQS.Pipeline;

/// <summary>Concise error summary recorded when a phase fails.</summary>
public sealed class ErrorSummary
{
    public SiqsPhase Phase { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ExceptionType { get; set; }
    public string? ArtifactPath { get; set; }
}
