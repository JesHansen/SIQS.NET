using System.Numerics;
using SIQS.Contracts.Numerics;

namespace SIQS.Contracts;

/// <summary>The outcome of attempting to extract factors from one dependency (or a precheck).</summary>
public sealed record FactorResultRecord(
    string DependencyId,
    FactorizationStatus Status,
    BigInteger? GcdMinus,
    BigInteger? GcdPlus,
    BigInteger? Factor1,
    BigInteger? Factor2,
    string? Reason,
    bool? Factor1IsComposite = null,
    bool? Factor2IsComposite = null,
    string? PrimalityTest = null,
    string? PrimalityRange = null)
{
    /// <summary>
    /// Builds a <see cref="FactorizationStatus.FactorFound"/> row, asserting the factor pair
    /// actually multiplies back to <paramref name="targetN"/> and marking each factor composite
    /// (via <see cref="Primality.IsBailliePswProbablePrime"/>) when it is not itself a prime. A
    /// non-trivial GCD is only guaranteed to be a proper divisor of N, not a prime one: for an N
    /// with three or more prime factors, one side of the pair can still be composite.
    /// </summary>
    public static FactorResultRecord FactorFound(
        string dependencyId,
        BigInteger targetN,
        BigInteger? gcdMinus,
        BigInteger? gcdPlus,
        BigInteger factor1,
        BigInteger factor2,
        string? reason = null)
    {
        if (factor1 * factor2 != targetN)
        {
            throw new InvalidOperationException(
                $"Factor pair ({factor1}, {factor2}) for dependency '{dependencyId}' does not multiply to {targetN}.");
        }

        return new FactorResultRecord(
            dependencyId, FactorizationStatus.FactorFound, gcdMinus, gcdPlus, factor1, factor2, reason,
            Factor1IsComposite: !Primality.IsBailliePswProbablePrime(factor1),
            Factor2IsComposite: !Primality.IsBailliePswProbablePrime(factor2));
    }
}
