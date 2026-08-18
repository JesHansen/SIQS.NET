namespace LinearAlgebra;

/// <summary>Immutable partition plan and reusable buffers for parallel sparse-matrix kernels.</summary>
internal sealed record BlockLanczosWorkspacePlan(
    int EffectiveParallelism,
    PartitionRange[] RelationPartitions,
    PartitionRange[] SparseColumnPartitions,
    ParallelOptions ParallelOptions,
    int[] ColumnRelationIndices,
    int[] ColumnOffsets,
    ulong[][] DenseScratch,
    ulong[] DenseImage)
{
    public static BlockLanczosWorkspacePlan Create(
        BlockLanczosMatrixStorage storage,
        int requestedParallelism,
        CancellationToken cancellationToken)
    {
        var effectiveParallelism = Math.Max(
            1, requestedParallelism == 0 ? Environment.ProcessorCount : requestedParallelism);
        var relationPartitions = PartitionRanges.ByNonzeros(storage.RowOffsets, effectiveParallelism);
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = relationPartitions.Length,
            CancellationToken = cancellationToken,
        };
        if (relationPartitions.Length <= 1)
        {
            return new BlockLanczosWorkspacePlan(
                effectiveParallelism, relationPartitions, [], parallelOptions, [], [], [], []);
        }

        var (columnOffsets, columnRelationIndices) = BuildColumnAdjacency(storage);
        var sparseColumnPartitions = PartitionRanges.ByNonzeros(
            columnOffsets, relationPartitions.Length);
        return new BlockLanczosWorkspacePlan(
            effectiveParallelism,
            relationPartitions,
            sparseColumnPartitions,
            parallelOptions,
            columnRelationIndices,
            columnOffsets,
            CreateScratch(relationPartitions.Length, storage.DenseParityColumns.Length),
            new ulong[storage.DenseParityColumns.Length]);
    }

    private static (int[] ColumnOffsets, int[] ColumnRelationIndices) BuildColumnAdjacency(
        BlockLanczosMatrixStorage storage)
    {
        var offsets = new int[storage.SparseParityColumnCount + 1];
        foreach (var column in storage.ColumnIndices)
        {
            offsets[column + 1]++;
        }

        for (var column = 0; column < storage.SparseParityColumnCount; column++)
        {
            offsets[column + 1] += offsets[column];
        }

        var relationIndices = new int[storage.ColumnIndices.Length];
        var cursor = new int[storage.SparseParityColumnCount];
        Array.Copy(offsets, cursor, storage.SparseParityColumnCount);
        for (var relation = 0; relation < storage.RelationCount; relation++)
        {
            for (var i = storage.RowOffsets[relation]; i < storage.RowOffsets[relation + 1]; i++)
            {
                relationIndices[cursor[storage.ColumnIndices[i]]++] = relation;
            }
        }

        return (offsets, relationIndices);
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
