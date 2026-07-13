using System.Numerics;
using SIQS.Contracts;
using SIQS.Contracts.Files;

namespace Sieving.Tests;

public class SievingParametersTests
{
    [Theory]
    [InlineData(45, 3)]
    [InlineData(46, 4)]
    [InlineData(52, 4)]
    [InlineData(53, 5)]
    [InlineData(69, 5)]
    [InlineData(70, 6)]
    [InlineData(77, 6)]
    [InlineData(78, 7)]
    [InlineData(81, 7)]
    [InlineData(82, 7)]
    [InlineData(87, 7)]
    [InlineData(88, 9)]
    public void Default_a_prime_count_uses_digit_thresholds(int digits, int expectedAPrimeCount)
    {
        var parameters = SievingParameters.Default(FactorBaseWithDigitCount(digits));

        Assert.Equal(expectedAPrimeCount, parameters.APrimeCount);
        Assert.True(parameters.APrimeWindowSize >= 16);
    }

    [Theory]
    [InlineData(70, 393_216)]
    [InlineData(77, 393_216)]
    [InlineData(78, 524_288)]
    [InlineData(81, 524_288)]
    [InlineData(82, 393_216)]
    [InlineData(88, 393_216)]
    [InlineData(89, 1_048_576)]
    public void Default_sieve_half_interval_uses_large_target_threshold(int digits, long expectedSieveHalfInterval)
    {
        var parameters = SievingParameters.Default(FactorBaseWithDigitCount(digits));

        Assert.Equal(expectedSieveHalfInterval, parameters.SieveHalfInterval);
    }

    [Fact]
    public void C78_to_c81_uses_c80_tuned_sieving_defaults()
    {
        var parameters = SievingParameters.Default(FactorBaseWithDigitCount(digits: 80, entryCount: 43_313, bound: 1_112_183));

        Assert.Equal(524_288, parameters.SieveHalfInterval);
        Assert.Equal(128L * 1_112_183, parameters.LargePrimeBound);
        Assert.Equal(40, parameters.ErrorMargin);
        Assert.Equal(524_288, parameters.SieveBlockSize);
        Assert.Equal(7, parameters.APrimeCount);
        Assert.Equal(45_361, parameters.RelationTarget);
    }

    [Fact]
    public void C70_to_c77_uses_tuned_sieving_defaults()
    {
        var parameters = SievingParameters.Default(FactorBaseWithDigitCount(digits: 76, entryCount: 26_002, bound: 638_558));

        Assert.Equal(393_216, parameters.SieveHalfInterval);
        Assert.Equal(128L * 638_558, parameters.LargePrimeBound);
        Assert.Equal(40, parameters.ErrorMargin);
        Assert.Equal(524_288, parameters.SieveBlockSize);
        Assert.Equal(6, parameters.APrimeCount);
        Assert.Equal(28_050, parameters.RelationTarget);
    }

    [Fact]
    public void C89_plus_uses_c90_tuned_sieving_defaults()
    {
        var parameters = SievingParameters.Default(FactorBaseWithDigitCount(digits: 90, entryCount: 94_923, bound: 2_596_002));

        Assert.Equal(1_048_576, parameters.SieveHalfInterval);
        Assert.Equal(192L * 2_596_002, parameters.LargePrimeBound);
        Assert.Equal(36, parameters.ErrorMargin);
        Assert.Equal(1_048_576, parameters.SieveBlockSize);
        Assert.Equal(1_048_576, parameters.BucketLargePrimeCutoff);
        Assert.Equal(262_144, parameters.ResieveLargePrimeCutoff);
        Assert.Equal(9, parameters.APrimeCount);
        Assert.Equal(144, parameters.APrimeWindowSize);
        Assert.Equal(96_971, parameters.RelationTarget);
        Assert.False(parameters.EnableTwoLargePrimes);
        Assert.Equal(192L * 2_596_002, parameters.LargePrime2Bound);
        Assert.Equal(31_250, parameters.LargePrime2ThresholdBound);
        Assert.Equal(CofactorSplitterKind.Squfof, parameters.CofactorSplitter);
    }

    [Fact]
    public void C91_to_c99_keeps_experimental_two_large_prime_parameters_disabled_by_default()
    {
        var parameters = SievingParameters.Default(FactorBaseWithDigitCount(digits: 95, entryCount: 137_750, bound: 3_882_876));

        Assert.False(parameters.EnableTwoLargePrimes);
        Assert.Equal(192L * 3_882_876, parameters.LargePrime2Bound);
        Assert.Equal(62_500, parameters.LargePrime2ThresholdBound);
        Assert.Equal(CofactorSplitterKind.Squfof, parameters.CofactorSplitter);
    }

