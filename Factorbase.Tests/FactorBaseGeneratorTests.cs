using System.Numerics;
using Factorbase;
using SIQS.Contracts;
using SIQS.Contracts.Numerics;

namespace Factorbase.Tests;

public class DefaultBoundTests
{
    [Theory]
    [InlineData("77")]
    [InlineData("100000000000000000003")]
    [InlineData("100000000000000000000000000003")]
    public void Defaults_stay_within_global_bounds(string n)
    {
        var bound = FactorBaseDefaults.DefaultBound(BigInteger.Parse(n));
        Assert.True(bound >= FactorBaseDefaults.MinBound && bound <= FactorBaseDefaults.MaxBound);
    }

    [Fact]
    public void Smooth_defaults_match_anchor_points()
    {
        Assert.Equal(1000, FactorBaseDefaults.DefaultBound(BigInteger.Pow(10, 19)));      // C20
        Assert.True(FactorBaseDefaults.DefaultBound(BigInteger.Pow(10, 89)) > FactorBaseDefaults.SmoothTargetBound); // C90
    }

    [Fact]
    public void Smooth_defaults_increase_every_five_digits_from_c20_to_c85()
    {
        var previous = FactorBaseDefaults.DefaultBound(BigInteger.Pow(10, 19)); // C20
        foreach (var digits in new[] { 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85 })
        {
            var current = FactorBaseDefaults.DefaultBound(BigInteger.Pow(10, digits - 1));
            Assert.True(current > previous, $"C{digits} bound {current} should be greater than previous {previous}.");
            previous = current;
        }
    }

    [Fact]
    public void C46_regression_input_uses_growing_large_input_heuristic()
    {
        var n = BigInteger.Parse("4941549382259840605686265489495136362573612531");
        Assert.True(FactorBaseDefaults.DefaultBound(n) > FactorBaseDefaults.DefaultBound(BigInteger.Pow(10, 44)));
    }

    [Fact]
    public void Large_input_uses_clamped_heuristic()
    {
        var bound = FactorBaseDefaults.DefaultBound(BigInteger.Pow(10, 85));
        Assert.InRange(bound, FactorBaseDefaults.MinBound, FactorBaseDefaults.MaxBound);
    }

    [Fact]
    public void Large_composite_tuning_raises_c80_and_c82_bounds()
    {
        var c79 = FactorBaseDefaults.DefaultBound(BigInteger.Pow(10, 78));
        var c80 = FactorBaseDefaults.DefaultBound(BigInteger.Pow(10, 79));
        var c81 = FactorBaseDefaults.DefaultBound(BigInteger.Pow(10, 80));
        var c82 = FactorBaseDefaults.DefaultBound(BigInteger.Pow(10, 81));

        Assert.True(c80 > 1.20 * c79, $"C80 bound {c80} should include the 1.25x large-composite boost over C79 {c79}.");
        Assert.True(c82 > 1.20 * c81, $"C82 bound {c82} should include the 1.50x large-composite boost over C81 {c81}.");
    }

    [Fact]
    public void C89_plus_uses_c90_bound_profile_above_old_clamp()
    {
        var c90 = FactorBaseDefaults.DefaultBound(BigInteger.Pow(10, 89));

        Assert.True(c90 > FactorBaseDefaults.SmoothTargetBound);
        Assert.True(c90 > FactorBaseDefaults.SmoothTargetBound);
    }

    [Fact]
    public void C90_benchmark_target_uses_tuned_bound()
    {
        var n = BigInteger.Parse("444472956719246442654022855045742357735038950523560838400811355688753532924848261952955507");

        Assert.Equal(2_596_002, FactorBaseDefaults.DefaultBound(n));
    }

    [Fact]
    public void C100_defaults_stay_on_legacy_large_composite_cap()
    {
        Assert.Equal(
            FactorBaseDefaults.LegacyLargeCompositeMaxBound,
            FactorBaseDefaults.DefaultBound(BigInteger.Pow(10, 99)));
    }

    [Fact]
    public void C105_defaults_use_tuned_bound()
    {
        Assert.Equal(
            FactorBaseDefaults.C105TunedBound,
            FactorBaseDefaults.DefaultBound(BigInteger.Pow(10, 104)));
    }

    [Fact]
    public void C110_defaults_use_tuned_bound()
    {
        Assert.Equal(
            FactorBaseDefaults.C110TunedBound,
            FactorBaseDefaults.DefaultBound(BigInteger.Pow(10, 109)));
    }
}

public class MultiplierSelectorTests
{
    private static readonly long[] OddScoringPrimes =
        { 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71, 73, 79, 83, 89, 97 };

    [Fact]
    public void Is_deterministic()
    {
        var n = BigInteger.Parse("1022117"); // 1009 * 1013
        Assert.Equal(MultiplierSelector.Select(n), MultiplierSelector.Select(n));
    }

    [Fact]
    public void Returns_candidate_from_allowed_set()
    {
        var allowed = new BigInteger[] { 1, 2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47 };
        var k = MultiplierSelector.Select(BigInteger.Parse("1022117"));
        Assert.Contains(k, allowed);
    }

