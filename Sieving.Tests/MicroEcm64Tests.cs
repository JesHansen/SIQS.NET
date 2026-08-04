namespace Sieving.Tests;

public class MicroEcm64Tests
{
    [Theory]
    [InlineData(3UL, 0.618033988749894903)]
    [InlineData(5UL, 0.618033988749894903)]
    [InlineData(7UL, 0.618033988749894903)]
    [InlineData(11UL, 0.580178728295464130)]
    [InlineData(23UL, 0.522786351415446049)]
    [InlineData(29UL, 0.548409048446403258)]
    [InlineData(37UL, 0.580178728295464130)]
    [InlineData(41UL, 0.548409048446403258)]
    [InlineData(47UL, 0.548409048446403258)]
    public void Prac_matches_binary_ladder(ulong scalar, double ratio)
    {
        Assert.True(MicroEcm64.PracMatchesBinaryLadder(1_000_003UL * 1_000_033UL, scalar, ratio));
    }

    [Theory]
    [InlineData(1_000_003UL, 1_000_033UL)]
    [InlineData(15_485_863UL, 32_452_843UL)]
    public void TryFactor_finds_a_nontrivial_factor(ulong left, ulong right)
    {
        var value = left * right;

        var factor = MicroEcm64.TryFactor(value, stage1Bound: 205, curves: 64, stage2Multiplier: 25);

        Assert.InRange(factor, 2UL, value - 1);
        Assert.Equal(0UL, value % factor);
    }

    [Theory]
    [InlineData(1_000_000_007UL, 1_000_000_009UL)]   // balanced ~30-bit primes (wide-corpus shape)
    [InlineData(998_244_353UL, 2_147_483_647UL)]     // ~30-bit and ~31-bit primes
    [InlineData(60_000_011UL, 2_147_483_647UL)]      // just-above-factor-base and ~31-bit
    [InlineData(1_000_000_007UL, 1_000_000_007UL)]   // perfect square
    [InlineData(1_000_003UL, 1_000_033UL)]           // close small primes
    public void TryFactorStage2_finds_a_nontrivial_factor(ulong left, ulong right)
    {
        var value = left * right;

        var factor = MicroEcm64.TryFactorStage2(value, b1: 300, b2: 30_000, curves: 64);

        Assert.InRange(factor, 2UL, value - 1);
        Assert.Equal(0UL, value % factor);
    }

    [Theory]
    [InlineData(998_244_353UL)]
    [InlineData(2_147_483_647UL)]
    [InlineData(1_000_000_007UL)]
    public void TryFactorStage2_returns_no_split_for_prime(ulong prime)
    {
        Assert.Equal(1UL, MicroEcm64.TryFactorStage2(prime, b1: 300, b2: 30_000, curves: 8));
    }
}
