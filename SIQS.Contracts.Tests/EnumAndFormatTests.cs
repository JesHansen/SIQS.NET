namespace SIQS.Contracts.Tests;

public class FileFormatsTests
{
    [Fact]
    public void Version_constants_match_spec_strings()
    {
        Assert.Equal("siqs-factor-base-v1", FileFormats.FactorBaseV1);
        Assert.Equal("siqs-raw-relations-v1", FileFormats.RawRelationsV1);
        Assert.Equal("siqs-raw-partials-v1", FileFormats.RawPartialsV1);
        Assert.Equal("siqs-filtered-relations-v1", FileFormats.FilteredRelationsV1);
        Assert.Equal("siqs-filtered-matrix-v1", FileFormats.FilteredMatrixV1);
        Assert.Equal("siqs-matrix-meta-v1", FileFormats.MatrixMetaV1);
        Assert.Equal("siqs-dependencies-v1", FileFormats.DependenciesV1);
        Assert.Equal("siqs-factors-v1", FileFormats.FactorsV1);
    }
}

public class EnumTokenTests
{
    [Theory]
    [InlineData(SiqsPhase.Pipeline, "pipeline")]
    [InlineData(SiqsPhase.FactorBase, "factor_base")]
    [InlineData(SiqsPhase.Sieving, "sieving")]
    [InlineData(SiqsPhase.Filtering, "filtering")]
    [InlineData(SiqsPhase.LinearAlgebra, "linear_algebra")]
    [InlineData(SiqsPhase.SquareRoot, "square_root")]
    [InlineData(SiqsPhase.Ui, "ui")]
    public void Phase_tokens_match_spec(SiqsPhase phase, string token)
    {
        Assert.Equal(token, SiqsTokens.ToToken(phase));
        Assert.Equal(phase, SiqsTokens.Parse<SiqsPhase>(token));
    }

    [Theory]
    [InlineData(RelationKind.Full, "full")]
    [InlineData(RelationKind.Partial, "partial")]
    [InlineData(RelationKind.CombinedPartial, "combined_partial")]
    public void RelationKind_tokens_match_spec(RelationKind kind, string token)
    {
        Assert.Equal(token, SiqsTokens.ToToken(kind));
        Assert.Equal(kind, SiqsTokens.Parse<RelationKind>(token));
    }

    [Theory]
    [InlineData(FactorizationStatus.FactorFound, "factor_found")]
    [InlineData(FactorizationStatus.Trivial, "trivial")]
    [InlineData(FactorizationStatus.Invalid, "invalid")]
    [InlineData(FactorizationStatus.NoFactor, "no_factor")]
    [InlineData(FactorizationStatus.InputPrime, "input_prime")]
    public void FactorizationStatus_tokens_match_spec(FactorizationStatus status, string token)
    {
        Assert.Equal(token, SiqsTokens.ToToken(status));
        Assert.Equal(status, SiqsTokens.Parse<FactorizationStatus>(token));
    }

    [Theory]
    [InlineData(JobStatus.CompletedNoFactor, "completed_no_factor")]
    [InlineData(JobStatus.CompletedPrime, "completed_prime")]
    [InlineData(JobStatus.CompletedFactorFound, "completed_factor_found")]
    [InlineData(JobStatus.CompletedTrivialFactor, "completed_trivial_factor")]
    [InlineData(JobStatus.Canceling, "canceling")]
    public void JobStatus_tokens_match_spec(JobStatus status, string token)
    {
        Assert.Equal(token, SiqsTokens.ToToken(status));
        Assert.Equal(status, SiqsTokens.Parse<JobStatus>(token));
    }

    [Fact]
    public void Parse_is_case_insensitive_and_throws_on_unknown()
    {
        Assert.Equal(ProgressLevel.Warning, SiqsTokens.Parse<ProgressLevel>("WARNING"));
        Assert.Throws<FormatException>(() => SiqsTokens.Parse<ProgressLevel>("bogus"));
    }
}
