using BenchmarkDotNet.Attributes;
using LinearAlgebra;

namespace SIQS.Benchmarks;

/// <summary>
/// Isolated benchmarks for the Block Lanczos sparse-matrix multiply kernels. Both the sequential
/// (span) API and the parallel (array) API are measured on the same deterministic fixture so a
/// kernel refactor can be checked for throughput and allocation regressions.
///
/// Fixture: <see cref="RowCount"/> relation rows over <see cref="ColumnCount"/> parity columns,
/// <see cref="RowWeight"/> set bits per row (typical filtered-matrix density). Parallelism is forced
/// to 1 for the sequential matrix and left at the default (processor count) for the parallel matrix.
/// </summary>
[MemoryDiagnoser]
public class BlockLanczosMatrixBenchmarks
{
    [Params(20_000)]
    public int RowCount;

    [Params(20_000)]
    public int ColumnCount;

    [Params(16)]
    public int RowWeight;

    private BlockLanczosSparseMatrix _sequential = null!;
    private BlockLanczosSparseMatrix _parallel = null!;
    private ulong[] _relationVector = null!;
    private ulong[] _sparseVector = null!;
    private ulong[] _sparseOutput = null!;
    private ulong[] _relationOutput = null!;
    private ulong[] _symmetricScratch = null!;

    [GlobalSetup]
    public void Setup()
    {
        var rows = SparseMatrixFixture.BuildRows(RowCount, ColumnCount, RowWeight, seed: 0xC0FFEE);
        var sequentialOptions = new BlockLanczosOptions(
            BlockLanczosOptions.DefaultPostLanczosRows,
            BlockLanczosOptions.DefaultMinPostLanczosDimension,
            Parallelism: 1);
        _sequential = BlockLanczosSparseMatrix.FromRelationRows(rows, ColumnCount, sequentialOptions);
        _parallel = BlockLanczosSparseMatrix.FromRelationRows(rows, ColumnCount, new BlockLanczosOptions());

        _relationVector = SparseMatrixFixture.BuildBlock(_sequential.RelationCount, seed: 0x1234);
        _sparseVector = SparseMatrixFixture.BuildBlock(_sequential.SparseParityColumnCount, seed: 0x5678);
        _sparseOutput = new ulong[_sequential.SparseParityColumnCount];
        _relationOutput = new ulong[_sequential.RelationCount];
        _symmetricScratch = new ulong[_sequential.SparseParityColumnCount];
    }

    [Benchmark]
    public void MultiplyA_Sequential() =>
        _sequential.MultiplyA(_relationVector.AsSpan(), _sparseOutput.AsSpan());

    [Benchmark]
    public void MultiplyA_Parallel() =>
        _parallel.MultiplyA(_relationVector, _sparseOutput);

    [Benchmark]
    public void MultiplyTranspose_Sequential() =>
        _sequential.MultiplyTranspose(_sparseVector.AsSpan(), _relationOutput.AsSpan());

    [Benchmark]
    public void MultiplyTranspose_Parallel() =>
        _parallel.MultiplyTranspose(_sparseVector, _relationOutput);

    [Benchmark]
    public void MultiplySymmetric_Sequential() =>
        _sequential.MultiplySymmetric(_relationVector.AsSpan(), _relationOutput.AsSpan(), _symmetricScratch.AsSpan());

    [Benchmark]
    public void MultiplySymmetric_Parallel() =>
        _parallel.MultiplySymmetric(_relationVector, _relationOutput, _symmetricScratch);
}
