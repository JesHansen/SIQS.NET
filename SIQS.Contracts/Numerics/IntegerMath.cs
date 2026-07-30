using System.Numerics;

namespace SIQS.Contracts.Numerics;

/// <summary>
/// General-purpose arbitrary-precision integer helpers shared by the phase libraries: floor
/// integer roots, perfect-power detection, and modular inverse. These are plain number-theory
/// utilities, not SIQS-specific algorithms.
/// </summary>
public static class IntegerMath
{
    /// <summary>Returns the floor of the square root of a non-negative integer.</summary>
    public static BigInteger Sqrt(BigInteger n)
    {
        if (n < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(n), "Square root is undefined for negative values.");
        }

        if (n.IsZero)
        {
            return BigInteger.Zero;
        }

        // Initial estimate from the bit length, then Newton's method to convergence.
        var bits = (int)n.GetBitLength();
        var x = BigInteger.One << ((bits + 1) / 2);

        while (true)
        {
            var next = (x + n / x) >> 1;
            if (next >= x)
            {
                break;
            }

            x = next;
        }

        // x is now floor(sqrt(n)) +/- small; correct downward then upward to be exact.
        while (x * x > n)
        {
            x--;
        }

        while ((x + 1) * (x + 1) <= n)
        {
            x++;
        }

        return x;
    }

    /// <summary>True when <paramref name="n"/> is a perfect square, with its root in <paramref name="root"/>.</summary>
    public static bool IsPerfectSquare(BigInteger n, out BigInteger root)
    {
        if (n < 0)
        {
            root = BigInteger.Zero;
            return false;
        }

        root = Sqrt(n);
        return root * root == n;
    }

    /// <summary>Returns the floor of the positive <paramref name="degree"/>th root of a non-negative integer.</summary>
    public static BigInteger NthRoot(BigInteger n, int degree)
    {
        if (n < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(n), "Integer root is undefined for negative values.");
        }

        if (degree <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(degree), "Root degree must be positive.");
        }

        if (degree == 1 || n <= 1)
        {
            return n;
        }

        var bitLength = checked((int)n.GetBitLength());
        var lower = BigInteger.One << ((bitLength - 1) / degree);
        var upper = BigInteger.One << ((bitLength + degree - 1) / degree);
        while (lower + 1 < upper)
        {
            var candidate = (lower + upper) >> 1;
            if (BigInteger.Pow(candidate, degree) <= n)
            {
                lower = candidate;
            }
            else
            {
                upper = candidate;
            }
        }

        return lower;
    }

    /// <summary>Returns the modular inverse of <paramref name="a"/> modulo <paramref name="m"/>.</summary>
    /// <exception cref="ArithmeticException"><paramref name="a"/> and <paramref name="m"/> are not coprime.</exception>
    public static BigInteger ModInverse(BigInteger a, BigInteger m)
    {
        if (m <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(m), "Modulus must be positive.");
        }

        var (g, x, _) = ExtendedGcd(Mod(a, m), m);
        if (g != BigInteger.One)
        {
            throw new ArithmeticException($"{a} has no inverse modulo {m}.");
        }

        return Mod(x, m);
    }

    /// <summary>Returns the modular inverse of <paramref name="a"/> modulo <paramref name="m"/> for native integer inputs.</summary>
    /// <exception cref="ArithmeticException"><paramref name="a"/> and <paramref name="m"/> are not coprime.</exception>
    public static long ModInverse(long a, long m)
    {
        if (m <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(m), "Modulus must be positive.");
        }

        // For m < 2^62 every intermediate of the recurrence fits in a long: the remainders
        // satisfy |q*r| <= oldR <= m, and the Bezout coefficients satisfy |t| <= m with
        // |q*t| = |oldT - nextT| <= 2m < 2^63. Larger moduli fall back to Int128.
        if (m < 1L << 62)
        {
            var oldR = m;
            var r = Mod(a, m);
            var oldT = 0L;
            var t = 1L;

            while (r != 0)
            {
                var q = oldR / r;
                (oldR, r) = (r, oldR - q * r);
                (oldT, t) = (t, oldT - q * t);
            }

            if (oldR != 1)
            {
                throw new ArithmeticException($"{a} has no inverse modulo {m}.");
            }

            return oldT < 0 ? oldT + m : oldT;
        }
        else
        {
            var oldR = (Int128)m;
            var r = (Int128)Mod(a, m);
            var oldT = Int128.Zero;
            var t = Int128.One;

            while (r != 0)
            {
                var q = oldR / r;
                (oldR, r) = (r, oldR - q * r);
                (oldT, t) = (t, oldT - q * t);
            }

            if (oldR != 1)
            {
                throw new ArithmeticException($"{a} has no inverse modulo {m}.");
            }

            var result = oldT % m;
            if (result < 0)
            {
                result += m;
            }

            return (long)result;
        }
    }

    /// <summary>Non-negative remainder of <paramref name="a"/> modulo <paramref name="m"/>.</summary>
    public static BigInteger Mod(BigInteger a, BigInteger m)
    {
        var r = a % m;
        return r < 0 ? r + m : r;
    }

    /// <summary>Non-negative remainder of <paramref name="a"/> modulo <paramref name="m"/>.</summary>
    public static long Mod(long a, long m)
    {
        var r = a % m;
        return r < 0 ? r + m : r;
    }

    private static (BigInteger Gcd, BigInteger X, BigInteger Y) ExtendedGcd(BigInteger a, BigInteger b)
    {
        if (b.IsZero)
        {
            return (a, BigInteger.One, BigInteger.Zero);
        }

        var (g, x, y) = ExtendedGcd(b, a % b);
        return (g, y, x - (a / b) * y);
    }
}
