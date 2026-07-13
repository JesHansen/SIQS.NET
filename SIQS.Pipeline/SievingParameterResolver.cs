using Sieving;
using SIQS.Contracts.Files;

namespace SIQS.Pipeline;

/// <summary>Builds the effective sieving configuration from pipeline defaults and request overrides.</summary>
public static class SievingParameterResolver
{
    public static SievingParameters Resolve(FactorizationRequest request, FactorBaseDocument factorBase)
    {
        var sieving = request.Sieving;
        var defaults = SievingParameters.Default(factorBase);
        var parameters = defaults with
        {
            SieveHalfInterval = sieving.HalfInterval ?? defaults.SieveHalfInterval,
            RelationTarget = sieving.RelationTarget ?? defaults.RelationTarget,
            LargePrimeBound = sieving.LargePrimeBound ?? defaults.LargePrimeBound,
            ErrorMargin = sieving.ErrorMargin ?? defaults.ErrorMargin,
            OutputBatchSize = sieving.OutputBatchSize ?? defaults.OutputBatchSize,
            APrimeCount = sieving.APrimeCount ?? defaults.APrimeCount,
            APrimeWindowSize = sieving.APrimeWindowSize ?? defaults.APrimeWindowSize,
            Parallelism = sieving.Parallelism ?? defaults.Parallelism,
            SieveBlockSize = sieving.BlockSize ?? defaults.SieveBlockSize,
            BucketLargePrimeCutoff = sieving.BucketLargePrimeCutoff ?? defaults.BucketLargePrimeCutoff,
            ResieveLargePrimeCutoff = sieving.ResieveLargePrimeCutoff ?? defaults.ResieveLargePrimeCutoff,
            EnableTwoLargePrimes = sieving.EnableTwoLargePrimes ?? defaults.EnableTwoLargePrimes,
            LargePrime2Bound = sieving.LargePrime2Bound ?? defaults.LargePrime2Bound,
            LargePrime2ThresholdBound = sieving.LargePrime2ThresholdBound ?? defaults.LargePrime2ThresholdBound,
        };

        parameters = parameters with
        {
            PolynomialCount = sieving.PolynomialCount
                ?? SievingParameters.AvailablePolynomialSupply(parameters.APrimeWindowSize, parameters.APrimeCount),
            // Honor an explicit CLI/persisted token (the request validator has already rejected any
            // unrecognized value); otherwise auto-select from the effective LP2 bound so an overridden
            // bound above 2³² still gets the rho fallback it needs for > 64-bit residuals.
            CofactorSplitter = sieving.CofactorSplitter is { } token && CofactorSplitterKinds.TryParse(token, out var kind)
                ? kind
                : CofactorSplitterKinds.SelectFor(parameters.LargePrime2Bound),
            LargePrime2ThresholdBound = sieving.LargePrime2ThresholdBound is null
                ? Math.Min(parameters.LargePrime2ThresholdBound, parameters.LargePrime2Bound)
                : parameters.LargePrime2ThresholdBound,
            TrialRawRelationTarget = request.TrialSievePercent is { } percent
                ? Math.Max(1, (int)Math.Ceiling(parameters.RelationTarget * percent / 100.0))
                : null,
        };

        ValidateTwoLargePrimeConfiguration(parameters);
        return parameters;
    }

    private static void ValidateTwoLargePrimeConfiguration(SievingParameters parameters)
    {
        if (!parameters.EnableTwoLargePrimes)
        {
            return;
        }

        if (parameters.LargePrime2Bound < 1)
        {
            throw new InvalidOperationException("Two-large-prime sieving requires a positive LP2 bound.");
        }

        if (parameters.LargePrime2ThresholdBound < 1)
        {
            throw new InvalidOperationException("Two-large-prime sieving requires a positive LP2 threshold bound.");
        }

        if (parameters.LargePrime2ThresholdBound > parameters.LargePrime2Bound)
        {
            throw new InvalidOperationException("The LP2 threshold bound must not exceed the LP2 relation bound.");
        }
    }
}
