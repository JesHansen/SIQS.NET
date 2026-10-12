using System.Numerics;
using Factorbase;
using Sieving;

namespace Sieving.Tests;

/// <summary>
/// A-set selection must stay within an explicit work budget on every path: exhaustive enumeration
/// only below <see cref="PolynomialGenerator.ExhaustiveCombinationLimit"/>, and sampling to a
/// target it can actually reach. These tests inspect the decision without running a huge path.
/// </summary>
public class PolynomialSelectionBudgetTests
{
    private static readonly SievingParameters HugeRequest = new(
        SieveHalfInterval: 128,
        PolynomialCount: 1_000_000_000,
        RelationTarget: 1,
        LargePrimeBound: 2_000,
        ErrorMargin: 0,
        OutputBatchSize: 100,
        APrimeCount: 3,
        APrimeWindowSize: 707,
        Parallelism: 1);

    [Fact]
    public void Wide_window_with_a_huge_polynomial_request_does_not_enumerate()
    {
        Assert.Equal(58_649_185, PolynomialGenerator.CombinationSpace(707, 3));

        Assert.False(PolynomialGenerator.ShouldEnumerate(707, 3));
    }

    [Theory]
    [InlineData(64, 3)]
    [InlineData(117, 3)]
    [InlineData(118, 3)]
    [InlineData(725, 2)]
    [InlineData(1024, 2)]
    [InlineData(1024, 10)]
    public void Enumeration_is_chosen_only_within_the_exhaustive_limit(int window, int s)
    {
        Assert.Equal(
            PolynomialGenerator.CombinationSpace(window, s) <= PolynomialGenerator.ExhaustiveCombinationLimit,
            PolynomialGenerator.ShouldEnumerate(window, s));
    }

    [Theory]
    [InlineData(707, 3)]
    [InlineData(100, 4)]
    [InlineData(1024, 2)]
    [InlineData(80, 5)]
    public void Sampling_target_is_bounded_and_leaves_reachable_headroom(int window, int s)
    {
        var target = PolynomialGenerator.SampledCandidateTarget(HugeRequest with { APrimeCount = s }, s, window);

        Assert.InRange(target, 1, PolynomialGenerator.MaxSampledCandidates);
        Assert.True(target <= Math.Max(1, PolynomialGenerator.CombinationSpace(window, s - 1) / 2));
    }

    [Fact]
    public void Unsaturated_sampling_target_is_unchanged_by_the_reachable_cap()
    {
        var parameters = HugeRequest with { APrimeCount = 5, APrimeWindowSize = 80 };

        Assert.Equal(
            PolynomialGenerator.MaxSampledCandidates,
            PolynomialGenerator.SampledCandidateTarget(parameters, 5, 80));
    }

    [Fact]
    public void Near_saturated_sampling_reaches_its_target_deterministically()
    {
        var fb = WideFactorBase();
        var parameters = HugeRequest with { APrimeCount = 4, APrimeWindowSize = 100 };
        Assert.False(PolynomialGenerator.ShouldEnumerate(100, 4));

        var first = PolynomialGenerator.SelectAPositions(fb, parameters);
        var second = PolynomialGenerator.SelectAPositions(fb, parameters);

        // Reaching the target exactly shows the sampler stopped on success, not on its attempt cap.
        Assert.Equal(PolynomialGenerator.SampledCandidateTarget(parameters, 4, 100), first.Count);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Small_window_enumerates_every_combination_in_a_stable_order()
    {
        var fb = WideFactorBase();
        var parameters = HugeRequest with { APrimeCount = 3, APrimeWindowSize = 24 };

        var first = PolynomialGenerator.SelectAPositions(fb, parameters);

        Assert.True(PolynomialGenerator.ShouldEnumerate(24, 3));
        Assert.Equal(PolynomialGenerator.CombinationSpace(24, 3), first.Count);
        Assert.Equal(first, PolynomialGenerator.SelectAPositions(fb, parameters));
    }

    private static FactorBaseData WideFactorBase()
        => FactorBaseData.From(FactorBaseGenerator.Generate(new FactorBaseOptions(
            (BigInteger.Pow(2, 127) - 1) * (BigInteger.Pow(2, 89) - 1), 5_000, 1)).FactorBase!);
}
