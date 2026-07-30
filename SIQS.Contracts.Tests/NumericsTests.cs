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
    [InlineData(0, 3, 0)]
    [InlineData(1, 7, 1)]
    [InlineData(26, 3, 2)]
    [InlineData(27, 3, 3)]
    [InlineData(28, 3, 3)]
    [InlineData(624, 4, 4)]
    [InlineData(625, 4, 5)]
    public void NthRoot_returns_floor_integer_root(long n, int degree, long expected)
    {
        Assert.Equal(new BigInteger(expected), IntegerMath.NthRoot(n, degree));
    }

    [Fact]
    public void NthRoot_handles_large_exact_power()
    {
        var root = BigInteger.Parse("87658437637587659584646521");
        var cube = BigInteger.Pow(root, 3);

        Assert.Equal(root, IntegerMath.NthRoot(cube, 3));
        Assert.Equal(root - 1, IntegerMath.NthRoot(cube - 1, 3));
    }

    [Fact]
    public void NthRoot_validates_its_inputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IntegerMath.NthRoot(-1, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => IntegerMath.NthRoot(8, 0));
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

    [Theory]
    [InlineData((1L << 62) - 1)]     // largest modulus on the narrow long path
    [InlineData(1L << 62)]           // smallest modulus on the wide Int128 path
    [InlineData(long.MaxValue - 1)]  // near the top of the wide path
    public void Long_ModInverse_agrees_with_BigInteger_across_the_width_threshold(long m)
    {
        var random = new Random(1234);
        for (var i = 0; i < 200; i++)
        {
            var a = random.NextInt64(1, m);
            if (BigInteger.GreatestCommonDivisor(a, m) != 1)
            {
                continue;
            }

            var inverse = IntegerMath.ModInverse(a, m);
            Assert.Equal(IntegerMath.ModInverse(new BigInteger(a), new BigInteger(m)), new BigInteger(inverse));
            Assert.Equal(BigInteger.One, IntegerMath.Mod(new BigInteger(a) * inverse, m));
        }
    }
}

public class PrimalityBoundaryTests
{
    [Fact]
    public void Thirteen_witnesses_reject_the_first_twelve_witness_pseudoprime()
    {
        var composite = BigInteger.Parse("318665857834031151167461");

        Assert.Equal(
            composite,
            BigInteger.Parse("399165290221") * BigInteger.Parse("798330580441"));
        Assert.False(Primality.IsProbablePrime(composite));
    }

    [Fact]
    public void Deterministic_range_is_exclusive()
    {
        Assert.True(Primality.IsWithinDeterministicRange(Primality.DeterministicUpperBound - 1));
        Assert.False(Primality.IsWithinDeterministicRange(Primality.DeterministicUpperBound));
    }
}

public class BailliePswTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(41, true)]  // largest small-prime witness
    [InlineData(97, true)]
    [InlineData(100, false)]
    [InlineData(1190281, false)] // 1091^2, a perfect square
    [InlineData(20000003, true)]
    [InlineData(20000005, false)]
    public void Classifies_small_numbers(long n, bool expected)
    {
        Assert.Equal(expected, Primality.IsBailliePswProbablePrime(n));
    }

    [Fact]
    public void Rejects_the_smallest_base2_strong_pseudoprime()
    {
        // 2047 = 23 * 89 passes the base-2 strong Miller-Rabin test; only the Lucas half rejects it.
        Assert.True(Primality.IsBailliePswProbablePrime(2047) == false);
        Assert.Equal(BigInteger.Parse("2047"), new BigInteger(23) * 89);
    }

    [Fact]
    public void Rejects_a_base2_and_base3_strong_pseudoprime()
    {
        // 1373653 = 829 * 1657 is a strong pseudoprime to bases 2 and 3.
        Assert.False(Primality.IsBailliePswProbablePrime(1373653));
    }

    [Fact]
    public void Rejects_the_fixed_witness_survivor_composite()
    {
        // Passes the 13-witness Miller-Rabin set (IsProbablePrime returns true) but is composite;
        // Baillie-PSW must reject it via the strong Lucas test.
        var composite = BigInteger.Parse("3317044064679887385961981");
        Assert.True(Primality.IsProbablePrime(composite));
        Assert.False(Primality.IsBailliePswProbablePrime(composite));
    }

    [Fact]
    public void Accepts_large_primes_above_the_deterministic_bound()
    {
        // Mersenne primes 2^89-1 and 2^127-1, both far above DeterministicUpperBound (~3.3e24).
        Assert.True(Primality.IsBailliePswProbablePrime(BigInteger.Pow(2, 89) - 1));
        Assert.True(Primality.IsBailliePswProbablePrime(BigInteger.Pow(2, 127) - 1));
    }

    [Fact]
    public void Rejects_large_composites_above_the_deterministic_bound()
    {
        // 2^89-1 has no small factors but 2^90-1 is composite; also a product of two large primes.
        Assert.False(Primality.IsBailliePswProbablePrime(BigInteger.Pow(2, 90) - 1));
        Assert.False(Primality.IsBailliePswProbablePrime(
            BigInteger.Parse("1287836182261") * BigInteger.Parse("2575672364521")));
    }

    [Fact]
    public void Agrees_with_deterministic_miller_rabin_across_a_dense_range()
    {
        // Below DeterministicUpperBound the 13-witness Miller-Rabin test is exact, so Baillie-PSW
        // must match it on every input. A dense sweep exercises the Lucas path on many composites.
        for (var n = 0; n <= 200_000; n++)
        {
            Assert.Equal(Primality.IsProbablePrime(n), Primality.IsBailliePswProbablePrime(n));
        }
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
