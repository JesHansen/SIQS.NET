namespace Sieving.Tests;

/// <summary>
/// Boundary coverage for the default tuning selectors extracted from <c>SievingParameters.Default</c>.
/// Each theory pins the value at a band edge and at the digit immediately below/above it, so a
/// change to any threshold is caught. These must match the pre-extraction v1 defaults exactly.
/// </summary>
public class SievingParameterSelectorsTests
{
    private const long Bound = 1000;

    [Theory]
    [InlineData(69, 32_768)]     // small-input clamp: 20*1000 clamped up to 32_768
    [InlineData(70, 393_216)]
    [InlineData(77, 393_216)]
    [InlineData(78, 524_288)]
    [InlineData(81, 524_288)]
    [InlineData(82, 393_216)]
    [InlineData(88, 393_216)]
    [InlineData(89, 1_048_576)]
    [InlineData(99, 1_048_576)]
    [InlineData(100, 8_388_608)]
    [InlineData(104, 8_388_608)]
    [InlineData(105, 16_777_216)]
    [InlineData(109, 16_777_216)]
    public void SelectSieveHalfInterval_bands(int digits, long expected)
        => Assert.Equal(expected, SievingParameters.SelectSieveHalfInterval(digits, Bound, digits >= 110));

    [Fact]
    public void SelectSieveHalfInterval_c110_uses_tuned_constant()
        => Assert.Equal(SievingParameters.C110SieveHalfInterval, SievingParameters.SelectSieveHalfInterval(110, Bound, true));

    [Theory]
    [InlineData(69, 0)]
    [InlineData(70, 524_288)]
    [InlineData(81, 524_288)]
    [InlineData(82, 0)]
    [InlineData(88, 0)]
    [InlineData(89, 1_048_576)]
    public void SelectSieveBlockSize_bands(int digits, int expected)
        => Assert.Equal(expected, SievingParameters.SelectSieveBlockSize(digits));

    [Theory]
    [InlineData(99, 10)]
    [InlineData(100, 30)]
    [InlineData(104, 30)]
    [InlineData(105, 64)]
    public void SelectPolynomialSupplyMultiplier_bands(int digits, long expected)
        => Assert.Equal(expected, SievingParameters.SelectPolynomialSupplyMultiplier(digits));

    [Theory]
    [InlineData(69, 64_000)]
    [InlineData(70, 128_000)]
    [InlineData(81, 128_000)]
    [InlineData(82, 64_000)]
    [InlineData(88, 64_000)]
    [InlineData(89, 192_000)]
    [InlineData(99, 192_000)]
    [InlineData(100, 384_000)]
    [InlineData(104, 384_000)]
    [InlineData(105, 512_000)]
    [InlineData(109, 512_000)]
    public void SelectLargePrimeBound_bands(int digits, long expected)
        => Assert.Equal(expected, SievingParameters.SelectLargePrimeBound(digits, Bound, digits >= 110));

    [Fact]
    public void SelectLargePrimeBound_c110_uses_tuned_constant()
        => Assert.Equal(SievingParameters.C110LargePrimeBound, SievingParameters.SelectLargePrimeBound(110, Bound, true));

    [Theory]
    [InlineData(69, 24)]
    [InlineData(70, 40)]
    [InlineData(81, 40)]
    [InlineData(82, 24)]
    [InlineData(88, 24)]
    [InlineData(89, 36)]
    [InlineData(99, 36)]
    [InlineData(100, 56)]
    [InlineData(104, 56)]
    [InlineData(105, 48)]
    [InlineData(109, 48)]
    public void SelectErrorMargin_bands(int digits, int expected)
        => Assert.Equal(expected, SievingParameters.SelectErrorMargin(digits, digits >= 110));

    [Fact]
    public void SelectErrorMargin_c110_uses_tuned_constant()
        => Assert.Equal(SievingParameters.C110ErrorMargin, SievingParameters.SelectErrorMargin(110, true));

    [Theory]
    [InlineData(79, 10_000_000L, 10_000_000L)] // below the 80-84 cap band: passes through
    [InlineData(80, 10_000_000L, 5_000_000L)]  // capped at 5M
    [InlineData(84, 10_000_000L, 5_000_000L)]
    [InlineData(85, 10_000_000L, 10_000_000L)] // above the cap band: passes through
    [InlineData(80, 3_000_000L, 3_000_000L)]   // cap only lowers, never raises
    public void SelectLargePrime2Bound_bands(int digits, long largePrimeBound, long expected)
        => Assert.Equal(expected, SievingParameters.SelectLargePrime2Bound(digits, largePrimeBound, digits >= 110));

    // Threshold selector: pass a large LP2 bound so Math.Min resolves to the band constant.
    [Theory]
    [InlineData(79, long.MaxValue, long.MaxValue)] // below all fine bands: passes through
    [InlineData(80, long.MaxValue, 62_500L)]
    [InlineData(82, long.MaxValue, 62_500L)]
    [InlineData(83, long.MaxValue, 31_250L)]
    [InlineData(84, long.MaxValue, 15_625L)]
    [InlineData(85, long.MaxValue, 15_625L)]
    [InlineData(86, long.MaxValue, 31_250L)]
    [InlineData(90, long.MaxValue, 31_250L)]
    [InlineData(91, long.MaxValue, 62_500L)]
    [InlineData(99, long.MaxValue, 62_500L)]
    [InlineData(100, long.MaxValue, 275_000_000L)]
    [InlineData(104, long.MaxValue, 275_000_000L)]
    [InlineData(105, long.MaxValue, 50_000_000L)]
    [InlineData(109, long.MaxValue, 50_000_000L)]
    public void SelectLargePrime2ThresholdBound_bands(int digits, long lp2Bound, long expected)
        => Assert.Equal(expected, SievingParameters.SelectLargePrime2ThresholdBound(digits, lp2Bound, digits >= 110));

    [Fact]
    public void SelectLargePrime2ThresholdBound_never_exceeds_lp2_bound()
        => Assert.Equal(1000L, SievingParameters.SelectLargePrime2ThresholdBound(100, 1000L, false));
}
