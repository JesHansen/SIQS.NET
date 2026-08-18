namespace LinearAlgebra;

/// <summary>Accumulates per-seed timing, convergence, extraction, and verified-dependency telemetry.</summary>
internal sealed class BlockLanczosRunTelemetry
{
    private readonly List<long> _milliseconds = new(4);
    private readonly List<int> _dimensions = new(4);

    public int Runs { get; private set; }
    public int VerifiedDependencies { get; private set; }
    public int CandidatesExtracted { get; private set; }
    public int MaximumDimensionsSolved { get; private set; }

    public int StartRun() => ++Runs;

    public void RecordRun(TimeSpan elapsed, int dimensionsSolved)
    {
        _milliseconds.Add((long)elapsed.TotalMilliseconds);
        _dimensions.Add(dimensionsSolved);
        MaximumDimensionsSolved = Math.Max(MaximumDimensionsSolved, dimensionsSolved);
    }

    public void RecordCandidates(int count) => CandidatesExtracted += count;
    public void RecordVerifiedDependency() => VerifiedDependencies++;

    public SolveResult Result(
        IReadOnlyList<Dependency> dependencies,
        int dimensionsSolved,
        string stopReason)
        => new(
            dependencies,
            dimensionsSolved,
            Solver: "block-lanczos",
            LanczosRuns: Runs,
            LanczosDependencies: VerifiedDependencies,
            LanczosRunMilliseconds: _milliseconds.ToArray(),
            LanczosRunDimensions: _dimensions.ToArray(),
            LanczosCandidatesExtracted: CandidatesExtracted,
            StopReason: stopReason);
}
