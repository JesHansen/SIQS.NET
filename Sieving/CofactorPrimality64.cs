using System.Runtime.CompilerServices;

namespace Sieving;

/// <summary>Allocation-free deterministic primality checks for unsigned 64-bit cofactors.</summary>
internal static class CofactorPrimality64
{
    private const ulong SmallWitnessLimit = 3_215_031_751UL;
    private static readonly ulong[] SmallPrimeScreen = [2, 3, 5, 7, 11, 13];
    private static readonly ulong[] SmallWitnesses = [2, 3, 5, 7];
    private static readonly ulong[] FullWitnesses = [2, 325, 9375, 28178, 450775, 9780504, 1795265022];
    private static readonly SmallFactorDivisor[] SmallFactorPrefilter = BuildSmallFactorPrefilter();

    public static bool IsBase2ProbablePrime(ulong value)
    {
        if (value < 2) return false;
        if ((value & 1) == 0) return value == 2;

        var (oddPart, powersOfTwo) = DecomposeWitnessExponent(value);
        return PassesWitness(value, 2, oddPart, powersOfTwo, Montgomery64.Create(value));
    }

    public static bool IsPrime(ulong value)
    {
        if (value < 2) return false;

        foreach (var prime in SmallPrimeScreen)
        {
            if (value == prime) return true;
            if (value % prime == 0) return false;
        }

        var (oddPart, powersOfTwo) = DecomposeWitnessExponent(value);
        var montgomery = Montgomery64.Create(value);
        var witnesses = value < SmallWitnessLimit ? SmallWitnesses : FullWitnesses;
        foreach (var witness in witnesses)
        {
            if (!PassesWitness(value, witness, oddPart, powersOfTwo, montgomery)) return false;
        }

        return true;
    }

    public static bool HasSmallFactorAtOrBelow(ulong value, ulong bound)
    {
        foreach (var divisor in SmallFactorPrefilter)
        {
            if (divisor.Prime > bound) break;
            if (value * divisor.Inverse <= divisor.Threshold) return true;
        }

        return false;
    }

    public static ulong PowMod(ulong value, ulong exponent, ulong modulus)
    {
        if (modulus == 0) throw new DivideByZeroException();
        if (modulus >= 3 && (modulus & 1) != 0) return Montgomery64.Create(modulus).PowMod(value, exponent);

        var result = 1UL % modulus;
        var factor = value % modulus;
        while (exponent > 0)
        {
            if ((exponent & 1) != 0) result = MulMod(result, factor, modulus);
            exponent >>= 1;
            if (exponent != 0) factor = MulMod(factor, factor, modulus);
        }

        return result;
    }

    private static (ulong OddPart, int PowersOfTwo) DecomposeWitnessExponent(ulong value)
    {
        var oddPart = value - 1;
        var powersOfTwo = 0;
        while ((oddPart & 1) == 0) { oddPart >>= 1; powersOfTwo++; }
        return (oddPart, powersOfTwo);
    }

    private static bool PassesWitness(ulong value, ulong witness, ulong oddPart, int powersOfTwo, Montgomery64 montgomery)
    {
        witness %= value;
        if (witness == 0) return true;

        var residue = montgomery.Pow(montgomery.ToMontgomery(witness), oddPart);
        var one = montgomery.RModN;
        var negativeOne = value - montgomery.RModN;
        if (residue == one || residue == negativeOne) return true;

        for (var round = 1; round < powersOfTwo; round++)
        {
            residue = montgomery.Square(residue);
            if (residue == negativeOne) return true;
        }

        return false;
    }

    private static SmallFactorDivisor[] BuildSmallFactorPrefilter()
    {
        ulong[] primes = [3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71, 73, 79, 83, 89, 97, 101, 103, 107, 109, 113, 127];
        var result = new SmallFactorDivisor[primes.Length];
        for (var index = 0; index < primes.Length; index++)
        {
            var prime = primes[index];
            result[index] = new SmallFactorDivisor(prime, FactorBaseData.ModularInverse64(prime), ulong.MaxValue / prime);
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong MulMod(ulong left, ulong right, ulong modulus) => (ulong)((UInt128)left * right % modulus);

    private readonly record struct SmallFactorDivisor(ulong Prime, ulong Inverse, ulong Threshold);
}
