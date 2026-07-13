using System.Numerics;
using SIQS.Contracts.Files;

namespace Sieving;

/// <summary>
/// Tunable sieving parameters. All values are overrideable by the CLI; <see cref="Default"/>
/// computes the deterministic v1 defaults from the factor base, matching the pipeline owner.
/// </summary>
public sealed record SievingParameters(
    long SieveHalfInterval,
    long PolynomialCount,
    int RelationTarget,
    long LargePrimeBound,
    int ErrorMargin,
    int OutputBatchSize,
    int APrimeCount,
    int APrimeWindowSize,
    int Parallelism = 0,
    int SieveBlockSize = 0,
    int BucketLargePrimeCutoff = 0,
    int ResieveLargePrimeCutoff = 0,
    int? TrialRawRelationTarget = null,
    bool EnableTwoLargePrimes = false,
    long LargePrime2Bound = 0,
    long LargePrime2ThresholdBound = 0,
    CofactorSplitterKind CofactorSplitter = CofactorSplitterKinds.Default)
{
    internal const double C100PlusRelationTargetSurplusFraction = 0.04;
    internal const double C70ToC99RelationTargetSurplusFraction = 0.01;
    internal const int C100PlusMinimumRelationTargetSurplus = 10_000;
    internal const int C70ToC99MinimumRelationTargetSurplus = 2_048;
    internal const long C110SieveHalfInterval = 4_194_304;
    internal const long C110LargePrimeBound = 60_000_000;
    internal const long C110LargePrime2Bound = 60_000_000;
    internal const long C110LargePrime2ThresholdBound = 60_000_000;
    internal const int C110ErrorMargin = 48;
    internal const int C110APrimeWindowSize = 120;
    internal const int TwoLargePrimeDefaultMinDigits = 100;

    internal bool DisableVectorScan { get; init; }

    /// <summary>
    /// Effective degree of parallelism: <c>Environment.ProcessorCount</c> when <see cref="Parallelism"/>
    /// is 0 (the default), otherwise the explicitly set value.
    /// </summary>
    public int EffectiveParallelism => Parallelism > 0 ? Parallelism : Environment.ProcessorCount;

    /// <summary>
    /// Cache block size for block sieving (sieve entries, not bytes).
    /// 0 → 262 144 (256 KB as bytes), which reduces per-block setup overhead while still
    /// fitting comfortably in the private cache of typical desktop x86-64 CPUs.
    /// </summary>
    public int EffectiveSieveBlockSize => SieveBlockSize > 0 ? SieveBlockSize : 262_144;

    /// <summary>
    /// Prime cutoff for large-prime bucket sieving. 0 disables the bucket path.
    /// When enabled, primes p >= cutoff are materialized as per-block hit lists once per
    /// polynomial and replayed during block fill; smaller primes use the direct fill paths.
    /// </summary>
    public int EffectiveBucketLargePrimeCutoff => BucketLargePrimeCutoff;

    /// <summary>
    /// Prime cutoff for candidate resieving. When enabled, primes in
    /// [cutoff, BucketLargePrimeCutoff) are rediscovered by walking their progressions
    /// once per block instead of root-gating them for every candidate. 0 disables resieving.
    /// </summary>
    public int EffectiveResieveLargePrimeCutoff => ResieveLargePrimeCutoff;

    public static SievingParameters Default(FactorBaseDocument factorBase)
    {
        var b = factorBase.Metadata.Bound;
        var fb = factorBase.Entries.Count;
        var digits = BigInteger.Abs(factorBase.Metadata.TargetN).ToString().Length;
        var useC110TunedDefaults = digits >= 110;

        var relationTarget = DefaultRelationTarget(fb, digits);
        var sieveHalfInterval = SelectSieveHalfInterval(digits, b, useC110TunedDefaults);
        var aPrimeCount = SelectAPrimeCount(digits);
        var sieveBlockSize = SelectSieveBlockSize(digits);
        var polynomialSupplyMultiplier = SelectPolynomialSupplyMultiplier(digits);
        var largePrimeBound = SelectLargePrimeBound(digits, b, useC110TunedDefaults);
        // A wide window is essential: with a narrow band of a-primes the polynomials are so
        // correlated that different (A, B) pairs rediscover the same smooth values (mirrored
        // duplicate relations), and the a-columns co-occur in enough relations to create
        // parity-column dependencies that leave Block Lanczos almost no extractable nullspace.
        var aPrimeWindowSize = useC110TunedDefaults
            ? C110APrimeWindowSize
            : SelectAPrimeWindowSize(
                aPrimeCount,
                relationTarget,
                polynomialSupplyMultiplier,
                long.MaxValue,
                Math.Max(16, 16 * aPrimeCount));
        var polynomialCount = AvailablePolynomialSupply(aPrimeWindowSize, aPrimeCount);

        var largePrime2Bound = SelectLargePrime2Bound(digits, largePrimeBound, useC110TunedDefaults);
        var largePrime2ThresholdBound = SelectLargePrime2ThresholdBound(digits, largePrime2Bound, useC110TunedDefaults);

        return new SievingParameters(
            SieveHalfInterval: sieveHalfInterval,
            PolynomialCount: polynomialCount,
            RelationTarget: relationTarget,
            LargePrimeBound: largePrimeBound,
            ErrorMargin: SelectErrorMargin(digits, useC110TunedDefaults),
            OutputBatchSize: 10_000,
            APrimeCount: aPrimeCount,
            APrimeWindowSize: aPrimeWindowSize,
            SieveBlockSize: sieveBlockSize,
            BucketLargePrimeCutoff: digits >= 89 ? sieveBlockSize : 0,
            ResieveLargePrimeCutoff: digits >= 89 ? sieveBlockSize / 4 : 0,
            EnableTwoLargePrimes: digits >= TwoLargePrimeDefaultMinDigits,
            LargePrime2Bound: largePrime2Bound,
            LargePrime2ThresholdBound: largePrime2ThresholdBound,
            CofactorSplitter: CofactorSplitterKinds.SelectFor(largePrime2Bound));
    }

    // ── Deterministic default tuning selectors ──────────────────────────────────────────────
    // Each selector is a pure function of the digit count (and the factor-base bound / C110 flag
    // where relevant). They preserve the exact banded values of the v1 defaults; the bands are
    // intentionally fine-grained and sometimes overlap, so they are kept as explicit expressions
    // rather than a single profile table. See Instructions/Sieving.md.

    /// <summary>
    /// Sieve half-interval M. C110+ uses the tuned constant; below that it is banded by digit size,
    /// with a factor-base-scaled clamp for the small-input tail (&lt; 70 digits).
    /// </summary>
    internal static long SelectSieveHalfInterval(int digits, long bound, bool useC110TunedDefaults)
        => useC110TunedDefaults ? C110SieveHalfInterval
            : digits >= 105 ? 16_777_216
            : digits is >= 100 and <= 104 ? 8_388_608
            : digits >= 89 ? 1_048_576
            : digits >= 78 && digits <= 81 ? 524_288
            : digits >= 70 && digits <= 77 ? 393_216
            : digits >= 82 ? 393_216
            : Math.Clamp(20L * bound, 32_768, 16_777_216);

    /// <summary>Number of factor-base primes whose product forms A, by digit size.</summary>
    internal static int SelectAPrimeCount(int digits)
        => digits <= 45 ? 3 : digits <= 52 ? 4 : digits <= 69 ? 5 : digits <= 77 ? 6 : digits <= 87 ? 7 : digits >= 100 ? 10 : 9;

    /// <summary>Cache block size for block sieving; 0 selects the single-block fallback.</summary>
    internal static int SelectSieveBlockSize(int digits)
        => digits >= 89 ? 1_048_576 : digits >= 70 && digits <= 81 ? 524_288 : 0;

    /// <summary>Target polynomial supply multiplier relative to the relation target.</summary>
    internal static long SelectPolynomialSupplyMultiplier(int digits)
        => digits >= 105 ? 64L : digits >= 100 ? 30L : 10L;

    /// <summary>Single-large-prime bound, as a multiple of the factor-base bound (C110+ uses the tuned constant).</summary>
    internal static long SelectLargePrimeBound(int digits, long bound, bool useC110TunedDefaults)
        => useC110TunedDefaults ? C110LargePrimeBound
            : digits >= 105 ? 512L * bound
            : digits >= 100 ? 384L * bound
            : digits >= 89 ? 192L * bound
            : digits >= 70 && digits <= 81 ? 128L * bound
            : 64L * bound;

    /// <summary>Log-credit scan error margin, by digit size (C110+ uses the tuned constant).</summary>
    internal static int SelectErrorMargin(int digits, bool useC110TunedDefaults)
        => useC110TunedDefaults ? C110ErrorMargin
            : digits >= 105 ? 48
            : digits >= 100 ? 56
            : digits >= 70 && digits <= 81 ? 40
            : digits >= 89 ? 36
            : 24;

    /// <summary>Two-large-prime relation bound (LP2). C110+ uses the tuned constant; 80–84 caps at 5M.</summary>
    internal static long SelectLargePrime2Bound(int digits, long largePrimeBound, bool useC110TunedDefaults)
        => useC110TunedDefaults ? C110LargePrime2Bound
            : digits is >= 80 and <= 84 ? Math.Min(5_000_000L, largePrimeBound)
            : largePrimeBound;

    /// <summary>Two-large-prime residual threshold bound. Finely banded; never exceeds LP2.</summary>
    internal static long SelectLargePrime2ThresholdBound(int digits, long largePrime2Bound, bool useC110TunedDefaults)
        => useC110TunedDefaults ? C110LargePrime2ThresholdBound
            : digits >= 105 ? Math.Min(50_000_000L, largePrime2Bound)
            : digits >= 100 ? Math.Min(275_000_000L, largePrime2Bound)
            : digits is >= 91 and <= 99 ? Math.Min(62_500L, largePrime2Bound)
            : digits >= 86 ? Math.Min(31_250L, largePrime2Bound)
            : digits is 84 or 85 ? Math.Min(15_625L, largePrime2Bound)
            : digits == 83 ? Math.Min(31_250L, largePrime2Bound)
            : digits is >= 80 and <= 82 ? Math.Min(62_500L, largePrime2Bound)
            : largePrime2Bound;

    internal static int SelectAPrimeWindowSize(
        int aPrimeCount,
        int relationTarget,
        long polynomialSupplyMultiplier,
        long polynomialCount,
        int minimumWindowSize)
    {
        if (aPrimeCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(aPrimeCount), aPrimeCount, "A-prime count must be positive.");
        }

        if (relationTarget < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(relationTarget), relationTarget, "Relation target must be positive.");
        }

        if (polynomialCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(polynomialCount), polynomialCount, "Polynomial count must be positive.");
        }

        if (polynomialSupplyMultiplier < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(polynomialSupplyMultiplier), polynomialSupplyMultiplier, "Polynomial supply multiplier must be positive.");
        }

        var window = Math.Max(minimumWindowSize, aPrimeCount);
        var targetSupply = Math.Min(polynomialCount, checked((long)relationTarget * polynomialSupplyMultiplier));
        while (AvailablePolynomialSupply(window, aPrimeCount) < targetSupply)
        {
            window++;
        }

        return window;
    }

    internal static int DefaultRelationTarget(int factorBaseCount, int digits)
    {
        if (factorBaseCount < 1)
        {
            return 64;
        }

        if (digits >= 100)
        {
            var surplus = Math.Max(
                C100PlusMinimumRelationTargetSurplus,
                (int)Math.Ceiling(C100PlusRelationTargetSurplusFraction * factorBaseCount));
            return checked(factorBaseCount + surplus);
        }

        if (digits >= 70)
        {
            var surplus = Math.Max(
                C70ToC99MinimumRelationTargetSurplus,
                (int)Math.Ceiling(C70ToC99RelationTargetSurplusFraction * factorBaseCount));
            return checked(factorBaseCount + surplus);
        }

        return factorBaseCount + 512;
    }

    public static long AvailablePolynomialSupply(int windowSize, int aPrimeCount)
    {
        if (windowSize < aPrimeCount)
        {
            return 0;
        }

        var combinations = Binomial(windowSize, aPrimeCount);
        var familySize = 1L << Math.Max(0, aPrimeCount - 1);
        return SaturatingMultiply(combinations, familySize);
    }

    private static long Binomial(int n, int k)
    {
        if (k < 0 || n < k)
        {
            return 0;
        }

        k = Math.Min(k, n - k);
        var result = 1L;
        for (var i = 1; i <= k; i++)
        {
            var factor = n - k + i;
            if (result > long.MaxValue / factor)
            {
                return long.MaxValue;
            }

            result *= factor;
            result /= i;
        }

        return result;
    }

    private static long SaturatingMultiply(long left, long right)
        => left > long.MaxValue / right ? long.MaxValue : left * right;
}
