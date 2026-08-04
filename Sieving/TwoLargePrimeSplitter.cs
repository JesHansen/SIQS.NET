using System.Numerics;
using SIQS.Contracts.Numerics;

namespace Sieving;

/// <summary>
/// Splits a post-trial-division residual into two large primes (which may be equal), both strictly above the
/// factor-base bound and at or below the two-large-prime bound. Delegates the actual factoring to
/// <see cref="CofactorFactorizer"/> and primality to <see cref="CofactorPrimality64"/>.
/// </summary>
internal static class TwoLargePrimeSplitter
{
    /// <summary>Which arithmetic actually split a residual, for telemetry attribution.</summary>
    private enum SplitMethod { MicroEcm, Squfof, Rho }

    // Production micro-ECM stage-two parameters, tuned on the Experiment 36 wide C110 corpora: this
    // (B1, B2, curves) point is a clear replay winner across the whole tight-to-wide LP2 range
    // (1.28x at the tight default up to 3.2x at LP2 = 40x) with a byte-identical accepted-pair set.
    private const int MicroEcmStage2B1 = 300;
    private const int MicroEcmStage2B2 = 12_000;
    private const int MicroEcmStage2Curves = 10;

    public static bool TrySplit(
        BigInteger value,
        long factorBaseBound,
        long largePrime2Bound,
        out BigInteger q1,
        out BigInteger q2)
        => TrySplit(value, factorBaseBound, largePrime2Bound, CofactorSplitterKind.SqufofRho, null, out q1, out q2);

    public static bool TrySplit(
        BigInteger value,
        long factorBaseBound,
        long largePrime2Bound,
        CofactorSplitterKind splitter,
        out BigInteger q1,
        out BigInteger q2)
        => TrySplit(value, factorBaseBound, largePrime2Bound, splitter, null, out q1, out q2);

    public static bool TrySplit(
        BigInteger value,
        long factorBaseBound,
        long largePrime2Bound,
        CofactorSplitterKind splitter,
        PolynomialSieveWorker? counters,
        out BigInteger q1,
        out BigInteger q2)
    {
        q1 = q2 = BigInteger.Zero;
        if (largePrime2Bound <= factorBaseBound || value <= 1)
        {
            return false;
        }

        var minComposite = new BigInteger(factorBaseBound) * factorBaseBound;
        if (value <= minComposite)
        {
            if (counters is not null) counters.Metrics.TwoLargePrime.ResidualTooSmall++;
            return false;
        }

        var bound = new BigInteger(largePrime2Bound);
        if (value > bound * bound)
        {
            if (counters is not null) counters.Metrics.TwoLargePrime.ResidualTooLarge++;
            return false;
        }

        if (value <= ulong.MaxValue)
        {
            return TrySplit64(
                (ulong)value,
                (ulong)factorBaseBound,
                (ulong)largePrime2Bound,
                splitter,
                counters,
                out q1,
                out q2);
        }

        if (Primality.IsProbablePrime(value))
        {
            if (counters is not null) counters.Metrics.TwoLargePrime.ResidualPrime++;
            return false;
        }

        // MicroEcmStage2 is 64-bit only; for the rare > 64-bit residual it falls back to rho, exactly
        // like SqufofRho. The other 64-bit-only splitters cannot handle this path.
        if (splitter is not (CofactorSplitterKind.SqufofRho or CofactorSplitterKind.MicroEcmStage2))
        {
            return false;
        }

        if (counters is not null) counters.Metrics.Cofactor.RhoAttempts++;
        var factor = CofactorFactorizer.PollardRho(value);
        if (factor <= 1 || factor >= value)
        {
            return false;
        }

        var other = value / factor;
        if (value % factor != 0)
        {
            return false;
        }

        if (factor <= factorBaseBound || other <= factorBaseBound || factor > bound || other > bound)
        {
            return false;
        }

        if (!Primality.IsProbablePrime(factor) || !Primality.IsProbablePrime(other))
        {
            return false;
        }

        q1 = factor;
        q2 = other;
        if (counters is not null) counters.Metrics.Cofactor.RhoSuccesses++;
        return true;
    }

    public static bool TrySplit64(
        ulong value,
        ulong factorBaseBound,
        ulong largePrime2Bound,
        out BigInteger q1,
        out BigInteger q2)
        => TrySplit64(value, factorBaseBound, largePrime2Bound, CofactorSplitterKind.SqufofRho, null, out q1, out q2);

