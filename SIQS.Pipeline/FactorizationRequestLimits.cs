namespace SIQS.Pipeline;

/// <summary>
/// Supported request envelope. These are availability limits as well as representation limits:
/// defaults through the widest C110+ profile fit inside them.
/// </summary>
public static class FactorizationRequestLimits
{
    public const int MaxTargetDigits = 150;
    public const long MaxFactorBaseBound = 60_000_000;
    public const int MaxMultiplier = 1_000_000;
    public const long MaxSieveHalfInterval = 33_554_432;
    public const long MaxPolynomialCount = 1_000_000_000;
    public const int MaxRelationTarget = 5_000_000;
    public const long MaxLargePrimeBound = 64_000_000_000;
    public const long MaxLargePrime2Bound = 1_000_000_000_000;
    public const int MaxErrorMargin = 256;
    public const int MaxOutputBatchSize = 1_000_000;
    public const int MaxAPrimeCount = 16;
    public const int MaxAPrimeWindowSize = 1_024;
    public const int MaxParallelism = 256;
    public const int MaxSieveBlockSize = 4_194_304;
    public const int MaxPrimeCutoff = 60_000_000;
    public const int MaxDependencies = 64;
}

/// <summary>One field-specific normalized-request validation failure.</summary>
public sealed record FactorizationValidationIssue(string Field, string Message);

/// <summary>Aggregates all invalid fields so HTTP and UI callers can present structured errors.</summary>
public sealed class FactorizationRequestValidationException : ArgumentOutOfRangeException
{
    public FactorizationRequestValidationException(IReadOnlyList<FactorizationValidationIssue> issues)
        : base(issues[0].Field, issues[0].Message)
    {
        Issues = issues;
    }

    public IReadOnlyList<FactorizationValidationIssue> Issues { get; }
}
