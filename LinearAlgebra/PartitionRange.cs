namespace LinearAlgebra;

/// <summary>A half-open row/index range [Start, End) used to partition parallel work.</summary>
internal readonly record struct PartitionRange(int Start, int End);

internal static class PartitionRanges
{
    /// <summary>Splits [0, length) into up to <paramref name="partitionCount"/> even, contiguous ranges.</summary>
    public static PartitionRange[] Even(int length, int partitionCount)
    {
        if (length <= 0 || partitionCount <= 1)
        {
            return new[] { new PartitionRange(0, length) };
        }

        var count = Math.Min(partitionCount, length);
        var partitions = new PartitionRange[count];
        for (var partition = 0; partition < partitions.Length; partition++)
        {
            var start = (int)((long)length * partition / count);
            var end = (int)((long)length * (partition + 1) / count);
            partitions[partition] = new PartitionRange(start, end);
        }

        return partitions;
    }

    /// <summary>
    /// Splits [0, rowCount) into up to <paramref name="requestedParallelism"/> contiguous ranges of
    /// roughly equal nonzero (sparse-entry) count, using <paramref name="rowOffsets"/> as the CSR
    /// prefix sum. Balances work rather than row count so the parallel multiplies stay even.
    /// </summary>
    public static PartitionRange[] ByNonzeros(int[] rowOffsets, int requestedParallelism)
    {
        var rowCount = rowOffsets.Length - 1;
        if (rowCount <= 0 || requestedParallelism <= 1)
        {
            return new[] { new PartitionRange(0, rowCount) };
        }

        var partitionCount = Math.Min(requestedParallelism, rowCount);
        var totalNonzeros = rowOffsets[rowCount];
        if (totalNonzeros == 0)
        {
            return Even(rowCount, partitionCount);
        }

        var partitions = new PartitionRange[partitionCount];
        var start = 0;
        for (var partition = 0; partition < partitionCount; partition++)
        {
            int end;
            if (partition == partitionCount - 1)
            {
                end = rowCount;
            }
            else
            {
                var target = (long)totalNonzeros * (partition + 1) / partitionCount;
                end = LowerBound(rowOffsets, target, start + 1, rowCount);
                if (end <= start)
                {
                    end = start + 1;
                }

                var remainingRows = rowCount - end;
                var remainingPartitions = partitionCount - partition - 1;
                if (remainingRows < remainingPartitions)
                {
                    end = rowCount - remainingPartitions;
                }
            }

            partitions[partition] = new PartitionRange(start, end);
            start = end;
        }

        return partitions;
    }

    private static int LowerBound(int[] values, long target, int start, int end)
    {
        while (start < end)
        {
            var mid = start + ((end - start) / 2);
            if (values[mid] < target)
            {
                start = mid + 1;
            }
            else
            {
                end = mid;
            }
        }

        return start;
    }
}
