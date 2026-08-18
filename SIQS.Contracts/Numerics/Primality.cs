using System.Numerics;

namespace SIQS.Contracts.Numerics;

/// <summary>
/// Deterministic Miller-Rabin primality test. The fixed witness set
/// {2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41} is a proven deterministic test for all
/// n &lt; <see cref="DeterministicUpperBound"/>. For larger values it remains a probable-prime
/// test. It is also used as an inexpensive precheck for factorization targets.
/// </summary>
public static class Primality
{
    private static readonly int[] Witnesses = { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41 };

    /// <summary>
    /// Exclusive upper bound below which <see cref="IsProbablePrime"/> is deterministic for the
    /// fixed witness set.
    /// </summary>
    public static BigInteger DeterministicUpperBound { get; } =
        BigInteger.Parse("3317044064679887385961981");

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

    /// <summary>True when the fixed witness set is a deterministic primality test for <paramref name="n"/>.</summary>
    public static bool IsWithinDeterministicRange(BigInteger n)
        => n < DeterministicUpperBound;

    /// <summary>
    /// The Baillie-PSW probable-prime test: a base-2 strong Miller-Rabin test combined with a
    /// strong Lucas test using Selfridge's parameters. No composite is known to pass it, but a
    /// positive result is a probable-prime classification, not a general primality proof. The test
    /// uses no random bases and always terminates.
    /// </summary>
    public static bool IsBailliePswProbablePrime(BigInteger n)
    {
        if (n < 2)
        {
            return false;
        }

        // Trial division by the small primes settles tiny inputs and cheaply rejects
        // most composites (including every even number, via the factor 2).
        foreach (var p in Witnesses)
        {
            if (n == p)
            {
                return true;
            }

            if (n % p == 0)
            {
                return false;
            }
        }

        // A perfect square admits no Lucas discriminant D with Jacobi(D, n) = -1, so the
        // Selfridge search below would never terminate; exclude squares up front.
        if (IntegerMath.IsPerfectSquare(n, out _))
        {
            return false;
        }

        if (!PassesStrongBase2(n))
        {
            return false;
        }

        // Selfridge parameter selection. A zero Jacobi symbol along the way exposes a proper
        // factor of n (gcd with a small D), which makes n composite.
        if (!TrySelectSelfridgeDiscriminant(n, out var discriminant, out var q))
        {
            return false;
        }

        return PassesStrongLucas(n, discriminant, q);
    }

    private static bool PassesStrongBase2(BigInteger n)
    {
        var d = n - 1;
        var s = 0;
        while ((d & 1) == 0)
        {
            d >>= 1;
            s++;
        }

        return PassesWitness(n, 2, d, s);
    }

    /// <summary>
    /// Selfridge's Method A: scan D over 5, -7, 9, -11, 13, ... and take the first with
    /// Jacobi(D, n) = -1. Returns the Lucas parameters (P is always 1, Q = (1 - D) / 4).
    /// Returns false if a scanned |D| shares a factor with n (Jacobi = 0), proving n composite.
    /// </summary>
    private static bool TrySelectSelfridgeDiscriminant(BigInteger n, out BigInteger discriminant, out BigInteger q)
    {
        var magnitude = 5;
        var sign = 1;
        while (true)
        {
            var d = sign * magnitude;
            var jacobi = Jacobi(d, n);
            if (jacobi == -1)
            {
                discriminant = d;
                q = (BigInteger.One - d) / 4;
                return true;
            }

            if (jacobi == 0)
            {
                // gcd(|D|, n) is a proper divisor of n (n is larger than every scanned |D|).
                discriminant = BigInteger.Zero;
                q = BigInteger.Zero;
                return false;
            }

            magnitude += 2;
            sign = -sign;
        }
    }

    /// <summary>
    /// Strong Lucas probable-prime test with P = 1 and the given <paramref name="discriminant"/>
    /// (D) and <paramref name="q"/> (Q). n is a strong Lucas probable prime when U_d = 0 or one
    /// of V_d, V_2d, ..., V_(d·2^(s-1)) is 0 (mod n), where n + 1 = d·2^s with d odd.
    /// </summary>
    private static bool PassesStrongLucas(BigInteger n, BigInteger discriminant, BigInteger q)
    {
        var d = n + 1;
        var s = 0;
        while ((d & 1) == 0)
        {
            d >>= 1;
            s++;
        }

        var (u, v, qPower) = LucasSequence(n, d, discriminant, q);
        if (u.IsZero)
        {
            return true;
        }

        for (var r = 0; r < s; r++)
        {
            if (v.IsZero)
            {
                return true;
            }

            v = IntegerMath.Mod(v * v - 2 * qPower, n);
            qPower = IntegerMath.Mod(qPower * qPower, n);
        }

        return false;
    }

    /// <summary>
    /// Computes (U_k, V_k, Q^k) mod n for the Lucas sequence with P = 1 and the given
    /// discriminant D and parameter Q, using the standard binary doubling method.
    /// </summary>
    private static (BigInteger U, BigInteger V, BigInteger QPower) LucasSequence(
        BigInteger n, BigInteger k, BigInteger discriminant, BigInteger q)
    {
        // n is odd, so (n + 1) / 2 is the modular inverse of 2 used to halve after each increment.
        var half = (n + 1) >> 1;
        var qBase = IntegerMath.Mod(q, n);

        // Seed with index 1: (U_1, V_1, Q^1) = (1, P, Q), then fold in the remaining bits of k.
        var u = BigInteger.One;
        var v = BigInteger.One; // P = 1
        var qPower = qBase;

        for (var i = (int)k.GetBitLength() - 2; i >= 0; i--)
        {
            // Doubling step: index k -> 2k.
            u = IntegerMath.Mod(u * v, n);
            v = IntegerMath.Mod(v * v - 2 * qPower, n);
            qPower = IntegerMath.Mod(qPower * qPower, n);

            if (((k >> i) & 1) == 1)
            {
                // Increment step: index 2k -> 2k + 1 (P = 1).
                var nextU = IntegerMath.Mod((u + v) * half, n);
                var nextV = IntegerMath.Mod((discriminant * u + v) * half, n);
                u = nextU;
                v = nextV;
                qPower = IntegerMath.Mod(qPower * qBase, n);
            }
        }

        return (u, v, qPower);
    }

    /// <summary>Jacobi symbol (a | n) for odd n &gt; 0.</summary>
    private static int Jacobi(BigInteger a, BigInteger n)
    {
        a = IntegerMath.Mod(a, n);
        var result = 1;
        while (!a.IsZero)
        {
            while (a.IsEven)
            {
                a >>= 1;
                var residue = (int)(n & 7);
                if (residue == 3 || residue == 5)
                {
                    result = -result;
                }
            }

            (a, n) = (n, a);
            if ((a & 3) == 3 && (n & 3) == 3)
            {
                result = -result;
            }

            a %= n;
        }

        return n == BigInteger.One ? result : 0;
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