    [Fact]
    public void Score_uses_classical_coefficients_for_residue_one_input()
    {
        var n = NumberWithOddPrimeResiduesOne(mod8: 1);
        var expected = 2.0 * Math.Log(2.0);
        foreach (var p in OddScoringPrimes)
        {
            expected += 2.0 * Math.Log(p) / (p - 1);
        }

        AssertClose(expected, MultiplierSelector.Score(1, n));
    }

    [Fact]
    public void Score_applies_power_of_two_table()
    {
        var score1 = MultiplierSelector.Score(1, NumberWithOddPrimeResiduesOne(mod8: 1));
        var score3 = MultiplierSelector.Score(1, NumberWithOddPrimeResiduesOne(mod8: 3));
        var score5 = MultiplierSelector.Score(1, NumberWithOddPrimeResiduesOne(mod8: 5));
        var score7 = MultiplierSelector.Score(1, NumberWithOddPrimeResiduesOne(mod8: 7));

        AssertClose(Math.Log(2.0), score1 - score5);
        AssertClose(0.5 * Math.Log(2.0), score5 - score3);
        AssertClose(0.0, score3 - score7);
    }

    [Fact]
    public void Score_credits_multiplier_prime_and_penalizes_multiplier_size()
    {
        var targetN = BigInteger.One;
        var scaled = 3 * targetN;
        var expectedWithoutPrimeThree = PowerOfTwoContribution(scaled) - 0.5 * Math.Log(3.0);
        foreach (var p in OddScoringPrimes)
        {
            if (p == 3)
            {
                continue;
            }

            if (NumberTheory.Legendre(scaled, p) == 1)
            {
                expectedWithoutPrimeThree += 2.0 * Math.Log(p) / (p - 1);
            }
        }

        var actual = MultiplierSelector.Score(3, targetN);

        AssertClose(Math.Log(3.0) / 3.0, actual - expectedWithoutPrimeThree);
        Assert.True(actual < MultiplierSelector.Score(1, targetN));
    }

    [Fact]
    public void Score_gives_even_scaled_values_no_power_of_two_credit()
    {
        var targetN = NumberWithOddPrimeResiduesOne(mod8: 1);
        var scaled = 2 * targetN;
        var expected = -0.5 * Math.Log(2.0);
        foreach (var p in OddScoringPrimes)
        {
            if (NumberTheory.Legendre(scaled, p) == 1)
            {
                expected += 2.0 * Math.Log(p) / (p - 1);
            }
        }

        AssertClose(expected, MultiplierSelector.Score(2, targetN));
    }

    [Fact]
    public void Select_can_choose_one_when_residues_are_already_favorable()
    {
        var n = NumberWithOddPrimeResiduesOne(mod8: 1);

        Assert.Equal(1, MultiplierSelector.Select(n));
    }

    [Fact]
    public void Select_penalizes_oversized_multiplier_when_bonus_does_not_pay()
    {
        var n = BigInteger.Parse("1030189"); // 1009 * 1021; old odd-prime-only score picked 19.

        Assert.True(MultiplierSelector.Score(1, n) > MultiplierSelector.Score(19, n));
        Assert.Equal(1, MultiplierSelector.Select(n));
    }

    private static BigInteger NumberWithOddPrimeResiduesOne(int mod8)
    {
        var oddPrimeProduct = BigInteger.One;
        foreach (var p in OddScoringPrimes)
        {
            oddPrimeProduct *= p;
        }

        for (var t = 0; t < 8; t++)
        {
            var candidate = 1 + oddPrimeProduct * t;
            if ((int)(candidate % 8) == mod8)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Unable to construct scoring fixture.");
    }

    private static double PowerOfTwoContribution(BigInteger scaled)
    {
        if (scaled.IsEven)
        {
            return 0.0;
        }

        var mod8 = (int)(scaled % 8);
        return mod8 switch
        {
            1 => 2.0 * Math.Log(2.0),
            5 => Math.Log(2.0),
            _ => 0.5 * Math.Log(2.0)
        };
    }

    private static void AssertClose(double expected, double actual)
    {
        Assert.True(
            Math.Abs(expected - actual) <= 1e-12,
            $"Expected {expected:R}, actual {actual:R}.");
    }
}

public class FactorBaseGeneratorTests
{
    private static FactorBaseGenerationResult Generate(string n, long? bound = null, BigInteger? multiplier = null)
        => FactorBaseGenerator.Generate(new FactorBaseOptions(BigInteger.Parse(n), bound, multiplier));

