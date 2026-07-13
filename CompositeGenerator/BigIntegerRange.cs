using System.Numerics;

namespace CompositeGenerator;

/// <summary>An inclusive range of arbitrary-precision integers.</summary>
internal readonly record struct BigIntegerRange(BigInteger Minimum, BigInteger Maximum)
{
    public bool Contains(BigInteger value) => value >= Minimum && value <= Maximum;
}

internal static class BigIntegerRangeExtensions
{
    public static BigIntegerRange SqrtRange(this BigIntegerRange range)
        => new(BigIntegerSquareRoot.Ceiling(range.Minimum), BigIntegerSquareRoot.Floor(range.Maximum));
}
