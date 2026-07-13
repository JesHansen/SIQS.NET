using System.Numerics;
using SIQS.Contracts;

namespace SIQS.Pipeline;

/// <summary>Context handed to each phase adapter.</summary>
public sealed record PhaseContext(
    string JobId,
    string JobDirectory,
    FactorizationRequest Request,
    IProgress<SiqsProgressEvent>? Progress,
    CancellationToken CancellationToken);

/// <summary>A factor pair discovered by a phase (early factor base factor or square-root factor).</summary>
public sealed record PhaseFactorOutcome(BigInteger Factor1, BigInteger Factor2);

/// <summary>The outcome of running one phase.</summary>
public sealed record PhaseResult(
    SiqsPhase Phase,
    PhaseStatus Status,
    IReadOnlyList<string> Artifacts,
    IReadOnlyDictionary<string, string> Counters,
    string? Error = null,
    PhaseFactorOutcome? Factor = null)
{
    public static PhaseResult Completed(
        SiqsPhase phase, IReadOnlyList<string> artifacts, IReadOnlyDictionary<string, string> counters,
        PhaseFactorOutcome? factor = null)
        => new(phase, PhaseStatus.Completed, artifacts, counters, null, factor);

    public static PhaseResult Failed(SiqsPhase phase, string error)
        => new(phase, PhaseStatus.Failed, Array.Empty<string>(), new Dictionary<string, string>(), error);
}

/// <summary>
/// Narrow seam for invoking the algorithmic phases. The real implementation calls the phase
/// libraries in-process; tests inject fakes to exercise orchestration without running SIQS work.
/// </summary>
public interface IPhaseExecutor
{
    Task<PhaseResult> RunFactorBaseAsync(PhaseContext context);
    Task<PhaseResult> RunSievingAsync(PhaseContext context);
    Task<PhaseResult> RunFilteringAsync(PhaseContext context);
    Task<PhaseResult> RunLinearAlgebraAsync(PhaseContext context);
    Task<PhaseResult> RunSquareRootAsync(PhaseContext context);
}
