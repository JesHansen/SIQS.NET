using System.Globalization;
using System.Numerics;
using SIQS.Contracts;

namespace SIQS.Pipeline;

/// <summary>Builds the public <see cref="FactorizationJobResult"/> and the stored parameter map from job state.</summary>
internal static class JobResultFactory
{
    public static FactorizationJobResult BuildResult(
        JobState state, FactorizationRequest request, bool factorFound, int attempted)
    {
        var factors = state.FinalFactors.Select(f => BigInteger.Parse(f, CultureInfo.InvariantCulture)).ToArray();
        var summaries = state.PhaseStates
            .Select(p => new PhaseSummary(p.Phase, p.Status, p.Counters, p.ElapsedSeconds, p.Error))
            .ToArray();

        return new FactorizationJobResult(
            state.JobId, state.Status, request.TargetN, factorFound, factors, attempted,
            state.ArtifactPaths.ToArray(), summaries, state.ErrorSummary?.Message);
    }

    public static Dictionary<string, string> BuildParameters(FactorizationRequest request)
    {
        var fb = request.FactorBase;
        var s = request.Sieving;
        var la = request.LinearAlgebra;
        return new()
        {
            [RunParameterKeys.TargetN] = request.TargetN.ToString(CultureInfo.InvariantCulture),
            [RunParameterKeys.FactorBaseBound] = fb.Bound?.ToString(CultureInfo.InvariantCulture) ?? "auto",
            [RunParameterKeys.Multiplier] = fb.Multiplier?.ToString(CultureInfo.InvariantCulture) ?? "auto",
            [RunParameterKeys.SieveHalfInterval] = s.HalfInterval?.ToString(CultureInfo.InvariantCulture) ?? "auto",
            [RunParameterKeys.PolynomialCount] = s.PolynomialCount?.ToString(CultureInfo.InvariantCulture) ?? "auto",
            [RunParameterKeys.RelationTarget] = s.RelationTarget?.ToString(CultureInfo.InvariantCulture) ?? "auto",
            [RunParameterKeys.LargePrimeBound] = s.LargePrimeBound?.ToString(CultureInfo.InvariantCulture) ?? "auto",
            [RunParameterKeys.LargePrime2Bound] = s.LargePrime2Bound?.ToString(CultureInfo.InvariantCulture) ?? "auto",
            [RunParameterKeys.LargePrime2ThresholdBound] = s.LargePrime2ThresholdBound?.ToString(CultureInfo.InvariantCulture) ?? "auto",
            [RunParameterKeys.CofactorSplitter] = s.CofactorSplitter ?? "auto",
            [RunParameterKeys.TwoLargePrimes] = s.EnableTwoLargePrimes?.ToString() ?? "auto",
            [RunParameterKeys.SieveErrorMargin] = s.ErrorMargin?.ToString(CultureInfo.InvariantCulture) ?? "auto",
            [RunParameterKeys.OutputBatchSize] = s.OutputBatchSize?.ToString(CultureInfo.InvariantCulture) ?? "auto",
            [RunParameterKeys.APrimeCount] = s.APrimeCount?.ToString(CultureInfo.InvariantCulture) ?? "auto",
            [RunParameterKeys.APrimeWindowSize] = s.APrimeWindowSize?.ToString(CultureInfo.InvariantCulture) ?? "auto",
            [RunParameterKeys.SievingParallelism] = s.Parallelism?.ToString(CultureInfo.InvariantCulture) ?? "auto",
            [RunParameterKeys.SieveBlockSize] = s.BlockSize?.ToString(CultureInfo.InvariantCulture) ?? "auto",
            [RunParameterKeys.BucketLargePrimeCutoff] = s.BucketLargePrimeCutoff?.ToString(CultureInfo.InvariantCulture) ?? "off",
            [RunParameterKeys.ResieveLargePrimeCutoff] = s.ResieveLargePrimeCutoff?.ToString(CultureInfo.InvariantCulture) ?? "auto",
            [RunParameterKeys.SmallPrimeVariationBound] = "auto",
            [RunParameterKeys.TrialSievePercent] = request.TrialSievePercent?.ToString("G", CultureInfo.InvariantCulture) ?? "off",
            [RunParameterKeys.LinearAlgebraMaxDependencies] = la.MaxDependencies?.ToString(CultureInfo.InvariantCulture) ?? "auto",
            [RunParameterKeys.LinearAlgebraParallelism] = la.Parallelism?.ToString(CultureInfo.InvariantCulture) ?? "auto",
            [RunParameterKeys.ContinueSquareRootAfterFactor] = request.SquareRoot.ContinueAfterFactor ? "true" : "false",
            [RunParameterKeys.AllowTinyInputTrialDivision] = fb.AllowTinyInputTrialDivision?.ToString() ?? "auto",
        };
    }
}
