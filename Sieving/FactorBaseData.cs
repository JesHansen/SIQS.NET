using System.Numerics;
using SIQS.Contracts.Files;

namespace Sieving;

/// <summary>
/// Column-oriented view of the factor base for the sieving hot path. Arrays are aligned and
/// indexed by position (ascending prime); <see cref="Columns"/> holds the original factor base
/// column index for each position (the value written to relation exponent maps).
/// </summary>
public sealed class FactorBaseData
{
    private int[]? _primes32;

    public required int Count { get; init; }
    public required long[] Primes { get; init; }

    /// <summary>
    /// 32-bit view of <see cref="Primes"/> for SIMD sieve kernels. Factor-base primes are already
    /// constrained to the signed 32-bit range by the int-indexed sieve representation. The view is
    /// materialized once, on first use, and safely shared by all workers.
    /// </summary>
    internal int[] Primes32
    {
        get
        {
            var primes = Volatile.Read(ref _primes32);
            if (primes is not null)
            {
                return primes;
            }

            var candidate = Array.ConvertAll(Primes, static p => checked((int)p));
            return Interlocked.CompareExchange(ref _primes32, candidate, null) ?? candidate;
        }
    }
    public required int[] Columns { get; init; }
    public required long[] Root1 { get; init; }
    public required long[] Root2 { get; init; }
    public required int[] LogP { get; init; }

    /// <summary>
    /// Modular inverses: <c>PrimeInverses[i] = Primes[i]⁻¹ mod 2⁶⁴</c> for odd primes;
    /// 0 for p = 2 (no inverse exists mod 2⁶⁴).
    /// </summary>
    public required ulong[] PrimeInverses { get; init; }

    /// <summary>
    /// Divisibility thresholds: <c>PrimeDivThresholds[i] = ⌊2⁶⁴ / Primes[i]⌋ = ulong.MaxValue / Primes[i]</c>.
    /// For odd p: <c>x</c> is divisible by p iff <c>(x * PrimeInverses[i]) ≤ PrimeDivThresholds[i]</c>,
    /// and when divisible the quotient is exactly <c>x * PrimeInverses[i]</c> (no BigInteger needed).
    /// </summary>
    public required ulong[] PrimeDivThresholds { get; init; }

    public required BigInteger TargetN { get; init; }
    public required BigInteger Multiplier { get; init; }
    public required BigInteger ScaledN { get; init; }
    public required long Bound { get; init; }
    public required double LogScale { get; init; }

    public static FactorBaseData From(FactorBaseDocument document)
    {
        var entries = document.Entries.OrderBy(e => e.Prime).ToArray();
        var count = entries.Length;
        var data = new FactorBaseData
        {
            Count = count,
            Primes = new long[count],
            Columns = new int[count],
            Root1 = new long[count],
            Root2 = new long[count],
            LogP = new int[count],
            PrimeInverses = new ulong[count],
            PrimeDivThresholds = new ulong[count],
            TargetN = document.Metadata.TargetN,
            Multiplier = document.Metadata.Multiplier,
            ScaledN = document.Metadata.ScaledN,
            Bound = document.Metadata.Bound,
            LogScale = document.Metadata.LogScale,
        };

        for (var i = 0; i < count; i++)
        {
            var p = entries[i].Prime;
            data.Primes[i] = p;
            data.Columns[i] = entries[i].Index;
            data.Root1[i] = entries[i].Root1;
            data.Root2[i] = entries[i].Root2;
            data.LogP[i] = entries[i].LogP;
            data.PrimeDivThresholds[i] = ulong.MaxValue / (ulong)p;
            data.PrimeInverses[i] = p == 2 ? 0UL : ModularInverse64((ulong)p);
        }

        return data;
    }

    /// <summary>
    /// Computes p⁻¹ mod 2⁶⁴ for odd p using Newton's method (6 lifting steps: 1→2→4→…→64 bits).
    /// </summary>
    internal static ulong ModularInverse64(ulong p)
    {
        // For odd p, p ≡ 1 mod 2, so x₀ = 1 is a valid starting point (p·1 ≡ 1 mod 2).
        // Each step x ← x·(2 − p·x) doubles the bit-precision of the inverse mod 2^k.
        ulong x = 1;
        x *= 2 - p * x; // mod 2^2
        x *= 2 - p * x; // mod 2^4
        x *= 2 - p * x; // mod 2^8
        x *= 2 - p * x; // mod 2^16
        x *= 2 - p * x; // mod 2^32
        x *= 2 - p * x; // mod 2^64
        return x;
    }
}
