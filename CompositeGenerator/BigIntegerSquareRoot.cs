using System.Numerics;

namespace CompositeGenerator;

/// <summary>Exact integer square-root operations.</summary>
internal static class BigIntegerSquareRoot
{
    public static BigInteger Floor(BigInteger value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        if (value < 2)
        {
            return value;
        }

        var estimate = BigInteger.One << (int)((value.GetBitLength() + 1) / 2);
        while (true)
        {
            var next = (estimate + value / estimate) >> 1;
            if (next >= estimate)
            {
                return estimate;
            }

            estimate = next;
        }
    }

    public static BigInteger Ceiling(BigInteger value)
    {
        var floor = Floor(value);
        return floor * floor == value ? floor : floor + BigInteger.One;
    }
}
