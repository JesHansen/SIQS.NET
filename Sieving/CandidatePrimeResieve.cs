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
