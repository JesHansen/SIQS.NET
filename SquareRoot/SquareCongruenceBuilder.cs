using System.Numerics;
using SIQS.Contracts;
using SIQS.Contracts.Numerics;
using SIQS.Contracts.Text;

namespace SquareRoot;

/// <summary>The reconstructed values in one dependency's congruence of squares.</summary>
internal readonly record struct SquareCongruence(BigInteger X, BigInteger Y);

/// <summary>Builds and validates a square congruence from selected filtered relations.</summary>
internal static class SquareCongruenceBuilder
{
    public static bool TryBuild(
        IReadOnlyList<FilteredRelationRecord> relations,
        BigInteger targetN,
        IReadOnlyDictionary<int, BigInteger> primeByColumn,
        out SquareCongruence congruence,
        out string? invalidReason)
    {
        var x = BigInteger.One;
        var exponentSums = new Dictionary<int, BigInteger>();
        foreach (var relation in relations)
        {
            x = IntegerMath.Mod(x * IntegerMath.Mod(relation.T, targetN), targetN);
            if (relation.Kind == RelationKind.CombinedPartial && relation.LargePrimes.Count == 0)
            {
                return Invalid("missing_large_prime", out congruence, out invalidReason);
            }

            if (!ExponentMapFormat.ParityColumns(relation.Exponents).SequenceEqual(relation.ParityColumns))
            {
                return Invalid("parity_mismatch", out congruence, out invalidReason);
            }

            foreach (var (column, exponent) in relation.Exponents)
            {
                exponentSums[column] = exponentSums.GetValueOrDefault(column) + exponent;
            }
        }

        if (exponentSums.Values.Any(exponent => !exponent.IsEven))
        {
            return Invalid("odd_exponent_sum", out congruence, out invalidReason);
        }

        if (!TryBuildY(relations, exponentSums, targetN, primeByColumn, out var y))
        {
            return Invalid("unknown_factor_base_column", out congruence, out invalidReason);
        }

        if (BigInteger.ModPow(x, 2, targetN) != BigInteger.ModPow(y, 2, targetN))
        {
            return Invalid("congruence_not_square", out congruence, out invalidReason);
        }

        congruence = new SquareCongruence(x, y);
        invalidReason = null;
        return true;
    }

    private static bool TryBuildY(
        IReadOnlyList<FilteredRelationRecord> relations,
        IReadOnlyDictionary<int, BigInteger> exponentSums,
        BigInteger targetN,
        IReadOnlyDictionary<int, BigInteger> primeByColumn,
        out BigInteger y)
    {
        y = BigInteger.One;
        foreach (var (column, exponent) in exponentSums)
        {
            if (column == 0)
            {
                continue;
            }

            if (!primeByColumn.TryGetValue(column, out var prime))
            {
                return false;
            }

            y = IntegerMath.Mod(y * BigInteger.ModPow(prime, exponent / 2, targetN), targetN);
        }

        foreach (var largePrime in relations
                     .Where(relation => relation.Kind == RelationKind.CombinedPartial)
                     .SelectMany(relation => relation.LargePrimes))
        {
            y = IntegerMath.Mod(y * IntegerMath.Mod(largePrime, targetN), targetN);
        }

        return true;
    }

    private static bool Invalid(string reason, out SquareCongruence congruence, out string? invalidReason)
    {
        congruence = default;
        invalidReason = reason;
        return false;
    }
}
