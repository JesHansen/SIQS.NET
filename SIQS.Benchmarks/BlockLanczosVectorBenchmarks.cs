using BenchmarkDotNet.Attributes;
using LinearAlgebra;

namespace SIQS.Benchmarks;

/// <summary>
/// Measures the four full-length 64x64 block-vector applications in one Block Lanczos recurrence
/// iteration. The parallel benchmark uses the same fixed workspace and partitions as the solver.
/// </summary>
[MemoryDiagnoser]
public class BlockLanczosVectorBenchmarks
{
    [Params(1_200_000)]
    public int Length;

    [Params(4, 0)]
    public int Parallelism;

    private ulong[][] _vectors = null!;
    private ulong[][] _matrices = null!;
    private ulong[] _vnext = null!;
    private ulong[] _x = null!;
    private BlockLanczosRecurrence.VectorWorkspace _workspace = null!;

    [GlobalSetup]
    public void Setup()
    {
        _vectors = Enumerable.Range(0, 3)
            .Select(i => SparseMatrixFixture.BuildBlock(Length, seed: 0x7100UL + (ulong)i))
            .ToArray();
        _matrices = Enumerable.Range(0, 4)
            .Select(i => SparseMatrixFixture.BuildBlock(64, seed: 0x8100UL + (ulong)i))
            .ToArray();
        _vnext = SparseMatrixFixture.BuildBlock(Length, seed: 0x9100);
        _x = SparseMatrixFixture.BuildBlock(Length, seed: 0xA100);
        var effectiveParallelism = Parallelism == 0 ? Environment.ProcessorCount : Parallelism;
        _workspace = new BlockLanczosRecurrence.VectorWorkspace(Length, effectiveParallelism);
    }

    [Benchmark(Baseline = true)]
    public void ApplyFour_Sequential()
    {
        Gf2Matrix64.ApplyToBlockVector(_vectors[0], _matrices[0], _vnext);
        Gf2Matrix64.ApplyToBlockVector(_vectors[1], _matrices[1], _vnext);
        Gf2Matrix64.ApplyToBlockVector(_vectors[2], _matrices[2], _vnext);
        Gf2Matrix64.ApplyToBlockVector(_vectors[0], _matrices[3], _x);
    }

    [Benchmark]
    public void ApplyFour_Parallel()
    {
        _workspace.ApplyRecurrenceUpdates(
            _vectors[0], _matrices[0],
            _vectors[1], _matrices[1],
            _vectors[2], _matrices[2],
            _matrices[3],
            _vnext,
            _x);
    }
}
