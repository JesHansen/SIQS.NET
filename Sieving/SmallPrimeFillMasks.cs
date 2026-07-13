using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Sieving;

/// <summary>
/// AVX2 sieve-fill masks for the small factor-base primes (<c>p ≤ 31</c>). For each such prime and
/// each phase <c>s ∈ [0, p)</c>, <see cref="Masks"/><c>[i][s]</c> is a 32-byte vector holding the
/// prime's log credit at every byte position <c>k</c> with <c>k % p == s</c>; <see cref="NextPhase"/>
/// gives the phase of the following 32-byte chunk. Together they let the fill loop replace
/// <c>≈ blockSize / p</c> scalar writes with <c>blockSize / 32</c> vector adds.
/// <para><see cref="Count"/> is zero (and the arrays null) when AVX2 is unavailable, in which case the
/// scalar fill path handles every prime.</para>
/// </summary>
internal readonly record struct SmallPrimeFillMasks(Vector256<byte>[][]? Masks, int[][]? NextPhase, int Count)
{
    private const int SmallPrimeThreshold = 31;

    public static SmallPrimeFillMasks Build(FactorBaseData fb, byte[] byteLogP)
    {
        var count = 0;
        while (count < fb.Count && fb.Primes[count] <= SmallPrimeThreshold)
        {
            count++;
        }

        if (!Avx2.IsSupported || count == 0)
        {
            return new SmallPrimeFillMasks(null, null, 0);
        }

        var masks = new Vector256<byte>[count][];
        var nextPhase = new int[count][];
        var buf = new byte[32];
        for (var si = 0; si < count; si++)
        {
            var p = (int)fb.Primes[si];
            var lp = byteLogP[si];
            masks[si] = new Vector256<byte>[p];
            nextPhase[si] = new int[p];
            for (var s = 0; s < p; s++)
            {
                Array.Clear(buf);
                for (var k = s; k < 32; k += p) buf[k] = lp;
                masks[si][s] = Vector256.LoadUnsafe(ref buf[0]);
                nextPhase[si][s] = (s + p - 32 % p) % p;
            }
        }

        return new SmallPrimeFillMasks(masks, nextPhase, count);
    }
}
