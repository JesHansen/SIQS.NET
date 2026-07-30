using System.Numerics;
using SIQS.Contracts;
using SIQS.Contracts.Files;

namespace Sieving.Tests;

public class SievingParametersTests
{
    [Theory]
    [InlineData(19, 2)]
    [InlineData(20, 3)]
    [InlineData(47, 3)]
    [InlineData(48, 5)]
    [InlineData(61, 5)]
    [InlineData(62, 6)]
    [InlineData(63, 7)]
    [InlineData(64, 7)]
    [InlineData(65, 7)]
    [InlineData(77, 7)]
    [InlineData(78, 8)]
    [InlineData(81, 8)]
    [InlineData(82, 8)]
    [InlineData(87, 8)]
    [InlineData(88, 9)]
    [InlineData(103, 9)]
    [InlineData(104, 10)]
    public void Default_a_prime_count_uses_digit_thresholds(int digits, int expectedAPrimeCount)
    {
        var parameters = SievingParameters.Default(FactorBaseWithDigitCount(digits));

        Assert.Equal(expectedAPrimeCount, parameters.APrimeCount);
        Assert.True(parameters.APrimeWindowSize >= 16);
    }

    [Theory]
    [InlineData(13, 128)]
    [InlineData(15, 256)]
    [InlineData(20, 1_024)]
    [InlineData(25, 8_192)]
    [InlineData(30, 32_768)]
    [InlineData(35, 262_144)]
    [InlineData(40, 1_048_576)]
    [InlineData(64, 1_048_576)]
    [InlineData(65, 1_048_576)]
    [InlineData(70, 1_048_576)]
    [InlineData(77, 1_048_576)]
    [InlineData(78, 1_048_576)]
    [InlineData(81, 1_048_576)]
    [InlineData(82, 1_048_576)]
    [InlineData(88, 1_048_576)]
    [InlineData(89, 1_048_576)]
    [InlineData(100, 1_048_576)]
    [InlineData(101, 2_097_152)]
    [InlineData(104, 4_194_304)]
    [InlineData(106, 8_388_608)]
    [InlineData(111, 16_777_216)]
    [InlineData(113, 33_554_432)]
    public void Default_sieve_half_interval_uses_tuned_curve(int digits, long expectedSieveHalfInterval)
    {
        var parameters = SievingParameters.Default(FactorBaseWithDigitCount(digits));

        Assert.Equal(expectedSieveHalfInterval, parameters.SieveHalfInterval);
    }

    [Fact]
    public void C13_uses_smallest_practical_siqs_defaults()
    {
        var parameters = SievingParameters.Default(FactorBaseWithDigitCount(digits: 13, entryCount: 90, bound: 1_000));

        Assert.Equal(128, parameters.SieveHalfInterval);
        Assert.Equal(8_000, parameters.LargePrimeBound);
        Assert.Equal(0, parameters.ErrorMargin);
        Assert.Equal(262_144, parameters.SieveBlockSize);
        Assert.Equal(2, parameters.APrimeCount);
        Assert.Equal(32, parameters.APrimeWindowSize);
        Assert.Equal(992, parameters.PolynomialCount);
    }

    [Fact]
    public void C64_joins_the_existing_c65_plateau_monotonically()
    {
        var parameters = SievingParameters.Default(FactorBaseWithDigitCount(digits: 64, entryCount: 9_316, bound: 207_073));

        Assert.Equal(1_048_576, parameters.SieveHalfInterval);
        Assert.Equal(64L * 207_073, parameters.LargePrimeBound);
        Assert.Equal(0, parameters.ErrorMargin);
        Assert.Equal(262_144, parameters.SieveBlockSize);
        Assert.Equal(7, parameters.APrimeCount);
        Assert.Equal(112, parameters.APrimeWindowSize);
        Assert.Equal(9_828, parameters.RelationTarget);
    }

