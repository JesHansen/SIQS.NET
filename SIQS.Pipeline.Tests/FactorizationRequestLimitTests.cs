using System.Numerics;
using Factorbase;
using SIQS.Pipeline;

namespace SIQS.Pipeline.Tests;

public sealed class FactorizationRequestLimitTests
{
    [Fact]
    public void Automatic_polynomial_supply_is_clamped_to_the_supported_limit()
    {
        var target = BigInteger.Parse(
            "2240967764550868034903762499830846134580436579500576485005611267821363");
        var factorBase = FactorBaseGenerator.Generate(new FactorBaseOptions(target)).FactorBase!;

        var resolved = SievingParameterResolver.Resolve(new FactorizationRequest(target), factorBase);

        Assert.Equal(FactorizationRequestLimits.MaxPolynomialCount, resolved.PolynomialCount);
    }

    [Fact]
    public void Every_scalar_limit_accepts_its_boundary_and_rejects_one_over()
    {
        var pipeline = new SiqsPipeline(new FakePhaseExecutor());
        var cases = new (string Field, FactorizationRequest AtLimit, FactorizationRequest Over)[]
        {
            ("TargetN", Request(BigInteger.Parse(new string('9', FactorizationRequestLimits.MaxTargetDigits))),
                Request(BigInteger.Parse(new string('9', FactorizationRequestLimits.MaxTargetDigits + 1)))),
            ("FactorBaseBound", Request() with { FactorBase = new FactorBaseRunOptions { Bound = FactorizationRequestLimits.MaxFactorBaseBound } },
                Request() with { FactorBase = new FactorBaseRunOptions { Bound = FactorizationRequestLimits.MaxFactorBaseBound + 1 } }),
            ("Multiplier", Request() with { FactorBase = new FactorBaseRunOptions { Multiplier = FactorizationRequestLimits.MaxMultiplier } },
                Request() with { FactorBase = new FactorBaseRunOptions { Multiplier = FactorizationRequestLimits.MaxMultiplier + 1 } }),
            ("SieveHalfInterval", WithSieving(s => s with { HalfInterval = FactorizationRequestLimits.MaxSieveHalfInterval }),
                WithSieving(s => s with { HalfInterval = FactorizationRequestLimits.MaxSieveHalfInterval + 1 })),
            ("PolynomialCount", WithSieving(s => s with { PolynomialCount = FactorizationRequestLimits.MaxPolynomialCount }),
                WithSieving(s => s with { PolynomialCount = FactorizationRequestLimits.MaxPolynomialCount + 1 })),
            ("RelationTarget", WithSieving(s => s with { RelationTarget = FactorizationRequestLimits.MaxRelationTarget }),
                WithSieving(s => s with { RelationTarget = FactorizationRequestLimits.MaxRelationTarget + 1 })),
            ("LargePrimeBound", WithSieving(s => s with { LargePrimeBound = FactorizationRequestLimits.MaxLargePrimeBound }),
                WithSieving(s => s with { LargePrimeBound = FactorizationRequestLimits.MaxLargePrimeBound + 1 })),
            ("LargePrime2Bound", WithSieving(s => s with { LargePrime2Bound = FactorizationRequestLimits.MaxLargePrime2Bound }),
                WithSieving(s => s with { LargePrime2Bound = FactorizationRequestLimits.MaxLargePrime2Bound + 1 })),
            ("SieveErrorMargin", WithSieving(s => s with { ErrorMargin = FactorizationRequestLimits.MaxErrorMargin }),
                WithSieving(s => s with { ErrorMargin = FactorizationRequestLimits.MaxErrorMargin + 1 })),
            ("OutputBatchSize", WithSieving(s => s with { OutputBatchSize = FactorizationRequestLimits.MaxOutputBatchSize }),
                WithSieving(s => s with { OutputBatchSize = FactorizationRequestLimits.MaxOutputBatchSize + 1 })),
            ("APrimeCount", WithSieving(s => s with { APrimeCount = FactorizationRequestLimits.MaxAPrimeCount }),
                WithSieving(s => s with { APrimeCount = FactorizationRequestLimits.MaxAPrimeCount + 1 })),
            ("APrimeWindowSize", WithSieving(s => s with { APrimeWindowSize = FactorizationRequestLimits.MaxAPrimeWindowSize }),
                WithSieving(s => s with { APrimeWindowSize = FactorizationRequestLimits.MaxAPrimeWindowSize + 1 })),
            ("SievingParallelism", WithSieving(s => s with { Parallelism = FactorizationRequestLimits.MaxParallelism }),
                WithSieving(s => s with { Parallelism = FactorizationRequestLimits.MaxParallelism + 1 })),
            ("SieveBlockSize", WithSieving(s => s with { BlockSize = FactorizationRequestLimits.MaxSieveBlockSize }),
                WithSieving(s => s with { BlockSize = FactorizationRequestLimits.MaxSieveBlockSize + 1 })),
            ("BucketLargePrimeCutoff", WithSieving(s => s with { BucketLargePrimeCutoff = FactorizationRequestLimits.MaxPrimeCutoff }),
                WithSieving(s => s with { BucketLargePrimeCutoff = FactorizationRequestLimits.MaxPrimeCutoff + 1 })),
            ("ResieveLargePrimeCutoff", WithSieving(s => s with
                {
                    BucketLargePrimeCutoff = FactorizationRequestLimits.MaxPrimeCutoff,
                    ResieveLargePrimeCutoff = FactorizationRequestLimits.MaxPrimeCutoff - 1,
                }), WithSieving(s => s with
                {
                    BucketLargePrimeCutoff = FactorizationRequestLimits.MaxPrimeCutoff,
                    ResieveLargePrimeCutoff = FactorizationRequestLimits.MaxPrimeCutoff + 1,
                })),
            ("LinearAlgebraMaxDependencies", WithLinearAlgebra(l => l with { MaxDependencies = FactorizationRequestLimits.MaxDependencies }),
                WithLinearAlgebra(l => l with { MaxDependencies = FactorizationRequestLimits.MaxDependencies + 1 })),
            ("LinearAlgebraParallelism", WithLinearAlgebra(l => l with { Parallelism = FactorizationRequestLimits.MaxParallelism }),
                WithLinearAlgebra(l => l with { Parallelism = FactorizationRequestLimits.MaxParallelism + 1 })),
        };

        foreach (var testCase in cases)
        {
            _ = pipeline.NormalizeAndValidate(testCase.AtLimit);
            var exception = Assert.Throws<FactorizationRequestValidationException>(() =>
                pipeline.NormalizeAndValidate(testCase.Over));
            Assert.Contains(exception.Issues, issue => issue.Field == testCase.Field);
        }
    }