    [Fact]
    public void Two_large_prime_defaults_switch_on_at_c100()
    {
        var c82 = SievingParameters.Default(FactorBaseWithDigitCount(digits: 82, bound: 1_250_000));
        Assert.False(c82.EnableTwoLargePrimes);
        Assert.Equal(5_000_000, c82.LargePrime2Bound);
        Assert.Equal(62_500, c82.LargePrime2ThresholdBound);

        var c83 = SievingParameters.Default(FactorBaseWithDigitCount(digits: 83, bound: 1_250_000));

        Assert.False(c83.EnableTwoLargePrimes);
        Assert.Equal(5_000_000, c83.LargePrime2Bound);
        Assert.Equal(31_250, c83.LargePrime2ThresholdBound);

        var c84 = SievingParameters.Default(FactorBaseWithDigitCount(digits: 84, bound: 1_250_000));

        Assert.False(c84.EnableTwoLargePrimes);
        Assert.Equal(5_000_000, c84.LargePrime2Bound);
        Assert.Equal(15_625, c84.LargePrime2ThresholdBound);

        var c85 = SievingParameters.Default(FactorBaseWithDigitCount(digits: 85, bound: 1_250_000));

        Assert.False(c85.EnableTwoLargePrimes);
        Assert.Equal(c85.LargePrimeBound, c85.LargePrime2Bound);
        Assert.Equal(15_625, c85.LargePrime2ThresholdBound);

        var c99 = SievingParameters.Default(FactorBaseWithDigitCount(digits: 99, bound: 5_000_000));

        Assert.False(c99.EnableTwoLargePrimes);
        Assert.Equal(c99.LargePrimeBound, c99.LargePrime2Bound);
        Assert.Equal(62_500, c99.LargePrime2ThresholdBound);

        var c100 = SievingParameters.Default(FactorBaseWithDigitCount(digits: 100, bound: 5_000_000));

        Assert.True(c100.EnableTwoLargePrimes);
        Assert.Equal(c100.LargePrimeBound, c100.LargePrime2Bound);
        Assert.Equal(275_000_000, c100.LargePrime2ThresholdBound);
    }

    [Fact]
    public void Explicit_two_large_prime_enable_below_c100_has_valid_lp2_parameters()
    {
        var defaults = SievingParameters.Default(FactorBaseWithDigitCount(digits: 90, entryCount: 94_923, bound: 2_596_002));
        var enabled = defaults with { EnableTwoLargePrimes = true };

        Assert.True(enabled.EnableTwoLargePrimes);
        Assert.True(enabled.LargePrime2Bound > 0);
        Assert.Equal(192L * 2_596_002, enabled.LargePrime2Bound);
        Assert.Equal(31_250, enabled.LargePrime2ThresholdBound);
    }

    [Fact]
    public void Bucket_and_resieve_defaults_switch_on_at_c89()
    {
        var c88 = SievingParameters.Default(FactorBaseWithDigitCount(digits: 88, bound: 4_000_000));
        Assert.Equal(0, c88.BucketLargePrimeCutoff);
        Assert.Equal(0, c88.ResieveLargePrimeCutoff);

        foreach (var digits in new[] { 89, 92, 97, 98, 105 })
        {
            var parameters = SievingParameters.Default(FactorBaseWithDigitCount(digits, bound: 4_000_000));
            Assert.Equal(1_048_576, parameters.BucketLargePrimeCutoff);
            Assert.Equal(parameters.EffectiveSieveBlockSize, parameters.BucketLargePrimeCutoff);
            Assert.Equal(262_144, parameters.ResieveLargePrimeCutoff);
            Assert.Equal(parameters.EffectiveSieveBlockSize / 4, parameters.ResieveLargePrimeCutoff);
        }
    }