    [Fact]
    public void C78_to_c81_uses_c80_tuned_sieving_defaults()
    {
        var parameters = SievingParameters.Default(FactorBaseWithDigitCount(digits: 80, entryCount: 43_313, bound: 1_112_183));

        Assert.Equal(1_048_576, parameters.SieveHalfInterval);
        Assert.Equal(192L * 1_112_183, parameters.LargePrimeBound);
        Assert.Equal(24, parameters.ErrorMargin);
        Assert.Equal(524_288, parameters.SieveBlockSize);
        Assert.Equal(8, parameters.APrimeCount);
        Assert.Equal(45_361, parameters.RelationTarget);
    }

    [Fact]
    public void C70_to_c77_uses_tuned_sieving_defaults()
    {
        var parameters = SievingParameters.Default(FactorBaseWithDigitCount(digits: 76, entryCount: 26_002, bound: 638_558));

        Assert.Equal(1_048_576, parameters.SieveHalfInterval);
        Assert.Equal(192L * 638_558, parameters.LargePrimeBound);
        Assert.Equal(24, parameters.ErrorMargin);
        Assert.Equal(524_288, parameters.SieveBlockSize);
        Assert.Equal(7, parameters.APrimeCount);
        Assert.Equal(28_050, parameters.RelationTarget);
    }

    [Fact]
    public void C89_plus_uses_c90_tuned_sieving_defaults()
    {
        var parameters = SievingParameters.Default(FactorBaseWithDigitCount(digits: 90, entryCount: 94_923, bound: 2_596_002));

        Assert.Equal(1_048_576, parameters.SieveHalfInterval);
        Assert.Equal(192L * 2_596_002, parameters.LargePrimeBound);
        Assert.Equal(36, parameters.ErrorMargin);
        Assert.Equal(524_288, parameters.SieveBlockSize);
        Assert.Equal(1_048_576, parameters.BucketLargePrimeCutoff);
        Assert.Equal(262_144, parameters.ResieveLargePrimeCutoff);
        Assert.Equal(9, parameters.APrimeCount);
        Assert.Equal(144, parameters.APrimeWindowSize);
        Assert.Equal(96_971, parameters.RelationTarget);
        Assert.False(parameters.EnableTwoLargePrimes);
        Assert.Equal(3_894_003, parameters.LargePrime2Bound);
        Assert.Equal(3_894_003, parameters.LargePrime2ThresholdBound);
        Assert.Equal(CofactorSplitterKind.Squfof, parameters.CofactorSplitter);
    }

    [Fact]
    public void C91_to_c99_keeps_experimental_two_large_prime_parameters_disabled_by_default()
    {
        var parameters = SievingParameters.Default(FactorBaseWithDigitCount(digits: 95, entryCount: 137_750, bound: 3_882_876));

        Assert.False(parameters.EnableTwoLargePrimes);
        Assert.Equal(5_824_314, parameters.LargePrime2Bound);
        Assert.Equal(parameters.LargePrime2Bound, parameters.LargePrime2ThresholdBound);
        Assert.Equal(524_288, parameters.SieveBlockSize);
        Assert.Equal(CofactorSplitterKind.Squfof, parameters.CofactorSplitter);
    }

