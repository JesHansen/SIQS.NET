using System.Globalization;
using System.Numerics;
using SIQS.Contracts;

namespace SIQS.Pipeline;

/// <summary>
/// Rejects resume overrides that conflict with a job's stored parameters: the immutable target and
/// run directory, and any explicitly-stored parameter supplied with a different value.
/// </summary>
internal static class ResumeOverrideValidator
{
    public static void Validate(
        JobState state,
        FactorizationRequest storedRequest,
        FactorizationRequest? overrides)
    {
        if (overrides is null)
        {
            return;
        }

        if (overrides.TargetN != storedRequest.TargetN)
        {
            throw new InvalidOperationException(
                $"Resume override conflict for target_n: stored value is {storedRequest.TargetN}, supplied value is {overrides.TargetN}.");
        }

        if (overrides.RunDirectory is not null &&
            !Path.GetFullPath(overrides.RunDirectory).Equals(Path.GetFullPath(storedRequest.RunDirectory!), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resume override conflict for run_dir: resume must use the stored job directory.");
        }

        var p = state.Parameters;
        var fb = overrides.FactorBase;
        var s = overrides.Sieving;
        var la = overrides.LinearAlgebra;
        CheckLong(p, RunParameterKeys.FactorBaseBound, fb.Bound);
        CheckBigInteger(p, RunParameterKeys.Multiplier, fb.Multiplier);
        CheckLong(p, RunParameterKeys.SieveHalfInterval, s.HalfInterval);
        CheckLong(p, RunParameterKeys.PolynomialCount, s.PolynomialCount);
        CheckInt(p, RunParameterKeys.RelationTarget, s.RelationTarget);
        CheckLong(p, RunParameterKeys.LargePrimeBound, s.LargePrimeBound);
        CheckInt(p, RunParameterKeys.SieveErrorMargin, s.ErrorMargin);
        CheckInt(p, RunParameterKeys.OutputBatchSize, s.OutputBatchSize);
        CheckInt(p, RunParameterKeys.APrimeCount, s.APrimeCount);
        CheckInt(p, RunParameterKeys.APrimeWindowSize, s.APrimeWindowSize);
        CheckInt(p, RunParameterKeys.LinearAlgebraMaxDependencies, la.MaxDependencies);
        CheckInt(p, RunParameterKeys.LinearAlgebraParallelism, la.Parallelism);
        CheckInt(p, RunParameterKeys.SievingParallelism, s.Parallelism);
        CheckInt(p, RunParameterKeys.SieveBlockSize, s.BlockSize);
        CheckInt(p, RunParameterKeys.BucketLargePrimeCutoff, s.BucketLargePrimeCutoff);
        CheckInt(p, RunParameterKeys.ResieveLargePrimeCutoff, s.ResieveLargePrimeCutoff);
        CheckBool(p, RunParameterKeys.TwoLargePrimes, s.EnableTwoLargePrimes);
        CheckLong(p, RunParameterKeys.LargePrime2Bound, s.LargePrime2Bound);
        CheckLong(p, RunParameterKeys.LargePrime2ThresholdBound, s.LargePrime2ThresholdBound);
        CheckString(p, RunParameterKeys.CofactorSplitter, s.CofactorSplitter);
        CheckDouble(p, RunParameterKeys.TrialSievePercent, overrides.TrialSievePercent);
        CheckBool(p, RunParameterKeys.AllowTinyInputTrialDivision, fb.AllowTinyInputTrialDivision);
        if (overrides.SquareRoot.ContinueAfterFactor)
        {
            CheckProvided(p, RunParameterKeys.ContinueSquareRootAfterFactor, "true");
        }
    }

    private static void CheckLong(IReadOnlyDictionary<string, string> parameters, string key, long? value)
    {
        if (value is { } supplied)
        {
            CheckProvided(parameters, key, supplied.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void CheckInt(IReadOnlyDictionary<string, string> parameters, string key, int? value)
    {
        if (value is { } supplied)
        {
            CheckProvided(parameters, key, supplied.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void CheckBigInteger(IReadOnlyDictionary<string, string> parameters, string key, BigInteger? value)
    {
        if (value is { } supplied)
        {
            CheckProvided(parameters, key, supplied.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void CheckDouble(IReadOnlyDictionary<string, string> parameters, string key, double? value)
    {
        if (value is { } supplied)
        {
            CheckProvided(parameters, key, supplied.ToString("G", CultureInfo.InvariantCulture));
        }
    }

    private static void CheckBool(IReadOnlyDictionary<string, string> parameters, string key, bool? value)
    {
        if (value is { } supplied)
        {
            CheckProvided(parameters, key, supplied ? "true" : "false");
        }
    }

    private static void CheckString(IReadOnlyDictionary<string, string> parameters, string key, string? value)
    {
        if (value is not null)
        {
            CheckProvided(parameters, key, value);
        }
    }

    private static void CheckProvided(IReadOnlyDictionary<string, string> parameters, string key, string supplied)
    {
        if (!parameters.TryGetValue(key, out var stored))
        {
            throw new InvalidOperationException(
                $"Resume override conflict for {key}: stored job does not contain this parameter.");
        }

        if (StoredParameterReader.IsStoredAutomatic(stored))
        {
            throw new InvalidOperationException(
                $"Resume override conflict for {key}: stored value is {stored}, supplied value is {supplied}.");
        }

        if (!stored.Equals(supplied, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Resume override conflict for {key}: stored value is {stored}, supplied value is {supplied}.");
        }
    }
}
