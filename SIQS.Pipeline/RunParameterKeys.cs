namespace SIQS.Pipeline;

/// <summary>
/// The persisted run-parameter keys written to and read from a job's <c>parameters</c> map in
/// <c>job.json</c>. These string values are part of the on-disk schema and must not change; the
/// named constants exist so every producer and consumer references one declaration.
/// </summary>
internal static class RunParameterKeys
{
    public const string TargetN = "target_n";
    public const string FactorBaseBound = "factor_base_bound";
    public const string Multiplier = "multiplier";
    public const string SieveHalfInterval = "sieve_half_interval";
    public const string PolynomialCount = "polynomial_count";
    public const string RelationTarget = "relation_target";
    public const string LargePrimeBound = "large_prime_bound";
    public const string LargePrime2Bound = "large_prime2_bound";
    public const string LargePrime2ThresholdBound = "large_prime2_threshold_bound";
    public const string CofactorSplitter = "cofactor_splitter";
    public const string TwoLargePrimes = "two_large_primes";
    public const string SieveErrorMargin = "sieve_error_margin";
    public const string OutputBatchSize = "output_batch_size";
    public const string APrimeCount = "a_prime_count";
    public const string APrimeWindowSize = "a_prime_window_size";
    public const string SievingParallelism = "sieving_parallelism";
    public const string SieveBlockSize = "sieve_block_size";
    public const string BucketLargePrimeCutoff = "bucket_large_prime_cutoff";
    public const string ResieveLargePrimeCutoff = "resieve_large_prime_cutoff";
    public const string TrialSievePercent = "trial_sieve_percent";
    public const string LinearAlgebraMaxDependencies = "linear_algebra_max_dependencies";
    public const string LinearAlgebraParallelism = "linear_algebra_parallelism";
    public const string ContinueSquareRootAfterFactor = "continue_square_root_after_factor";
    public const string AllowTinyInputTrialDivision = "allow_tiny_input_trial_division";

    /// <summary>The complete persisted key set, for schema-compatibility assertions.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        TargetN, FactorBaseBound, Multiplier, SieveHalfInterval, PolynomialCount, RelationTarget,
        LargePrimeBound, LargePrime2Bound, LargePrime2ThresholdBound, CofactorSplitter, TwoLargePrimes,
        SieveErrorMargin, OutputBatchSize, APrimeCount, APrimeWindowSize, SievingParallelism,
        SieveBlockSize, BucketLargePrimeCutoff, ResieveLargePrimeCutoff, TrialSievePercent,
        LinearAlgebraMaxDependencies, LinearAlgebraParallelism, ContinueSquareRootAfterFactor,
        AllowTinyInputTrialDivision,
    ];
}
