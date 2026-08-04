// Copyright (c) 2014 Ben Buhrow and (c) 2022 Jeff Hurchalla.
// Copyright (c) 2026 SIQS.NET contributors.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted
// provided that the following conditions are met:
// 1. Redistributions of source code must retain the above copyright notice, this list of
//    conditions and the following disclaimer.
// 2. Redistributions in binary form must reproduce the above copyright notice, this list of
//    conditions and the following disclaimer in the documentation and/or other materials provided
//    with the distribution.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR
// IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
// FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR
// CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
// DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER
// IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT
// OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Sieving;

/// <summary>
/// Experimental managed 64-bit ECM splitter. The Suyama curve construction and Montgomery x/z
/// formulas and PRAC stage-one chains follow YAFU micro-ECM's FreeBSD-licensed scalar
/// implementation. Stage two deliberately uses a simple differential Montgomery ladder so it can
/// be measured independently before investing in a specialized continuation. Arithmetic stays in
/// <see cref="ulong"/>/<see cref="UInt128"/>.
/// </summary>
internal static class MicroEcm64
{
    private static readonly int[] Primes =
    [
        2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61,
        67, 71, 73, 79, 83, 89, 97, 101, 103, 107, 109, 113, 127, 131, 137,
        139, 149, 151, 157, 163, 167, 173, 179, 181, 191, 193, 197, 199, 211,
        223, 227, 229, 233, 239, 241, 251, 257, 263, 269, 271, 277, 281, 283,
        293, 307, 311, 313, 317, 331, 337, 347, 349, 353, 359, 367, 373, 379,
        383, 389, 397, 401, 409, 419, 421, 431, 433, 439, 443, 449, 457, 461,
        463, 467, 479, 487, 491, 499, 503, 509, 521, 523, 541, 547, 557, 563,
        569, 571, 577, 587, 593, 599, 601, 607, 613, 617, 619, 631, 641, 643,
        647, 653, 659, 661, 673, 677, 683, 691, 701, 709, 719, 727, 733, 739,
        743, 751, 757, 761, 769, 773, 787, 797, 809, 811, 821, 823, 827, 829,
        839, 853, 857, 859, 863, 877, 881, 883, 887, 907, 911, 919, 929, 937,
        941, 947, 953, 967, 971, 977, 983, 991, 997, 1009, 1013, 1019, 1021,
        1031, 1033, 1039, 1049, 1051, 1061, 1063, 1069, 1087, 1091, 1093,
        1097, 1103, 1109, 1117, 1123, 1129, 1151, 1153, 1163, 1171, 1181,
        1187, 1193, 1201, 1213, 1217, 1223, 1229, 1231, 1237, 1249, 1259,
        1277, 1279, 1283, 1289, 1291, 1297, 1301, 1303, 1307, 1319, 1321,
        1327, 1361, 1367, 1373, 1381, 1399, 1409, 1423, 1427, 1429, 1433,
        1439, 1447, 1451, 1453, 1459, 1471, 1481, 1483, 1487, 1489, 1493,
        1499, 1511, 1523, 1531, 1543, 1549, 1553, 1559, 1567, 1571, 1579,
        1583, 1597, 1601, 1607, 1609, 1613, 1619, 1621, 1627, 1637, 1657,
        1663, 1667, 1669, 1693, 1697, 1699, 1709, 1721, 1723, 1733, 1741,
        1747, 1753, 1759, 1777, 1783, 1787, 1789, 1801, 1811, 1823, 1831,
        1847, 1861, 1867, 1871, 1873, 1877, 1879, 1889, 1901, 1907, 1913,
        1931, 1933, 1949, 1951, 1973, 1979, 1987, 1993, 1997, 1999, 2003,
        2011, 2017, 2027, 2029, 2039, 2053, 2063, 2069, 2081, 2083, 2087,
        2089, 2099, 2111, 2113, 2129, 2131, 2137, 2141, 2143, 2153, 2161,
        2179, 2203, 2207, 2213, 2221, 2237, 2239, 2243, 2251, 2267, 2269,
        2273, 2281, 2287, 2293, 2297, 2309, 2311, 2333, 2339, 2341, 2347,
        2351, 2357, 2371, 2377, 2381, 2383, 2389, 2393, 2399, 2411, 2417,
        2423, 2437, 2441, 2447, 2459, 2467, 2473, 2477, 2503, 2521, 2531,
        2539, 2543, 2549, 2551, 2557, 2579, 2591, 2593, 2609, 2617, 2621,
        2633, 2647, 2657, 2659, 2663, 2671, 2677, 2683, 2687, 2689, 2693,
        2699, 2707, 2711, 2713, 2719, 2729, 2731, 2741, 2749, 2753, 2767,
        2777, 2789, 2791, 2797, 2801, 2803, 2819, 2833, 2837, 2843, 2851,
        2857, 2861, 2879, 2887, 2897, 2903, 2909, 2917, 2927, 2939, 2953,
        2957, 2963, 2969, 2971, 2999, 3001, 3011, 3019, 3023, 3037, 3041,
        3049, 3061, 3067, 3079, 3083, 3089, 3109, 3119, 3121, 3137, 3163,
        3167, 3169, 3181, 3187, 3191, 3203, 3209, 3217, 3221, 3229, 3251,
        3253, 3257, 3259, 3271, 3299, 3301, 3307, 3313, 3319, 3323, 3329,
        3331, 3343, 3347, 3359, 3361, 3371, 3373, 3389, 3391, 3407, 3413,
        3433, 3449, 3457, 3461, 3463, 3467, 3469, 3491, 3499, 3511, 3517,
        3527, 3529, 3533, 3539, 3541, 3547, 3557, 3559, 3571, 3581, 3583,
        3593, 3607, 3613, 3617, 3623, 3631, 3637, 3643, 3659, 3671, 3673,
        3677, 3691, 3697, 3701, 3709, 3719, 3727, 3733, 3739, 3761, 3767,
        3769, 3779, 3793, 3797, 3803, 3821, 3823, 3833, 3847, 3851, 3853,
        3863, 3877, 3881, 3889, 3907, 3911, 3917, 3919, 3923, 3929, 3931,
        3943, 3947, 3967, 3989, 4001, 4003, 4007, 4013, 4019, 4021, 4027,
        4049, 4051, 4057, 4073, 4079, 4091, 4093, 4099, 4111, 4127, 4129,
        4133, 4139, 4153, 4157, 4159, 4177, 4201, 4211, 4217, 4219, 4229,
        4231, 4241, 4243, 4253, 4259, 4261, 4271, 4273, 4283, 4289, 4297,
        4327, 4337, 4339, 4349, 4357, 4363, 4373, 4391, 4397, 4409, 4421,
        4423, 4441, 4447, 4451, 4457, 4463, 4481, 4483, 4493, 4507, 4513,
        4517, 4519, 4523, 4547, 4549, 4561, 4567, 4583, 4591, 4597, 4603,
        4621, 4637, 4639, 4643, 4649, 4651, 4657, 4663, 4673, 4679, 4691,
        4703, 4721, 4723, 4729, 4733, 4751, 4759, 4783, 4787, 4789, 4793,
        4799, 4801, 4813, 4817, 4831, 4861, 4871, 4877, 4889, 4903, 4909,
        4919, 4931, 4933, 4937, 4943, 4951, 4957, 4967, 4969, 4973, 4987,
        4993, 5003, 5009, 5011, 5021, 5023, 5039, 5051, 5059, 5077, 5081, 5087,
        5099, 5101, 5107, 5113, 5119, 5147
    ];

