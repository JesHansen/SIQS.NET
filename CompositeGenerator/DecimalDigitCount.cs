using System.Globalization;
using System.Numerics;

namespace CompositeGenerator;

/// <summary>A validated decimal digit count used to derive exact product bounds.</summary>
internal readonly record struct DecimalDigitCount(int Value)
{
    public static DecimalDigitCount Create(int value)
        => value > 0
            ? new DecimalDigitCount(value)
            : throw new ArgumentOutOfRangeException(nameof(value), "Digit count must be positive.");

    public bool Matches(BigInteger value)
        => value.ToString(CultureInfo.InvariantCulture).Length == Value;
}

internal static class DecimalDigitCountExtensions
{
    public static BigIntegerRange ProductRange(this DecimalDigitCount digitCount)
        => new(
            BigInteger.Pow(10, digitCount.Value - 1),
            BigInteger.Pow(10, digitCount.Value) - BigInteger.One);
}
