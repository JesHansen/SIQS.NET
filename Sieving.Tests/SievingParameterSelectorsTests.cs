namespace Sieving.Tests;

/// <summary>
/// Boundary coverage for the default tuning selectors extracted from <c>SievingParameters.Default</c>.
/// Each theory pins the value at a band edge and at the digit immediately below/above it.
/// </summary>
public class SievingParameterSelectorsTests
{
    private const long Bound = 1000;

    [Theory]
    [InlineData(12, 128)]         // SIQS is bypassed here, but the selector remains well-defined.
    [InlineData(13, 128)]
    [InlineData(14, 128)]
    [InlineData(15, 256)]
    [InlineData(20, 1_024)]
    [InlineData(25, 8_192)]
    [InlineData(30, 32_768)]
    [InlineData(35, 262_144)]
    [InlineData(38, 524_288)]
    [InlineData(39, 1_048_576)]
    [InlineData(40, 1_048_576)]
    [InlineData(64, 1_048_576)]
    [InlineData(70, 1_048_576)]
    [InlineData(81, 1_048_576)]
    [InlineData(82, 1_048_576)]
    [InlineData(88, 1_048_576)]
    [InlineData(89, 1_048_576)]
    [InlineData(99, 1_048_576)]
    [InlineData(100, 1_048_576)]
    [InlineData(101, 2_097_152)]
    [InlineData(103, 2_097_152)]
    [InlineData(104, 4_194_304)]
    [InlineData(105, 4_194_304)]
    [InlineData(106, 8_388_608)]
    [InlineData(110, 8_388_608)]
    [InlineData(111, 16_777_216)]
    [InlineData(112, 16_777_216)]
    [InlineData(113, 33_554_432)]
    [InlineData(115, 33_554_432)]
    public void SelectSieveHalfInterval_bands(int digits, long expected)
        => Assert.Equal(expected, SievingParameters.SelectSieveHalfInterval(digits));

    [Theory]
    [InlineData(13, 262_144)]
    [InlineData(64, 262_144)]
    [InlineData(65, 262_144)]
    [InlineData(69, 262_144)]
    [InlineData(70, 524_288)]
    [InlineData(90, 524_288)]
    [InlineData(91, 524_288)]
    [InlineData(99, 524_288)]
    [InlineData(100, 524_288)]
    [InlineData(115, 524_288)]
    public void SelectSieveBlockSize_bands(int digits, int expected)
        => Assert.Equal(expected, SievingParameters.SelectSieveBlockSize(digits));

    [Theory]
    [InlineData(84, 0, 0)]
    [InlineData(85, 1_048_576, 262_144)]
    [InlineData(90, 1_048_576, 262_144)]
    public void SelectBucketAndResieveCutoffs_bands(int digits, int expectedBucket, int expectedResieve)
    {
        Assert.Equal(expectedBucket, SievingParameters.SelectBucketLargePrimeCutoff(digits));
        Assert.Equal(expectedResieve, SievingParameters.SelectResieveLargePrimeCutoff(digits));
    }

    [Theory]
    [InlineData(13, 1)]
    [InlineData(19, 1)]
    [InlineData(20, 10)]
    [InlineData(99, 10)]
    [InlineData(100, 30)]
    [InlineData(104, 30)]
    [InlineData(105, 64)]
    public void SelectPolynomialSupplyMultiplier_bands(int digits, long expected)
        => Assert.Equal(expected, SievingParameters.SelectPolynomialSupplyMultiplier(digits));

    [Theory]
    [InlineData(58, 8_000)]
    [InlineData(59, 16_000)]
    [InlineData(60, 32_000)]
    [InlineData(63, 32_000)]
    [InlineData(64, 64_000)]
    [InlineData(69, 64_000)]
    [InlineData(70, 128_000)]
    [InlineData(74, 128_000)]
    [InlineData(75, 192_000)]
    [InlineData(81, 192_000)]
    [InlineData(82, 192_000)]
    [InlineData(88, 192_000)]
    [InlineData(89, 192_000)]
    [InlineData(99, 192_000)]
    [InlineData(100, 1_000_000_000)]
    [InlineData(104, 1_000_000_000)]
    [InlineData(110, 1_000_000_000)]
    [InlineData(115, 1_000_000_000)]
    public void SelectLargePrimeBound_bands(int digits, long expected)
        => Assert.Equal(expected, SievingParameters.SelectLargePrimeBound(digits, Bound));

