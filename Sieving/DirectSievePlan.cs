using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Sieving;

/// <summary>
/// Immutable hot-path view of the directly sieved factor-base band. Prime values are compacted to
/// 32 bits, and ascending index boundaries identify ranges which can produce at most one through
/// four hits per root in a sieve block.
/// </summary>
internal sealed class DirectSievePlan
{
    public required int StartIndex { get; init; }
    public required int BucketStart { get; init; }
    public required int ResieveStart { get; init; }
    public required int FourHitStart { get; init; }
    public required int ThreeHitStart { get; init; }
    public required int TwoHitStart { get; init; }
    public required int OneHitStart { get; init; }
    public required int[] Primes { get; init; }

    public static DirectSievePlan Build(
        FactorBaseData factorBase, int smallPrimeCount, SievingParameters parameters)
    {
        var bucketStart = factorBase.Count;
        var bucketCutoff = parameters.BucketLargePrimeCutoff;
        if (bucketCutoff > 0)
        {
            bucketStart = LowerBound(factorBase.Primes, smallPrimeCount, factorBase.Count, bucketCutoff);
        }

        var resieveStart = bucketStart;
        var resieveCutoff = parameters.ResieveLargePrimeCutoff;
        if (resieveCutoff > 0)
        {
            resieveStart = LowerBound(factorBase.Primes, smallPrimeCount, bucketStart, resieveCutoff);
        }

        var primes = new int[bucketStart];
        for (var i = 0; i < bucketStart; i++)
        {
            primes[i] = checked((int)factorBase.Primes[i]);
        }

        var blockSize = parameters.EffectiveSieveBlockSize;
        var fourHitStart = LowerBound(primes, smallPrimeCount, bucketStart, CeilingDivide(blockSize, 4));
        var threeHitStart = LowerBound(primes, fourHitStart, bucketStart, CeilingDivide(blockSize, 3));
        var twoHitStart = LowerBound(primes, threeHitStart, bucketStart, CeilingDivide(blockSize, 2));
        var oneHitStart = LowerBound(primes, twoHitStart, bucketStart, blockSize);

        return new DirectSievePlan
        {
            StartIndex = smallPrimeCount,
            BucketStart = bucketStart,
            ResieveStart = resieveStart,
            FourHitStart = fourHitStart,
            ThreeHitStart = threeHitStart,
            TwoHitStart = twoHitStart,
            OneHitStart = oneHitStart,
            Primes = primes,
        };
    }

    private static int CeilingDivide(int dividend, int divisor) => (dividend + divisor - 1) / divisor;

    private static int LowerBound(long[] values, int start, int end, long target)
    {
        while (start < end)
        {
            var middle = start + ((end - start) >> 1);
            if (values[middle] < target) start = middle + 1;
            else end = middle;
        }

        return start;
    }

    private static int LowerBound(int[] values, int start, int end, int target)
    {
        while (start < end)
        {
            var middle = start + ((end - start) >> 1);
            if (values[middle] < target) start = middle + 1;
            else end = middle;
        }

        return start;
    }
}

/// <summary>Scalar direct-sieve kernels selected by the maximum possible hits per root.</summary>
// These kernels are deliberately scalar and deliberately modest. Band dispatch (splitting the
// generic two-root loop into statically bounded 1/2/3/4-hit kernels) was measured as a repeatable
// but small ~3-5% fill win, and that is the ceiling for this shape of change on this host:
//   • An AVX2 version of the fill is not merely unwritten but impossible to make pay: .NET/AVX2 has
//     no byte-granular scatter and Zen 3 has no AVX-512, so each hit still needs an individual scalar
//     byte write. FillGeneric is kept only as an interleaved correctness/benchmark control.
//   • Cache-tiling the dense band into L1 windows, software-prefetching the strided writes, hoisting
//     block-relative write positions, and interleaving two primes for wider ILP were all measured
//     slower-to-neutral (prefetch as bad as +48%). The fill is instruction-throughput bound on an
//     L2-resident block, so latency hiding and µop hoisting have nothing to remove.
internal static class DirectSieveKernel
{
    public static void FillBanded(
        ref byte sieve0, int blockStart, int blockEnd, int fullLength,
        DirectSievePlan plan, byte[] logCredits, int[] position1, int[] position2)
    {
        FillGenericCompact(
            ref sieve0, blockStart, blockEnd, fullLength,
            plan.Primes, logCredits, position1, position2,
            plan.StartIndex, plan.FourHitStart);
        FillAtMostFour(
            ref sieve0, blockStart, blockEnd,
            plan.Primes, logCredits, position1, position2,
            plan.FourHitStart, plan.ThreeHitStart);
        FillAtMostThree(
            ref sieve0, blockStart, blockEnd,
            plan.Primes, logCredits, position1, position2,
            plan.ThreeHitStart, plan.TwoHitStart);
        FillAtMostTwo(
            ref sieve0, blockStart, blockEnd,
            plan.Primes, logCredits, position1, position2,
            plan.TwoHitStart, plan.OneHitStart);
        FillAtMostOne(
            ref sieve0, blockStart, blockEnd,
            plan.Primes, logCredits, position1, position2,
            plan.OneHitStart, plan.BucketStart);
    }

    public static void FillGeneric(
        ref byte sieve0, int blockStart, int blockEnd, int fullLength,
        long[] primes, byte[] logCredits, int[] position1, int[] position2,
        int startIndex, int endIndex)
    {
        for (var i = startIndex; i < endIndex; i++)
        {
            FillGenericPrime(
                ref sieve0, blockStart, blockEnd, fullLength, (int)primes[i], logCredits[i],
                position1, position2, i);
        }
    }