    private readonly record struct Point(ulong X, ulong Z);

    internal static int SelectPrefilterStage1Bound(ulong value)
        => BitLength(value) <= 47 ? 47 : BitLength(value) <= 55 ? 125 : 205;

    public static ulong TryFactor(ulong value, int stage1Bound = 70, int curves = 32, int stage2Multiplier = 0)
    {
        if (value < 2) return 1;
        if ((value & 1) == 0) return 2;

        var montgomery = Montgomery64.Create(value);
        var state = value ^ 0x9E3779B97F4A7C15UL;
        for (var curve = 0; curve < curves; curve++)
        {
            var buildFactor = TryBuildCurve(montgomery, ref state, out var point, out var a24);
            if (buildFactor > 1 && buildFactor < value) return buildFactor;
            if (buildFactor != 1) continue;

            var stage1Point = point;
            foreach (var prime in Primes)
            {
                if (prime > stage1Bound) break;
                for (var power = prime; power <= stage1Bound; power *= prime)
                {
                    stage1Point = prime == 2
                        ? Double(stage1Point, a24, montgomery)
                        : Prac(stage1Point, (ulong)prime, SelectPracRatio(prime), a24, montgomery);
                    if (power > stage1Bound / prime) break;
                }
            }

            var factor = Gcd(stage1Point.Z, value);
            if (factor > 1 && factor < value) return factor;
            if (factor == value) continue;

            if (stage2Multiplier > 1)
            {
                factor = TryStage2(
                    stage1Point, a24, montgomery, stage1Bound,
                    checked(stage1Bound * stage2Multiplier));
                if (factor > 1 && factor < value) return factor;
            }
        }

        return 1;
    }

