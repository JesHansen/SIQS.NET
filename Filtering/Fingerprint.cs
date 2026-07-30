using System.Numerics;
using SIQS.Contracts;

namespace Filtering;

/// <summary>
/// A 128-bit FNV-1a-style fingerprint of a relation's canonical congruence, folded directly over
/// the numeric payload (canonical t, exponent pairs, large primes) so no key string is ever built.
/// Duplicate sets store these 16 bytes; a false match needs a full 128-bit collision, far below the
/// error rates tolerated elsewhere.
/// </summary>
internal static class Fingerprint
{
    private const ulong Seed1 = 0xcbf29ce484222325UL;
    private const ulong Seed2 = 0x9E3779B97F4A7C15UL;
    private const ulong Prime1 = 0x100000001B3UL;
    private const ulong Prime2 = 0xD1B54A32D192ED03UL;

    // Folded between sections and elements so adjacent fields cannot alias each other.
    private const uint Separator = 0x1F1F1F1F;

    public static (ulong, ulong) Of(
        BigInteger canonicalT, SparseExponentVector exponents, IReadOnlyList<BigInteger> largePrimes)
    {
        var h1 = Seed1;
        var h2 = Seed2;
        FoldBigInteger(ref h1, ref h2, canonicalT);
        Fold(ref h1, ref h2, Separator);

        var columns = exponents.ColumnsSpan;
        var values = exponents.ValuesSpan;
        for (var i = 0; i < columns.Length; i++)
        {
            Fold(ref h1, ref h2, (uint)columns[i]);
            Fold(ref h1, ref h2, (uint)values[i]);
        }

        Fold(ref h1, ref h2, Separator);
        foreach (var q in largePrimes)
        {
            FoldBigInteger(ref h1, ref h2, q);
            Fold(ref h1, ref h2, Separator);
        }

        return (h1, h2);
    }

    private static void FoldBigInteger(ref ulong h1, ref ulong h2, BigInteger value)
    {
        var byteCount = value.GetByteCount();
        Span<byte> buffer = byteCount <= 128 ? stackalloc byte[128] : new byte[byteCount];
        value.TryWriteBytes(buffer, out var written);
        for (var i = 0; i < written; i++)
        {
            Fold(ref h1, ref h2, buffer[i]);
        }
    }

    private static void Fold(ref ulong h1, ref ulong h2, uint v)
    {
        h1 = (h1 ^ v) * Prime1;
        h2 = (h2 ^ v) * Prime2;
    }
}
