using Sieving;

namespace Sieving.Tests;

public class CofactorSplitterKindsTests
{
    [Theory]
    [InlineData(CofactorSplitterKind.Squfof, "squfof")]
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
}
