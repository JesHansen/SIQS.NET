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

    /// <summary>
    /// Candidate-major control arm for the SIMD-resieve experiment. It preserves
    /// the exact reciprocal predicate while exposing the traversal order that a
    /// future vector implementation would use.
    /// </summary>
    public static void CollectCandidateMajor(
        FactorBaseData fb, int blockStart, int startPrimeIndex, int endPrimeIndex,
        int[] root1Res, int[] root2Res, ReadOnlySpan<int> candidateOffsets,
        List<int>[] primeHits)
    {
        for (var candidateIndex = 0; candidateIndex < candidateOffsets.Length; candidateIndex++)
        {
            var sieveIndex = blockStart + candidateOffsets[candidateIndex];
            for (var primeIndex = startPrimeIndex; primeIndex < endPrimeIndex; primeIndex++)
            {
                var firstDifference = sieveIndex - root1Res[primeIndex];
                var hit = firstDifference >= 0
                    && (ulong)firstDifference * fb.PrimeInverses[primeIndex] <= fb.PrimeDivThresholds[primeIndex];
                if (!hit)
                {
                    var secondRoot = root2Res[primeIndex];
                    var secondDifference = sieveIndex - secondRoot;
                    hit = secondRoot >= 0 && secondDifference >= 0
                        && (ulong)secondDifference * fb.PrimeInverses[primeIndex] <= fb.PrimeDivThresholds[primeIndex];
                }

                if (hit) primeHits[candidateIndex].Add(primeIndex);
            }
        }
    }

    /// <summary>
    /// Candidate-major AVX2 control which compares eight primes' block-local
    /// progression positions at a time.  It avoids reciprocal modular arithmetic
    /// and is exact for any block size by walking the statically bounded number of
    /// progression steps in vector lanes.
    /// </summary>
    public static void CollectCandidateMajorVector(
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

        var vectorEnd = startPrimeIndex + (endPrimeIndex - startPrimeIndex) / 8 * 8;
        var blockStartVector = Vector256.Create(blockStart);
        var negativeOne = Vector256.Create(-1);
        for (var candidateIndex = 0; candidateIndex < candidateOffsets.Length; candidateIndex++)
        {
            var candidate = Vector256.Create(candidateOffsets[candidateIndex]);
            var primeIndex = startPrimeIndex;
            for (; primeIndex < vectorEnd; primeIndex += 8)
            {
                var primes = CreatePrimeVector(fb, primeIndex);
                var first = CreateVector(pos1, primeIndex) - blockStartVector - primes;
                var second = CreateVector(pos2, primeIndex) - blockStartVector - primes;
                var hasSecond = Avx2.CompareGreaterThan(
                    CreateVector(root2Res, primeIndex), negativeOne);
                var matches = Vector256<int>.Zero;
                var steps = (blockSize + (int)fb.Primes[primeIndex] - 1)
                    / (int)fb.Primes[primeIndex] + 1;
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

    private static Vector256<int> CreatePrimeVector(FactorBaseData fb, int index)
        => Vector256.Create(
            (int)fb.Primes[index], (int)fb.Primes[index + 1],
            (int)fb.Primes[index + 2], (int)fb.Primes[index + 3],
            (int)fb.Primes[index + 4], (int)fb.Primes[index + 5],
            (int)fb.Primes[index + 6], (int)fb.Primes[index + 7]);

    private static Vector256<int> CreateVector(int[] values, int index)
        => Vector256.Create(
            values[index], values[index + 1], values[index + 2], values[index + 3],
            values[index + 4], values[index + 5], values[index + 6], values[index + 7]);

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
    /// Same progression kernel as <see cref="CollectCandidateMajorVector"/>, with contiguous
    /// unaligned vector loads from the compact prime and root arrays instead of constructing each
    /// vector from eight scalar element reads.
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
