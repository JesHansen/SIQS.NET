using System.Numerics;
using SIQS.Contracts;
using SIQS.Contracts.Numerics;
using SIQS.Contracts.Text;

namespace Filtering;

/// <summary>
/// Canonical representations of a relation's congruence: large-prime normalization, compact exponent
/// columns, and the canonical congruence key used for duplicate detection and cycle combination.
/// </summary>
internal static class RelationCongruence
{
    public static IReadOnlyList<BigInteger> NormalizeLargePrimes(RawRelationRecord partial)
        => partial.LargePrimes.Count > 0
            ? partial.LargePrimes
            : partial.LargePrime is { } q
                ? new[] { q }
                : Array.Empty<BigInteger>();

    public static SparseExponentVector CompactExponents(IReadOnlyDictionary<int, int> exponents)
    {
        return new SparseExponentVector(exponents);
    }

    public static string CanonicalKey(
        BigInteger t,
        SparseExponentVector exponents,
        IEnumerable<BigInteger> largePrimes,
        BigInteger scaledN)
    {
        var tc = IntegerMath.Mod(t, scaledN);
        tc = BigInteger.Min(tc, scaledN - tc);
        return $"{tc}::{ExponentMapFormat.Write(exponents)}::{string.Join(' ', largePrimes)}";
    }

    public static string CanonicalKey(
        BigInteger t,
        IReadOnlyDictionary<int, int> exponents,
        IEnumerable<BigInteger> largePrimes,
        BigInteger scaledN)
    {
        var tc = IntegerMath.Mod(t, scaledN);
        tc = BigInteger.Min(tc, scaledN - tc);
        return $"{tc}::{ExponentMapFormat.Write(exponents)}::{string.Join(' ', largePrimes)}";
    }

}
