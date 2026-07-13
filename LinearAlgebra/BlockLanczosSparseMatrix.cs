using System.Collections.ObjectModel;

namespace LinearAlgebra;

/// <summary>
/// Sparse column-oriented view used by Block Lanczos. Original relation rows are Lanczos columns;
/// original parity columns are Lanczos rows.
/// </summary>
/// <remarks>
/// Parallel multiplies reuse per-matrix scratch buffers, so a matrix instance is intended for one
/// solve at a time.
/// </remarks>
public sealed class BlockLanczosSparseMatrix
{
    private readonly int[] _columnIndices;
    private readonly int[] _rowOffsets;
    private readonly int[] _denseParityColumns;
    private readonly ulong[] _denseRowMasks;
    private readonly ReadOnlyCollection<int> _denseParityColumnsView;
    private readonly ReadOnlyCollection<ulong> _denseRowMasksView;
    private readonly PartitionRange[] _relationPartitions;
    private readonly PartitionRange[] _sparseColumnPartitions;
    private readonly ParallelOptions _parallelOptions;
    private readonly ulong[][] _parallelSparseScratch;
    private readonly ulong[][] _parallelDenseScratch;
    private readonly ulong[] _parallelDenseImage;

    private BlockLanczosSparseMatrix(BlockLanczosMatrixStorage storage, int requestedParallelism)
    {
        RelationCount = storage.RelationCount;
        OriginalParityColumnCount = storage.OriginalParityColumnCount;
        SparseParityColumnCount = storage.SparseParityColumnCount;
        _columnIndices = storage.ColumnIndices;
        _rowOffsets = storage.RowOffsets;
        _denseParityColumns = storage.DenseParityColumns;
        _denseRowMasks = storage.DenseRowMasks;
        _denseParityColumnsView = Array.AsReadOnly(_denseParityColumns);
        _denseRowMasksView = Array.AsReadOnly(_denseRowMasks);

        EffectiveParallelism = requestedParallelism == 0 ? Environment.ProcessorCount : requestedParallelism;
        EffectiveParallelism = Math.Max(1, EffectiveParallelism);
        _relationPartitions = PartitionRanges.ByNonzeros(_rowOffsets, EffectiveParallelism);
        _sparseColumnPartitions = PartitionRanges.Even(SparseParityColumnCount, _relationPartitions.Length);
        _parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = _relationPartitions.Length };

