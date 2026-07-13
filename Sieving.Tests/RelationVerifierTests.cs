using System.Numerics;
using Factorbase;
using Sieving;
using SIQS.Contracts;
using SIQS.Contracts.Files;

namespace Sieving.Tests;

public class RelationVerifierTests
{
    private static FactorBaseDocument FactorBase(string n, long bound, BigInteger multiplier)
        => FactorBaseGenerator.Generate(new FactorBaseOptions(BigInteger.Parse(n), bound, multiplier)).FactorBase!;

    private static (FactorBaseDocument Fb, SievingParameters Params, SievingResult Result) Sieve()
    {
        var fb = FactorBase("1022117", 1000, 1);
        var parameters = SievingParameters.Default(fb) with
        {
            SieveHalfInterval = 20_000, APrimeCount = 2, APrimeWindowSize = 24,
            ErrorMargin = 20, RelationTarget = 40, PolynomialCount = 5_000, Parallelism = 1,
        };
        return (fb, parameters, SievingEngine.Sieve(fb, parameters));
    }

    private static RelationVerifier VerifierFor(FactorBaseDocument fb, SievingParameters p)
        => new(fb, p.LargePrimeBound, p.EnableTwoLargePrimes ? p.LargePrime2Bound : null);

    private static RawRelationRecord With(
        RawRelationRecord r,
        IReadOnlyDictionary<int, int>? exponents = null,
        BigInteger? t = null,
        BigInteger? largePrime = null)
        => new(
            r.RelationId, r.Kind, r.PolyId, r.A, r.B, r.C, r.X,
            t ?? r.T, r.Sign,
            exponents ?? r.FactorExponents.ToDictionary(kv => kv.Key, kv => kv.Value),
            r.ParityColumns.ToArray(),
            largePrime ?? r.LargePrime)
        {
            LargePrimes = largePrime is { } q ? new[] { q } : r.LargePrimes,
        };

    [Fact]
    public void Accepts_all_genuine_sieved_relations()
    {
        var (fb, p, result) = Sieve();
        var verifier = VerifierFor(fb, p);

        Assert.NotEmpty(result.FullRelations);
        Assert.NotEmpty(result.Partials);
        foreach (var r in result.FullRelations.Concat(result.Partials))
        {
            Assert.True(verifier.TryVerify(r, out var error), $"rejected genuine relation {r.RelationId}: {error}");
        }
    }

    [Fact]
    public void Rejects_an_inflated_exponent_that_preserves_parity()
    {
        var (fb, p, result) = Sieve();
        var verifier = VerifierFor(fb, p);
        var genuine = result.FullRelations[0];

        // Bump a factor column by 2 so declared parity still matches; only the arithmetic identity breaks.
        var column = genuine.FactorExponents.First(kv => kv.Key != 0).Key;
        var exponents = genuine.FactorExponents.ToDictionary(kv => kv.Key, kv => kv.Value);
        exponents[column] += 2;

        Assert.False(verifier.IsValid(With(genuine, exponents: exponents)));
    }

    [Fact]
    public void Rejects_a_tampered_root()
    {
        var (fb, p, result) = Sieve();
        var verifier = VerifierFor(fb, p);
        var genuine = result.FullRelations[0];

        Assert.False(verifier.IsValid(With(genuine, t: genuine.T + 1)));
    }

    [Fact]
    public void Rejects_a_forged_large_prime()
    {
        var (fb, p, result) = Sieve();
        var verifier = VerifierFor(fb, p);
        var partial = result.Partials[0];

        // Swap in a different prime above the bound: the reconstructed product no longer matches A·Q(x).
        Assert.False(verifier.IsValid(With(partial, largePrime: partial.LargePrime!.Value + 2)));
    }

    [Fact]
    public void Rejects_an_out_of_range_factor_column()
    {
        var (fb, p, result) = Sieve();
        var verifier = VerifierFor(fb, p);
        var genuine = result.FullRelations[0];
        var exponents = genuine.FactorExponents.ToDictionary(kv => kv.Key, kv => kv.Value);
        exponents[fb.Entries.Count + 5] = 1;

        Assert.False(verifier.TryVerify(With(genuine, exponents: exponents), out var error));
        Assert.Contains("column", error);
    }
}