    [Fact]
    public void Important_invalid_combinations_are_reported_together()
    {
        var pipeline = new SiqsPipeline(new FakePhaseExecutor());
        var request = Request() with
        {
            Sieving = new SievingRunOptions
            {
                HalfInterval = 100,
                BlockSize = 202,
                APrimeCount = 8,
                APrimeWindowSize = 7,
                LargePrime2Bound = 1_000,
                LargePrime2ThresholdBound = 1_001,
                ResieveLargePrimeCutoff = 100,
            },
        };

        var exception = Assert.Throws<FactorizationRequestValidationException>(() =>
            pipeline.NormalizeAndValidate(request));

        Assert.Contains(exception.Issues, issue => issue.Field == "SieveBlockSize");
        Assert.Contains(exception.Issues, issue => issue.Field == "APrimeCount");
        Assert.Contains(exception.Issues, issue => issue.Field == "LargePrime2ThresholdBound");
        Assert.Contains(exception.Issues, issue => issue.Field == "ResieveLargePrimeCutoff");
    }

    private static FactorizationRequest Request(BigInteger? target = null) => new(target ?? 91);

    private static FactorizationRequest WithSieving(Func<SievingRunOptions, SievingRunOptions> mutate) =>
        Request() with { Sieving = mutate(new SievingRunOptions()) };

    private static FactorizationRequest WithLinearAlgebra(
        Func<LinearAlgebraRunOptions, LinearAlgebraRunOptions> mutate) =>
        Request() with { LinearAlgebra = mutate(new LinearAlgebraRunOptions()) };
}
