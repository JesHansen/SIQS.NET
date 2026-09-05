using System.Runtime.InteropServices;

namespace Sieving.Tests;

public class BucketScatterTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(11)]
    public void Scatter_matches_reference_with_negative_roots_tails_and_overflowed_advances(int blockSize)
    {
        const int length = 35;
        long[] primes = [5, 11, 17, int.MaxValue];
        int[] first = [0, -1, length - 1, 7];
        int[] second = [1, 3, int.MinValue, length];
        byte[] credits = [3, 5, 7, 11];
        var fb = new FactorBaseData
        {
            Count = primes.Length, Primes = primes, Columns = [], Root1 = [], Root2 = [],
            LogP = [], PrimeInverses = [], PrimeDivThresholds = [],
            TargetN = 1, Multiplier = 1, ScaledN = 1, Bound = int.MaxValue, LogScale = 1,
        };
        var blockCount = (length + blockSize - 1) / blockSize;
        var buckets = new LargePrimeBuckets(new(new(blockCount), new(32), new(blockSize)));

        PolynomialSieveWorker.ScatterBandPrimeHits(
            fb, 0, length, blockSize, credits, first, second, buckets);

        for (var block = 0; block < blockCount; block++)
        {
            var actual = new byte[blockSize];
            ref var actual0 = ref MemoryMarshal.GetArrayDataReference(actual);
            buckets.PrepareBlock(new(block), ref actual0);
            for (var offset = 0; offset < blockSize; offset++)
            {
                var position = block * blockSize + offset;
                var expectedPrimes = Enumerable.Range(0, primes.Length)
                    .Where(i => position < length && new[] { first[i], second[i] }
                        .Any(root => root >= 0 && position >= root && (position - root) % primes[i] == 0))
                    .ToArray();
                Assert.Equal(expectedPrimes.Sum(i => credits[i]), actual[offset]);
                var primeHits = new List<int>[] { [] };
                buckets.CollectCandidateHits(new(block), [offset], primeHits);
                Assert.Equal(expectedPrimes, primeHits[0]);
            }
        }

        Assert.Equal(0, buckets.OverflowHitCount);
    }
}