        if (UseParallel)
        {
            _parallelSparseScratch = CreateScratch(_relationPartitions.Length, SparseParityColumnCount);
            _parallelDenseScratch = CreateScratch(_relationPartitions.Length, _denseParityColumns.Length);
            _parallelDenseImage = new ulong[_denseParityColumns.Length];
        }
        else
        {
            _parallelSparseScratch = Array.Empty<ulong[]>();
            _parallelDenseScratch = Array.Empty<ulong[]>();
            _parallelDenseImage = Array.Empty<ulong>();
        }
    }

    public int RelationCount { get; }

    public int OriginalParityColumnCount { get; }

    public int SparseParityColumnCount { get; }

    public int EffectiveParallelism { get; }

    // Read-only views wrap the backing arrays so callers cannot mutate them through the property.
    public IReadOnlyList<int> DenseParityColumns => _denseParityColumnsView;

    public IReadOnlyList<ulong> DenseRowMasks => _denseRowMasksView;

    private bool UseParallel => _relationPartitions.Length > 1;

    public static BlockLanczosSparseMatrix FromRelationRows(
        IReadOnlyList<RelationRow> rows,
        int columnCount,
        BlockLanczosOptions? options = null)
    {
        options ??= new BlockLanczosOptions();
        var storage = BlockLanczosMatrixBuilder.Build(rows, columnCount, options);
        return new BlockLanczosSparseMatrix(storage, options.Parallelism);
    }

    public ReadOnlySpan<int> SparseColumnsForRelation(int relationRow)
    {
        if ((uint)relationRow >= (uint)RelationCount)
        {
            throw new ArgumentOutOfRangeException(nameof(relationRow));
        }

        return _columnIndices.AsSpan(_rowOffsets[relationRow], _rowOffsets[relationRow + 1] - _rowOffsets[relationRow]);
    }

    public void MultiplyA(ReadOnlySpan<ulong> x, Span<ulong> y)
    {
        ValidateRelationInput(x.Length, nameof(x));
        ValidateSparseOutput(y.Length, nameof(y));
        MultiplyASequential(x, y);
    }

    public void MultiplyA(ulong[] x, ulong[] y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ValidateRelationInput(x.Length, nameof(x));
        ValidateSparseOutput(y.Length, nameof(y));

        if (UseParallel)
        {
            MultiplyAParallel(x, y, 0);
        }
        else
        {
            MultiplyASequential(x, y);
        }
    }

    public void MultiplyA(ulong[] x, ulong[] y, int yOffset, int yLength)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ValidateRelationInput(x.Length, nameof(x));
        ValidateSparseOutput(yLength, nameof(yLength));
        ValidateSegment(y, yOffset, yLength, nameof(y));

        if (UseParallel)
        {
            MultiplyAParallel(x, y, yOffset);
        }
        else
        {
            MultiplyASequential(x, y.AsSpan(yOffset, yLength));
        }
    }

    public void MultiplyTranspose(ReadOnlySpan<ulong> y, Span<ulong> x)
    {
        ValidateSparseInput(y.Length, nameof(y));
        ValidateRelationOutput(x.Length, nameof(x));
        MultiplyTransposeSequential(y, x);
    }

    public void MultiplyTranspose(ulong[] y, ulong[] x)
    {
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(x);
        ValidateSparseInput(y.Length, nameof(y));
        ValidateRelationOutput(x.Length, nameof(x));

        if (UseParallel)
        {
            MultiplyTransposeParallel(y, x);
        }
        else
        {
            MultiplyTransposeSequential(y, x);
        }
    }

    public void MultiplySymmetric(ReadOnlySpan<ulong> x, Span<ulong> z)
    {
        var scratch = new ulong[SparseParityColumnCount];
        MultiplySymmetric(x, z, scratch);
    }

    public void MultiplySymmetric(ulong[] x, ulong[] z)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(z);
        var scratch = new ulong[SparseParityColumnCount];
        MultiplySymmetric(x, z, scratch);
    }

    public void MultiplySymmetric(ReadOnlySpan<ulong> x, Span<ulong> z, Span<ulong> scratch)
    {
        ValidateRelationInput(x.Length, nameof(x));
        ValidateRelationOutput(z.Length, nameof(z));
        ValidateSparseScratch(scratch.Length, nameof(scratch));
        MultiplySymmetricSequential(x, z, scratch);
    }

    public void MultiplySymmetric(ulong[] x, ulong[] z, ulong[] scratch)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(z);
        ArgumentNullException.ThrowIfNull(scratch);
        ValidateRelationInput(x.Length, nameof(x));
        ValidateRelationOutput(z.Length, nameof(z));
        ValidateSparseScratch(scratch.Length, nameof(scratch));

        if (UseParallel)
        {
            MultiplySymmetricParallel(x, z, scratch);
        }
        else
        {
            MultiplySymmetricSequential(x, z, scratch);
        }
    }

    public void MultiplyDenseRows(ReadOnlySpan<ulong> x, Span<ulong> y)
    {
        ValidateRelationInput(x.Length, nameof(x));
        ValidateDenseOutput(y.Length, nameof(y));
        MultiplyDenseRowsSequential(x, y);
    }

    public void MultiplyDenseRows(ulong[] x, ulong[] y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ValidateRelationInput(x.Length, nameof(x));
        ValidateDenseOutput(y.Length, nameof(y));

        if (UseParallel)
        {
            MultiplyDenseRowsParallel(x, y, 0);
        }
        else
        {
            MultiplyDenseRowsSequential(x, y);
        }
    }

    public void MultiplyDenseRows(ulong[] x, ulong[] y, int yOffset, int yLength)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ValidateRelationInput(x.Length, nameof(x));
        ValidateDenseOutput(yLength, nameof(yLength));
        ValidateSegment(y, yOffset, yLength, nameof(y));

        if (UseParallel)
        {
            MultiplyDenseRowsParallel(x, y, yOffset);
        }
        else
        {
            MultiplyDenseRowsSequential(x, y.AsSpan(yOffset, yLength));
        }
    }

    public bool DenseRowsAreZero(IEnumerable<int> relationRows)
    {
        ulong accum = 0;
        foreach (var relationRow in relationRows)
        {
            if ((uint)relationRow >= (uint)_denseRowMasks.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(relationRows), $"Relation row {relationRow} is out of range.");
            }

            accum ^= _denseRowMasks[relationRow];
        }

        return accum == 0;
    }

    private void MultiplyASequential(ReadOnlySpan<ulong> x, Span<ulong> y)
    {
        y.Clear();
        for (var relation = 0; relation < RelationCount; relation++)
        {
            var word = x[relation];
            if (word == 0)
            {
                continue;
            }

            for (var i = _rowOffsets[relation]; i < _rowOffsets[relation + 1]; i++)
            {
                y[_columnIndices[i]] ^= word;
            }
        }
    }

    private void MultiplyAParallel(ulong[] x, ulong[] y, int yOffset)
    {
        Parallel.For(0, _relationPartitions.Length, _parallelOptions, partitionIndex =>
        {
            var scratch = _parallelSparseScratch[partitionIndex];
            Array.Clear(scratch);
            ScatterSparsePartition(x, scratch, _relationPartitions[partitionIndex]);
        });

        ReduceSparseScratch(y, yOffset);
    }

    private void MultiplyTransposeSequential(ReadOnlySpan<ulong> y, Span<ulong> x)
    {
        for (var relation = 0; relation < RelationCount; relation++)
        {
            ulong accum = 0;
            for (var i = _rowOffsets[relation]; i < _rowOffsets[relation + 1]; i++)
            {
                accum ^= y[_columnIndices[i]];
            }

            x[relation] = accum;
        }
    }

    private void MultiplyTransposeParallel(ulong[] y, ulong[] x)
    {
        Parallel.For(0, _relationPartitions.Length, _parallelOptions, partitionIndex =>
        {
            var range = _relationPartitions[partitionIndex];
            for (var relation = range.Start; relation < range.End; relation++)
            {
                ulong accum = 0;
                for (var i = _rowOffsets[relation]; i < _rowOffsets[relation + 1]; i++)
                {
                    accum ^= y[_columnIndices[i]];
                }

                x[relation] = accum;
            }
        });
    }

    private void MultiplyTransposePlusDenseSequential(
        ReadOnlySpan<ulong> y,
        ReadOnlySpan<ulong> denseImage,
        Span<ulong> x)
    {
        for (var relation = 0; relation < RelationCount; relation++)
        {
            ulong accum = 0;
            for (var i = _rowOffsets[relation]; i < _rowOffsets[relation + 1]; i++)
            {
                accum ^= y[_columnIndices[i]];
            }

            var denseMask = _denseRowMasks[relation];
            if (denseMask != 0)
            {
                accum ^= GatherDenseRows(denseImage, denseMask);
            }

            x[relation] = accum;
        }
    }

    private void MultiplyTransposePlusDenseParallel(ulong[] y, ulong[] denseImage, ulong[] x)
    {
        Parallel.For(0, _relationPartitions.Length, _parallelOptions, partitionIndex =>
        {
            var range = _relationPartitions[partitionIndex];
            for (var relation = range.Start; relation < range.End; relation++)
            {
                ulong accum = 0;
                for (var i = _rowOffsets[relation]; i < _rowOffsets[relation + 1]; i++)
                {
                    accum ^= y[_columnIndices[i]];
                }

                var denseMask = _denseRowMasks[relation];
                if (denseMask != 0)
                {
                    accum ^= GatherDenseRows(denseImage, denseMask);
                }

                x[relation] = accum;
            }
        });
    }

    private void MultiplySymmetricSequential(ReadOnlySpan<ulong> x, Span<ulong> z, Span<ulong> scratch)
    {
        // The Lanczos symmetric operator must be A^T*A over the WHOLE matrix, including the deferred
        // dense parity columns. If the dense columns are left out of the operator, the solution block
        // only solves the sparse remainder; the dense columns are then re-imposed during extraction as
        // up to PostLanczosRows extra constraints, which consumes that much block width. On large
        // matrices the solution already carries an isotropic defect that grows with size, so those
        // extra constraints wipe out every recoverable dependency. Folding the dense contribution in
        // here (via the compact per-relation bit masks) keeps the fast sparse kernels while making the
        // operator the true A^T*A. See Instructions/LinearAlgebra.md (Dense Row Handling).
        MultiplyASequential(x, scratch);

        // Dense forward pass reads x and must run BEFORE MultiplyTranspose overwrites z, because callers
        // invoke this in place (z aliases x, e.g. the initial v0 = C*Y).
        var denseCount = _denseParityColumns.Length;
        Span<ulong> denseImage = denseCount == 0 ? default : stackalloc ulong[denseCount];
        if (denseCount != 0)
        {
            DenseForwardSequential(x, denseImage);
        }

        if (denseCount != 0)
        {
            MultiplyTransposePlusDenseSequential(scratch, denseImage, z);
        }
        else
        {
            MultiplyTransposeSequential(scratch, z);
        }
    }

    private void MultiplySymmetricParallel(ulong[] x, ulong[] z, ulong[] scratch)
    {
        var denseCount = _denseParityColumns.Length;
        if (denseCount == 0)
        {
            MultiplyAParallel(x, scratch, 0);
        }
        else
        {
            MultiplyAAndDenseForwardParallel(x, scratch, _parallelDenseImage);
        }

        if (denseCount != 0)
        {
            MultiplyTransposePlusDenseParallel(scratch, _parallelDenseImage, z);
        }
        else
        {
            MultiplyTransposeParallel(scratch, z);
        }
    }

    private void MultiplyDenseRowsSequential(ReadOnlySpan<ulong> x, Span<ulong> y)
    {
        y.Clear();
        if (y.Length == 0)
        {
            return;
        }

        DenseForwardSequential(x, y);
    }

    private void MultiplyDenseRowsParallel(ulong[] x, ulong[] y, int yOffset)
    {
        if (_denseParityColumns.Length == 0)
        {
            return;
        }

        Parallel.For(0, _relationPartitions.Length, _parallelOptions, partitionIndex =>
        {
            var denseScratch = _parallelDenseScratch[partitionIndex];
            Array.Clear(denseScratch);
            DenseForwardPartition(x, denseScratch, _relationPartitions[partitionIndex]);
        });

        ReduceDenseScratch(y, yOffset);
    }

    private void MultiplyAAndDenseForwardParallel(ulong[] x, ulong[] sparseOutput, ulong[] denseOutput)
    {
        Parallel.For(0, _relationPartitions.Length, _parallelOptions, partitionIndex =>
        {
            var sparseScratch = _parallelSparseScratch[partitionIndex];
            var denseScratch = _parallelDenseScratch[partitionIndex];
            Array.Clear(sparseScratch);
            Array.Clear(denseScratch);
            var range = _relationPartitions[partitionIndex];
            ScatterSparsePartition(x, sparseScratch, range);
            DenseForwardPartition(x, denseScratch, range);
        });

        ReduceSparseScratch(sparseOutput, 0);
        ReduceDenseScratch(denseOutput, 0);
    }

    private void ScatterSparsePartition(ulong[] x, ulong[] scratch, PartitionRange range)
    {
        for (var relation = range.Start; relation < range.End; relation++)
        {
            var word = x[relation];
            if (word == 0)
            {
                continue;
            }

            for (var i = _rowOffsets[relation]; i < _rowOffsets[relation + 1]; i++)
            {
                scratch[_columnIndices[i]] ^= word;
            }
        }
    }

    private void DenseForwardSequential(ReadOnlySpan<ulong> x, Span<ulong> denseImage)
    {
        denseImage.Clear();
        for (var relation = 0; relation < _denseRowMasks.Length; relation++)
        {
            var word = x[relation];
            if (word == 0)
            {
                continue;
            }

            XorDenseRows(denseImage, _denseRowMasks[relation], word);
        }
    }

    private void DenseForwardPartition(ulong[] x, ulong[] denseScratch, PartitionRange range)
    {
        for (var relation = range.Start; relation < range.End; relation++)
        {
            var word = x[relation];
            if (word == 0)
            {
                continue;
            }

            XorDenseRows(denseScratch, _denseRowMasks[relation], word);
        }
    }

    private void ReduceSparseScratch(ulong[] output, int outputOffset)
    {
        if (SparseParityColumnCount == 0)
        {
            return;
        }

        Parallel.For(0, _sparseColumnPartitions.Length, _parallelOptions, partitionIndex =>
        {
            var range = _sparseColumnPartitions[partitionIndex];
            for (var column = range.Start; column < range.End; column++)
            {
                ulong accum = 0;
                for (var worker = 0; worker < _parallelSparseScratch.Length; worker++)
                {
                    accum ^= _parallelSparseScratch[worker][column];
                }

                output[outputOffset + column] = accum;
            }
        });
    }

    private void ReduceDenseScratch(ulong[] output, int outputOffset)
    {
        for (var denseRow = 0; denseRow < _denseParityColumns.Length; denseRow++)
        {
            ulong accum = 0;
            for (var worker = 0; worker < _parallelDenseScratch.Length; worker++)
            {
                accum ^= _parallelDenseScratch[worker][denseRow];
            }

            output[outputOffset + denseRow] = accum;
        }
    }

    private static void XorDenseRows(Span<ulong> denseImage, ulong denseMask, ulong word)
    {
        while (denseMask != 0)
        {
            var denseRow = System.Numerics.BitOperations.TrailingZeroCount(denseMask);
            denseImage[denseRow] ^= word;
            denseMask &= denseMask - 1;
        }
    }

    private static ulong GatherDenseRows(ReadOnlySpan<ulong> denseImage, ulong denseMask)
    {
        ulong accum = 0;
        while (denseMask != 0)
        {
            var denseRow = System.Numerics.BitOperations.TrailingZeroCount(denseMask);
            accum ^= denseImage[denseRow];
            denseMask &= denseMask - 1;
        }

        return accum;
    }

    private void ValidateRelationInput(int length, string parameterName)
    {
        if (length != RelationCount)
        {
            throw new ArgumentException("Input vector length must match the relation count.", parameterName);
        }
    }

    private void ValidateRelationOutput(int length, string parameterName)
    {
        if (length != RelationCount)
        {
            throw new ArgumentException("Output vector length must match the relation count.", parameterName);
        }
    }

    private void ValidateSparseInput(int length, string parameterName)
    {
        if (length != SparseParityColumnCount)
        {
            throw new ArgumentException("Input vector length must match the sparse parity column count.", parameterName);
        }
    }

    private void ValidateSparseOutput(int length, string parameterName)
    {
        if (length != SparseParityColumnCount)
        {
            throw new ArgumentException("Output vector length must match the sparse parity column count.", parameterName);
        }
    }

    private void ValidateSparseScratch(int length, string parameterName)
    {
        if (length != SparseParityColumnCount)
        {
            throw new ArgumentException("Scratch vector length must match the sparse parity column count.", parameterName);
        }
    }

    private void ValidateDenseOutput(int length, string parameterName)
    {
        if (length != _denseParityColumns.Length)
        {
            throw new ArgumentException("Output vector length must match the dense parity row count.", parameterName);
        }
    }

    private static void ValidateSegment(ulong[] vector, int offset, int length, string parameterName)
    {
        if (offset < 0 || length < 0 || offset > vector.Length - length)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static ulong[][] CreateScratch(int workerCount, int length)
    {
        var scratch = new ulong[workerCount][];
        for (var i = 0; i < scratch.Length; i++)
        {
            scratch[i] = new ulong[length];
        }

        return scratch;
    }
}
