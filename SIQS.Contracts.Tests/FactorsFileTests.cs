using System.Numerics;
using SIQS.Contracts.Files;

namespace SIQS.Contracts.Tests;

public class FactorsFileTests
{
    [Fact]
    public void Writes_precheck_factor_row()
    {
        var doc = new FactorsDocument(
            TargetN: 77, Multiplier: 1, ScaledN: 77, DependencyCount: 0,
            Results: new[]
            {
                FactorResultRecord.FactorFound(
                    "precheck", targetN: 77, gcdMinus: null, gcdPlus: null,
                    factor1: 7, factor2: 11, reason: "small_prime_factor"),
            });

        var lines = FactorsFile.Write(doc).Split('\n');

        Assert.Equal("# format=siqs-factors-v1", lines[0]);
        Assert.Equal("# target_n=77", lines[1]);
        Assert.Equal("# multiplier=1", lines[2]);
        Assert.Equal("# scaled_n=77", lines[3]);
        Assert.Equal("# dependency_count=0", lines[4]);
        Assert.Equal(
            "# columns=dependency_id,status,gcd_minus,gcd_plus,factor1,factor2,reason,factor1_composite,factor2_composite",
            lines[5]);
        Assert.Equal(
            "dependency_id,status,gcd_minus,gcd_plus,factor1,factor2,reason,factor1_composite,factor2_composite",
            lines[6]);
        Assert.Equal("precheck,factor_found,,,7,11,small_prime_factor,false,false", lines[7]);
    }

    [Fact]
    public void Writes_dependency_factor_row()
    {
        var doc = new FactorsDocument(77, 1, 77, 1, new[]
        {
            new FactorResultRecord("0", FactorizationStatus.FactorFound, 7, 11, 7, 11, Reason: null),
        });

        var line = FactorsFile.Write(doc).Split('\n')[7];
        Assert.Equal("0,factor_found,7,11,7,11,,,", line);
    }

    [Fact]
    public void Marks_composite_factor_when_N_has_three_prime_factors()
    {
        // N = 5 * 7 * 11 = 385. A GCD step can legitimately land on the single prime 5,
        // leaving factor2 = 77 = 7 * 11 composite; nothing about the GCD computation itself
        // reveals that, so it must be checked explicitly.
        var row = FactorResultRecord.FactorFound(
            "0", targetN: 385, gcdMinus: 5, gcdPlus: 77, factor1: 5, factor2: 77);

        Assert.False(row.Factor1IsComposite);
        Assert.True(row.Factor2IsComposite);

        var line = FactorsFile.Write(new FactorsDocument(385, 1, 385, 1, new[] { row })).Split('\n')[7];
        Assert.Equal("0,factor_found,5,77,5,77,,false,true", line);
    }

    [Fact]
    public void FactorFound_throws_when_factors_do_not_multiply_to_N()
    {
        Assert.Throws<InvalidOperationException>(
            () => FactorResultRecord.FactorFound("0", targetN: 77, gcdMinus: null, gcdPlus: null, factor1: 7, factor2: 12));
    }

    [Fact]
    public void Round_trips_mixed_statuses()
    {
        var doc = new FactorsDocument(1000003, 3, 3000009, 2, new[]
        {
            new FactorResultRecord("0", FactorizationStatus.Trivial, 1, 123456789, null, null, "x_equals_y_or_negative_y"),
            new FactorResultRecord("1", FactorizationStatus.FactorFound, 123457, 765431, 123457, 765431, null),
        });

        var parsed = FactorsFile.Parse(FactorsFile.Write(doc));

        Assert.Equal(new BigInteger(1000003), parsed.TargetN);
        Assert.Equal(2, parsed.DependencyCount);
        Assert.Equal(doc.Results, parsed.Results);
    }

    [Fact]
    public void FindsFirstFactorFound_returns_factor_row()
    {
        var doc = new FactorsDocument(77, 1, 77, 1, new[]
        {
            new FactorResultRecord("0", FactorizationStatus.NoFactor, 1, 1, null, null, "no_factor"),
            new FactorResultRecord("1", FactorizationStatus.FactorFound, 7, 11, 7, 11, null),
        });

        var found = doc.Results.FirstOrDefault(r => r.Status == FactorizationStatus.FactorFound);
        Assert.NotNull(found);
        Assert.Equal(new BigInteger(7), found!.Factor1);
    }
}