    [Fact]
    public void Two_large_prime_defaults_activate_at_measured_c110_crossover()
    {
        var c82 = SievingParameters.Default(FactorBaseWithDigitCount(digits: 82, bound: 1_250_000));
        Assert.False(c82.EnableTwoLargePrimes);
        Assert.Equal(1_875_000, c82.LargePrime2Bound);
        Assert.Equal(c82.LargePrime2Bound, c82.LargePrime2ThresholdBound);

        var c83 = SievingParameters.Default(FactorBaseWithDigitCount(digits: 83, bound: 1_250_000));

        Assert.False(c83.EnableTwoLargePrimes);
        Assert.Equal(1_875_000, c83.LargePrime2Bound);
        Assert.Equal(c83.LargePrime2Bound, c83.LargePrime2ThresholdBound);

        var c84 = SievingParameters.Default(FactorBaseWithDigitCount(digits: 84, bound: 1_250_000));

        Assert.False(c84.EnableTwoLargePrimes);
        Assert.Equal(1_875_000, c84.LargePrime2Bound);
        Assert.Equal(c84.LargePrime2Bound, c84.LargePrime2ThresholdBound);

        var c85 = SievingParameters.Default(FactorBaseWithDigitCount(digits: 85, bound: 1_250_000));

        Assert.False(c85.EnableTwoLargePrimes);
        Assert.Equal(1_875_000, c85.LargePrime2Bound);
        Assert.Equal(c85.LargePrime2Bound, c85.LargePrime2ThresholdBound);

        var c99 = SievingParameters.Default(FactorBaseWithDigitCount(digits: 99, bound: 5_000_000));

        Assert.False(c99.EnableTwoLargePrimes);
        Assert.Equal(7_500_000, c99.LargePrime2Bound);
        Assert.Equal(c99.LargePrime2Bound, c99.LargePrime2ThresholdBound);

        foreach (var digits in new[] { 100, 105, 109 })
        {
            var parameters = SievingParameters.Default(FactorBaseWithDigitCount(digits, bound: 60_000_000));
            Assert.False(parameters.EnableTwoLargePrimes);
            Assert.Equal(90_000_000, parameters.LargePrime2Bound);
            Assert.Equal(parameters.LargePrime2Bound, parameters.LargePrime2ThresholdBound);
        }

        foreach (var digits in new[] { 110, 115, 116 })
        {
            var parameters = SievingParameters.Default(FactorBaseWithDigitCount(digits, bound: 60_000_000));
            Assert.True(parameters.EnableTwoLargePrimes);
            Assert.Equal(90_000_000, parameters.LargePrime2Bound);
            Assert.Equal(parameters.LargePrime2Bound, parameters.LargePrime2ThresholdBound);
        }
    }

    [Fact]
    public void Explicit_two_large_prime_enable_below_default_crossover_has_valid_lp2_parameters()
    {
        var defaults = SievingParameters.Default(FactorBaseWithDigitCount(digits: 90, entryCount: 94_923, bound: 2_596_002));
        var enabled = defaults with { EnableTwoLargePrimes = true };

        Assert.True(enabled.EnableTwoLargePrimes);
        Assert.True(enabled.LargePrime2Bound > 0);
        Assert.Equal(3_894_003, enabled.LargePrime2Bound);
        Assert.Equal(enabled.LargePrime2Bound, enabled.LargePrime2ThresholdBound);
    }

    [Fact]
    public void Bucket_and_resieve_defaults_switch_on_at_c85()
    {
        var c84 = SievingParameters.Default(FactorBaseWithDigitCount(digits: 84, bound: 4_000_000));
        Assert.Equal(0, c84.BucketLargePrimeCutoff);
        Assert.Equal(0, c84.ResieveLargePrimeCutoff);

        foreach (var digits in new[] { 85, 89, 92, 97, 98, 105 })
        {
            var parameters = SievingParameters.Default(FactorBaseWithDigitCount(digits, bound: 4_000_000));
            Assert.Equal(1_048_576, parameters.BucketLargePrimeCutoff);
            Assert.Equal(262_144, parameters.ResieveLargePrimeCutoff);
        }
    }

    [Fact]
    public void C100_uses_end_of_c95_plateau()
    {
        var parameters = SievingParameters.Default(FactorBaseWithDigitCount(digits: 100, entryCount: 174_605, bound: 5_000_000));

        Assert.Equal(1_048_576, parameters.SieveHalfInterval);
        Assert.Equal(1_000_000_000, parameters.LargePrimeBound);
        Assert.Equal(7_500_000, parameters.LargePrime2Bound);
        Assert.Equal(7_500_000, parameters.LargePrime2ThresholdBound);
        Assert.Equal(CofactorSplitterKind.Squfof, parameters.CofactorSplitter);
        Assert.Equal(36, parameters.ErrorMargin);
        Assert.Equal(524_288, parameters.SieveBlockSize);
        Assert.Equal(1_048_576, parameters.BucketLargePrimeCutoff);
        Assert.Equal(1_048_576, parameters.EffectiveBucketLargePrimeCutoff);
        Assert.Equal(262_144, parameters.ResieveLargePrimeCutoff);
        Assert.False(parameters.EnableTwoLargePrimes);
        Assert.Equal(262_144, parameters.EffectiveResieveLargePrimeCutoff);
        Assert.Equal(9, parameters.APrimeCount);
        Assert.Equal(144, parameters.APrimeWindowSize);
        Assert.Equal(184_605, parameters.RelationTarget);
        Assert.True(parameters.PolynomialCount >= 30L * parameters.RelationTarget);
    }

