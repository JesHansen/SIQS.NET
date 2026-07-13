using System.Numerics;

namespace CompositeGenerator;

/// <summary>Generates semiprimes with an exact decimal digit count.</summary>
public static class SemiprimeGenerator
{
    private const int MillerRabinRounds = 64;
    private static readonly MillerRabinPrimalityTest PrimalityTest = new(MillerRabinRounds);
    private static readonly SecureBigIntegerSampler Random = new();

    public static BigInteger Generate(int digits)
    {
        var digitCount = DecimalDigitCount.Create(digits);
        if (digitCount.Value == 1)
        {
            return Random.NextInt32(0, 2) == 0 ? 4 : 6;
        }

        var productRange = digitCount.ProductRange();
        var factorRange = productRange.SqrtRange();
        while (true)
        {
            var p = RandomPrimeIn(factorRange);
            var q = RandomPrimeIn(factorRange);
            var product = p * q;
            if (productRange.Contains(product) && digitCount.Matches(product))
            {
                return product;
            }
        }
    }

    private static BigInteger RandomPrimeIn(BigIntegerRange range)
    {
        while (true)
        {
            var candidate = Random.Next(range);
            if (PrimalityTest.IsProbablePrime(candidate))
            {
                return candidate;
            }
        }
    }
}
