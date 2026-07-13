using System.Runtime.CompilerServices;

namespace Sieving;

internal readonly struct Montgomery64
{
    public ulong N { get; }
    public ulong NPrime { get; }
    public ulong RModN { get; }
    public ulong R2ModN { get; }

    private Montgomery64(ulong n, ulong nPrime, ulong rModN, ulong r2ModN)
    {
        N = n;
        NPrime = nPrime;
        RModN = rModN;
        R2ModN = r2ModN;
    }

    public static Montgomery64 Create(ulong oddN)
    {
        if (oddN < 3 || (oddN & 1) == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(oddN), oddN, "Montgomery modulus must be an odd integer greater than 1.");
        }

        var r = (UInt128)1 << 64;
        var rModN = (ulong)(r % oddN);
        var r2ModN = (ulong)((UInt128)rModN * rModN % oddN);
        var nPrime = unchecked(0UL - FactorBaseData.ModularInverse64(oddN));
        return new Montgomery64(oddN, nPrime, rModN, r2ModN);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong ToMontgomery(ulong value)
        => Reduce((UInt128)(value % N) * R2ModN);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong FromMontgomery(ulong value)
        => Reduce(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong Multiply(ulong left, ulong right)
        => Reduce((UInt128)left * right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong Square(ulong value)
        => Multiply(value, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong Add(ulong left, ulong right)
    {
        var sum = unchecked(left + right);
        if (sum < left || sum >= N)
        {
            sum = unchecked(sum - N);
        }

        return sum;
    }

    public ulong Pow(ulong valueMontgomery, ulong exponent)
    {
        var result = RModN;
        var b = valueMontgomery;
        while (exponent > 0)
        {
            if ((exponent & 1) != 0)
            {
                result = Multiply(result, b);
            }

            exponent >>= 1;
            if (exponent != 0)
            {
                b = Square(b);
            }
        }

        return result;
    }

    public ulong PowMod(ulong value, ulong exponent)
        => FromMontgomery(Pow(ToMontgomery(value), exponent));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ulong Reduce(UInt128 value)
    {
        var m = unchecked((ulong)value * NPrime);
        var product = (UInt128)m * N;
        var sum = unchecked(value + product);
        var carry = sum < value;
        var reduced = (ulong)(sum >> 64);
        if (carry || reduced >= N)
        {
            reduced = unchecked(reduced - N);
        }

        return reduced;
    }
}
