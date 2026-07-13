using System.Numerics;
using Factorbase;

namespace Factorbase.Tests;

public class NumberTheoryTests
{
    [Theory]
    [InlineData(1, 7, 1)]   // 1 is a QR
    [InlineData(2, 7, 1)]   // 3^2=9=2 mod 7
    [InlineData(3, 7, -1)]  // 3 is a non-residue mod 7
    [InlineData(7, 7, 0)]   // divisible
    [InlineData(10, 13, 1)] // 6^2=36=10 mod 13
    public void Legendre_matches_known_values(long a, long p, int expected)
    {
        Assert.Equal(expected, NumberTheory.Legendre(a, p));
    }

    [Theory]
    [InlineData(2, 7)]
    [InlineData(10, 13)]
    [InlineData(5, 41)]
    [InlineData(1000003 % 104729, 104729)]
    public void TonelliShanks_returns_root_whose_square_is_n(long n, long p)
    {
        var r = NumberTheory.TonelliShanks(n, p);
        Assert.Equal(BigInteger.Remainder(n, p), (r * r) % p);
        Assert.InRange(r, 0, p - 1);
    }

    [Fact]
    public void TonelliShanks_handles_p_equal_3_mod_4()
    {
        // p = 7 (3 mod 4), n = 2 -> roots 3 and 4
        var r = NumberTheory.TonelliShanks(2, 7);
        Assert.True(r == 3 || r == 4);
    }
}

public class PrimeSieveTests
{
    [Fact]
    public void Generates_primes_up_to_bound()
    {
        Assert.Equal(new long[] { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29 }, PrimeSieve.PrimesUpTo(30));
    }

    [Fact]
    public void Includes_bound_when_prime()
    {
        Assert.Contains(29L, PrimeSieve.PrimesUpTo(29));
    }

    [Fact]
    public void Empty_below_two()
    {
        Assert.Empty(PrimeSieve.PrimesUpTo(1));
    }
}