    /// <summary>
    /// Runs ECM with a proper baby-step/giant-step (standard continuation) stage two: stage one to
    /// <paramref name="b1"/>, then a single-large-prime continuation to <paramref name="b2"/> that
    /// accumulates one running product of coordinate-difference terms and takes a single GCD.
    /// This is the Experiment 37 replacement for the slow per-prime stage two.
    /// </summary>
    public static ulong TryFactorStage2(ulong value, int b1, int b2, int curves)
    {
        if (value < 2) return 1;
        if ((value & 1) == 0) return 2;

        var montgomery = Montgomery64.Create(value);
        var plan = Stage2Plan.For(b1, b2);
        var state = value ^ 0x9E3779B97F4A7C15UL;
        for (var curve = 0; curve < curves; curve++)
        {
            var buildFactor = TryBuildCurve(montgomery, ref state, out var point, out var a24);
            if (buildFactor > 1 && buildFactor < value) return buildFactor;
            if (buildFactor != 1) continue;

            var stage1Point = RunStage1(point, a24, montgomery, b1);
            var factor = Gcd(stage1Point.Z, value);
            if (factor > 1 && factor < value) return factor;
            if (factor == value) continue;

            factor = TryStage2Standard(stage1Point, a24, montgomery, plan);
            if (factor > 1 && factor < value) return factor;
        }

        return 1;
    }

    private static Point RunStage1(Point point, ulong a24, Montgomery64 montgomery, int stage1Bound)
    {
        var stage1Point = point;
        foreach (var prime in Primes)
        {
            if (prime > stage1Bound) break;
            for (var power = prime; power <= stage1Bound; power *= prime)
            {
                stage1Point = prime == 2
                    ? Double(stage1Point, a24, montgomery)
                    : Prac(stage1Point, (ulong)prime, SelectPracRatio(prime), a24, montgomery);
                if (power > stage1Bound / prime) break;
            }
        }

        return stage1Point;
    }

