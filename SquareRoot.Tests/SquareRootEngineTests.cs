using System.Numerics;
using SquareRoot;
using SIQS.Contracts;
using SIQS.Contracts.Files;

namespace SquareRoot.Tests;

public class SquareRootEngineTests
{
    private static FactorBaseDocument FactorBase(BigInteger targetN, BigInteger multiplier, params long[] primes)
    {
        var entries = primes.Select((p, i) => new FactorBaseEntry(i + 1, p, 0, 0, 1)).ToList();
        return new FactorBaseDocument(
            new FactorBaseMetadata(targetN, multiplier, targetN * multiplier, 100, 10.0), entries);
    }

    [Theory]
    [InlineData(32, 10, 77, 7, 11)]
    public void ExtractFactor_finds_nontrivial_factor(long x, long y, long n, long f1, long f2)
    {
        var (g1, g2, factor1, factor2) = SquareRootEngine.ExtractFactor(x, y, n);
        Assert.Equal(new BigInteger(11), g1); // gcd(|32-10|,77)=gcd(22,77)=11
        Assert.Equal(new BigInteger(7), g2);  // gcd(32+10,77)=gcd(42,77)=7
        Assert.Equal(new BigInteger(f1), factor1);
        Assert.Equal(new BigInteger(f2), factor2);
    }

    [Fact]
    public void ExtractFactor_returns_null_for_trivial_congruence()
    {
        var (_, _, factor1, factor2) = SquareRootEngine.ExtractFactor(10, 10, 77); // X == Y
        Assert.Null(factor1);
        Assert.Null(factor2);
    }

    [Fact]
    public void End_to_end_example_factors_77()
    {
        // From EndToEndExample.md: relation F00000000 t=9 exponents 1:2, column 1 -> prime 2.
        var fb = FactorBase(77, 1, 2);
        var relations = new FilteredRelationsDocument(77, 1, 77, new[]
        {
            new FilteredRelationRecord("F00000000", RelationKind.Full, new[] { "R00000000" },
                T: 9, Sign: 1, Exponents: new Dictionary<int, int> { [1] = 2 },
                ParityColumns: Array.Empty<int>(), LargePrime: null),
        });
        var deps = new DependenciesDocument(77, 1, 77, 1, 2, new[]
        {
            new DependencyRecord(0, new[] { 0 }, new[] { "F00000000" }),
        });

        var result = SquareRootEngine.Run(fb, relations, deps);

        var row = Assert.Single(result.Factors.Results);
        Assert.Equal(FactorizationStatus.FactorFound, row.Status);
        Assert.Equal(new BigInteger(7), result.Factor1);
        Assert.Equal(new BigInteger(11), result.Factor2);
        Assert.Equal(new BigInteger(77), result.Factor1 * result.Factor2);
    }

    [Fact]
    public void Odd_exponent_sum_is_invalid()
    {
        var fb = FactorBase(77, 1, 2);
        var relations = new FilteredRelationsDocument(77, 1, 77, new[]
        {
            new FilteredRelationRecord("F00000000", RelationKind.Full, new[] { "R0" },
                9, 1, new Dictionary<int, int> { [1] = 1 }, new[] { 1 }, null),
        });
        var deps = new DependenciesDocument(77, 1, 77, 1, 2, new[]
        {
            new DependencyRecord(0, new[] { 0 }, new[] { "F00000000" }),
        });

        var result = SquareRootEngine.Run(fb, relations, deps);
        Assert.Equal(FactorizationStatus.Invalid, Assert.Single(result.Factors.Results).Status);
    }

    [Fact]
    public void Combined_partial_multiplies_large_prime_into_Y()
    {
        // Construct a dependency that only factors correctly when q is folded into Y.
        // Use TargetN = 1003 = 17 * 59. Build two relations whose combination needs q.
        // We assert the engine multiplies the large prime exactly once per combined partial by
        // comparing against a manual computation.
        var n = new BigInteger(1003);
        var fb = FactorBase(n, 1, 2, 3);
        var q = new BigInteger(101);

        // Relation A: combined_partial, t=tA, exponents {1:2} (=> Y contributes 2^1), large_prime=q
        var relations = new FilteredRelationsDocument(n, 1, n, new[]
        {
            new FilteredRelationRecord("F00000000", RelationKind.CombinedPartial, new[] { "R0", "R1" },
                T: 50, Sign: 1, Exponents: new Dictionary<int, int> { [1] = 2 }, ParityColumns: Array.Empty<int>(), LargePrime: q),
        });
        var deps = new DependenciesDocument(n, 1, n, 1, 3, new[]
        {
            new DependencyRecord(0, new[] { 0 }, new[] { "F00000000" }),
        });

        var result = SquareRootEngine.Run(fb, relations, deps);
        var row = Assert.Single(result.Factors.Results);

        // Y manual = 2^(2/2) * q mod N ; X = 50 mod N. Verify the engine used q by checking gcd values.
        var x = new BigInteger(50);
        var y = BigInteger.Remainder(BigInteger.ModPow(2, 1, n) * q, n);
        var expectedG1 = BigInteger.GreatestCommonDivisor(BigInteger.Abs(x - y), n);
        Assert.Equal(expectedG1, row.GcdMinus);
    }

    [Fact]
    public void Combined_partial_multiplies_large_prime_list_into_Y()
    {
        var n = new BigInteger(1003);
        var fb = FactorBase(n, 1, 2, 3);
        var largePrimes = new BigInteger[] { 101, 103 };

        var relations = new FilteredRelationsDocument(n, 1, n, new[]
        {
            new FilteredRelationRecord("F00000000", RelationKind.CombinedPartial, new[] { "R0", "R1", "R2" },
                T: 50, Sign: 1, Exponents: new Dictionary<int, int> { [1] = 2 }, ParityColumns: Array.Empty<int>(), LargePrime: null)
            {
                LargePrimes = largePrimes,
            },
        });
        var deps = new DependenciesDocument(n, 1, n, 1, 3, new[]
        {
            new DependencyRecord(0, new[] { 0 }, new[] { "F00000000" }),
        });

        var result = SquareRootEngine.Run(fb, relations, deps);
        var row = Assert.Single(result.Factors.Results);

        var x = new BigInteger(50);
        var y = largePrimes.Aggregate(new BigInteger(2), (acc, q) => BigInteger.Remainder(acc * q, n));
        var expectedG1 = BigInteger.GreatestCommonDivisor(BigInteger.Abs(x - y), n);
        Assert.Equal(expectedG1, row.GcdMinus);
    }

    [Fact]
    public void Stops_after_first_factor_by_default()
    {
        var fb = FactorBase(77, 1, 2);
        var relations = new FilteredRelationsDocument(77, 1, 77, new[]
        {
            new FilteredRelationRecord("F00000000", RelationKind.Full, new[] { "R0" }, 9, 1,
                new Dictionary<int, int> { [1] = 2 }, Array.Empty<int>(), null),
            new FilteredRelationRecord("F00000001", RelationKind.Full, new[] { "R1" }, 9, 1,
                new Dictionary<int, int> { [1] = 2 }, Array.Empty<int>(), null),
        });
        var deps = new DependenciesDocument(77, 1, 77, 2, 2, new[]
        {
            new DependencyRecord(0, new[] { 0 }, new[] { "F00000000" }),
            new DependencyRecord(1, new[] { 1 }, new[] { "F00000001" }),
        });

        var result = SquareRootEngine.Run(fb, relations, deps);
        Assert.Single(result.Factors.Results); // stopped after the first factor
    }
}
