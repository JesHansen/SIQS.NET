using Sieving;

namespace Sieving.Tests;

public class CofactorSplitterKindsTests
{
    private const long NarrowBound = uint.MaxValue;
    private const long WideBound = (long)uint.MaxValue + 1;

    [Theory]
    [InlineData(CofactorSplitterKind.Squfof, "squfof")]
    [InlineData(CofactorSplitterKind.MicroEcmSqufof, "micro-ecm-squfof")]
    [InlineData(CofactorSplitterKind.MicroEcmStage2, "micro-ecm-stage2")]
    [InlineData(CofactorSplitterKind.SqufofRho, "squfof-rho")]
    public void ToToken_then_TryParse_round_trips(CofactorSplitterKind kind, string expectedToken)
    {
        var token = kind.ToToken();
        Assert.Equal(expectedToken, token);
        Assert.True(CofactorSplitterKinds.TryParse(token, out var parsed));
        Assert.Equal(kind, parsed);
    }

    [Theory]
    [InlineData("Squfof", CofactorSplitterKind.Squfof)]
    [InlineData("  MICRO-ECM-SQUFOF  ", CofactorSplitterKind.MicroEcmSqufof)]
    [InlineData("  MICRO-ECM-STAGE2  ", CofactorSplitterKind.MicroEcmStage2)]
    [InlineData("  SQUFOF-RHO  ", CofactorSplitterKind.SqufofRho)]
    public void TryParse_is_case_insensitive_and_trims(string token, CofactorSplitterKind expected)
    {
        Assert.True(CofactorSplitterKinds.TryParse(token, out var parsed));
        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("brent")]
    [InlineData("squfof_rho")]
    [InlineData("rho")]
    public void TryParse_rejects_null_and_unknown_tokens(string? token)
    {
        Assert.False(CofactorSplitterKinds.TryParse(token, out var parsed));
        Assert.Equal(CofactorSplitterKinds.Default, parsed);
    }

    [Fact]
    public void Default_is_squfof()
    {
        Assert.Equal(CofactorSplitterKind.Squfof, CofactorSplitterKinds.Default);
    }

    [Theory]
    // bound² ≤ ulong.MaxValue → SQUFOF alone suffices.
    [InlineData(0L, CofactorSplitterKind.Squfof)]
    [InlineData(60_000_000L, CofactorSplitterKind.Squfof)]
    [InlineData((long)uint.MaxValue, CofactorSplitterKind.Squfof)]
    // bound² overflows 64 bits → rho fallback required.
    [InlineData((long)uint.MaxValue + 1, CofactorSplitterKind.SqufofRho)]
    [InlineData(7_168_000_000L, CofactorSplitterKind.SqufofRho)]
    public void SelectFor_picks_rho_fallback_only_above_the_64_bit_bound(long largePrime2Bound, CofactorSplitterKind expected)
    {
        Assert.Equal(expected, CofactorSplitterKinds.SelectFor(largePrime2Bound));
    }

    [Theory]
    [InlineData(CofactorSplitterKind.Squfof, NarrowBound, CofactorSplitterKind.Squfof)]
    [InlineData(CofactorSplitterKind.Squfof, WideBound, CofactorSplitterKind.SqufofRho)]
    [InlineData(CofactorSplitterKind.MicroEcmSqufof, WideBound, CofactorSplitterKind.SqufofRho)]
    [InlineData(CofactorSplitterKind.MicroEcmStage2, NarrowBound, CofactorSplitterKind.MicroEcmStage2)]
    [InlineData(CofactorSplitterKind.MicroEcmStage2, WideBound, CofactorSplitterKind.MicroEcmStage2)]
    [InlineData(CofactorSplitterKind.SqufofRho, WideBound, CofactorSplitterKind.SqufofRho)]
    public void Auto_selection_keeps_the_preferred_strategy_unless_it_cannot_split_wide_residuals(
        CofactorSplitterKind preferred, long largePrime2Bound, CofactorSplitterKind expected)
    {
        Assert.Equal(expected, CofactorSplitterKinds.AutoSelect(preferred, largePrime2Bound));
        Assert.Equal(expected, CofactorSplitterKinds.Resolve(null, preferred, largePrime2Bound));
    }

    [Fact]
    public void Supplied_tokens_override_auto_selection_and_invalid_tokens_are_rejected()
    {
        Assert.Equal(CofactorSplitterKind.Squfof,
            CofactorSplitterKinds.Resolve("SQUFOF", CofactorSplitterKind.MicroEcmStage2, WideBound));
        var error = Assert.Throws<ArgumentException>(() =>
            CofactorSplitterKinds.Resolve("definitely-invalid", CofactorSplitterKind.Squfof, NarrowBound));
        Assert.Contains("definitely-invalid", error.Message);
    }
}
