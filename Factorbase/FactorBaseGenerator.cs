using System.Numerics;
using SIQS.Contracts;
using SIQS.Contracts.Files;

namespace Factorbase;

/// <summary>Inputs to factor base generation. Optional values fall back to deterministic defaults.</summary>
public sealed record FactorBaseOptions(
    BigInteger TargetN,
    long? Bound = null,
    BigInteger? Multiplier = null,
    bool? AllowTinyInputTrialDivision = null);

/// <summary>
/// Result of factor base generation: either a built factor base, or an early precheck outcome
/// (in which case <see cref="FactorBase"/> is null and <see cref="EarlyOutcome"/> holds the result).
/// </summary>
public sealed record FactorBaseGenerationResult(
    FactorBaseDocument? FactorBase,
    FactorsDocument? EarlyOutcome)
{
    public bool HasEarlyOutcome => EarlyOutcome is not null;
}

/// <summary>
/// Builds the factor base for <c>ScaledN = Multiplier * TargetN</c>, performing the trivial-factor
/// prechecks first. See <c>Instructions/Factorbase.md</c> for the full specification.
/// </summary>
public static class FactorBaseGenerator
{
    public static FactorBaseGenerationResult Generate(
        FactorBaseOptions options,
        IProgress<SiqsProgressEvent>? progress = null)
    {
        var n = options.TargetN;
        if (n <= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "TargetN must be greater than 1.");
        }

        var allowTinyTrialDivision = options.AllowTinyInputTrialDivision
            ?? (options.Bound is null && options.Multiplier is null);
        if (FactorBasePrecheck.TryFind(n, allowTinyTrialDivision) is { } early)
        {
            Report(progress, ProgressLevel.Info, "precheck completed", artifact: "factors.txt");
            return new FactorBaseGenerationResult(null, early);
        }

        var multiplier = options.Multiplier ?? MultiplierSelector.Select(n);
        if (multiplier < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Multiplier must be positive.");
        }

        var scaledN = multiplier * n;
        var bound = options.Bound ?? FactorBaseDefaults.DefaultBound(n);
        if (bound < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Bound must be at least 2.");
        }

        Report(progress, ProgressLevel.Info, "generating primes",
            counters: new() { ["bound"] = bound.ToString(), ["multiplier"] = multiplier.ToString() });

        var build = FactorBaseDocumentBuilder.Build(n, multiplier, scaledN, bound);
        if (build.EarlyOutcome is { } factorFoundDuringBuild)
        {
            return new FactorBaseGenerationResult(null, factorFoundDuringBuild);
        }

        var factorBase = build.FactorBase!;
        Report(progress, ProgressLevel.Info, "factor base built",
            counters: new() { ["factor_base_size"] = factorBase.Entries.Count.ToString() }, artifact: "factor_base.txt");

        return new FactorBaseGenerationResult(factorBase, null);
    }

    private static void Report(
        IProgress<SiqsProgressEvent>? progress,
        ProgressLevel level,
        string message,
        Dictionary<string, string>? counters = null,
        string? artifact = null)
    {
        progress?.Report(new SiqsProgressEvent(
            DateTimeOffset.UtcNow, null, SiqsPhase.FactorBase, level, message,
            Percent: null, Counters: counters ?? new(), ArtifactPath: artifact));
    }
}
