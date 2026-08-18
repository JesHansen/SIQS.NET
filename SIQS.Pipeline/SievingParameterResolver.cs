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
                ?? Math.Min(
                    FactorizationRequestLimits.MaxPolynomialCount,
                    SievingParameters.AvailablePolynomialSupply(parameters.APrimeWindowSize, parameters.APrimeCount)),
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
        ValidateSmallPrimeVariationConfiguration(parameters);
        FactorizationRequestValidator.NormalizeAndValidate(request with
        {
            Sieving = new SievingRunOptions
            {
                HalfInterval = parameters.SieveHalfInterval,
                PolynomialCount = parameters.PolynomialCount,
                RelationTarget = parameters.RelationTarget,
                LargePrimeBound = parameters.LargePrimeBound,
                ErrorMargin = parameters.ErrorMargin,
                OutputBatchSize = parameters.OutputBatchSize,
                APrimeCount = parameters.APrimeCount,
                APrimeWindowSize = parameters.APrimeWindowSize,
                Parallelism = parameters.Parallelism,
                // An automatic block may intentionally exceed a tiny interval; the worker clamps
                // it to the interval. Only an explicit block/interval pair is rejected as wasteful.
                BlockSize = request.Sieving.BlockSize is null ? null : parameters.SieveBlockSize,
                BucketLargePrimeCutoff = parameters.BucketLargePrimeCutoff,
                ResieveLargePrimeCutoff = parameters.ResieveLargePrimeCutoff,
                EnableTwoLargePrimes = parameters.EnableTwoLargePrimes,
                LargePrime2Bound = parameters.LargePrime2Bound,
                LargePrime2ThresholdBound = parameters.LargePrime2ThresholdBound,
                CofactorSplitter = CofactorSplitterKinds.ToToken(parameters.CofactorSplitter),
            },
        });
        return parameters;
    }

    private static void ValidateSmallPrimeVariationConfiguration(SievingParameters parameters)
    {
        var bound = parameters.SmallPrimeVariationBound;
        if (bound == 0)
        {
            return;
        }

        if (parameters.BucketLargePrimeCutoff > 0 &&
            bound >= parameters.BucketLargePrimeCutoff)
        {
            throw new InvalidOperationException(
                "The small-prime-variation bound must be below the bucket cutoff.");
        }

        if (parameters.ResieveLargePrimeCutoff > 0 &&
            bound >= parameters.ResieveLargePrimeCutoff)
        {
            throw new InvalidOperationException(
                "The small-prime-variation bound must be below the resieve cutoff.");
        }
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