    [Fact]
    public void C105_uses_first_deeper_sieving_tier()
    {
        var parameters = SievingParameters.Default(FactorBaseWithDigitCount(digits: 105, entryCount: 929_231, bound: 30_000_000));

        Assert.Equal(4_194_304, parameters.SieveHalfInterval);
        Assert.Equal(1_000_000_000, parameters.LargePrimeBound);
        Assert.Equal(45_000_000, parameters.LargePrime2Bound);
        Assert.Equal(45_000_000, parameters.LargePrime2ThresholdBound);
        Assert.Equal(CofactorSplitterKind.Squfof, parameters.CofactorSplitter);
        Assert.Equal(48, parameters.ErrorMargin);
        Assert.Equal(524_288, parameters.SieveBlockSize);
        Assert.Equal(1_048_576, parameters.BucketLargePrimeCutoff);
        Assert.Equal(262_144, parameters.ResieveLargePrimeCutoff);
        Assert.False(parameters.EnableTwoLargePrimes);
        Assert.Equal(10, parameters.APrimeCount);
        Assert.Equal(160, parameters.APrimeWindowSize);
        Assert.Equal(966_401, parameters.RelationTarget);
        Assert.True(parameters.PolynomialCount >= 64L * parameters.RelationTarget);
    }

    [Fact]
    public void C110_uses_measured_monotonic_sieving_defaults()
    {
        var parameters = SievingParameters.Default(FactorBaseWithDigitCount(digits: 110, entryCount: 1_782_307, bound: 60_000_000));

        Assert.Equal(8_388_608, parameters.SieveHalfInterval);
        Assert.Equal(1_000_000_000, parameters.LargePrimeBound);
        Assert.Equal(90_000_000, parameters.LargePrime2Bound);
        Assert.Equal(90_000_000, parameters.LargePrime2ThresholdBound);
        Assert.Equal(CofactorSplitterKind.Squfof, parameters.CofactorSplitter);
        Assert.Equal(48, parameters.ErrorMargin);
        Assert.Equal(524_288, parameters.SieveBlockSize);
        Assert.Equal(1_048_576, parameters.BucketLargePrimeCutoff);
        Assert.Equal(262_144, parameters.ResieveLargePrimeCutoff);
        Assert.True(parameters.EnableTwoLargePrimes);
        Assert.Equal(10, parameters.APrimeCount);
        Assert.Equal(160, parameters.APrimeWindowSize);
        Assert.Equal(1_853_600, parameters.RelationTarget);
        Assert.Equal(
            SievingParameters.AvailablePolynomialSupply(parameters.APrimeWindowSize, parameters.APrimeCount),
            parameters.PolynomialCount);
    }

    [Fact]
    public void C115_uses_end_of_measured_profile()
    {
        var parameters = SievingParameters.Default(FactorBaseWithDigitCount(
            digits: 115,
            entryCount: 1_781_520,
            bound: 60_000_000));

        Assert.Equal(33_554_432, parameters.SieveHalfInterval);
        Assert.Equal(1_000_000_000, parameters.LargePrimeBound);
        Assert.Equal(48, parameters.ErrorMargin);
        Assert.Equal(524_288, parameters.SieveBlockSize);
        Assert.Equal(1_048_576, parameters.BucketLargePrimeCutoff);
        Assert.Equal(262_144, parameters.ResieveLargePrimeCutoff);
        Assert.Equal(10, parameters.APrimeCount);
        Assert.Equal(160, parameters.APrimeWindowSize);
        Assert.True(parameters.EnableTwoLargePrimes);
        Assert.Equal(90_000_000, parameters.LargePrime2Bound);
        Assert.Equal(90_000_000, parameters.LargePrime2ThresholdBound);
        Assert.Equal(1_852_781, parameters.RelationTarget);
    }

