using SIQS.UI.Services;

namespace SIQS.UI.Tests;

public sealed class RunParameterValidatorTests
{
    [Fact]
    public void Dependency_cap_above_one_lanczos_block_is_rejected()
    {
        var result = new RunParameterValidator().Validate(new RunParameterForm
        {
            TargetN = "91",
            LinearAlgebraMaxDependencies = "65",
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("at most 64", StringComparison.Ordinal));
    }
}
