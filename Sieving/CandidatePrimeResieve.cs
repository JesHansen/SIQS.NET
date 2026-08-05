using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Sieving;

/// <summary>
/// Rediscovers which resieve-band primes divide a block's candidates by walking each root's hit
/// progression <em>backwards</em> from its post-fill position, so no first-hit division is needed and
/// the pass only runs for blocks that actually produced candidates.
/// </summary>
internal static class CandidatePrimeResieve
{
    public static void Collect(
        FactorBaseData fb,
        int blockStart,
        int startPrimeIndex,
        int endPrimeIndex,
        bool[] isAPrime,
        int[] pos1,
        int[] pos2,
        int[] root2Res,
        ReadOnlySpan<int> candidateOffsets,
        List<int>[] primeHits)
    {
        var bloom = 0UL;
        foreach (var offset in candidateOffsets)
        {
            bloom |= 1UL << (offset & 63);
        }

        for (var i = startPrimeIndex; i < endPrimeIndex; i++)
        {
            var p = (int)fb.Primes[i];
            if (isAPrime[i] && p == 2)
            {
                // Defensive: mirrors the init sentinel — pos1 never advances for this case,
                // so a back-walk from it would fabricate hits.
                continue;
            }

            WalkProgressionBack(i, pos1[i] - blockStart - p, p, bloom, candidateOffsets, primeHits);
            if (root2Res[i] >= 0)
            {
                // root2Res >= 0 marks a genuine second root; pos2 is otherwise a sentinel
                // that never advances and must not be walked.
                WalkProgressionBack(i, pos2[i] - blockStart - p, p, bloom, candidateOffsets, primeHits);
            }
        }
    }

    private static void CollectCandidateMajorFromProgressions(
        FactorBaseData fb, int blockStart, int startPrimeIndex, int endPrimeIndex,
        int[] pos1, int[] pos2, int[] root2Res, ReadOnlySpan<int> candidateOffsets,
        List<int>[] primeHits)
    {
        for (var candidateIndex = 0; candidateIndex < candidateOffsets.Length; candidateIndex++)
            CollectCandidateProgressionsScalar(
                fb, blockStart, startPrimeIndex, endPrimeIndex, pos1, pos2,
                root2Res, candidateOffsets[candidateIndex], primeHits[candidateIndex]);
    }

    private static void CollectCandidateProgressionsScalar(
        FactorBaseData fb, int blockStart, int startPrimeIndex, int endPrimeIndex,
        int[] pos1, int[] pos2, int[] root2Res, int candidateOffset,
        List<int> primeHits)
    {
        for (var primeIndex = startPrimeIndex; primeIndex < endPrimeIndex; primeIndex++)
        {
            var prime = (int)fb.Primes[primeIndex];
            var hit = ProgressionContains(pos1[primeIndex] - blockStart - prime, prime,
                candidateOffset);
            if (!hit && root2Res[primeIndex] >= 0)
                hit = ProgressionContains(pos2[primeIndex] - blockStart - prime, prime,
                    candidateOffset);
            if (hit) primeHits.Add(primeIndex);
        }
    }

    private static bool ProgressionContains(int last, int prime, int candidateOffset)
    {
        for (var position = last; position >= 0; position -= prime)
            if (position == candidateOffset) return true;
        return false;
    }

    public static void CollectWithOffsetMap(
        FactorBaseData fb, int blockStart, int startPrimeIndex, int endPrimeIndex,
        bool[] isAPrime, int[] pos1, int[] pos2, int[] root2Res,
        ReadOnlySpan<int> offsetToCandidate, List<int>[] primeHits)
    {
        for (var i = startPrimeIndex; i < endPrimeIndex; i++)
        {
            var p = (int)fb.Primes[i];
            if (isAPrime[i] && p == 2) continue;
            AddMappedProgression(i, pos1[i] - blockStart - p, p,
                offsetToCandidate, primeHits);
            if (root2Res[i] >= 0)
                AddMappedProgression(i, pos2[i] - blockStart - p, p,
                    offsetToCandidate, primeHits);
        }
    }

