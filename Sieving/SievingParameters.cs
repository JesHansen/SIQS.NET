using System.Numerics;
using SIQS.Contracts.Files;

namespace Sieving;

/// <summary>
/// Tunable sieving parameters. All values are overrideable by the CLI; <see cref="Default"/>
/// computes the deterministic v1 defaults from the factor base, matching the pipeline owner.
/// </summary>
/// <remarks>
/// <c>BucketLargePrimeCutoff</c>: prime cutoff for large-prime bucket sieving. 0 disables the
/// bucket path. When enabled, primes p >= cutoff are materialized as per-block hit lists once per
/// polynomial and replayed during block fill; smaller primes use the direct fill paths.
/// <para>
/// <c>ResieveLargePrimeCutoff</c>: prime cutoff for candidate resieving. When enabled, primes in
/// [cutoff, BucketLargePrimeCutoff) are rediscovered by walking their progressions once per block
/// instead of root-gating them for every candidate. 0 disables resieving.
/// </para>
/// <para>
/// <c>SmallPrimeVariationBound</c>: upper prime bound for small-prime variation. Primes at or
/// below the bound are omitted from dense sieve fill and recovered only for provisional reports.
/// Zero disables it.
/// </para>
/// </remarks>
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
    int SmallPrimeVariationBound = 0,
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
    internal const long C100LargePrimeBound = 1_000_000_000;
    internal const int SmallPrimeVariationDefaultMinDigits = 35;
    internal const int SmallPrimeVariationDefaultBound = 256;
    internal const int TwoLargePrimeDefaultMinDigits = 110;

    // These four thresholds are the only sieve dispatch knobs left: each picks between two
    // collection strategies based on how many candidates a block actually produced (a quantity that
    // varies block-to-block within a single run, not by digit size). Past optimization-experiment
    // on/off toggles that never varied in production have been removed; see git history if one of
    // their measurements is needed again.
    internal int VectorCandidateMajorMaximumCandidates { get; init; } = 16;
    internal int FlatCandidateOffsetMapMinimumCandidates { get; init; } = 16;
    internal int VectorCandidateMajorBucketMaximumCandidates { get; init; } = 15;
    internal int CandidateResieveMinimumCandidates { get; init; } = 2;

    /// <summary>
    /// When set, each worker appends every composite cofactor residual it sees to
    /// <see cref="PolynomialSieveWorker.CompositeResiduals"/> before splitting. Observation-only: it
    /// never affects a relation decision. Used by <c>SIQS.Benchmarks</c>' residual-corpus tooling to
    /// build the cofactor-splitter research corpora (e.g. the ones behind the current
    /// <see cref="CofactorSplitterKind.MicroEcmStage2"/> default), not by the pipeline.
    /// </summary>
    internal bool CaptureCompositeResiduals { get; init; }

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

    public static SievingParameters Default(FactorBaseDocument factorBase)
    {
        var b = factorBase.Metadata.Bound;
        var fb = factorBase.Entries.Count;
        var digits = BigInteger.Abs(factorBase.Metadata.TargetN).ToString().Length;

        var relationTarget = DefaultRelationTarget(fb, digits);
        var sieveHalfInterval = SelectSieveHalfInterval(digits);
        var aPrimeCount = SelectAPrimeCount(digits);
        var sieveBlockSize = SelectSieveBlockSize(digits);
        var polynomialSupplyMultiplier = SelectPolynomialSupplyMultiplier(digits);
        var largePrimeBound = SelectLargePrimeBound(digits, b);
        // A wide window is essential: with a narrow band of a-primes the polynomials are so
        // correlated that different (A, B) pairs rediscover the same smooth values (mirrored
        // duplicate relations), and the a-columns co-occur in enough relations to create
        // parity-column dependencies that leave Block Lanczos almost no extractable nullspace.
        var aPrimeWindowSize = SelectAPrimeWindowSize(
            aPrimeCount,
            relationTarget,
            polynomialSupplyMultiplier,
            long.MaxValue,
            Math.Max(16, 16 * aPrimeCount));
        var polynomialCount = AvailablePolynomialSupply(aPrimeWindowSize, aPrimeCount);

        var largePrime2Bound = SelectLargePrime2Bound(b);
        var largePrime2ThresholdBound = SelectLargePrime2ThresholdBound(largePrime2Bound);

        return new SievingParameters(
            SieveHalfInterval: sieveHalfInterval,
            PolynomialCount: polynomialCount,
            RelationTarget: relationTarget,
            LargePrimeBound: largePrimeBound,
            ErrorMargin: SelectErrorMargin(digits),
            OutputBatchSize: 10_000,
            APrimeCount: aPrimeCount,
            APrimeWindowSize: aPrimeWindowSize,
            SieveBlockSize: sieveBlockSize,
            BucketLargePrimeCutoff: SelectBucketLargePrimeCutoff(digits),
            ResieveLargePrimeCutoff: SelectResieveLargePrimeCutoff(digits),
            SmallPrimeVariationBound: SelectSmallPrimeVariationBound(digits),
            EnableTwoLargePrimes: digits >= TwoLargePrimeDefaultMinDigits,
            LargePrime2Bound: largePrime2Bound,
            LargePrime2ThresholdBound: largePrime2ThresholdBound,
            // When two-large-primes are active the residuals are large (52+ bits at C110), where the
            // micro-ECM stage-two hybrid is a clear win over SQUFOF/rho at identical yield
            // (Experiment 37). Below that gate the cofactor path is unused, so keep the historical
            // SQUFOF/rho auto-selection there.
            CofactorSplitter: digits >= TwoLargePrimeDefaultMinDigits
                ? CofactorSplitterKind.MicroEcmStage2
                : CofactorSplitterKinds.SelectFor(largePrime2Bound));
    }

    // ── Deterministic default tuning selectors ──────────────────────────────────────────────
    // Each selector is a pure function of the digit count (and the factor-base bound where
    // relevant). Structural work parameters are monotonic. Kernel boundaries may move downward at
    // a measured crossover when larger geometry changes the balance between two execution paths.

    /// <summary>
    /// Sieve half-interval M. Powers of two approximate a log-linear rise from 128 at C13 to
    /// 1,048,576 at C40, followed by measured plateaus through C115.
    /// </summary>
    internal static long SelectSieveHalfInterval(int digits)
        => digits >= 113 ? 33_554_432
            : digits >= 111 ? 16_777_216
            : digits >= 106 ? 8_388_608
            : digits >= 104 ? 4_194_304
            : digits >= 101 ? 2_097_152
            : digits >= 40 ? 1_048_576
            : 1L << (7 + ((13 * Math.Max(0, digits - 13) + 13) / 27));

    /// <summary>Number of factor-base primes whose product forms A, by digit size.</summary>
    internal static int SelectAPrimeCount(int digits)
        => digits <= 19 ? 2
            : digits <= 47 ? 3
            : digits <= 61 ? 5
            : digits <= 62 ? 6
            : digits <= 77 ? 7
            : digits <= 87 ? 8
            : digits <= 103 ? 9
            : 10;

    /// <summary>Cache block size for block sieving, using the measured monotonic C13-C115 tiers.</summary>
    // These block sizes were re-confirmed, not just inherited. Copying YAFU's much smaller (~32 KB)
    // blocks, and separately an "integrated" cache-sized kernel that walks true small blocks, were
    // both measured and rejected: small blocks force each prime's root to be re-advanced far more
    // often, and the retuning pass that followed the kernel work left the block size unchanged. Do
    // not lower it in isolation — it only interacts favourably with a compact fused bucket layout,
    // which itself has no measured win (see LargePrimeBuckets).
    internal static int SelectSieveBlockSize(int digits)
        => digits >= 70 ? 524_288
            : 262_144;

    /// <summary>Prime cutoff for bucket sieving; enabled where the factor base is wide enough to benefit.</summary>
    internal static int SelectBucketLargePrimeCutoff(int digits)
        => digits >= 111 ? 1_048_576
            : digits >= 100 ? 655_360
            : digits >= 85 ? 1_048_576
            : 0;

    /// <summary>Prime cutoff for candidate resieving; enabled with the bucket tier.</summary>
    // The bucket cutoff was retuned downward at a measured crossover, but the resieve cutoff was
    // swept in the same study and left unchanged: moving it showed no reliable win once the rejected
    // hot-loop prototypes were removed from the comparison.
    internal static int SelectResieveLargePrimeCutoff(int digits)
        => digits >= 85 ? 262_144 : 0;

    /// <summary>
    /// Small-prime variation is a measured net win from C35. Wider bounds repay their additional
    /// exact-recovery work as the sieve interval grows, then ease at C100+ where scan cost rises.
    /// </summary>
    internal static int SelectSmallPrimeVariationBound(int digits)
        => digits >= 100 ? 768
            : digits >= 60 ? 1_024
            : digits >= 50 ? 512
            : digits >= SmallPrimeVariationDefaultMinDigits ? SmallPrimeVariationDefaultBound
            : 0;

    /// <summary>Target polynomial supply multiplier relative to the relation target.</summary>
    internal static long SelectPolynomialSupplyMultiplier(int digits)
        => digits >= 105 ? 64L
            : digits >= 100 ? 30L
            : digits >= 20 ? 10L
            : 1L;

    /// <summary>Single-large-prime bound, as a multiple of the factor-base bound below C100.</summary>
    internal static long SelectLargePrimeBound(int digits, long bound)
        => digits >= 100 ? C100LargePrimeBound
            : digits >= 75 ? 192L * bound
            : digits >= 70 ? 128L * bound
            : digits >= 64 ? 64L * bound
            : digits >= 60 ? 32L * bound
            : digits >= 59 ? 16L * bound
            : 8L * bound;

    /// <summary>Log-credit scan error margin, by digit size.</summary>
    internal static int SelectErrorMargin(int digits)
        => digits >= 104 ? 48
            : digits >= 89 ? 36
            : digits >= 75 ? 24
            : digits >= 70 ? 16
            : digits >= 65 ? 8
            : 0;

    /// <summary>
    /// Two-large-prime relation bound (LP2). The measured C110 full factorization used 1.5 times
    /// the factor-base bound: wide enough to add useful graph cycles without creating the much
    /// larger sparse graph produced by the single-large-prime bound.
    /// </summary>
    internal static long SelectLargePrime2Bound(long factorBaseBound)
        => checked(factorBaseBound + factorBaseBound / 2 + factorBaseBound % 2);

    /// <summary>
    /// Bound used to grant scan log credit in 2LP mode. This does not independently limit accepted
    /// relation cofactors; the actual per-prime relation bound is <c>LargePrime2Bound</c>.
    /// </summary>
    internal static long SelectLargePrime2ThresholdBound(long largePrime2Bound) => largePrime2Bound;

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
