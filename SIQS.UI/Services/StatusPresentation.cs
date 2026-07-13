using SIQS.Contracts;

namespace SIQS.UI.Services;

/// <summary>Centralizes the compact status and duration presentation used by operational views.</summary>
internal static class StatusPresentation
{
    public static string CssClass(JobStatus status) => status switch
    {
        JobStatus.CompletedFactorFound or JobStatus.CompletedTrivialFactor => "ok",
        JobStatus.Failed => "fail",
        JobStatus.Canceled or JobStatus.Canceling => "warn",
        JobStatus.Running => "run",
        _ => "idle",
    };

    public static string CssClass(PhaseStatus status) => status switch
    {
        PhaseStatus.Completed => "ok",
        PhaseStatus.Failed => "fail",
        PhaseStatus.Canceled => "warn",
        PhaseStatus.Running => "run",
        _ => "idle",
    };

    public static string FormatElapsed(double? seconds)
        => seconds is { } value
            ? value < 60 ? $"{value:F1}s" : TimeSpan.FromSeconds(value).ToString(@"m\:ss")
            : "—";

    public static string DisplayName(SiqsPhase phase) => phase switch
    {
        SiqsPhase.FactorBase => "Factor base",
        SiqsPhase.LinearAlgebra => "Linear algebra",
        SiqsPhase.SquareRoot => "Square root",
        _ => SiqsTokens.ToToken(phase),
    };
}
