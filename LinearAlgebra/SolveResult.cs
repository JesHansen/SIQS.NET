namespace LinearAlgebra;

/// <summary>Result of nullspace solving: dependencies and Block Lanczos counters.</summary>
public sealed record SolveResult(
    IReadOnlyList<Dependency> Dependencies,
    int Pivots,
    string Solver = "block-lanczos",
    int LanczosRuns = 0,
    int LanczosDependencies = 0,
    IReadOnlyList<long>? LanczosRunMilliseconds = null,
    IReadOnlyList<int>? LanczosRunDimensions = null,
    int LanczosCandidatesExtracted = 0,
    string StopReason = "not-started");
