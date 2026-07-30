using System.Numerics;
using SIQS.Contracts;
using SIQS.Contracts.Files;
using SIQS.Contracts.Numerics;

namespace Factorbase;

/// <summary>Performs inexpensive factor checks before factor-base construction.</summary>
internal static class FactorBasePrecheck
{
    private const long PrecheckBound = 1_000;
    private const long TinyInputTrialDivisionBound = 1_000_000;
    private static readonly IReadOnlyList<long> PrecheckPrimes = PrimeSieve.PrimesUpTo(PrecheckBound);

    public static FactorsDocument? TryFind(BigInteger targetN, bool allowTinyTrialDivision)
    {
        if (targetN == 2)
        {
            return EarlyFactor.Prime(targetN);
        }

        if (targetN.IsEven)
        {
            return EarlyFactor.Create(targetN, 2, "even_target");
        }

        if (IntegerMath.IsPerfectSquare(targetN, out var root))
        {
            return EarlyFactor.Create(targetN, root, "perfect_square");
        }

        foreach (var prime in PrecheckPrimes)
        {
            if (targetN == prime)
            {
                return EarlyFactor.Prime(targetN);
            }

            if (targetN % prime == 0)
            {
                return EarlyFactor.Create(targetN, prime, "small_prime_factor");
            }
        }

        var squareRoot = IntegerMath.Sqrt(targetN);
        if (allowTinyTrialDivision && squareRoot <= TinyInputTrialDivisionBound)
        {
            foreach (var prime in PrimeSieve.PrimesUpTo((long)squareRoot).Where(prime => prime > PrecheckBound))
            {
                if (targetN % prime == 0)
                {
                    return EarlyFactor.Create(targetN, prime, "tiny_input_trial_division");
                }
            }

            return EarlyFactor.Prime(targetN);
        }

        // Baillie-PSW has no known counterexample, so a positive result is trusted as proof of
        // primality at every input size and terminates the job before any SIQS work begins.
        if (Primality.IsBailliePswProbablePrime(targetN))
        {
            return EarlyFactor.Prime(targetN);
        }

        if (TryFindPerfectPowerFactor(targetN, out root))
        {
            return EarlyFactor.Create(targetN, root, "perfect_power");
        }

        return null;
    }

    private static bool TryFindPerfectPowerFactor(BigInteger targetN, out BigInteger factor)
    {
        var bitLength = checked((int)targetN.GetBitLength());
        foreach (var exponent in PrimeSieve.PrimesUpTo(bitLength).Where(exponent => exponent >= 3))
        {
            var degree = checked((int)exponent);
            var root = IntegerMath.NthRoot(targetN, degree);
            if (BigInteger.Pow(root, degree) == targetN)
            {
                factor = root;
                return true;
            }
        }

        factor = BigInteger.Zero;
        return false;
    }
}

/// <summary>Creates the standard factors artifact for a factor-base precheck result.</summary>
internal static class EarlyFactor
{
    public static FactorsDocument Create(BigInteger targetN, BigInteger factor, string reason)
        => new(
            targetN,
            1,
            targetN,
            DependencyCount: 0,
            Results:
            [
                new FactorResultRecord(
                    DependencyId: "precheck",
                    Status: FactorizationStatus.FactorFound,
                    GcdMinus: null,
                    GcdPlus: null,
                    Factor1: factor,
                    Factor2: targetN / factor,
                    Reason: reason),
            ]);

    public static FactorsDocument Prime(BigInteger targetN)
        => new(
            targetN,
            1,
            targetN,
            DependencyCount: 0,
            Results:
            [
                new FactorResultRecord(
                    DependencyId: "precheck",
                    Status: FactorizationStatus.InputPrime,
                    GcdMinus: null,
                    GcdPlus: null,
                    Factor1: null,
                    Factor2: null,
                    Reason: "input_is_prime"),
            ]);
}
