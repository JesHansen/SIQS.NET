using System.Numerics;
using Factorbase;
using Sieving;

namespace Sieving.Tests;

public class SmallPrimeVariationTests
{
    [Fact]
    public void Recovered_credit_matches_direct_root_congruence_tests()
    {
        var document = FactorBaseGenerator.Generate(new FactorBaseOptions(
            BigInteger.Parse("1022117"), Bound: 1000, Multiplier: 1)).FactorBase!;
        var fb = FactorBaseData.From(document);
        var byteLogP = fb.LogP.Select(logP => (byte)Math.Max(1, logP / 10)).ToArray();
        var variation = SmallPrimeVariation.Build(fb, byteLogP, primeBound: 256);
        var root1 = fb.Root1.Select(root => (int)root).ToArray();
        var root2 = fb.Root2.Select(root => (int)root).ToArray();

        Assert.True(variation.Count > 0);
        Assert.True(variation.Allowance > 0);
        for (var sieveIndex = 0; sieveIndex < 10_000; sieveIndex++)
        {
            var expected = 0;
            for (var i = 0; i < variation.Count; i++)
            {
                var p = fb.Primes[i];
                if ((sieveIndex - root1[i]) % p == 0 || (sieveIndex - root2[i]) % p == 0)
                {
                    expected += byteLogP[i];
                }
            }

            Assert.Equal(expected, variation.RecoverCredit(fb, byteLogP, sieveIndex, root1, root2));
        }
    }

    [Theory]
    [InlineData(100, 16, 84)]
    [InlineData(20, 20, 0)]
    [InlineData(10, 20, 0)]
    public void Preliminary_threshold_subtracts_allowance_without_underflow(
        byte exactThreshold, byte allowance, byte expected)
    {
        var variation = new SmallPrimeVariation(Count: 1, allowance);

        Assert.Equal(expected, variation.PreliminaryThreshold(exactThreshold));
    }
}
