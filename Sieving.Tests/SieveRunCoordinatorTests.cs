using System.Numerics;

namespace Sieving.Tests;

public sealed class SieveRunCoordinatorTests
{
    [Fact]
    public void Byte_rescale_uses_scaled_target()
    {
        var targetN = BigInteger.Parse("38127244994561062829768358347629172784112837577215296840540570131739631158898750392365398772087439127886073223");
        const long multiplier = 47;
        const long halfInterval = 8_388_608;
        const double logScale = 14.0;
        var factorBase = new FactorBaseData
        {
            Count = 0,
            Primes = [],
            Columns = [],
            Root1 = [],
            Root2 = [],
            LogP = [],
            PrimeInverses = [],
            PrimeDivThresholds = [],
            TargetN = targetN,
            Multiplier = multiplier,
            ScaledN = multiplier * targetN,
            Bound = 1,
            LogScale = logScale,
        };

        var actual = SieveRunCoordinator.ComputeByteRescale(factorBase, halfInterval);
        var qMaxEstimate = (double)halfInterval * Math.Sqrt(2.0 * (double)factorBase.ScaledN) / 2.0;
        var expected = 200.0 / (logScale * Math.Log(qMaxEstimate));

        Assert.Equal(expected, actual, precision: 12);
    }
}
