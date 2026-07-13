using System.Numerics;

namespace SIQS.Pipeline;

/// <summary>Factor-base phase settings. Null values are filled from deterministic defaults by the pipeline.</summary>
public sealed record FactorBaseRunOptions
{
    public long? Bound { get; init; }
    public BigInteger? Multiplier { get; init; }
    public bool? AllowTinyInputTrialDivision { get; init; }
}

/// <summary>Sieving phase settings. Null values are filled from deterministic defaults by the pipeline.</summary>
public sealed record SievingRunOptions
{
    public long? HalfInterval { get; init; }
    public long? PolynomialCount { get; init; }
    public int? RelationTarget { get; init; }
    public long? LargePrimeBound { get; init; }
    public int? ErrorMargin { get; init; }
    public int? OutputBatchSize { get; init; }
    public int? APrimeCount { get; init; }
    public int? APrimeWindowSize { get; init; }
    public int? Parallelism { get; init; }
    public int? BlockSize { get; init; }
    public int? BucketLargePrimeCutoff { get; init; }
    public int? ResieveLargePrimeCutoff { get; init; }
    public bool? EnableTwoLargePrimes { get; init; }
    public long? LargePrime2Bound { get; init; }
    public long? LargePrime2ThresholdBound { get; init; }
    public string? CofactorSplitter { get; init; }

    /// <summary>
    /// The cross-field consistency rule owned by this group: the two-large-prime residual threshold
    /// must not exceed the two-large-prime relation bound.
    /// </summary>
    public void EnsureConsistent()
    {
        if (LargePrime2ThresholdBound is { } threshold &&
            LargePrime2Bound is { } bound &&
            threshold > bound)
        {
            throw new ArgumentOutOfRangeException(
                nameof(LargePrime2ThresholdBound),
                "Value must not exceed LargePrime2Bound.");
        }
    }
}

/// <summary>Linear-algebra phase settings. Null values are filled from deterministic defaults by the pipeline.</summary>
public sealed record LinearAlgebraRunOptions
{
    public int? MaxDependencies { get; init; }
    public int? Parallelism { get; init; }
}

/// <summary>Square-root phase settings.</summary>
public sealed record SquareRootRunOptions
{
    public bool ContinueAfterFactor { get; init; }
}
