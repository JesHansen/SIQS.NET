using System.Numerics;
using Factorbase;
using Sieving;

namespace SIQS.Pipeline;

/// <summary>Normalizes request-wide defaults and enforces the supported work envelope.</summary>
internal static class FactorizationRequestValidator
{
    public static FactorizationRequest NormalizeAndValidate(FactorizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var issues = new List<FactorizationValidationIssue>();
        var factorBase = request.FactorBase;
        var sieving = request.Sieving;
        var linearAlgebra = request.LinearAlgebra;

        AddIf(request.TargetN <= 1, nameof(request.TargetN), "TargetN must be greater than 1.");
        var targetDigits = BigInteger.Abs(request.TargetN).ToString().Length;
        AddIf(targetDigits > FactorizationRequestLimits.MaxTargetDigits, nameof(request.TargetN),
            $"TargetN cannot exceed {FactorizationRequestLimits.MaxTargetDigits} decimal digits.");

        var allowTinyInputTrialDivision = factorBase.AllowTinyInputTrialDivision
            ?? (factorBase.Bound is null && factorBase.Multiplier is null);
        var bound = factorBase.Bound ?? FactorBaseDefaults.DefaultBound(request.TargetN);
        Range(bound, 2, FactorizationRequestLimits.MaxFactorBaseBound, "FactorBaseBound");
        if (factorBase.Multiplier is { } multiplier)
        {
            Range(multiplier, BigInteger.One, new BigInteger(FactorizationRequestLimits.MaxMultiplier), "Multiplier");
        }

        OptionalRange(sieving.HalfInterval, 1, FactorizationRequestLimits.MaxSieveHalfInterval, "SieveHalfInterval");
        OptionalRange(sieving.PolynomialCount, 1, FactorizationRequestLimits.MaxPolynomialCount, "PolynomialCount");
        OptionalRange(sieving.RelationTarget, 1, FactorizationRequestLimits.MaxRelationTarget, "RelationTarget");
        OptionalRange(sieving.LargePrimeBound, 1, FactorizationRequestLimits.MaxLargePrimeBound, "LargePrimeBound");
        OptionalRange(sieving.LargePrime2Bound, 1, FactorizationRequestLimits.MaxLargePrime2Bound, "LargePrime2Bound");
        OptionalRange(sieving.LargePrime2ThresholdBound, 1, FactorizationRequestLimits.MaxLargePrime2Bound,
            "LargePrime2ThresholdBound");
        OptionalRange(sieving.ErrorMargin, 0, FactorizationRequestLimits.MaxErrorMargin, "SieveErrorMargin");
        OptionalRange(sieving.OutputBatchSize, 1, FactorizationRequestLimits.MaxOutputBatchSize, "OutputBatchSize");
        OptionalRange(sieving.APrimeCount, 1, FactorizationRequestLimits.MaxAPrimeCount, "APrimeCount");
        OptionalRange(sieving.APrimeWindowSize, 1, FactorizationRequestLimits.MaxAPrimeWindowSize, "APrimeWindowSize");
        OptionalRange(sieving.Parallelism, 0, FactorizationRequestLimits.MaxParallelism, "SievingParallelism");
        OptionalRange(sieving.BlockSize, 0, FactorizationRequestLimits.MaxSieveBlockSize, "SieveBlockSize");
        OptionalRange(sieving.BucketLargePrimeCutoff, 0, FactorizationRequestLimits.MaxPrimeCutoff,
            "BucketLargePrimeCutoff");
        OptionalRange(sieving.ResieveLargePrimeCutoff, 0, FactorizationRequestLimits.MaxPrimeCutoff,
            "ResieveLargePrimeCutoff");
        OptionalRange(linearAlgebra.MaxDependencies, 1, FactorizationRequestLimits.MaxDependencies,
            "LinearAlgebraMaxDependencies");
        OptionalRange(linearAlgebra.Parallelism, 0, FactorizationRequestLimits.MaxParallelism,
            "LinearAlgebraParallelism");

        if (sieving.APrimeCount is { } aPrimeCount && sieving.APrimeWindowSize is { } windowSize)
        {
            AddIf(aPrimeCount > windowSize, "APrimeCount",
                "APrimeCount cannot exceed APrimeWindowSize.");
        }

        if (sieving.HalfInterval is { } halfInterval && sieving.BlockSize is > 0 and var blockSize)
        {
            try
            {
                var intervalLength = checked(2L * halfInterval + 1);
                AddIf(blockSize > intervalLength, "SieveBlockSize",
                    "SieveBlockSize cannot exceed the full sieve interval length.");
            }
            catch (OverflowException)
            {
                Add("SieveHalfInterval", "The full sieve interval length overflows its representation.");
            }
        }

        if (sieving.LargePrime2ThresholdBound is { } threshold && sieving.LargePrime2Bound is { } lp2Bound)
        {
            AddIf(threshold > lp2Bound, "LargePrime2ThresholdBound",
                "LargePrime2ThresholdBound cannot exceed LargePrime2Bound.");
        }

        if (sieving.ResieveLargePrimeCutoff is > 0 and var resieve)
        {
            AddIf(sieving.BucketLargePrimeCutoff is null or 0, "ResieveLargePrimeCutoff",
                "ResieveLargePrimeCutoff requires bucket sieving to be enabled.");
            if (sieving.BucketLargePrimeCutoff is > 0 and var bucket)
            {
                AddIf(resieve >= bucket, "ResieveLargePrimeCutoff",
                    "ResieveLargePrimeCutoff must be below BucketLargePrimeCutoff.");
            }
        }

        if (request.TrialSievePercent is { } trialPercent)
        {
            AddIf(!double.IsFinite(trialPercent) || trialPercent <= 0.0 || trialPercent > 100.0,
                nameof(request.TrialSievePercent), "Value must be finite, greater than 0, and at most 100.");
        }

        if (sieving.CofactorSplitter is { } splitter && !CofactorSplitterKinds.TryParse(splitter, out _))
        {
            Add("CofactorSplitter",
                "Value must be squfof, squfof-rho, micro-ecm-squfof, or micro-ecm-stage2.");
        }

        if (issues.Count > 0)
        {
            throw new FactorizationRequestValidationException(issues.AsReadOnly());
        }

        return request with
        {
            FactorBase = factorBase with
            {
                Bound = bound,
                AllowTinyInputTrialDivision = allowTinyInputTrialDivision,
            },
        };

        void Add(string field, string message) => issues.Add(new FactorizationValidationIssue(field, message));
        void AddIf(bool condition, string field, string message)
        {
            if (condition)
            {
                Add(field, message);
            }
        }

        void Range<T>(T value, T minimum, T maximum, string field) where T : IComparable<T>
        {
            if (value.CompareTo(minimum) < 0 || value.CompareTo(maximum) > 0)
            {
                Add(field, $"Value must be between {minimum} and {maximum}.");
            }
        }

        void OptionalRange<T>(T? value, T minimum, T maximum, string field) where T : struct, IComparable<T>
        {
            if (value is { } present)
            {
                Range(present, minimum, maximum, field);
            }
        }
    }
}