    [Fact]
    public void Rejects_n_less_than_two()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Generate("1"));
    }

    [Fact]
    public void Even_input_yields_factor_two()
    {
        var result = Generate("100");
        Assert.True(result.FoundEarlyFactor);
        var row = Assert.Single(result.EarlyFactors!.Results);
        Assert.Equal("even_target", row.Reason);
        Assert.Equal(new BigInteger(2), row.Factor1);
        Assert.Equal(new BigInteger(50), row.Factor2);
    }

    [Fact]
    public void Perfect_square_yields_root()
    {
        var result = Generate("1190281"); // 1091^2
        Assert.True(result.FoundEarlyFactor);
        var row = Assert.Single(result.EarlyFactors!.Results);
        Assert.Equal("perfect_square", row.Reason);
        Assert.Equal(new BigInteger(1091), row.Factor1);
        Assert.Equal(new BigInteger(1091), row.Factor2);
    }

    [Fact]
    public void Small_prime_factor_is_detected()
    {
        var result = Generate("1022217"); // 3 * 340739, odd and not a perfect square
        Assert.True(result.FoundEarlyFactor);
        var row = Assert.Single(result.EarlyFactors!.Results);
        Assert.Equal("small_prime_factor", row.Reason);
        Assert.Equal(new BigInteger(3), row.Factor1);
    }

    [Fact]
    public void Tiny_semiprime_is_detected_by_trial_division_to_square_root()
    {
        var result = Generate("3141407"); // 1663 * 1889
        Assert.True(result.FoundEarlyFactor);
        var row = Assert.Single(result.EarlyFactors!.Results);
        Assert.Equal("tiny_input_trial_division", row.Reason);
        Assert.Equal(new BigInteger(1663), row.Factor1);
        Assert.Equal(new BigInteger(1889), row.Factor2);
    }

    [Fact]
    public void Builds_factor_base_for_composite_without_small_factors()
    {
        var result = Generate("1000036000099"); // 1000003 * 1000033, both above tiny precheck bound
        Assert.False(result.FoundEarlyFactor);
        var doc = result.FactorBase!;

        // scaled_n = multiplier * target_n
        Assert.Equal(doc.Metadata.ScaledN, doc.Metadata.Multiplier * doc.Metadata.TargetN);

        // Indexes contiguous from 1, sorted ascending by prime.
        for (var i = 0; i < doc.Entries.Count; i++)
        {
            Assert.Equal(i + 1, doc.Entries[i].Index);
            if (i > 0)
            {
                Assert.True(doc.Entries[i].Prime > doc.Entries[i - 1].Prime);
            }
        }

        // p = 2 is the first entry with roots 0,0.
        Assert.Equal(2, doc.Entries[0].Prime);
        Assert.Equal(0, doc.Entries[0].Root1);
        Assert.Equal(0, doc.Entries[0].Root2);
    }

    [Fact]
    public void Includes_odd_primes_that_divide_only_the_multiplier_as_zero_roots()
    {
        var doc = Generate("1000036000099", bound: 50, multiplier: 3).FactorBase!;

        var entry = Assert.Single(doc.Entries, e => e.Prime == 3);
        Assert.Equal(2, entry.Index);
        Assert.Equal(0, entry.Root1);
        Assert.Equal(0, entry.Root2);
        Assert.Equal(BigInteger.Zero, doc.Metadata.ScaledN % 3);
        Assert.NotEqual(BigInteger.Zero, doc.Metadata.TargetN % 3);

        for (var i = 0; i < doc.Entries.Count; i++)
        {
            Assert.Equal(i + 1, doc.Entries[i].Index);
            if (i > 0)
            {
                Assert.True(doc.Entries[i].Prime > doc.Entries[i - 1].Prime);
            }
        }
    }

    [Fact]
    public void Even_multiplier_keeps_prime_two_special_case()
    {
        var doc = Generate("1000036000099", bound: 50, multiplier: 2).FactorBase!;

        Assert.Equal(2, doc.Entries[0].Prime);
        Assert.Equal(0, doc.Entries[0].Root1);
        Assert.Equal(0, doc.Entries[0].Root2);
    }

    [Fact]
    public void Odd_entries_have_valid_modular_roots()
    {
        var doc = Generate("1000036000099").FactorBase!;
        var scaledN = doc.Metadata.ScaledN;

        foreach (var e in doc.Entries.Where(e => e.Prime != 2))
        {
            Assert.True(e.Root1 <= e.Root2);
            if (scaledN % e.Prime == 0)
            {
                Assert.Equal(0, e.Root1);
                Assert.Equal(0, e.Root2);
                Assert.Equal(BigInteger.Zero, doc.Metadata.Multiplier % e.Prime);
                Assert.NotEqual(BigInteger.Zero, doc.Metadata.TargetN % e.Prime);
                continue;
            }

            Assert.Equal(e.Prime - e.Root1, e.Root2);
            var r2 = (BigInteger)e.Root1 * e.Root1 % e.Prime;
            Assert.Equal(IntegerMath.Mod(scaledN, e.Prime), r2);
            Assert.Equal(1, NumberTheory.Legendre(scaledN, e.Prime));
        }
    }

    [Fact]
    public void Respects_explicit_multiplier_and_bound()
    {
        var doc = Generate("1000036000099", bound: 500, multiplier: 1).FactorBase!;
        Assert.Equal(BigInteger.One, doc.Metadata.Multiplier);
        Assert.Equal(500, doc.Metadata.Bound);
        Assert.All(doc.Entries, e => Assert.True(e.Prime <= 500));
    }
}
