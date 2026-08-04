using System.Runtime.InteropServices;

namespace Sieving.Tests;

public class DirectSieveKernelTests
{
    [Theory]
    [InlineData(512, 2_061)]
    [InlineData(500, 2_013)]
    public void Banded_fill_matches_generic_across_full_and_tail_blocks(int blockSize, int fullLength)
    {
        long[] primes =
        [
            37, 43, 67, 127, 128, 131, 167, 251, 256, 257, 263,
            499, 500, 503, 509, 521, 997, 1009,
        ];
        var factorBase = CreateFactorBase(primes);
        var parameters = CreateParameters(blockSize);
        var plan = DirectSievePlan.Build(factorBase, smallPrimeCount: 0, parameters);
        var logCredits = Enumerable.Range(0, primes.Length).Select(i => (byte)(i % 17 + 1)).ToArray();
        var initial1 = new int[primes.Length];
        var initial2 = new int[primes.Length];
        for (var i = 0; i < primes.Length; i++)
        {
            var prime = (int)primes[i];
            initial1[i] = (17 * i + 3) % prime;
            initial2[i] = (31 * i + 11) % prime;
            if (initial1[i] == initial2[i]) initial2[i] = (initial2[i] + 1) % prime;
        }

        initial2[5] = fullLength; // single-root prime
        initial1[11] = fullLength; // A-prime sentinel
        initial2[11] = fullLength;

        var generic1 = (int[])initial1.Clone();
        var generic2 = (int[])initial2.Clone();
        var banded1 = (int[])initial1.Clone();
        var banded2 = (int[])initial2.Clone();
        var genericSieve = new byte[blockSize];
        var bandedSieve = new byte[blockSize];

        for (var blockStart = 0; blockStart < fullLength; blockStart += blockSize)
        {
            var blockEnd = Math.Min(blockStart + blockSize, fullLength);
            Array.Clear(genericSieve);
            Array.Clear(bandedSieve);
            ref var generic0 = ref MemoryMarshal.GetArrayDataReference(genericSieve);
            ref var banded0 = ref MemoryMarshal.GetArrayDataReference(bandedSieve);

            DirectSieveKernel.FillGeneric(
                ref generic0, blockStart, blockEnd, fullLength,
                primes, logCredits, generic1, generic2, 0, primes.Length);
            DirectSieveKernel.FillBanded(
                ref banded0, blockStart, blockEnd, fullLength,
                plan, logCredits, banded1, banded2);

            Assert.Equal(genericSieve, bandedSieve);
            for (var i = 0; i < primes.Length; i++)
            {
                Assert.Equal(
                    new[] { generic1[i], generic2[i] }.Order(),
                    new[] { banded1[i], banded2[i] }.Order());
            }
        }
    }

    private static FactorBaseData CreateFactorBase(long[] primes) => new()
    {
        Count = primes.Length,
        Primes = primes,
        Columns = new int[primes.Length],
        Root1 = new long[primes.Length],
        Root2 = new long[primes.Length],
        LogP = new int[primes.Length],
        PrimeInverses = new ulong[primes.Length],
        PrimeDivThresholds = new ulong[primes.Length],
        TargetN = 1,
        Multiplier = 1,
        ScaledN = 1,
        Bound = primes[^1],
        LogScale = 1,
    };

    private static SievingParameters CreateParameters(int blockSize) => new(
        SieveHalfInterval: 1_024,
        PolynomialCount: 1,
        RelationTarget: 1,
        LargePrimeBound: 10_000,
        ErrorMargin: 0,
        OutputBatchSize: 1,
        APrimeCount: 1,
        APrimeWindowSize: 1,
        SieveBlockSize: blockSize);
}
