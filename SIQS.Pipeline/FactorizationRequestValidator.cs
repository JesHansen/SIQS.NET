using Factorbase;
using Sieving;
using SIQS.Contracts;

namespace SIQS.Pipeline;

/// <summary>
/// Normalizes a <see cref="FactorizationRequest"/> — filling the defaulted factor-base bound and the
/// tiny-input trial-division flag — and validates its numeric parameters before a job starts.
/// </summary>
internal static class FactorizationRequestValidator
{
    public static FactorizationRequest NormalizeAndValidate(FactorizationRequest request)
    {
        if (request.TargetN <= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "TargetN must be greater than 1.");
        }

        var factorBase = request.FactorBase;
        var sieving = request.Sieving;
        var linearAlgebra = request.LinearAlgebra;

        var allowTinyInputTrialDivision = factorBase.AllowTinyInputTrialDivision
            ?? (factorBase.Bound is null && factorBase.Multiplier is null);

        var bound = factorBase.Bound ?? FactorBaseDefaults.DefaultBound(request.TargetN);
        if (bound < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "FactorBaseBound must be at least 2.");
        }

        RejectNonPositive(sieving.HalfInterval, "SieveHalfInterval");
        RejectNonPositive(sieving.PolynomialCount, "PolynomialCount");
        RejectNonPositive(sieving.RelationTarget, "RelationTarget");
        RejectNonPositive(sieving.LargePrimeBound, "LargePrimeBound");
        RejectNonPositive(sieving.LargePrime2Bound, "LargePrime2Bound");
        RejectNonPositive(sieving.LargePrime2ThresholdBound, "LargePrime2ThresholdBound");
        RejectNonPositive(sieving.OutputBatchSize, "OutputBatchSize");
        RejectNonPositive(sieving.APrimeCount, "APrimeCount");
        RejectNonPositive(sieving.APrimeWindowSize, "APrimeWindowSize");
        RejectNonPositive(linearAlgebra.MaxDependencies, "LinearAlgebraMaxDependencies");
        RejectCofactorSplitter(sieving.CofactorSplitter, "CofactorSplitter");

        RejectNegative(linearAlgebra.Parallelism, "LinearAlgebraParallelism");
        RejectNegative(sieving.Parallelism, "SievingParallelism");
        RejectNegative(sieving.BlockSize, "SieveBlockSize");
        RejectNegative(sieving.BucketLargePrimeCutoff, "BucketLargePrimeCutoff");
        RejectNegative(sieving.ResieveLargePrimeCutoff, "ResieveLargePrimeCutoff");
        // Cross-field consistency for the two-large-prime bounds is owned by the sieving group.
        sieving.EnsureConsistent();

        if (request.TrialSievePercent is { } trialPercent && (trialPercent <= 0.0 || trialPercent > 100.0))
        {
            throw new ArgumentOutOfRangeException(nameof(request.TrialSievePercent), "Value must be greater than 0 and at most 100.");
        }

        return request with
        {
            FactorBase = factorBase with
            {
                Bound = bound,
                AllowTinyInputTrialDivision = allowTinyInputTrialDivision,
            },
        };
    }

    private static void RejectNonPositive(long? value, string name)
    {
        if (value is { } v && v <= 0)
        {
            throw new ArgumentOutOfRangeException(name, "Value must be positive.");
        }
    }

    private static void RejectNegative(int? value, string name)
    {
        if (value is { } v && v < 0)
        {
            throw new ArgumentOutOfRangeException(name, "Value must be zero or positive.");
        }
    }

    private static void RejectCofactorSplitter(string? value, string name)
    {
        if (value is null)
        {
            return;
        }

        if (!CofactorSplitterKinds.TryParse(value, out _))
        {
            throw new ArgumentOutOfRangeException(name, "Value must be squfof or squfof-rho.");
        }
    }
}
