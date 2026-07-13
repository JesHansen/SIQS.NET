using System.Numerics;
using SIQS.Contracts.Numerics;

namespace SIQS.Contracts.Tests;

public class IntegerMathTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(15, 3)]
    [InlineData(16, 4)]
    [InlineData(17, 4)]
    [InlineData(99, 9)]
    [InlineData(100, 10)]
    public void Sqrt_returns_floor_square_root(long n, long expected)
    {
        Assert.Equal(new BigInteger(expected), IntegerMath.Sqrt(n));
    }

    [Fact]
    public void Sqrt_handles_large_values()
    {
        var n = BigInteger.Parse("100000000000000000000000000000000"); // 10^32
        var r = IntegerMath.Sqrt(n);
        Assert.True(r * r <= n && (r + 1) * (r + 1) > n);
    }

    [Fact]
    public void Sqrt_throws_on_negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IntegerMath.Sqrt(-1));
    }

    [Theory]
    [InlineData(16, true, 4)]
    [InlineData(81, true, 9)]
    [InlineData(77, false, 0)]
    [InlineData(1, true, 1)]
    public void IsPerfectSquare_detects_squares(long n, bool isSquare, long root)
    {
        Assert.Equal(isSquare, IntegerMath.IsPerfectSquare(n, out var r));
        if (isSquare)
        {
            Assert.Equal(new BigInteger(root), r);
        }
    }

    [Theory]
    [InlineData(3, 11, 4)]   // 3*4 = 12 == 1 mod 11
    [InlineData(7, 26, 15)]  // 7*15 = 105 == 1 mod 26
    public void BigInteger_ModInverse_returns_inverse(long a, long m, long expected)
    {
        Assert.Equal(new BigInteger(expected), IntegerMath.ModInverse(new BigInteger(a), new BigInteger(m)));
    }

    [Theory]
    [InlineData(3, 11, 4)]   // 3*4 = 12 == 1 mod 11
    [InlineData(7, 26, 15)]  // 7*15 = 105 == 1 mod 26
    [InlineData(-3, 11, 7)]  // -3*7 = -21 == 1 mod 11
    public void Long_ModInverse_returns_inverse(long a, long m, long expected)
    {
        Assert.Equal(expected, IntegerMath.ModInverse(a, m));
        Assert.Equal(1, IntegerMath.Mod(a * expected, m));
    }

    [Fact]
    public void BigInteger_ModInverse_throws_when_not_coprime()
    {
        Assert.Throws<ArithmeticException>(() => IntegerMath.ModInverse(new BigInteger(4), new BigInteger(8)));
    }

    [Fact]
    public void Long_ModInverse_throws_when_not_coprime()
    {
        Assert.Throws<ArithmeticException>(() => IntegerMath.ModInverse(4L, 8L));
    }
}

public class PrimalityTests
{
    [Theory]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(1, false)]
    [InlineData(0, false)]
    [InlineData(97, true)]
    [InlineData(100, false)]
    [InlineData(20000003, true)]
    [InlineData(20000005, false)]
    public void IsProbablePrime_classifies_small_numbers(long n, bool expected)
    {
        Assert.Equal(expected, Primality.IsProbablePrime(n));
    }

    [Fact]
    public void IsProbablePrime_handles_large_prime()
    {
        Assert.True(Primality.IsProbablePrime(BigInteger.Parse("2147483647"))); // Mersenne prime 2^31-1
    }

    [Fact]
    public void IsProbablePrime_handles_large_composite()
    {
        Assert.False(Primality.IsProbablePrime(BigInteger.Parse("2147483649")));
    }
}
