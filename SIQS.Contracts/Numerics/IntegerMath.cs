using System.Numerics;

namespace SIQS.Contracts.Numerics;

/// <summary>
/// General-purpose arbitrary-precision integer helpers shared by the phase libraries: floor
/// square root, perfect-square detection, and modular inverse. These are plain number-theory
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
