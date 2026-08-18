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

    public static FactorsDocument? TryFind(
        BigInteger targetN,
        bool allowTinyTrialDivision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (targetN == 2)
        {
            return EarlyFactor.Prime(targetN, "exact_trial_division", "n <= 2");
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
            cancellationToken.ThrowIfCancellationRequested();
            if (targetN == prime)
            {
                return EarlyFactor.Prime(targetN, "exact_trial_division", $"p <= {PrecheckBound}");
            }

            if (targetN % prime == 0)
            {
                return EarlyFactor.Create(targetN, prime, "small_prime_factor");
            }
        }

        var squareRoot = IntegerMath.Sqrt(targetN);
        if (allowTinyTrialDivision && squareRoot <= TinyInputTrialDivisionBound)
        {
            foreach (var prime in PrimeSieve.PrimesUpTo((long)squareRoot, cancellationToken).Where(prime => prime > PrecheckBound))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (targetN % prime == 0)
                {
                    return EarlyFactor.Create(targetN, prime, "tiny_input_trial_division");
                }
            }

            return EarlyFactor.Prime(
                targetN, "exact_trial_division", $"trial division through floor(sqrt(n)) <= {TinyInputTrialDivisionBound}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (Primality.IsWithinDeterministicRange(targetN) && Primality.IsProbablePrime(targetN))
        {
            return EarlyFactor.Prime(
                targetN,
                "deterministic_miller_rabin_13_witnesses",
                $"n < {Primality.DeterministicUpperBound}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!Primality.IsWithinDeterministicRange(targetN) &&
            Primality.IsBailliePswProbablePrime(targetN))
        {
            return EarlyFactor.ProbablePrime(
                targetN,
                "baillie_psw",
                $"n >= {Primality.DeterministicUpperBound}; no proof certificate");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (TryFindPerfectPowerFactor(targetN, cancellationToken, out root))
        {
            return EarlyFactor.Create(targetN, root, "perfect_power");
        }

        return null;
    }

    private static bool TryFindPerfectPowerFactor(
        BigInteger targetN,
        CancellationToken cancellationToken,
        out BigInteger factor)
    {
        var bitLength = checked((int)targetN.GetBitLength());
        foreach (var exponent in PrimeSieve.PrimesUpTo(bitLength, cancellationToken).Where(exponent => exponent >= 3))
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                FactorResultRecord.FactorFound(
                    dependencyId: "precheck",
                    targetN: targetN,
                    gcdMinus: null,
                    gcdPlus: null,
                    factor1: factor,
                    factor2: targetN / factor,
                    reason: reason),
            ]);

    public static FactorsDocument Prime(BigInteger targetN, string test, string range)
        => PrimalityResult(targetN, FactorizationStatus.InputPrime, "input_is_prime", test, range);

    public static FactorsDocument ProbablePrime(BigInteger targetN, string test, string range)
        => PrimalityResult(
            targetN, FactorizationStatus.InputProbablePrime, "input_is_probable_prime", test, range);

    private static FactorsDocument PrimalityResult(
        BigInteger targetN,
        FactorizationStatus status,
        string reason,
        string test,
        string range)
        => new(
            targetN,
            1,
            targetN,
            DependencyCount: 0,
            Results:
            [
                new FactorResultRecord(
                    DependencyId: "precheck",
                    Status: status,
                    GcdMinus: null,
                    GcdPlus: null,
                    Factor1: null,
                    Factor2: null,
                    Reason: reason,
                    PrimalityTest: test,
                    PrimalityRange: range),
            ]);
}
