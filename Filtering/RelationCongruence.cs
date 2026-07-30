using System.Numerics;
using SIQS.Contracts;
using SIQS.Contracts.Numerics;

namespace Filtering;

/// <summary>
/// Canonical representations of a relation's congruence: large-prime normalization and the canonical
/// congruence fingerprint used for duplicate detection and cycle combination.
/// </summary>
internal static class RelationCongruence
{
    public static IReadOnlyList<BigInteger> NormalizeLargePrimes(RawRelationRecord partial)
        => partial.LargePrimes.Count > 0
            ? partial.LargePrimes
            : partial.LargePrime is { } q
                ? new[] { q }
                : Array.Empty<BigInteger>();

    /// <summary>
    /// Fingerprints the congruence itself: t is canonicalized to min(t mod N, N - t mod N) so a
    /// relation and its mirror (t' = N - t, identical factorization) fingerprint identically.
    /// </summary>
    public static (ulong, ulong) FingerprintOf(
        BigInteger t,
        SparseExponentVector exponents,
        IReadOnlyList<BigInteger> largePrimes,
        BigInteger scaledN)
    {
        var tc = IntegerMath.Mod(t, scaledN);
        tc = BigInteger.Min(tc, scaledN - tc);
        return Fingerprint.Of(tc, exponents, largePrimes);
    }
}
