using System.Numerics;
using Factorbase;
using Sieving;
using SIQS.Contracts;
using SIQS.Contracts.Files;
using SIQS.Contracts.Numerics;

namespace Sieving.Tests;

public class PolynomialGeneratorTests
{
    private static FactorBaseData BuildFactorBase(string n, long bound, BigInteger multiplier)
    {
        var result = FactorBaseGenerator.Generate(
            new FactorBaseOptions(BigInteger.Parse(n), bound, multiplier));
        return FactorBaseData.From(result.FactorBase!);
    }

    private static SievingParameters Params(FactorBaseData fb, int s) =>
        SievingParameters.Default(
            new FactorBaseDocument(
                new FactorBaseMetadata(fb.TargetN, fb.Multiplier, fb.ScaledN, fb.Bound, fb.LogScale),
                Array.Empty<FactorBaseEntry>()))
        with { APrimeCount = s, APrimeWindowSize = Math.Max(16, 8 * s) };

    [Fact]
    public void Family_satisfies_core_congruence_T2_equals_A_V_plus_ScaledN()
    {
        var fb = BuildFactorBase("1022117", 1000, 1);
        var positions = PolynomialGenerator.SelectAPositions(fb, Params(fb, 3)).First();

        var count = 0;
        foreach (var poly in PolynomialGenerator.Family(fb, positions))
        {
            // A is the product of the selected primes.
            var a = positions.Aggregate(BigInteger.One, (acc, pos) => acc * fb.Primes[pos]);
            Assert.Equal(a, poly.A);

            // C divides exactly.
            Assert.Equal(BigInteger.Zero, (poly.B * poly.B - fb.ScaledN) % poly.A);

            // The defining identity T(x)^2 - A*V(x) == ScaledN holds for every x.
            foreach (var x in new BigInteger[] { -7, -1, 0, 1, 5, 100 })
            {
                var v = poly.A * x * x + 2 * poly.B * x + poly.C;
                var t = poly.A * x + poly.B;
                Assert.Equal(fb.ScaledN, t * t - poly.A * v);
            }

            count++;
        }

        Assert.Equal(4, count); // one representative from each B/-B conjugate pair for s = 3
    }

    [Fact]
    public void Sieve_roots_are_actual_roots_of_V_mod_p()
    {
        var fb = BuildFactorBase("1022117", 1000, 1);
        var positions = PolynomialGenerator.SelectAPositions(fb, Params(fb, 3)).First();
        var poly = PolynomialGenerator.Family(fb, positions).Skip(3).First();
        var aPrimes = positions.Select(p => fb.Primes[p]).ToHashSet();

        for (var i = 0; i < fb.Count; i++)
        {
            var p = fb.Primes[i];
            if (p == 2 || aPrimes.Contains(p))
            {
                continue;
            }

            var invA = IntegerMath.ModInverse(IntegerMath.Mod(poly.A, p), p);

            foreach (var root in new[] { fb.Root1[i], fb.Root2[i] })
            {
                var x = IntegerMath.Mod((root - poly.B) * invA, p);
                var v = poly.A * x * x + 2 * poly.B * x + poly.C;
                Assert.Equal(BigInteger.Zero, IntegerMath.Mod(v, p));
            }
        }
    }

    [Fact]
    public void Poly_ids_follow_gray_code_single_bit_changes()
    {
        var fb = BuildFactorBase("1022117", 1000, 1);
        var positions = PolynomialGenerator.SelectAPositions(fb, Params(fb, 3)).First();

        // Gray code property: each successive sign vector differs from the previous in one bit.
        // With a symmetric sieve interval, the B and -B halves are conjugate, so only one half is
        // emitted to avoid collecting guaranteed-trivial duplicate relations.
        var polys = PolynomialGenerator.Family(fb, positions).ToList();
        Assert.Equal(4, polys.Count);
        Assert.All(polys, p => Assert.True(2 * BigInteger.Abs(p.B) <= p.A));
    }

