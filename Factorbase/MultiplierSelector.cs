using System.Numerics;

namespace Factorbase;

/// <summary>
/// Selects the SIQS multiplier <c>k</c> maximizing the classical Knuth-Schroeppel score over
/// <c>p = 2</c> and odd primes <c>p &lt;= 100</c>, including multiplier size penalty. Ties are
/// broken deterministically by choosing the smaller multiplier.
/// </summary>
public static class MultiplierSelector
{
    /// <summary>The deterministic set of candidate multipliers tested when none is supplied.</summary>
    public static readonly IReadOnlyList<long> Candidates =
        new long[] { 1, 2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47 };

    private static readonly IReadOnlyList<long> ScoringPrimes =
        PrimeSieve.PrimesUpTo(100).Where(p => p != 2).ToArray();

    /// <summary>Selects the best multiplier for <paramref name="targetN"/>.</summary>
    public static long Select(BigInteger targetN)
    {
        var best = Candidates[0];
        var bestScore = double.NegativeInfinity;

        foreach (var k in Candidates)
        {
            var score = Score(k, targetN);
            if (score > bestScore)
            {
                bestScore = score;
                best = k;
            }
        }

        return best;
    }

    /// <summary>Computes the deterministic Knuth-Schroeppel score of a multiplier.</summary>
    public static double Score(long k, BigInteger targetN)
    {
        if (k < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(k), "Multiplier must be positive.");
        }

        var scaled = k * targetN;
        var score = 0.0;

        foreach (var p in ScoringPrimes)
        {
            if (k % p == 0)
            {
                // p divides only k after the factor-base precheck, so its special root contributes
                // to 1/p of sieve values.
                score += Math.Log(p) / p;
                continue;
            }

            if (NumberTheory.Legendre(scaled, p) == 1)
            {
                score += 2.0 * Math.Log(p) / (p - 1);
            }
        }

        score += PowerOfTwoContribution(scaled);
        score -= 0.5 * Math.Log(k);

        return score;
    }

    private static double PowerOfTwoContribution(BigInteger scaled)
    {
        if (scaled.IsEven)
        {
            return 0.0;
        }

        var mod8 = (int)(scaled % 8);
        return mod8 switch
        {
            1 => 2.0 * Math.Log(2.0),
            5 => Math.Log(2.0),
            _ => 0.5 * Math.Log(2.0)
        };
    }
}
