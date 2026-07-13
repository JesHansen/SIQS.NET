using SIQS.UI.Services;

namespace SIQS.Pipeline.Tests;

public class RunParameterValidatorTests
{
    [Fact]
    public void Validate_maps_sieving_parallelism_and_block_size()
    {
        var validator = new RunParameterValidator();

        var outcome = validator.Validate(new RunParameterForm
        {
            TargetN = "1022117",
            SievingParallelism = "4",
            SieveBlockSize = "4096",
        });

        Assert.True(outcome.IsValid);
        Assert.NotNull(outcome.Request);
        Assert.Equal(4, outcome.Request.Sieving.Parallelism);
        Assert.Equal(4096, outcome.Request.Sieving.BlockSize);
    }

    [Fact]
    public void Validate_accepts_zero_for_sieving_parallelism_and_block_size()
    {
        var validator = new RunParameterValidator();

        var outcome = validator.Validate(new RunParameterForm
        {
            TargetN = "1022117",
            SievingParallelism = "0",
            SieveBlockSize = "0",
        });

        Assert.True(outcome.IsValid);
        Assert.NotNull(outcome.Request);
        Assert.Equal(0, outcome.Request.Sieving.Parallelism);
        Assert.Equal(0, outcome.Request.Sieving.BlockSize);
    }

    [Fact]
    public void Validate_rejects_negative_sieving_parallelism_and_block_size()
    {
        var validator = new RunParameterValidator();

        var outcome = validator.Validate(new RunParameterForm
        {
            TargetN = "1022117",
            SievingParallelism = "-1",
            SieveBlockSize = "-1",
        });

        Assert.False(outcome.IsValid);
        Assert.Null(outcome.Request);
        Assert.Contains("Sieving parallelism must be zero or a positive integer.", outcome.Errors);
        Assert.Contains("Sieve block size must be zero or a positive integer.", outcome.Errors);
    }

    [Fact]
    public void Validate_accepts_blank_positive_int_field_as_unset()
    {
        var validator = new RunParameterValidator();

        var outcome = validator.Validate(new RunParameterForm
        {
            TargetN = "1022117",
            RelationTarget = "   ",
        });

        Assert.True(outcome.IsValid);
        Assert.NotNull(outcome.Request);
        Assert.Null(outcome.Request.Sieving.RelationTarget);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("2147483647", int.MaxValue)]
    public void Validate_accepts_positive_int_values_in_range(string input, int expected)
    {
        var validator = new RunParameterValidator();

        var outcome = validator.Validate(new RunParameterForm
        {
            TargetN = "1022117",
            RelationTarget = input,
        });

        Assert.True(outcome.IsValid);
        Assert.NotNull(outcome.Request);
        Assert.Equal(expected, outcome.Request.Sieving.RelationTarget);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("2147483648")]           // int.MaxValue + 1
    [InlineData("9223372036854775808")]  // long.MaxValue + 1
    public void Validate_rejects_out_of_range_positive_int_without_wrapping(string input)
    {
        var validator = new RunParameterValidator();

        var outcome = validator.Validate(new RunParameterForm
        {
            TargetN = "1022117",
            RelationTarget = input,
        });

        Assert.False(outcome.IsValid);
        Assert.Null(outcome.Request);
        Assert.Contains("Relation target must be a positive integer.", outcome.Errors);
    }
}