    [Fact]
    public void Default_polynomial_count_uses_selected_a_window_supply()
    {
        var parameters = SievingParameters.Default(FactorBaseWithDigitCount(digits: 90, entryCount: 94_923, bound: 2_596_002));

        Assert.Equal(
            SievingParameters.AvailablePolynomialSupply(parameters.APrimeWindowSize, parameters.APrimeCount),
            parameters.PolynomialCount);
        Assert.True(parameters.PolynomialCount > 1_000_000);
    }

    [Fact]
    public void Default_a_prime_window_size_scales_to_available_polynomial_supply()
    {
        var parameters = SievingParameters.Default(FactorBaseWithDigitCount(digits: 80, entryCount: 35_202));

        Assert.Equal(8, parameters.APrimeCount);
        Assert.Equal(37_250, parameters.RelationTarget);
        Assert.Equal(128, parameters.APrimeWindowSize);
        Assert.True(
            AvailablePolynomialSupply(parameters.APrimeWindowSize, parameters.APrimeCount) >=
            10L * parameters.RelationTarget);
        Assert.Equal(Math.Max(16, 16 * parameters.APrimeCount), parameters.APrimeWindowSize);
    }

    [Fact]
    public void Complete_default_profile_is_non_decreasing_per_digit_through_c115()
    {
        var profiles = Enumerable.Range(13, 103)
            .Select(digits => SievingParameters.Default(
                FactorBaseWithDigitCount(digits, entryCount: 1, bound: 1_000)))
            .ToArray();

        Assert.All(profiles.Zip(profiles.Skip(1)), pair =>
        {
            Assert.True(pair.First.SieveHalfInterval <= pair.Second.SieveHalfInterval);
            Assert.True(pair.First.PolynomialCount <= pair.Second.PolynomialCount);
            Assert.True(pair.First.RelationTarget <= pair.Second.RelationTarget);
            Assert.True(pair.First.LargePrimeBound <= pair.Second.LargePrimeBound);
            Assert.True(pair.First.ErrorMargin <= pair.Second.ErrorMargin);
            Assert.True(pair.First.OutputBatchSize <= pair.Second.OutputBatchSize);
            Assert.True(pair.First.APrimeCount <= pair.Second.APrimeCount);
            Assert.True(pair.First.APrimeWindowSize <= pair.Second.APrimeWindowSize);
            Assert.True(pair.First.Parallelism <= pair.Second.Parallelism);
            Assert.True(pair.First.SieveBlockSize <= pair.Second.SieveBlockSize);
            Assert.True(pair.First.BucketLargePrimeCutoff <= pair.Second.BucketLargePrimeCutoff);
            Assert.True(pair.First.ResieveLargePrimeCutoff <= pair.Second.ResieveLargePrimeCutoff);
            Assert.True((pair.First.EnableTwoLargePrimes ? 1 : 0) <= (pair.Second.EnableTwoLargePrimes ? 1 : 0));
            Assert.True(pair.First.LargePrime2Bound <= pair.Second.LargePrime2Bound);
            Assert.True(pair.First.LargePrime2ThresholdBound <= pair.Second.LargePrime2ThresholdBound);
            Assert.True((int)pair.First.CofactorSplitter <= (int)pair.Second.CofactorSplitter);
        });
    }

    private static FactorBaseDocument FactorBaseWithDigitCount(int digits, int entryCount = 0, long bound = 1_000)
    {
        var n = BigInteger.Pow(10, digits - 1);
        var metadata = new FactorBaseMetadata(
            TargetN: n,
            Multiplier: BigInteger.One,
            ScaledN: n,
            Bound: bound,
            LogScale: 1.0);
        var entries = Enumerable.Range(0, entryCount)
            .Select(i => new FactorBaseEntry(i, i + 2, 0, 0, 1))
            .ToArray();

        return new FactorBaseDocument(metadata, entries);
    }

    private static long AvailablePolynomialSupply(int windowSize, int aPrimeCount)
    {
        var combinations = 1L;
        for (var i = 1; i <= aPrimeCount; i++)
        {
            combinations *= windowSize - aPrimeCount + i;
            combinations /= i;
        }

        return combinations * (1L << (aPrimeCount - 1));
    }
}