    /// <summary>
    /// Standard continuation. For every prime p in (B1, B2] write p = i·D ± r with the baby residue
    /// r odd in [1, D/2]; the term x([iD]Q)·z([r]Q) − x([r]Q)·z([iD]Q) vanishes modulo a factor f
    /// exactly when [p]Q ≡ O (mod f). Accumulate the product of all terms and take one GCD.
    /// </summary>
    private static ulong TryStage2Standard(Point q, ulong a24, Montgomery64 montgomery, Stage2Plan plan)
    {
        var d = plan.GiantStep;

        // Baby table: baby[j] = [2j+1]Q, built by differential addition with step [2]Q.
        Span<ulong> babyX = stackalloc ulong[plan.BabyCount];
        Span<ulong> babyZ = stackalloc ulong[plan.BabyCount];
        var q2 = Double(q, a24, montgomery);
        var babyPrev = q;                                  // [1]Q
        babyX[0] = babyPrev.X;
        babyZ[0] = babyPrev.Z;
        if (plan.BabyCount > 1)
        {
            var babyCur = Add(q2, q, q, montgomery);       // [3]Q = [2]Q + [1]Q, difference [1]Q
            babyX[1] = babyCur.X;
            babyZ[1] = babyCur.Z;
            for (var j = 2; j < plan.BabyCount; j++)
            {
                var babyNext = Add(babyCur, q2, babyPrev, montgomery); // [2j+1]Q
                babyX[j] = babyNext.X;
                babyZ[j] = babyNext.Z;
                babyPrev = babyCur;
                babyCur = babyNext;
            }
        }

        // Giant chain: H_i = [i·D]Q, i = 1..MaxGiantIndex, via differential addition with step [D]Q.
        var giantStep = Multiply(q, (ulong)d, a24, montgomery);   // [D]Q
        var primeGiant = plan.PrimeGiant;
        var primeBaby = plan.PrimeBaby;
        var pointer = 0;
        var accumulator = montgomery.RModN;

        var hPrev = giantStep;                                    // H_1
        Accumulate(ref accumulator, ref pointer, 1, hPrev, babyX, babyZ, primeGiant, primeBaby, montgomery);

        if (plan.MaxGiantIndex >= 2)
        {
            var hCur = Double(giantStep, a24, montgomery);        // H_2
            Accumulate(ref accumulator, ref pointer, 2, hCur, babyX, babyZ, primeGiant, primeBaby, montgomery);
            for (var i = 3; i <= plan.MaxGiantIndex; i++)
            {
                var hNext = Add(hCur, giantStep, hPrev, montgomery); // H_i = H_{i-1} + [D]Q, diff H_{i-2}
                Accumulate(ref accumulator, ref pointer, i, hNext, babyX, babyZ, primeGiant, primeBaby, montgomery);
                hPrev = hCur;
                hCur = hNext;
            }
        }

        var factor = Gcd(accumulator, montgomery.N);
        return factor > 1 && factor < montgomery.N ? factor : 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Accumulate(
        ref ulong accumulator, ref int pointer, int giantIndex, Point giant,
        ReadOnlySpan<ulong> babyX, ReadOnlySpan<ulong> babyZ,
        int[] primeGiant, int[] primeBaby, Montgomery64 montgomery)
    {
        while (pointer < primeGiant.Length && primeGiant[pointer] == giantIndex)
        {
            var j = primeBaby[pointer];
            var term = Subtract(
                montgomery.Multiply(giant.X, babyZ[j]),
                montgomery.Multiply(babyX[j], giant.Z),
                montgomery.N);
            accumulator = montgomery.Multiply(accumulator, term);
            pointer++;
        }
    }

    private static ulong TryBuildCurve(
        Montgomery64 montgomery, ref ulong state, out Point point, out ulong a24)
    {
        state = unchecked(6364136223846793005UL * state + 1442695040888963407UL);
        var sigma = 7UL + (uint)(state >> 32);
        var u = montgomery.ToMontgomery(sigma);
        var v = Double(Double(u, montgomery), montgomery);
        u = Subtract(montgomery.Square(u), montgomery.ToMontgomery(5), montgomery.N);

        var u2 = montgomery.Square(u);
        var x = montgomery.Multiply(u2, u);
        var v2 = montgomery.Square(v);
        var z = montgomery.Multiply(v2, v);

        var denominator = montgomery.Multiply(Double(Double(Double(Double(v, montgomery), montgomery), montgomery), montgomery), x);
        var difference = Subtract(v, u, montgomery.N);
        var difference2 = montgomery.Square(difference);
        var difference3 = montgomery.Multiply(difference2, difference);
        var threeUPlusV = montgomery.Add(Double(u, montgomery), montgomery.Add(u, v));
        var numerator = montgomery.Multiply(difference3, threeUPlusV);

        var denominatorNormal = montgomery.FromMontgomery(denominator);
        var divisor = Gcd(denominatorNormal, montgomery.N);
        if (divisor != 1)
        {
            point = default;
            a24 = 0;
            return divisor;
        }

        var inverse = ModInverse(denominatorNormal, montgomery.N);
        a24 = montgomery.Multiply(numerator, montgomery.ToMontgomery(inverse));
        point = new(x, z);
        return 1;
    }

    private static ulong TryStage2(
        Point stage1Point, ulong a24, Montgomery64 montgomery, int stage1Bound, int stage2Bound)
    {
        var accumulator = montgomery.RModN;
        Span<ulong> batch = stackalloc ulong[32];
        var batchCount = 0;
        foreach (var prime in Primes)
        {
            if (prime <= stage1Bound) continue;
            if (prime > stage2Bound) break;

            var multiple = Multiply(stage1Point, (ulong)prime, a24, montgomery);
            batch[batchCount++] = multiple.Z;
            accumulator = montgomery.Multiply(accumulator, multiple.Z);
            if (batchCount < batch.Length) continue;

            var factor = Gcd(accumulator, montgomery.N);
            if (factor > 1 && factor < montgomery.N) return factor;
            if (factor == montgomery.N)
            {
                foreach (var z in batch)
                {
                    factor = Gcd(z, montgomery.N);
                    if (factor > 1 && factor < montgomery.N) return factor;
                }
            }

            accumulator = montgomery.RModN;
            batchCount = 0;
        }

        var finalFactor = Gcd(accumulator, montgomery.N);
        if (finalFactor > 1 && finalFactor < montgomery.N) return finalFactor;
        if (finalFactor == montgomery.N)
        {
            for (var i = 0; i < batchCount; i++)
            {
                finalFactor = Gcd(batch[i], montgomery.N);
                if (finalFactor > 1 && finalFactor < montgomery.N) return finalFactor;
            }
        }

        return 1;
    }

    private static Point Multiply(Point point, ulong scalar, ulong a24, Montgomery64 montgomery)
    {
        if (scalar == 1) return point;

        var r0 = point;
        var r1 = Double(point, a24, montgomery);
        var topBit = 63 - BitOperations.LeadingZeroCount(scalar);
        for (var bit = topBit - 1; bit >= 0; bit--)
        {
            if (((scalar >> bit) & 1) == 0)
            {
                r1 = Add(r0, r1, point, montgomery);
                r0 = Double(r0, a24, montgomery);
            }
            else
            {
                r0 = Add(r0, r1, point, montgomery);
                r1 = Double(r1, a24, montgomery);
            }
        }

        return r0;
    }

    private static Point Prac(
        Point point, ulong scalar, double ratio, ulong a24, Montgomery64 montgomery)
    {
        var shifts = BitOperations.TrailingZeroCount(scalar);
        scalar >>= shifts;
        var d = scalar;
        var r = (ulong)(d * ratio + 0.5);
        d = scalar - r;
        var e = 2 * r - scalar;

        var point1 = Double(point, a24, montgomery);
        var point2 = point;
        var point3 = point;

        while (d != e)
        {
            if (d < e)
            {
                (d, e) = (e, d);
                (point1, point2) = (point2, point1);
            }

            if (d - e <= e / 4 && (d + e) % 3 == 0)
            {
                d = (2 * d - e) / 3;
                e = (e - d) / 2;
                var point4 = Add(point1, point2, point3, montgomery);
                var point5 = Add(point4, point1, point2, montgomery);
                point2 = Add(point2, point4, point1, montgomery);
                point1 = point5;
            }
            else if (d - e <= e / 4 && (d - e) % 6 == 0)
            {
                d = (d - e) / 2;
                point2 = Add(point1, point2, point3, montgomery);
                point1 = Double(point1, a24, montgomery);
            }
            else if ((d + 3) / 4 <= e)
            {
                d -= e;
                var point4 = Add(point2, point1, point3, montgomery);
                (point2, point3) = (point4, point2);
            }
            else if ((d + e) % 2 == 0)
            {
                d = (d - e) / 2;
                point2 = Add(point2, point1, point3, montgomery);
                point1 = Double(point1, a24, montgomery);
            }
            else if (d % 2 == 0)
            {
                d /= 2;
                point3 = Add(point3, point1, point2, montgomery);
                point1 = Double(point1, a24, montgomery);
            }
            else if (d % 3 == 0)
            {
                d = d / 3 - e;
                var point4 = Double(point1, a24, montgomery);
                var point5 = Add(point1, point2, point3, montgomery);
                point1 = Add(point4, point1, point1, montgomery);
                point4 = Add(point4, point5, point3, montgomery);
                (point3, point2) = (point2, point4);
            }
            else if ((d + e) % 3 == 0)
            {
                d = (d - 2 * e) / 3;
                var point4 = Add(point1, point2, point3, montgomery);
                point2 = Add(point4, point1, point2, montgomery);
                point4 = Double(point1, a24, montgomery);
                point1 = Add(point1, point4, point1, montgomery);
            }
            else if ((d - e) % 3 == 0)
            {
                d = (d - e) / 3;
                var point4 = Add(point1, point2, point3, montgomery);
                point3 = Add(point3, point1, point2, montgomery);
                (point2, point4) = (point4, point2);
                point4 = Double(point1, a24, montgomery);
                point1 = Add(point1, point4, point1, montgomery);
            }
            else
            {
                e /= 2;
                point3 = Add(point3, point2, point1, montgomery);
                point2 = Double(point2, a24, montgomery);
            }
        }

        var result = Add(point1, point2, point3, montgomery);
        for (var i = 0; i < shifts; i++) result = Double(result, a24, montgomery);
        return result;
    }

    private static double SelectPracRatio(int prime) => prime switch
    {
        23 => 0.522786351415446049,
        29 or 41 or 47 => 0.548409048446403258,
        11 or 37 => 0.580178728295464130,
        _ => 0.618033988749894903,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point Add(Point left, Point right, Point difference, Montgomery64 montgomery)
    {
        var leftDifference = Subtract(left.X, left.Z, montgomery.N);
        var leftSum = montgomery.Add(left.X, left.Z);
        var rightDifference = Subtract(right.X, right.Z, montgomery.N);
        var rightSum = montgomery.Add(right.X, right.Z);
        var u = montgomery.Multiply(leftDifference, rightSum);
        var v = montgomery.Multiply(leftSum, rightDifference);
        var sum = montgomery.Add(u, v);
        var differenceUv = Subtract(u, v, montgomery.N);
        return new(
            montgomery.Multiply(montgomery.Square(sum), difference.Z),
            montgomery.Multiply(montgomery.Square(differenceUv), difference.X));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point Double(Point point, ulong a24, Montgomery64 montgomery)
    {
        var difference = Subtract(point.X, point.Z, montgomery.N);
        var sum = montgomery.Add(point.X, point.Z);
        var difference2 = montgomery.Square(difference);
        var sum2 = montgomery.Square(sum);
        var delta = Subtract(sum2, difference2, montgomery.N);
        var x = montgomery.Multiply(difference2, sum2);
        var z = montgomery.Multiply(delta, montgomery.Add(difference2, montgomery.Multiply(delta, a24)));
        return new(x, z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Double(ulong value, Montgomery64 montgomery)
        => montgomery.Add(value, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Subtract(ulong left, ulong right, ulong modulus)
        => left >= right ? left - right : modulus - (right - left);

    private static ulong ModInverse(ulong value, ulong modulus)
    {
        var oldR = modulus;
        var r = value;
        Int128 oldT = 0;
        Int128 t = 1;
        while (r != 0)
        {
            var quotient = oldR / r;
            (oldR, r) = (r, oldR - quotient * r);
            (oldT, t) = (t, oldT - (Int128)quotient * t);
        }

        if (oldR != 1) return 0;
        if (oldT < 0) oldT += modulus;
        return (ulong)oldT;
    }

    internal static ulong Gcd(ulong left, ulong right)
    {
        while (right != 0)
        {
            (left, right) = (right, left % right);
        }

        return left;
    }

    private static int BitLength(ulong value) => 64 - BitOperations.LeadingZeroCount(value);

    internal static bool PracMatchesBinaryLadder(ulong modulus, ulong scalar, double ratio)
    {
        var montgomery = Montgomery64.Create(modulus);
        var state = 1UL;
        if (TryBuildCurve(montgomery, ref state, out var point, out var a24) != 1) return true;
        var binary = Multiply(point, scalar, a24, montgomery);
        var prac = Prac(point, scalar, ratio, a24, montgomery);
        return montgomery.Multiply(binary.X, prac.Z) == montgomery.Multiply(prac.X, binary.Z);
    }

    /// <summary>
    /// A cached, curve-independent plan for the standard-continuation stage two of one (B1, B2) pair:
    /// the giant step D, the baby-table size, and every prime in (B1, B2] decomposed as
    /// p = giantIndex·D ± (2·babyIndex + 1). Built once and reused across residuals and curves.
    /// </summary>
    private sealed class Stage2Plan
    {
        private static readonly Dictionary<(int B1, int B2), Stage2Plan> Cache = new();

        public required int GiantStep { get; init; }
        public required int BabyCount { get; init; }
        public required int MaxGiantIndex { get; init; }
        public required int[] PrimeGiant { get; init; }
        public required int[] PrimeBaby { get; init; }

        public static Stage2Plan For(int b1, int b2)
        {
            lock (Cache)
            {
                if (!Cache.TryGetValue((b1, b2), out var plan))
                {
                    plan = Build(b1, b2);
                    Cache[(b1, b2)] = plan;
                }

                return plan;
            }
        }

        private static Stage2Plan Build(int b1, int b2)
        {
            if (b2 <= b1) throw new ArgumentOutOfRangeException(nameof(b2), b2, "B2 must exceed B1.");

            // Giant step: even, near 2·sqrt(B2) but never larger than B1 (so every prime > B1 lands
            // at giant index ≥ 1 with |p − i·D| ≤ D/2).
            var d = Math.Min(b1, 2 * (int)Math.Sqrt(b2));
            if (d < 2) d = 2;
            d &= ~1;
            var babyCount = (d / 2 + 1) / 2;

            // Primes in (B1, B2] by a simple sieve; they are already ascending, so the giant indices
            // are non-decreasing and the accumulate walk needs no sort.
            var composite = new bool[b2 + 1];
            var giants = new List<int>();
            var babies = new List<int>();
            var maxGiantIndex = 1;
            for (var candidate = 2; candidate <= b2; candidate++)
            {
                if (composite[candidate]) continue;
                for (var multiple = (long)candidate * candidate; multiple <= b2; multiple += candidate)
                {
                    composite[multiple] = true;
                }

                if (candidate <= b1) continue;
                var giantIndex = (candidate + d / 2) / d;
                var residue = Math.Abs(candidate - giantIndex * d);
                giants.Add(giantIndex);
                babies.Add((residue - 1) / 2);
                if (giantIndex > maxGiantIndex) maxGiantIndex = giantIndex;
            }

            return new Stage2Plan
            {
                GiantStep = d,
                BabyCount = babyCount,
                MaxGiantIndex = maxGiantIndex,
                PrimeGiant = giants.ToArray(),
                PrimeBaby = babies.ToArray(),
            };
        }
    }
}
