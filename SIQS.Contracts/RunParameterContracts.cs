using System.Numerics;

namespace SIQS.Contracts;

/// <summary>
/// The full set of effective run parameters for a factorization. Optional values are null until
/// filled from deterministic defaults by the pipeline.
/// </summary>
public sealed record RunParameterSet(
    BigInteger TargetN,
    string? RunDirectory = null,
    long? FactorBaseBound = null,
    BigInteger? Multiplier = null,
    long? SieveHalfInterval = null,
    long? PolynomialCount = null,
    int? RelationTarget = null,
    long? LargePrimeBound = null,
    int? SieveErrorMargin = null,
    int? OutputBatchSize = null,
    int? APrimeCount = null,
    int? APrimeWindowSize = null,
    int? LinearAlgebraMaxDependencies = null,
    bool ContinueSquareRootAfterFactor = false);