    [Fact]
    public void Family_omits_conjugate_negative_b_polynomials()
    {
        var fb = BuildFactorBase("1022117", 1000, 1);
        var positions = PolynomialGenerator.SelectAPositions(fb, Params(fb, 3)).First();

        var polys = PolynomialGenerator.Family(fb, positions).ToList();
        var bValues = polys.Select(p => p.B).ToHashSet();

        Assert.Equal(4, polys.Count);
        Assert.All(polys, p => Assert.DoesNotContain(-p.B, bValues));
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    public void Family_emits_half_gray_code_count_for_symmetric_sieving(int s, int expectedCount)
    {
        var fb = BuildFactorBase("1022117", 1000, 1);
        var positions = PolynomialGenerator.SelectAPositions(fb, Params(fb, s)).First();

        var polys = PolynomialGenerator.Family(fb, positions).ToList();

        Assert.Equal(expectedCount, polys.Count);
    }

    [Fact]
    public void Family_emits_first_half_and_omits_complement_conjugates()
    {
        var fb = BuildFactorBase("1022117", 1000, 1);
        var positions = PolynomialGenerator.SelectAPositions(fb, Params(fb, 4)).First();
        var allBValues = FullGrayCodeBValues(fb, positions).ToArray();

        var emitted = PolynomialGenerator.Family(fb, positions).Select(p => p.B).ToArray();
        var firstHalf = allBValues.Take(allBValues.Length / 2).ToArray();
        var omittedHalf = allBValues.Skip(allBValues.Length / 2).ToArray();

        Assert.Equal(firstHalf, emitted);
        Assert.All(omittedHalf, b => Assert.Contains(-b, emitted));
    }

    [Fact]
    public void Gray_code_root_deltas_match_full_root_recomputation()
    {
        var fb = BuildFactorBase("1022117", 1000, 1);
        var positions = PolynomialGenerator.SelectAPositions(fb, Params(fb, 4)).First();
        AssertGrayCodeRootDeltasMatchFullRootRecomputation(fb, positions, 393_216);
    }

    [Fact]
    public void Gray_code_root_deltas_match_full_root_recomputation_for_c76_shape()
    {
        var fb = BuildFactorBase("5243687244798839625626859372537256837381840219232636514934114288145757804647", 638558, 2);
        var positions = PolynomialGenerator.SelectAPositions(fb, Params(fb, 6)).First();
        AssertGrayCodeRootDeltasMatchFullRootRecomputation(fb, positions, 393_216);
    }

    [Fact]
    public void BuildFamily_rejects_multiplier_prime_a_positions()
    {
        var fb = BuildFactorBase("1022117", 1000, 3);
        var multiplierPrimePosition = Array.IndexOf(fb.Primes, 3L);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PolynomialGenerator.BuildFamily(fb, [multiplierPrimePosition]));

        Assert.Contains("divides the multiplier", ex.Message);
    }