    public static bool TrySplit64(
        ulong value,
        ulong factorBaseBound,
        ulong largePrime2Bound,
        CofactorSplitterKind splitter,
        PolynomialSieveWorker? counters,
        out BigInteger q1,
        out BigInteger q2)
    {
        q1 = q2 = BigInteger.Zero;
        if ((UInt128)value <= (UInt128)factorBaseBound * factorBaseBound)
        {
            if (counters is not null) counters.Metrics.TwoLargePrime.ResidualTooSmall++;
            return false;
        }

        if ((UInt128)value > (UInt128)largePrime2Bound * largePrime2Bound)
        {
            if (counters is not null) counters.Metrics.TwoLargePrime.ResidualTooLarge++;
            return false;
        }

        if (CofactorPrimality64.HasSmallFactorAtOrBelow(value, factorBaseBound))
        {
            if (counters is not null) counters.Metrics.TwoLargePrime.ResidualSmallFactor++;
            return false;
        }

        // Every 64-bit splitter benefits from rejecting probable primes before any factorization.
        if (CofactorPrimality64.IsBase2ProbablePrime(value))
        {
            if (counters is not null) counters.Metrics.TwoLargePrime.ResidualPrime++;
            return false;
        }

        counters?.CompositeResiduals?.Add(value);

        var factor = 1UL;
        SplitMethod? splitterUsed = null;

        if (splitter is CofactorSplitterKind.MicroEcmSqufof or CofactorSplitterKind.MicroEcmStage2)
        {
            if (counters is not null) counters.Metrics.Cofactor.MicroEcmAttempts++;
            factor = splitter == CofactorSplitterKind.MicroEcmStage2
                ? MicroEcm64.TryFactorStage2(value, MicroEcmStage2B1, MicroEcmStage2B2, MicroEcmStage2Curves)
                : MicroEcm64.TryFactor(value, stage1Bound: 47, curves: 1);
            if (factor > 1 && factor < value && value % factor == 0)
            {
                splitterUsed = SplitMethod.MicroEcm;
            }
            else
            {
                factor = 1;
            }
        }

        if (factor <= 1)
        {
            if (counters is not null) counters.Metrics.Cofactor.SqufofAttempts++;
            factor = CofactorFactorizer.Squfof64(value);
            if (factor > 1 && factor < value && value % factor == 0)
            {
                splitterUsed = SplitMethod.Squfof;
            }
            else
            {
                factor = 1;
            }
        }

        if (factor <= 1 && splitter is CofactorSplitterKind.SqufofRho or CofactorSplitterKind.MicroEcmStage2)
        {
            if (counters is not null) counters.Metrics.Cofactor.RhoAttempts++;
            factor = CofactorFactorizer.PollardRho64(value);
            if (factor > 1 && factor < value && value % factor == 0)
            {
                splitterUsed = SplitMethod.Rho;
            }
        }

        if (factor <= 1 || factor >= value || value % factor != 0)
        {
            return false;
        }

        var other = value / factor;
        if (factor <= factorBaseBound || other <= factorBaseBound ||
            factor > largePrime2Bound || other > largePrime2Bound)
        {
            return false;
        }

        if (!CofactorPrimality64.IsPrime(factor) || !CofactorPrimality64.IsPrime(other))
        {
            return false;
        }

        q1 = factor;
        q2 = other;
        if (counters is not null)
        {
            if (splitterUsed == SplitMethod.MicroEcm) counters.Metrics.Cofactor.MicroEcmSuccesses++;
            else if (splitterUsed == SplitMethod.Squfof) counters.Metrics.Cofactor.SqufofSuccesses++;
            else if (splitterUsed == SplitMethod.Rho) counters.Metrics.Cofactor.RhoSuccesses++;
        }

        return true;
    }

    public static void RecordResidual(PolynomialSieveWorker counters, ulong value)
    {
        var bits = 64 - BitOperations.LeadingZeroCount(value);
        if (bits <= 32) counters.Metrics.TwoLargePrime.ResidualBitsLe32++;
        else if (bits <= 48) counters.Metrics.TwoLargePrime.ResidualBitsLe48++;
        else counters.Metrics.TwoLargePrime.ResidualBitsLe64++;
    }

    public static void RecordResidual(PolynomialSieveWorker counters, BigInteger value)
    {
        if (value <= ulong.MaxValue)
        {
            RecordResidual(counters, (ulong)value);
        }
        else
        {
            counters.Metrics.TwoLargePrime.ResidualBitsGt64++;
        }
    }
}