    [Theory]
    [InlineData(13, 0)]
    [InlineData(64, 0)]
    [InlineData(65, 8)]
    [InlineData(69, 8)]
    [InlineData(70, 16)]
    [InlineData(74, 16)]
    [InlineData(75, 24)]
    [InlineData(81, 24)]
    [InlineData(82, 24)]
    [InlineData(88, 24)]
    [InlineData(89, 36)]
    [InlineData(99, 36)]
    [InlineData(100, 36)]
    [InlineData(103, 36)]
    [InlineData(104, 48)]
    [InlineData(115, 48)]
    public void SelectErrorMargin_bands(int digits, int expected)
        => Assert.Equal(expected, SievingParameters.SelectErrorMargin(digits));

    [Theory]
    [InlineData(1_000L, 1_500L)]
    [InlineData(10_000_001L, 15_000_002L)]
    [InlineData(60_000_000L, 90_000_000L)]
    public void Two_large_prime_bound_is_one_and_a_half_times_factor_base_bound(
        long factorBaseBound,
        long expected)
    {
        var largePrime2Bound = SievingParameters.SelectLargePrime2Bound(factorBaseBound);

        Assert.Equal(expected, largePrime2Bound);
        Assert.Equal(largePrime2Bound, SievingParameters.SelectLargePrime2ThresholdBound(largePrime2Bound));
    }

    [Fact]
    public void Measured_c13_to_c115_numeric_profile_is_non_decreasing()
    {
        var profiles = Enumerable.Range(13, 103)
            .Select(digits =>
            {
                var largePrimeBound = SievingParameters.SelectLargePrimeBound(digits, Bound);
                var largePrime2Bound = SievingParameters.SelectLargePrime2Bound(Bound);
                return new
                {
                    HalfInterval = SievingParameters.SelectSieveHalfInterval(digits),
                    APrimeCount = SievingParameters.SelectAPrimeCount(digits),
                    BlockSize = SievingParameters.SelectSieveBlockSize(digits),
                    LargePrimeBound = largePrimeBound,
                    ErrorMargin = SievingParameters.SelectErrorMargin(digits),
                    LargePrime2Bound = largePrime2Bound,
                    LargePrime2Threshold = SievingParameters.SelectLargePrime2ThresholdBound(largePrime2Bound),
                    BucketCutoff = SievingParameters.SelectBucketLargePrimeCutoff(digits),
                    ResieveCutoff = SievingParameters.SelectResieveLargePrimeCutoff(digits),
                    PolynomialSupplyMultiplier = SievingParameters.SelectPolynomialSupplyMultiplier(digits),
                    TwoLargePrimes = digits >= SievingParameters.TwoLargePrimeDefaultMinDigits ? 1 : 0,
                };
            })
            .ToArray();

        Assert.All(profiles.Zip(profiles.Skip(1)), pair =>
        {
            Assert.True(pair.First.HalfInterval <= pair.Second.HalfInterval);
            Assert.True(pair.First.APrimeCount <= pair.Second.APrimeCount);
            Assert.True(pair.First.BlockSize <= pair.Second.BlockSize);
            Assert.True(pair.First.LargePrimeBound <= pair.Second.LargePrimeBound);
            Assert.True(pair.First.ErrorMargin <= pair.Second.ErrorMargin);
            Assert.True(pair.First.LargePrime2Bound <= pair.Second.LargePrime2Bound);
            Assert.True(pair.First.LargePrime2Threshold <= pair.Second.LargePrime2Threshold);
            Assert.True(pair.First.BucketCutoff <= pair.Second.BucketCutoff);
            Assert.True(pair.First.ResieveCutoff <= pair.Second.ResieveCutoff);
            Assert.True(pair.First.PolynomialSupplyMultiplier <= pair.Second.PolynomialSupplyMultiplier);
            Assert.True(pair.First.TwoLargePrimes <= pair.Second.TwoLargePrimes);
        });
    }
}
