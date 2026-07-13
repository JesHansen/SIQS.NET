using System.Globalization;
using System.Numerics;
using SIQS.Pipeline;

namespace SIQS.UI.Services;

/// <summary>Raw run-form input as strings, before validation.</summary>
public sealed class RunParameterForm
{
    public string TargetN { get; set; } = string.Empty;
    public string? FactorBaseBound { get; set; }
    public string? Multiplier { get; set; }
    public string? SieveHalfInterval { get; set; }
    public string? RelationTarget { get; set; }
    public string? LargePrimeBound { get; set; }
    public string? SieveErrorMargin { get; set; }
    public string? APrimeCount { get; set; }
    public string? SievingParallelism { get; set; }
    public string? SieveBlockSize { get; set; }
    public string? LinearAlgebraMaxDependencies { get; set; }
    public bool ContinueSquareRootAfterFactor { get; set; }
}

/// <summary>Validation outcome: either a built request or a list of error messages.</summary>
public sealed record ValidationOutcome(FactorizationRequest? Request, IReadOnlyList<string> Errors)
{
    public bool IsValid => Request is not null;
}

/// <summary>Validates and converts the run form into a <see cref="FactorizationRequest"/>.</summary>
public sealed class RunParameterValidator
{
    public ValidationOutcome Validate(RunParameterForm form)
    {
        var errors = new List<string>();

        BigInteger? n = null;
        if (string.IsNullOrWhiteSpace(form.TargetN))
        {
            errors.Add("Target N is required.");
        }
        else if (!BigInteger.TryParse(form.TargetN.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            errors.Add("Target N must be a non-negative decimal integer.");
        }
        else if (parsed <= 1)
        {
            errors.Add("Target N must be greater than 1.");
        }
        else
        {
            n = parsed;
        }

        var bound = ParsePositiveLong(form.FactorBaseBound, "Factor base bound", errors);
        var multiplier = ParsePositiveBig(form.Multiplier, "Multiplier", errors);
        var halfInterval = ParsePositiveLong(form.SieveHalfInterval, "Sieve half interval", errors);
        var relationTarget = ParsePositiveInt(form.RelationTarget, "Relation target", errors);
        var largePrime = ParsePositiveLong(form.LargePrimeBound, "Large-prime bound", errors);
        var errorMargin = ParsePositiveInt(form.SieveErrorMargin, "Sieve error margin", errors);
        var aPrimeCount = ParsePositiveInt(form.APrimeCount, "A prime count", errors);
        var sievingParallelism = ParseNonNegativeInt(form.SievingParallelism, "Sieving parallelism", errors);
        var sieveBlockSize = ParseNonNegativeInt(form.SieveBlockSize, "Sieve block size", errors);
        var maxDeps = ParsePositiveInt(form.LinearAlgebraMaxDependencies, "Max dependencies", errors);

        if (errors.Count > 0 || n is null)
        {
            return new ValidationOutcome(null, errors);
        }

        var request = new FactorizationRequest(n.Value)
        {
            FactorBase = new FactorBaseRunOptions
            {
                Bound = bound,
                Multiplier = multiplier,
            },
            Sieving = new SievingRunOptions
            {
                HalfInterval = halfInterval,
                RelationTarget = relationTarget,
                LargePrimeBound = largePrime,
                ErrorMargin = errorMargin,
                APrimeCount = aPrimeCount,
                Parallelism = sievingParallelism,
                BlockSize = sieveBlockSize,
            },
            LinearAlgebra = new LinearAlgebraRunOptions
            {
                MaxDependencies = maxDeps,
            },
            SquareRoot = new SquareRootRunOptions
            {
                ContinueAfterFactor = form.ContinueSquareRootAfterFactor,
            },
        };

        return new ValidationOutcome(request, errors);
    }

    private static long? ParsePositiveLong(string? value, string name, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            errors.Add($"{name} must be a positive integer.");
            return null;
        }

        return parsed;
    }

    private static int? ParsePositiveInt(string? value, string name, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            errors.Add($"{name} must be a positive integer.");
            return null;
        }

        return parsed;
    }

    private static int? ParseNonNegativeInt(string? value, string name, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            || parsed < 0
            || parsed > int.MaxValue)
        {
            errors.Add($"{name} must be zero or a positive integer.");
            return null;
        }

        return (int)parsed;
    }

    private static BigInteger? ParsePositiveBig(string? value, string name, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!BigInteger.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            errors.Add($"{name} must be a positive integer.");
            return null;
        }

        return parsed;
    }
}