    private static void FillGenericCompact(
        ref byte sieve0, int blockStart, int blockEnd, int fullLength,
        int[] primes, byte[] logCredits, int[] position1, int[] position2,
        int startIndex, int endIndex)
    {
        for (var i = startIndex; i < endIndex; i++)
        {
            FillGenericPrime(
                ref sieve0, blockStart, blockEnd, fullLength, primes[i], logCredits[i],
                position1, position2, i);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FillGenericPrime(
        ref byte sieve0, int blockStart, int blockEnd, int fullLength, int prime, byte logCredit,
        int[] position1, int[] position2, int index)
    {
        var first = position1[index];
        var second = position2[index];
        if (second < fullLength)
        {
            if (first > second) (first, second) = (second, first);
            while (second < blockEnd)
            {
                Unsafe.Add(ref sieve0, first - blockStart) += logCredit;
                first += prime;
                Unsafe.Add(ref sieve0, second - blockStart) += logCredit;
                second += prime;
            }

            if (first < blockEnd)
            {
                Unsafe.Add(ref sieve0, first - blockStart) += logCredit;
                first += prime;
            }
        }
        else
        {
            while (first < blockEnd)
            {
                Unsafe.Add(ref sieve0, first - blockStart) += logCredit;
                first += prime;
            }
        }

        position1[index] = first;
        position2[index] = second;
    }

    private static void FillAtMostOne(
        ref byte sieve0, int blockStart, int blockEnd,
        int[] primes, byte[] logCredits, int[] position1, int[] position2,
        int startIndex, int endIndex)
    {
        for (var i = startIndex; i < endIndex; i++)
        {
            var prime = primes[i];
            var logCredit = logCredits[i];
            var first = AddIfInBlock(ref sieve0, position1[i], blockStart, blockEnd, prime, logCredit);
            var second = AddIfInBlock(ref sieve0, position2[i], blockStart, blockEnd, prime, logCredit);
            position1[i] = first;
            position2[i] = second;
        }
    }

    private static void FillAtMostTwo(
        ref byte sieve0, int blockStart, int blockEnd,
        int[] primes, byte[] logCredits, int[] position1, int[] position2,
        int startIndex, int endIndex)
    {
        for (var i = startIndex; i < endIndex; i++)
        {
            var prime = primes[i];
            var logCredit = logCredits[i];
            var first = AddIfInBlock(ref sieve0, position1[i], blockStart, blockEnd, prime, logCredit);
            first = AddIfInBlock(ref sieve0, first, blockStart, blockEnd, prime, logCredit);
            var second = AddIfInBlock(ref sieve0, position2[i], blockStart, blockEnd, prime, logCredit);
            second = AddIfInBlock(ref sieve0, second, blockStart, blockEnd, prime, logCredit);
            position1[i] = first;
            position2[i] = second;
        }
    }

    private static void FillAtMostThree(
        ref byte sieve0, int blockStart, int blockEnd,
        int[] primes, byte[] logCredits, int[] position1, int[] position2,
        int startIndex, int endIndex)
    {
        for (var i = startIndex; i < endIndex; i++)
        {
            var prime = primes[i];
            var logCredit = logCredits[i];
            var first = AddIfInBlock(ref sieve0, position1[i], blockStart, blockEnd, prime, logCredit);
            first = AddIfInBlock(ref sieve0, first, blockStart, blockEnd, prime, logCredit);
            first = AddIfInBlock(ref sieve0, first, blockStart, blockEnd, prime, logCredit);
            var second = AddIfInBlock(ref sieve0, position2[i], blockStart, blockEnd, prime, logCredit);
            second = AddIfInBlock(ref sieve0, second, blockStart, blockEnd, prime, logCredit);
            second = AddIfInBlock(ref sieve0, second, blockStart, blockEnd, prime, logCredit);
            position1[i] = first;
            position2[i] = second;
        }
    }

    private static void FillAtMostFour(
        ref byte sieve0, int blockStart, int blockEnd,
        int[] primes, byte[] logCredits, int[] position1, int[] position2,
        int startIndex, int endIndex)
    {
        for (var i = startIndex; i < endIndex; i++)
        {
            var prime = primes[i];
            var logCredit = logCredits[i];
            var first = AddIfInBlock(ref sieve0, position1[i], blockStart, blockEnd, prime, logCredit);
            first = AddIfInBlock(ref sieve0, first, blockStart, blockEnd, prime, logCredit);
            first = AddIfInBlock(ref sieve0, first, blockStart, blockEnd, prime, logCredit);
            first = AddIfInBlock(ref sieve0, first, blockStart, blockEnd, prime, logCredit);
            var second = AddIfInBlock(ref sieve0, position2[i], blockStart, blockEnd, prime, logCredit);
            second = AddIfInBlock(ref sieve0, second, blockStart, blockEnd, prime, logCredit);
            second = AddIfInBlock(ref sieve0, second, blockStart, blockEnd, prime, logCredit);
            second = AddIfInBlock(ref sieve0, second, blockStart, blockEnd, prime, logCredit);
            position1[i] = first;
            position2[i] = second;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int AddIfInBlock(
        ref byte sieve0, int position, int blockStart, int blockEnd, int prime, byte logCredit)
    {
        Debug.Assert(position >= blockStart);
        if (position < blockEnd)
        {
            Unsafe.Add(ref sieve0, position - blockStart) += logCredit;
            position += prime;
        }

        return position;
    }
}
