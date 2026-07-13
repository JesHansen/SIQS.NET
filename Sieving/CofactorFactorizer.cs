using System.Numerics;

namespace Sieving;

/// <summary>Small, deterministic cofactor splitters used after factor-base division.</summary>
internal static class CofactorFactorizer
{
    private static readonly ulong[] SqufofMultipliers = [1, 3, 5, 7, 11, 15, 21, 33, 35, 55, 77, 105, 165, 231, 385, 1155];

    public static ulong PollardRho64(ulong value)
    {
        if ((value & 1) == 0) return 2;

        var montgomery = Montgomery64.Create(value);
        for (var constant = 1UL; constant <= 32; constant++)
        {
            var x = montgomery.ToMontgomery(2);
            var y = x;
            var constantMontgomery = montgomery.ToMontgomery(constant);
            var divisor = 1UL;
            while (divisor == 1)
            {
                x = montgomery.Add(montgomery.Square(x), constantMontgomery);
                y = montgomery.Add(montgomery.Square(y), constantMontgomery);
                y = montgomery.Add(montgomery.Square(y), constantMontgomery);
                divisor = Gcd(x > y ? x - y : y - x, value);
            }

            if (divisor != value) return divisor;
        }

        return 1;
    }

    public static ulong Squfof64(ulong value)
    {
        if (value < 2) return 1;
        if ((value & 1) == 0) return 2;

        var root = SqrtFloor(value);
        if (root * root == value) return root;

        foreach (var multiplier in SqufofMultipliers)
        {
            if (value > ulong.MaxValue / multiplier) continue;

            var discriminant = value * multiplier;
            var rootDiscriminant = SqrtFloor(discriminant);
            var p = rootDiscriminant;
            var previousP = rootDiscriminant;
            var previousQ = 1UL;
            var q = discriminant - rootDiscriminant * rootDiscriminant;
            if (q == 0)
            {
                var factor = Gcd(value, rootDiscriminant);
                if (factor > 1 && factor < value) return factor;
                continue;
            }

            var limit = 3UL * Math.Max(1UL, 2UL * SqrtFloor(2UL * root));
            ulong squareRoot = 0;
            ulong iteration;
            for (iteration = 2; iteration < limit; iteration++)
            {
                var quotient = (rootDiscriminant + p) / q;
                var nextP = (ulong)((UInt128)quotient * q - p);
                var oldQ = q;
                var nextQ = (ulong)(previousQ + (UInt128)quotient * (previousP - nextP));
                squareRoot = SqrtFloor(nextQ);
                if ((iteration & 1) == 0 && squareRoot * squareRoot == nextQ)
                {
                    p = nextP;
                    q = nextQ;
                    break;
                }

                previousQ = oldQ;
                q = nextQ;
                previousP = p = nextP;
            }

            if (iteration >= limit || squareRoot == 0) continue;

            var quotient2 = (rootDiscriminant - p) / squareRoot;
            previousP = p = quotient2 * squareRoot + p;
            previousQ = squareRoot;
            q = (ulong)(((UInt128)discriminant - (UInt128)previousP * previousP) / previousQ);

            for (ulong recovery = 0; recovery < limit; recovery++)
            {
                if (q == 0) break;

                var quotient = (rootDiscriminant + p) / q;
                previousP = p;
                p = (ulong)((UInt128)quotient * q - p);
                var oldQ = q;
                q = (ulong)(previousQ + (UInt128)quotient * (previousP - p));
                previousQ = oldQ;
                if (p != previousP) continue;

                var factor = Gcd(value, previousQ);
                if (factor > 1 && factor < value) return factor;
                break;
            }
        }

        return 1;
    }

    public static BigInteger PollardRho(BigInteger value)
    {
        if (value % 2 == 0) return 2;

        for (var constant = 1; constant <= 32; constant++)
        {
            var x = new BigInteger(2);
            var y = new BigInteger(2);
            var divisor = BigInteger.One;
            while (divisor.IsOne)
            {
                x = (x * x + constant) % value;
                y = (y * y + constant) % value;
                y = (y * y + constant) % value;
                divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(x - y), value);
            }

            if (divisor != value) return divisor;
        }

        return BigInteger.One;
    }

    private static ulong SqrtFloor(ulong value)
    {
        var root = (ulong)Math.Sqrt(value);
        while ((UInt128)(root + 1) * (root + 1) <= value) root++;
        while ((UInt128)root * root > value) root--;
        return root;
    }

    private static ulong Gcd(ulong left, ulong right)
    {
        while (right != 0) { var remainder = left % right; left = right; right = remainder; }
        return left;
    }
}
