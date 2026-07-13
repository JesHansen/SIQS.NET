using System.Numerics;

namespace CompositeGenerator;

/// <summary>Probabilistic primality testing with small-prime trial division.</summary>
internal sealed class MillerRabinPrimalityTest(int rounds)
{
    private static readonly IReadOnlyList<int> TrialDivisors =
        new[] { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71, 73, 79, 83, 89, 97 };

    private readonly SecureBigIntegerSampler _random = new();

    public bool IsProbablePrime(BigInteger value)
    {
        if (value < 2)
        {
            return false;
        }

        foreach (var divisor in TrialDivisors)
        {
            if (value == divisor)
            {
                return true;
            }

            if (value % divisor == 0)
            {
                return false;
            }
        }

        var (oddPart, twos) = FactorOutTwos(value - BigInteger.One);
        for (var i = 0; i < rounds; i++)
        {
            var witness = 2 + _random.NextBelow(value - 3);
            if (!PassesWitness(value, witness, oddPart, twos))
            {
                return false;
            }
        }

        return true;
    }

    private static (BigInteger OddPart, int Twos) FactorOutTwos(BigInteger value)
    {
        var twos = 0;
        while (value.IsEven)
        {
            value >>= 1;
            twos++;
        }

        return (value, twos);
    }

    private static bool PassesWitness(BigInteger value, BigInteger witness, BigInteger oddPart, int twos)
    {
        var power = BigInteger.ModPow(witness, oddPart, value);
        if (power == BigInteger.One || power == value - BigInteger.One)
        {
            return true;
        }

        for (var round = 1; round < twos; round++)
        {
            power = BigInteger.ModPow(power, 2, value);
            if (power == value - BigInteger.One)
            {
                return true;
            }
        }

        return false;
    }
}