    /// <summary>
    /// Candidate-major AVX2 progression walk that compares eight primes' block-local progression
    /// positions at a time, using contiguous unaligned vector loads from the compact prime and root
    /// arrays. Exact for any block size: it walks the statically bounded number of progression steps
    /// in vector lanes rather than using reciprocal modular arithmetic.
    /// </summary>
    public static void CollectCandidateMajorVectorContiguous(
        FactorBaseData fb, int blockStart, int blockSize,
        int startPrimeIndex, int endPrimeIndex, int[] pos1, int[] pos2,
        int[] root2Res, ReadOnlySpan<int> candidateOffsets, List<int>[] primeHits)
    {
        if (!Avx2.IsSupported)
        {
            CollectCandidateMajorFromProgressions(
                fb, blockStart, startPrimeIndex, endPrimeIndex,
                pos1, pos2, root2Res, candidateOffsets, primeHits);
            return;
        }

        var primes32 = fb.Primes32;
        var vectorEnd = startPrimeIndex + (endPrimeIndex - startPrimeIndex) / 8 * 8;
        var blockStartVector = Vector256.Create(blockStart);
        var negativeOne = Vector256.Create(-1);
        ref var primes0 = ref MemoryMarshal.GetArrayDataReference(primes32);
        ref var pos10 = ref MemoryMarshal.GetArrayDataReference(pos1);
        ref var pos20 = ref MemoryMarshal.GetArrayDataReference(pos2);
        ref var root20 = ref MemoryMarshal.GetArrayDataReference(root2Res);

        for (var candidateIndex = 0; candidateIndex < candidateOffsets.Length; candidateIndex++)
        {
            var candidate = Vector256.Create(candidateOffsets[candidateIndex]);
            var primeIndex = startPrimeIndex;
            for (; primeIndex < vectorEnd; primeIndex += 8)
            {
                var primes = Vector256.LoadUnsafe(ref Unsafe.Add(ref primes0, primeIndex));
                var first = Vector256.LoadUnsafe(ref Unsafe.Add(ref pos10, primeIndex))
                    - blockStartVector - primes;
                var second = Vector256.LoadUnsafe(ref Unsafe.Add(ref pos20, primeIndex))
                    - blockStartVector - primes;
                var hasSecond = Avx2.CompareGreaterThan(
                    Vector256.LoadUnsafe(ref Unsafe.Add(ref root20, primeIndex)), negativeOne);
                var matches = Vector256<int>.Zero;
                var steps = (blockSize + primes32[primeIndex] - 1) / primes32[primeIndex] + 1;
                for (var step = 0; step < steps; step++)
                {
                    matches = Avx2.Or(matches, Avx2.CompareEqual(first, candidate));
                    matches = Avx2.Or(matches,
                        Avx2.And(Avx2.CompareEqual(second, candidate), hasSecond));
                    first = Avx2.Subtract(first, primes);
                    second = Avx2.Subtract(second, primes);
                }

                var mask = Avx.MoveMask(matches.AsSingle());
                while (mask != 0)
                {
                    var lane = System.Numerics.BitOperations.TrailingZeroCount((uint)mask);
                    primeHits[candidateIndex].Add(primeIndex + lane);
                    mask &= mask - 1;
                }
            }

            CollectCandidateProgressionsScalar(
                fb, blockStart, primeIndex, endPrimeIndex, pos1, pos2,
                root2Res, candidateOffsets[candidateIndex], primeHits[candidateIndex]);
        }
    }

    private static void AddMappedProgression(
        int primeIndex, int offset, int p, ReadOnlySpan<int> offsetToCandidate,
        List<int>[] primeHits)
    {
        for (; offset >= 0; offset -= p)
        {
            var candidateIndex = offsetToCandidate[offset];
            if (candidateIndex >= 0) primeHits[candidateIndex].Add(primeIndex);
        }
    }
    private static void WalkProgressionBack(
        int primeIndex,
        int offset,
        int p,
        ulong bloom,
        ReadOnlySpan<int> candidateOffsets,
        List<int>[] primeHits)
    {
        for (; offset >= 0; offset -= p)
        {
            if ((bloom & (1UL << (offset & 63))) == 0)
            {
                continue;
            }

            var match = candidateOffsets.BinarySearch(offset);
            if (match >= 0)
            {
                primeHits[match].Add(primeIndex);
            }
        }
    }
}
