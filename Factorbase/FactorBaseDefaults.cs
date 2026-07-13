using System.Numerics;

namespace Factorbase;

/// <summary>
/// Deterministic default for the factor base bound. The pipeline is the canonical owner of
/// defaults; standalone <c>qs-fb</c> uses the same formula when <c>--bound</c> is omitted. This is
/// a conservative v1 heuristic chosen for practical factor base sizes, not a claim of optimality.
/// </summary>
public static class FactorBaseDefaults
{
    public const long MinBound = 1_000;
    public const long SmoothTargetBound = 2_000_000;
    public const long MaxBound = 40_000_000;
    public const long LegacyLargeCompositeMaxBound = 5_000_000;
    public const long C105TunedBound = 14_000_000;
    public const long C110TunedBound = 40_000_000;
    private const int SmoothAnchorDigits = 20;
    private const int SmoothTargetDigits = 90;

    /// <summary>Returns the default factor base bound for <paramref name="targetN"/>.</summary>
    public static long DefaultBound(BigInteger targetN)
    {
        var digits = BigInteger.Abs(targetN).ToString().Length;
        var lnN = BigInteger.Log(targetN);
        var l = Math.Sqrt(lnN * Math.Log(lnN));
        var anchorL = LForDecimalDigits(SmoothAnchorDigits);
        var targetL = LForDecimalDigits(SmoothTargetDigits);
        var coefficient = Math.Log((double)SmoothTargetBound / MinBound) / (targetL - anchorL);
        var bound = MinBound * Math.Exp(coefficient * (l - anchorL));

        // Trial-sieve benchmarks showed that larger composites need a wider factor base
        // than the smooth anchor curve alone provides. C89+ uses the C90-tuned
        // profile; C80-C88 keep the earlier broad boost that carried C80/C82.
        var largeCompositeMultiplier = digits switch
        {
            >= 89 => 1.2301071294618595,
            >= 82 => 1.50,
            >= 80 => 1.25,
            _ => 1.00,
        };

        var rawBound = (long)Math.Ceiling(bound * largeCompositeMultiplier);
        if (digits >= 110)
        {
            return C110TunedBound;
        }

        if (digits >= 105)
        {
            return C105TunedBound;
        }

        return Math.Clamp(rawBound, MinBound, LegacyLargeCompositeMaxBound);
    }

    private static double LForDecimalDigits(int digits)
    {
        var lnN = (digits - 1) * Math.Log(10);
        return Math.Sqrt(lnN * Math.Log(lnN));
    }
}
