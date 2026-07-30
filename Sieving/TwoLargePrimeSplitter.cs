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
    private enum SplitMethod { Squfof, Rho }

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

        if (splitter == CofactorSplitterKind.Squfof)
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

        // Both surviving modes try SQUFOF first, so the fast base-2 primality screen always applies.
        if (CofactorPrimality64.IsBase2ProbablePrime(value))
        {
            if (counters is not null) counters.Metrics.TwoLargePrime.ResidualPrime++;
            return false;
        }

        var factor = 1UL;
        SplitMethod? splitterUsed = null;

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

        if (factor <= 1 && splitter == CofactorSplitterKind.SqufofRho)
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
            if (splitterUsed == SplitMethod.Squfof) counters.Metrics.Cofactor.SqufofSuccesses++;
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