    [Fact]
    public void C100_to_c104_uses_intermediate_sieving_defaults()
    {
        var parameters = SievingParameters.Default(FactorBaseWithDigitCount(digits: 100, entryCount: 174_605, bound: 5_000_000));

        Assert.Equal(8_388_608, parameters.SieveHalfInterval);
        Assert.Equal(384L * 5_000_000, parameters.LargePrimeBound);
        Assert.Equal(384L * 5_000_000, parameters.LargePrime2Bound);
        Assert.Equal(275_000_000, parameters.LargePrime2ThresholdBound);
        Assert.Equal(CofactorSplitterKind.Squfof, parameters.CofactorSplitter);
        Assert.Equal(56, parameters.ErrorMargin);
        Assert.Equal(1_048_576, parameters.SieveBlockSize);
        Assert.Equal(1_048_576, parameters.BucketLargePrimeCutoff);
        Assert.Equal(1_048_576, parameters.EffectiveBucketLargePrimeCutoff);
        Assert.Equal(262_144, parameters.ResieveLargePrimeCutoff);
        Assert.True(parameters.EnableTwoLargePrimes);
        Assert.Equal(262_144, parameters.EffectiveResieveLargePrimeCutoff);
        Assert.Equal(10, parameters.APrimeCount);
        Assert.Equal(160, parameters.APrimeWindowSize);
        Assert.Equal(184_605, parameters.RelationTarget);
        Assert.True(parameters.PolynomialCount >= 30L * parameters.RelationTarget);
    }

    [Fact]
    public void C105_plus_uses_deeper_sieving_defaults()
    {
        var parameters = SievingParameters.Default(FactorBaseWithDigitCount(digits: 105, entryCount: 455_500, bound: 14_000_000));

        Assert.Equal(16_777_216, parameters.SieveHalfInterval);
        Assert.Equal(512L * 14_000_000, parameters.LargePrimeBound);
        Assert.Equal(512L * 14_000_000, parameters.LargePrime2Bound);
        Assert.Equal(50_000_000, parameters.LargePrime2ThresholdBound);
        // LP2 bound (7.168e9) exceeds 2³², so residuals may exceed 64 bits: the rho fallback is auto-selected.
        Assert.Equal(CofactorSplitterKind.SqufofRho, parameters.CofactorSplitter);
        Assert.Equal(48, parameters.ErrorMargin);
        Assert.Equal(1_048_576, parameters.SieveBlockSize);
        Assert.Equal(1_048_576, parameters.BucketLargePrimeCutoff);
        Assert.Equal(262_144, parameters.ResieveLargePrimeCutoff);
        Assert.True(parameters.EnableTwoLargePrimes);
        Assert.Equal(10, parameters.APrimeCount);
        Assert.Equal(160, parameters.APrimeWindowSize);
        Assert.Equal(473_720, parameters.RelationTarget);
        Assert.True(parameters.PolynomialCount >= 64L * parameters.RelationTarget);
    }

    [Fact]
    public void C110_uses_tuned_sieving_defaults()
    {
        var parameters = SievingParameters.Default(FactorBaseWithDigitCount(digits: 110, entryCount: 1_216_946, bound: 40_000_000));

        Assert.Equal(SievingParameters.C110SieveHalfInterval, parameters.SieveHalfInterval);
        Assert.Equal(SievingParameters.C110LargePrimeBound, parameters.LargePrimeBound);
        Assert.Equal(SievingParameters.C110LargePrime2Bound, parameters.LargePrime2Bound);
        Assert.Equal(SievingParameters.C110LargePrime2ThresholdBound, parameters.LargePrime2ThresholdBound);
        Assert.Equal(CofactorSplitterKind.Squfof, parameters.CofactorSplitter);
        Assert.Equal(SievingParameters.C110ErrorMargin, parameters.ErrorMargin);
        Assert.Equal(1_048_576, parameters.SieveBlockSize);
        Assert.Equal(1_048_576, parameters.BucketLargePrimeCutoff);
        Assert.Equal(262_144, parameters.ResieveLargePrimeCutoff);
        Assert.True(parameters.EnableTwoLargePrimes);
        Assert.Equal(10, parameters.APrimeCount);
        Assert.Equal(SievingParameters.C110APrimeWindowSize, parameters.APrimeWindowSize);
        Assert.Equal(1_265_624, parameters.RelationTarget);
        Assert.Equal(
            SievingParameters.AvailablePolynomialSupply(parameters.APrimeWindowSize, parameters.APrimeCount),
            parameters.PolynomialCount);
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

        Assert.Equal(7, parameters.APrimeCount);
        Assert.Equal(37_250, parameters.RelationTarget);
        Assert.Equal(112, parameters.APrimeWindowSize);
        Assert.True(
            AvailablePolynomialSupply(parameters.APrimeWindowSize, parameters.APrimeCount) >=
            10L * parameters.RelationTarget);
        Assert.Equal(Math.Max(16, 16 * parameters.APrimeCount), parameters.APrimeWindowSize);
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
