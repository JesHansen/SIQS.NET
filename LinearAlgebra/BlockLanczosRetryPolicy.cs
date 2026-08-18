namespace LinearAlgebra;

/// <summary>
/// Defines the one-block dependency budget and the bounded retry policy. A new seed is attempted
/// only when the preceding run produced no verified dependency.
/// </summary>
internal static class BlockLanczosRetryPolicy
{
    public const int RetryLimit = 4;

    public static bool IsDisabled(int maximumDependencies)
    {
        if (maximumDependencies > BlockLanczos.MaximumDependencies)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDependencies), maximumDependencies,
                $"The dependency cap cannot exceed one {BlockLanczos.MaximumDependencies}-column Lanczos block.");
        }

        return maximumDependencies <= 0;
    }

    public static bool ShouldStartRun(int retry, int verifiedLanczosDependencies)
        => retry < RetryLimit && verifiedLanczosDependencies == 0;
}