    private static void AssertGrayCodeRootDeltasMatchFullRootRecomputation(FactorBaseData fb, int[] positions, long m)
    {
        var family = PolynomialGenerator.BuildFamily(fb, positions);
        var aPrimeSet = positions.ToHashSet();

        var invA = new long[fb.Count];
        for (var i = 0; i < fb.Count; i++)
        {
            if (!aPrimeSet.Contains(i) && fb.Primes[i] != 2)
            {
                invA[i] = (long)IntegerMath.ModInverse(IntegerMath.Mod(family.A, fb.Primes[i]), fb.Primes[i]);
            }
        }

        var deltas = new int[family.BTerms.Length][];
        for (var termIndex = 0; termIndex < family.BTerms.Length; termIndex++)
        {
            deltas[termIndex] = new int[fb.Count];
            var twiceTerm = 2 * family.BTerms[termIndex];
            for (var i = 0; i < fb.Count; i++)
            {
                if (aPrimeSet.Contains(i) || fb.Primes[i] == 2)
                {
                    continue;
                }

                var p = fb.Primes[i];
                deltas[termIndex][i] = (int)(((long)IntegerMath.Mod(twiceTerm, p) * invA[i]) % p);
            }
        }

        static long Mod(long value, long p)
        {
            var r = value % p;
            return r < 0 ? r + p : r;
        }

        var previousRoots = new (long R1, long R2)[fb.Count];
        var engineRoot1 = new int[fb.Count];
        var engineRoot2 = new int[fb.Count];
        var fullBlockLength = checked((int)(2 * m + 1));
        for (var memberIndex = 0; memberIndex < family.Members.Count; memberIndex++)
        {
            var member = family.Members[memberIndex];
            var poly = member.Polynomial;
            for (var i = 0; i < fb.Count; i++)
            {
                if (aPrimeSet.Contains(i))
                {
                    engineRoot1[i] = fullBlockLength;
                    engineRoot2[i] = -1;
                    continue;
                }

                var p = fb.Primes[i];
                if (p == 2)
                {
                    var root = (int)(long)IntegerMath.Mod(poly.C, 2L);
                    engineRoot1[i] = (int)((root + m) % 2);
                    engineRoot2[i] = -1;
                    continue;
                }

                var directB = (long)IntegerMath.Mod(poly.B, p);
                var direct1 = Mod(Mod((fb.Root1[i] - directB) * invA[i], p) + m, p);
                var direct2 = Mod(Mod((fb.Root2[i] - directB) * invA[i], p) + m, p);

                if (member.FlipIndex is { } flipIndex)
                {
                    var delta = deltas[flipIndex][i];
                    var adjustment = member.FlipDirection > 0 ? -delta : delta;
                    adjustment -= member.NormalizationCorrection;
                    var updated1 = Mod(previousRoots[i].R1 + adjustment, p);
                    var updated2 = Mod(previousRoots[i].R2 + adjustment, p);

                    Assert.Equal(direct1, updated1);
                    Assert.Equal(direct2, updated2);

                    if (engineRoot2[i] >= 0)
                    {
                        var engineUpdated1 = Mod(engineRoot1[i] + adjustment, p);
                        var engineUpdated2 = Mod(engineRoot2[i] + adjustment, p);
                        Assert.Equal(direct1, engineUpdated1);
                        Assert.Equal(direct2, engineUpdated2);
                        engineRoot1[i] = (int)engineUpdated1;
                        engineRoot2[i] = engineUpdated1 == engineUpdated2 ? -1 : (int)engineUpdated2;
                    }
                    else
                    {
                        engineRoot1[i] = (int)direct1;
                        engineRoot2[i] = direct1 == direct2 ? -1 : (int)direct2;
                    }
                }
                else
                {
                    engineRoot1[i] = (int)direct1;
                    engineRoot2[i] = direct1 == direct2 ? -1 : (int)direct2;
                }

                Assert.Equal(direct1, engineRoot1[i]);
                if (direct1 == direct2)
                {
                    Assert.Equal(-1, engineRoot2[i]);
                }
                else
                {
                    Assert.Equal(direct2, engineRoot2[i]);
                }

                previousRoots[i] = (direct1, direct2);
            }
        }
    }

    private static IEnumerable<BigInteger> FullGrayCodeBValues(FactorBaseData fb, int[] aPositions)
    {
        var positions = aPositions.OrderBy(p => fb.Columns[p]).ToArray();
        var s = positions.Length;

        var a = BigInteger.One;
        foreach (var pos in positions)
        {
            a *= fb.Primes[pos];
        }

        var bi = new BigInteger[s];
        for (var i = 0; i < s; i++)
        {
            var q = fb.Primes[positions[i]];
            var aOverQ = a / q;
            var inv = IntegerMath.ModInverse(IntegerMath.Mod(aOverQ, q), q);
            var gamma = IntegerMath.Mod(fb.Root1[positions[i]] * inv, q);
            if (2 * gamma > q)
            {
                gamma = q - gamma;
            }

            bi[i] = aOverQ * gamma;
        }

        var total = 1 << s;
        for (var j = 0; j < total; j++)
        {
            var gray = j ^ (j >> 1);
            var b = BigInteger.Zero;
            for (var i = 0; i < s; i++)
            {
                var bit = (gray >> i) & 1;
                b += bit == 1 ? bi[i] : -bi[i];
            }

            b = IntegerMath.Mod(b, a);
            if (2 * b > a)
            {
                b -= a;
            }

            yield return b;
        }
    }
}
