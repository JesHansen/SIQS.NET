using System.Numerics;
using Sieving;
using SIQS.Contracts;
using SIQS.Contracts.Files;
using SIQS.Pipeline;

namespace SIQS.Pipeline.Tests;

/// <summary>
/// The pipeline must run the splitter the library defaults document: the size-tuned default unless
/// a token is supplied, widened only when that default cannot split the LP2 bound's residuals.
/// </summary>
public sealed class CofactorSplitterResolutionTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "siqs-splitter-resolution", Guid.NewGuid().ToString("N"));

    [Fact]
    public void C110_default_resolves_to_the_documented_micro_ecm_strategy()
    {
        var factorBase = MetadataOnlyFactorBase(BigInteger.Pow(10, 109) + 1);

        var resolved = SievingParameterResolver.Resolve(new FactorizationRequest(factorBase.Metadata.TargetN), factorBase);

        Assert.Equal(SievingParameters.Default(factorBase).CofactorSplitter, resolved.CofactorSplitter);
        Assert.Equal(CofactorSplitterKind.MicroEcmStage2, resolved.CofactorSplitter);
        Assert.Equal(90_000_000, resolved.LargePrime2Bound);
    }

    [Theory]
    [InlineData("squfof", CofactorSplitterKind.Squfof)]
    [InlineData("squfof-rho", CofactorSplitterKind.SqufofRho)]
    [InlineData("micro-ecm-squfof", CofactorSplitterKind.MicroEcmSqufof)]
    [InlineData("micro-ecm-stage2", CofactorSplitterKind.MicroEcmStage2)]
    public void Explicit_tokens_are_authoritative(string token, CofactorSplitterKind expected)
    {
        var factorBase = MetadataOnlyFactorBase(BigInteger.Pow(10, 109) + 1);
        var request = new FactorizationRequest(factorBase.Metadata.TargetN)
        {
            Sieving = new SievingRunOptions { CofactorSplitter = token, LargePrime2Bound = 5_000_000_000 },
        };

        Assert.Equal(expected, SievingParameterResolver.Resolve(request, factorBase).CofactorSplitter);
    }

    [Theory]
    [InlineData(109, CofactorSplitterKind.MicroEcmStage2)] // the C110 default already handles wide residuals
    [InlineData(60, CofactorSplitterKind.SqufofRho)]       // the SQUFOF default must widen
    public void Wide_lp2_bound_keeps_a_capable_automatic_splitter(int exponent, CofactorSplitterKind expected)
    {
        var factorBase = MetadataOnlyFactorBase(BigInteger.Pow(10, exponent) + 1);
        var request = new FactorizationRequest(factorBase.Metadata.TargetN)
        {
            Sieving = new SievingRunOptions { EnableTwoLargePrimes = true, LargePrime2Bound = (long)uint.MaxValue + 1 },
        };

        var resolved = SievingParameterResolver.Resolve(request, factorBase);

        Assert.Equal(expected, resolved.CofactorSplitter);
        Assert.True(resolved.CofactorSplitter.SplitsWideResiduals());
    }

    [Theory]
    [InlineData(null, "squfof")]
    [InlineData("micro-ecm-stage2", "micro-ecm-stage2")]
    public async Task Sieving_reports_the_resolved_splitter(string? token, string expected)
    {
        var request = new FactorizationRequest(BigInteger.Parse("1022117"))
        {
            RunDirectory = _directory,
            FactorBase = new FactorBaseRunOptions { Bound = 1000, Multiplier = 1 },
            Sieving = new SievingRunOptions
            {
                HalfInterval = 20_000,
                APrimeCount = 2,
                APrimeWindowSize = 24,
                RelationTarget = 150,
                PolynomialCount = 200_000,
                EnableTwoLargePrimes = true,
                CofactorSplitter = token,
            },
        };
        var pipeline = new SiqsPipeline();

        await pipeline.RunAsync(request, null, CancellationToken.None);

        var sieving = pipeline.LoadJob(_directory).PhaseStates.Single(phase => phase.Phase == SiqsPhase.Sieving);
        Assert.Equal(expected, sieving.Counters["cofactor_splitter"]);
    }

    private static FactorBaseDocument MetadataOnlyFactorBase(BigInteger target)
        => new(new FactorBaseMetadata(target, 1, target, 60_000_000, 255.0 / Math.Log(60_000_000)),
            [new FactorBaseEntry(1, 2, 0, 0, 10)]);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
