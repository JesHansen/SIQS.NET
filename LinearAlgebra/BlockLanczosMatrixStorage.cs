namespace LinearAlgebra;

/// <summary>
/// Immutable CSR-style storage for a Block Lanczos sparse matrix, produced by
/// <see cref="BlockLanczosMatrixBuilder"/> and consumed by the runtime multiply kernels in
/// <see cref="BlockLanczosSparseMatrix"/>. Arrays are assembly-internal and are never handed out
/// mutably beyond that boundary.
/// </summary>
internal sealed class BlockLanczosMatrixStorage
{
    public BlockLanczosMatrixStorage(
        int relationCount,
        int originalParityColumnCount,
        int sparseParityColumnCount,
        int[] columnIndices,
        int[] rowOffsets,
        int[] denseParityColumns,
        ulong[] denseRowMasks)
    {
        RelationCount = relationCount;
        OriginalParityColumnCount = originalParityColumnCount;
        SparseParityColumnCount = sparseParityColumnCount;
        ColumnIndices = columnIndices;
        RowOffsets = rowOffsets;
        DenseParityColumns = denseParityColumns;
        DenseRowMasks = denseRowMasks;
    }

    public int RelationCount { get; }

    public int OriginalParityColumnCount { get; }

    public int SparseParityColumnCount { get; }

    /// <summary>Flattened sparse column indices for every relation row (CSR values).</summary>
    public int[] ColumnIndices { get; }

    /// <summary>Per-row start offsets into <see cref="ColumnIndices"/> (CSR row pointers, length RelationCount+1).</summary>
    public int[] RowOffsets { get; }

    /// <summary>Original parity columns deferred to the dense post-Lanczos block, ascending.</summary>
    public int[] DenseParityColumns { get; }

    /// <summary>Per-relation dense-column membership bitmask (bit i set ⇒ row hits DenseParityColumns[i]).</summary>
    public ulong[] DenseRowMasks { get; }
}
