using Factorbase;
using QsSieve;
using Sieving;
using SIQS.Contracts.Cli;
using SIQS.Contracts.Files;

namespace QS.Sieve.Tests;

/// <summary>
/// <c>qs-sieve --cofactor-splitter</c> must reach the sieve configuration, and an unknown value must
/// fail before any sieving work starts.
/// </summary>
public sealed class CofactorSplitterOptionTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("qs-sieve-splitter-").FullName;
    private readonly string _factorBasePath;

    public CofactorSplitterOptionTests()
    {
        var factorBase = FactorBaseGenerator.Generate(new FactorBaseOptions(1022117, 1000, 1)).FactorBase!;
        _factorBasePath = Path.Combine(_directory, "factor_base.txt");
        File.WriteAllText(_factorBasePath, FactorBaseFile.Write(factorBase));
    }

    [Fact]
    public void Unknown_splitter_is_rejected_before_sieving()
    {
        var outDir = Path.Combine(_directory, "out");

        var error = Assert.Throws<ArgumentException>(() => Execute(outDir, "--cofactor-splitter", "definitely-invalid"));

        Assert.Contains("definitely-invalid", error.Message);
        Assert.False(Directory.Exists(outDir));
    }

    [Theory]
    [InlineData("squfof", CofactorSplitterKind.Squfof)]
    [InlineData("squfof-rho", CofactorSplitterKind.SqufofRho)]
    [InlineData("micro-ecm-squfof", CofactorSplitterKind.MicroEcmSqufof)]
    [InlineData("micro-ecm-stage2", CofactorSplitterKind.MicroEcmStage2)]
    public void Every_documented_token_reaches_the_sieve_configuration(string token, CofactorSplitterKind expected)
    {
        var result = Execute(Path.Combine(_directory, token), "--cofactor-splitter", token);

        Assert.Equal(expected, result.Parameters.CofactorSplitter);
    }

    [Fact]
    public void Omitted_splitter_matches_the_pipeline_policy()
    {
        var factorBase = FactorBaseFile.Parse(File.ReadAllText(_factorBasePath));
        var defaults = SievingParameters.Default(factorBase);

        var result = Execute(Path.Combine(_directory, "auto"));

        Assert.Equal(
            CofactorSplitterKinds.AutoSelect(defaults.CofactorSplitter, defaults.LargePrime2Bound),
            result.Parameters.CofactorSplitter);
    }

    [Theory]
    [InlineData("micro-ecm-stage2", true)]
    [InlineData("squfof-rho", false)]
    public void Two_large_prime_sieving_runs_the_selected_splitter(string token, bool usesMicroEcm)
    {
        var result = Execute(Path.Combine(_directory, token), "--cofactor-splitter", token,
            "--two-large-primes", "true", "--polynomial-count", "200", "--sieve-half-interval", "20000");

        Assert.True(result.Counters.TwoLargePrimeSplitAttempts > 0);
        Assert.Equal(usesMicroEcm, result.Counters.CofactorMicroEcmAttempts > 0);
    }

    private SievingCommandResult Execute(string outDir, params string[] extra)
        => new SievingCommandHandler().Execute(
            CommandLine.Parse(
                [
                    "--factor-base", _factorBasePath, "--out-dir", outDir, "--polynomial-count", "1",
                    "--relations-target", "1", "--sieve-half-interval", "128", "--parallelism", "1", .. extra,
                ],
                CommandLineSyntax.Strict),
            progress: null);

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
