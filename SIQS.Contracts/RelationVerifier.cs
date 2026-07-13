using System.Numerics;
using SIQS.Contracts.Files;
using SIQS.Contracts.Numerics;

namespace SIQS.Contracts;

/// <summary>
/// Independently re-derives a raw relation's congruence to confirm it is genuinely smooth over the
/// declared factor base (plus large primes) before it is trusted. This is the anti-poison check for
/// relations arriving from untrusted distributed clients: whereas structural validation only checks
/// column ranges and declared parity, this recomputes the polynomial value and checks the exact
/// integer identity <c>A·(A·x² + 2B·x + C) == sign·∏ p_col^e · ∏ q</c>, which in turn implies the
/// matrix invariant <c>t² ≡ (that product) (mod ScaledN)</c>. A relation that passes is valid for
/// the linear algebra regardless of how (or by whom) it was produced.
/// </summary>
public sealed class RelationVerifier
{
    private readonly BigInteger _scaledN;
    private readonly IReadOnlyDictionary<int, long> _primeByColumn;
    private readonly int _factorBaseColumnCount;
    private readonly long _factorBaseBound;
    private readonly long _largePrimeBound;
    private readonly long? _largePrime2Bound;

    /// <param name="factorBase">The factor base the client was told to sieve against.</param>
    /// <param name="largePrimeBound">Upper bound for a single large prime cofactor.</param>
    /// <param name="largePrime2Bound">
    /// Upper bound for each factor of a two-large-prime cofactor, or null when two-large-prime
    /// relations are not permitted for this job.
    /// </param>
    public RelationVerifier(FactorBaseDocument factorBase, long largePrimeBound, long? largePrime2Bound = null)
    {
        _scaledN = factorBase.Metadata.ScaledN;
        _primeByColumn = factorBase.Entries.ToDictionary(e => e.Index, e => e.Prime);
        _factorBaseColumnCount = factorBase.Entries.Count;
        _factorBaseBound = factorBase.Metadata.Bound;
        _largePrimeBound = largePrimeBound;
        _largePrime2Bound = largePrime2Bound;
    }

    /// <summary>Returns true when the relation re-derives to a valid smooth congruence.</summary>
    public bool IsValid(RawRelationRecord relation) => TryVerify(relation, out _);

    /// <summary>
    /// Verifies the relation, returning false with a human-readable <paramref name="error"/> on the
    /// first failing check.
    /// </summary>
    public bool TryVerify(RawRelationRecord relation, out string? error)
    {
        // Reconstruct M = sign · ∏ p_col^e from the declared exponent map (column 0 is the -1 sign
        // column; every other column must be a real factor base prime).
        var m = BigInteger.One;
        foreach (var (column, exponent) in relation.FactorExponents)
        {
            if (exponent < 1)
            {
                error = $"Relation '{relation.RelationId}' has non-positive exponent {exponent} at column {column}.";
                return false;
            }

            if (column == 0)
            {
                m *= BigInteger.Pow(BigInteger.MinusOne, exponent);
                continue;
            }

            if (!_primeByColumn.TryGetValue(column, out var prime))
            {
                error = $"Relation '{relation.RelationId}' references factor base column {column} outside [0, {_factorBaseColumnCount}].";
                return false;
            }

            m *= BigInteger.Pow(prime, exponent);
        }

        if (!TryValidateLargePrimes(relation, out var largePrimeProduct, out error))
        {
            return false;
        }

        m *= largePrimeProduct;

        // Exact provenance + smoothness: A·(A·x² + 2B·x + C) must equal the reconstructed product
        // as integers (this is the SIQS identity A·Q(x) = t² − ScaledN factored over the base).
        var value = relation.A * ((BigInteger)relation.X * relation.X) + 2 * relation.B * relation.X + relation.C;
        if (relation.A * value != m)
        {
            error = $"Relation '{relation.RelationId}' polynomial value does not match its declared factorization.";
            return false;
        }

        // Matrix invariant: t² ≡ M (mod ScaledN). Guards that T is the correct root of the congruence.
        if (BigInteger.ModPow(relation.T, 2, _scaledN) != IntegerMath.Mod(m, _scaledN))
        {
            error = $"Relation '{relation.RelationId}' fails the congruence t² ≡ product (mod ScaledN).";
            return false;
        }

        // Declared parity columns must be exactly the odd-exponent columns.
        var expectedParity = relation.FactorExponents
            .Where(kv => (kv.Value & 1) == 1)
            .Select(kv => kv.Key)
            .OrderBy(c => c);
        if (!expectedParity.SequenceEqual(relation.ParityColumns.OrderBy(c => c)))
        {
            error = $"Relation '{relation.RelationId}' declared parity does not match its exponents.";
            return false;
        }

        error = null;
        return true;
    }

    private bool TryValidateLargePrimes(RawRelationRecord relation, out BigInteger product, out string? error)
    {
        product = BigInteger.One;
        var primes = relation.LargePrimes;

        if (relation.Kind == RelationKind.Full)
        {
            if (primes.Count != 0)
            {
                error = $"Relation '{relation.RelationId}' is a full relation but declares large primes.";
                return false;
            }

            error = null;
            return true;
        }

        if (primes.Count is not (1 or 2))
        {
            error = $"Partial relation '{relation.RelationId}' must declare one or two large primes, not {primes.Count}.";
            return false;
        }

        // A single large prime is bounded by the 1LP bound; two large primes are each bounded by the
        // (smaller) 2LP bound, which must be enabled for the job.
        var upperBound = primes.Count == 1
            ? _largePrimeBound
            : _largePrime2Bound ?? -1;
        if (upperBound < 0)
        {
            error = $"Relation '{relation.RelationId}' declares two large primes but two-large-prime relations are not permitted.";
            return false;
        }

        foreach (var q in primes)
        {
            if (q <= _factorBaseBound || q > upperBound)
            {
                error = $"Relation '{relation.RelationId}' large prime {q} is outside ({_factorBaseBound}, {upperBound}].";
                return false;
            }

            if (!Primality.IsProbablePrime(q))
            {
                error = $"Relation '{relation.RelationId}' large prime {q} is not prime.";
                return false;
            }

            product *= q;
        }

        error = null;
        return true;
    }
}
