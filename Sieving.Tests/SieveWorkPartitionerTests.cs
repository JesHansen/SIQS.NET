namespace Sieving.Tests;

public sealed class SieveWorkPartitionerTests
{
    [Fact]
    public void Work_partitioner_claims_one_expensive_a_family_at_a_time()
    {
        var enumerated = 0;
        var partitions = SieveRunCoordinator
            .CreateWorkPartitioner(Source())
            .GetDynamicPartitions();

        try
        {
            using var partition = partitions.GetEnumerator();

            Assert.True(partition.MoveNext());
            Assert.Equal(1, Volatile.Read(ref enumerated));
            Assert.True(partition.MoveNext());
            Assert.Equal(2, Volatile.Read(ref enumerated));
        }
        finally
        {
            (partitions as IDisposable)?.Dispose();
        }

        IEnumerable<int> Source()
        {
            for (var value = 0; value < 100; value++)
            {
                Interlocked.Increment(ref enumerated);
                yield return value;
            }
        }
    }
}
