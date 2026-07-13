using System.Numerics;
using SIQS.Contracts;

namespace SIQS.Pipeline;

/// <summary>Per-phase summary included in the final job result.</summary>
public sealed record PhaseSummary(
    SiqsPhase Phase,
    PhaseStatus Status,
    IReadOnlyDictionary<string, string> Counters,
    double? ElapsedSeconds,
    string? Error);

/// <summary>The final outcome of a factorization run.</summary>
public sealed record FactorizationJobResult(
    string JobId,
    JobStatus Status,
    BigInteger TargetN,
    bool FactorFound,
    IReadOnlyList<BigInteger> Factors,
    int AttemptedDependencies,
    IReadOnlyList<string> ArtifactPaths,
    IReadOnlyList<PhaseSummary> PhaseSummaries,
    string? ErrorSummary);
