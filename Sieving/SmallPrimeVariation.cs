namespace Sieving;

/// <summary>
/// Describes the small-prime-variation band and its statistical preliminary-scan
/// allowance. The exact skipped-prime credit is recovered before a provisional report becomes a
/// candidate; the allowance controls only how generously reports enter that recovery stage.
/// </summary>
internal readonly record struct SmallPrimeVariation(int Count, byte Allowance)
{
    private const double StandardDeviationMultiplier = 2.5;

    public bool Enabled => Count > 0;

    public static SmallPrimeVariation Build(FactorBaseData fb, byte[] byteLogP, int primeBound)
    {
        if (primeBound <= 0)
        {
            return default;
        }

        var count = 0;
        var mean = 0.0;
        var variance = 0.0;
        while (count < fb.Count && fb.Primes[count] <= primeBound)
        {
            var p = fb.Primes[count];
            var roots = fb.Root1[count] == fb.Root2[count] ? 1.0 : 2.0;
            var hitProbability = Math.Min(1.0, roots / p);
            var credit = byteLogP[count];
            mean += credit * hitProbability;
            variance += credit * credit * hitProbability * (1.0 - hitProbability);
            count++;
        }

        var allowance = (int)Math.Ceiling(mean + StandardDeviationMultiplier * Math.Sqrt(variance));
        return new SmallPrimeVariation(count, (byte)Math.Clamp(allowance, 0, byte.MaxValue));
    }

    public byte PreliminaryThreshold(byte exactThreshold)
        => (byte)Math.Max(0, exactThreshold - Allowance);

    // Recovery is a single pass over the whole omitted band. A two-stage variant was tried and
    // rejected: recover the primes up to a lower bound, apply a remaining statistical allowance, then
    // recover the upper band only for the survivors. It helped C87 (~2.7%) but was neutral to ~2%
    // slower at C110 depending on the SPV bound — the extra branches and second pass do not justify a
    // size-specific path. Only the wider single-stage bound survived to production.
    public int RecoverCredit(
        FactorBaseData fb,
        byte[] byteLogP,
        int sieveIndex,
        int[] root1Residues,
        int[] root2Residues)
    {
        var credit = 0;
        for (var i = 0; i < Count; i++)
        {
            if (HitsRoot(fb, i, sieveIndex, root1Residues[i]) ||
                HitsRoot(fb, i, sieveIndex, root2Residues[i]))
            {
                credit += byteLogP[i];
            }
        }

        return credit;
    }

    private static bool HitsRoot(FactorBaseData fb, int primeIndex, int sieveIndex, int root)
    {
        if (root < 0 || sieveIndex < root)
        {
            return false;
        }

        var difference = sieveIndex - root;
        var prime = fb.Primes[primeIndex];
        if (prime == 2)
        {
            return (difference & 1) == 0;
        }

        return (ulong)difference * fb.PrimeInverses[primeIndex] <= fb.PrimeDivThresholds[primeIndex];
    }
}
