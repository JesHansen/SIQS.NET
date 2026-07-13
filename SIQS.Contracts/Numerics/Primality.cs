using System.Numerics;

namespace SIQS.Contracts.Numerics;

/// <summary>
/// Deterministic Miller-Rabin primality test. The fixed witness set
/// {2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37} is a proven deterministic test for all
/// n &lt; 3.3 * 10^24, which covers every value this implementation tests (factor base primes and
/// large-prime cofactors are far smaller).
/// </summary>
public static class Primality
{
    private static readonly int[] Witnesses = { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37 };

    /// <summary>True when <paramref name="n"/> is (probably, but deterministically in range) prime.</summary>
    public static bool IsProbablePrime(BigInteger n)
    {
        if (n < 2)
        {
            return false;
        }

        foreach (var w in Witnesses)
        {
            if (n == w)
            {
                return true;
            }

            if (n % w == 0)
            {
                return false;
            }
        }

        // Write n - 1 as d * 2^s with d odd.
        var d = n - 1;
        var s = 0;
        while ((d & 1) == 0)
        {
            d >>= 1;
            s++;
        }

        foreach (var a in Witnesses)
        {
            if (!PassesWitness(n, a, d, s))
            {
                return false;
            }
        }

        return true;
    }

    private static bool PassesWitness(BigInteger n, BigInteger a, BigInteger d, int s)
    {
        var x = BigInteger.ModPow(a, d, n);
        if (x == 1 || x == n - 1)
        {
            return true;
        }

        for (var r = 1; r < s; r++)
        {
            x = BigInteger.ModPow(x, 2, n);
            if (x == n - 1)
            {
                return true;
            }
        }

        return false;
    }
}
