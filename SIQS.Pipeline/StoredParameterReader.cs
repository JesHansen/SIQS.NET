using System.Globalization;
using System.Numerics;
using SIQS.Contracts;

namespace SIQS.Pipeline;

/// <summary>
/// Reconstructs a <see cref="FactorizationRequest"/> from a job's stored parameter map, treating the
/// sentinel values <c>auto</c> and <c>off</c> as "not supplied".
/// </summary>
internal static class StoredParameterReader
{
    public static FactorizationRequest BuildRequest(JobState state, string jobDirectory)
    {
        var p = state.Parameters;
        var target = BigInteger.Parse(
            p.TryGetValue(RunParameterKeys.TargetN, out var targetValue) ? targetValue : state.TargetN,
            CultureInfo.InvariantCulture);

        return new FactorizationRequest(target, jobDirectory, ParseDouble(p, RunParameterKeys.TrialSievePercent))
        {
            FactorBase = new FactorBaseRunOptions
            {
                Bound = ParseLong(p, RunParameterKeys.FactorBaseBound),
                Multiplier = ParseBigInteger(p, RunParameterKeys.Multiplier),
                AllowTinyInputTrialDivision = ParseBool(p, RunParameterKeys.AllowTinyInputTrialDivision),
            },
            Sieving = new SievingRunOptions
            {
                HalfInterval = ParseLong(p, RunParameterKeys.SieveHalfInterval),
                PolynomialCount = ParseLong(p, RunParameterKeys.PolynomialCount),
                RelationTarget = ParseInt(p, RunParameterKeys.RelationTarget),
                LargePrimeBound = ParseLong(p, RunParameterKeys.LargePrimeBound),
                ErrorMargin = ParseInt(p, RunParameterKeys.SieveErrorMargin),
                OutputBatchSize = ParseInt(p, RunParameterKeys.OutputBatchSize),
                APrimeCount = ParseInt(p, RunParameterKeys.APrimeCount),
                APrimeWindowSize = ParseInt(p, RunParameterKeys.APrimeWindowSize),
                Parallelism = ParseInt(p, RunParameterKeys.SievingParallelism),
                BlockSize = ParseInt(p, RunParameterKeys.SieveBlockSize),
                BucketLargePrimeCutoff = ParseInt(p, RunParameterKeys.BucketLargePrimeCutoff),
                ResieveLargePrimeCutoff = ParseInt(p, RunParameterKeys.ResieveLargePrimeCutoff),
                EnableTwoLargePrimes = ParseBool(p, RunParameterKeys.TwoLargePrimes),
                LargePrime2Bound = ParseLong(p, RunParameterKeys.LargePrime2Bound),
                LargePrime2ThresholdBound = ParseLong(p, RunParameterKeys.LargePrime2ThresholdBound),
                CofactorSplitter = ParseString(p, RunParameterKeys.CofactorSplitter),
            },
            LinearAlgebra = new LinearAlgebraRunOptions
            {
                MaxDependencies = ParseInt(p, RunParameterKeys.LinearAlgebraMaxDependencies),
                Parallelism = ParseInt(p, RunParameterKeys.LinearAlgebraParallelism),
            },
            SquareRoot = new SquareRootRunOptions
            {
                ContinueAfterFactor = ParseBool(p, RunParameterKeys.ContinueSquareRootAfterFactor) ?? false,
            },
        };
    }

    public static bool IsStoredAutomatic(string value)
        => value.Equals("auto", StringComparison.OrdinalIgnoreCase)
           || value.Equals("off", StringComparison.OrdinalIgnoreCase);

    private static string? StoredValue(IReadOnlyDictionary<string, string> parameters, string key)
        => parameters.TryGetValue(key, out var value) && !IsStoredAutomatic(value) ? value : null;

    private static long? ParseLong(IReadOnlyDictionary<string, string> parameters, string key)
        => StoredValue(parameters, key) is { } value ? long.Parse(value, CultureInfo.InvariantCulture) : null;

    private static int? ParseInt(IReadOnlyDictionary<string, string> parameters, string key)
        => StoredValue(parameters, key) is { } value ? int.Parse(value, CultureInfo.InvariantCulture) : null;

    private static BigInteger? ParseBigInteger(IReadOnlyDictionary<string, string> parameters, string key)
        => StoredValue(parameters, key) is { } value ? BigInteger.Parse(value, CultureInfo.InvariantCulture) : null;

    private static double? ParseDouble(IReadOnlyDictionary<string, string> parameters, string key)
        => StoredValue(parameters, key) is { } value ? double.Parse(value, CultureInfo.InvariantCulture) : null;

    private static bool? ParseBool(IReadOnlyDictionary<string, string> parameters, string key)
        => StoredValue(parameters, key) is { } value ? bool.Parse(value) : null;

    private static string? ParseString(IReadOnlyDictionary<string, string> parameters, string key)
        => StoredValue(parameters, key);
}
