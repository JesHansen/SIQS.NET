using System.Numerics;

namespace Factorbase;

/// <summary>
/// Number-theory routines specific to factor base construction: the Legendre symbol and the
/// Tonelli-Shanks modular square root. Only the Factorbase phase computes modular roots; later
/// phases read them from <c>factor_base.txt</c>.
/// </summary>
public static class NumberTheory
{
    /// <summary>
    /// Computes the Legendre symbol <c>(a | p)</c> for an odd prime <paramref name="p"/>:
    /// 0 if p divides a, 1 if a is a non-zero quadratic residue, -1 otherwise.
    /// </summary>
    public static int Legendre(BigInteger a, long p)
    {
        var residue = Mod(a, p);
        if (residue.IsZero)
        {
            return 0;
        }

        var power = BigInteger.ModPow(residue, (p - 1) / 2, p);
        return power == 1 ? 1 : -1;
    }

    /// <summary>
    /// Returns a square root <c>r</c> of <paramref name="n"/> modulo the odd prime
    /// <paramref name="p"/>, i.e. <c>r^2 == n (mod p)</c>, with <c>0 &lt;= r &lt; p</c>.
    /// Must only be called when <c>n mod p</c> is a non-zero quadratic residue.
    /// </summary>
    public static long TonelliShanks(BigInteger n, long p)
    {
        var a = Mod(n, p);
        if (a.IsZero)
        {
            throw new ArgumentException("Tonelli-Shanks requires n mod p != 0.", nameof(n));
        }

        if (p == 2)
        {
            return (long)a;
        }

        // Fast path for p == 3 (mod 4).
        if (p % 4 == 3)
        {
            return (long)BigInteger.ModPow(a, (p + 1) / 4, p);
        }

        // Factor p - 1 as q * 2^s with q odd.
        long s = 0;
        var q = p - 1;
        while ((q & 1) == 0)
        {
            q >>= 1;
            s++;
        }

        // Find a quadratic non-residue z.
        BigInteger z = 2;
        while (Legendre(z, p) != -1)
        {
            z++;
        }

        var m = s;
        var c = BigInteger.ModPow(z, q, p);
        var t = BigInteger.ModPow(a, q, p);
        var r = BigInteger.ModPow(a, (q + 1) / 2, p);

        while (t != 1)
        {
            // Find the least i, 0 < i < m, such that t^(2^i) == 1.
            long i = 0;
            var temp = t;
            while (temp != 1)
            {
                temp = temp * temp % p;
                i++;
            }

            var b = BigInteger.ModPow(c, BigInteger.Pow(2, (int)(m - i - 1)), p);
            m = i;
            c = b * b % p;
            t = t * c % p;
            r = r * b % p;
        }

        return (long)r;
    }

    private static BigInteger Mod(BigInteger a, long p)
    {
        var r = a % p;
        return r < 0 ? r + p : r;
    }
}
