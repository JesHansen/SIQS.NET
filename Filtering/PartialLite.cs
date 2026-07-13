using System.Numerics;
using SIQS.Contracts;

namespace Filtering;

/// <summary>
/// The subset of a partial record that cycle combination needs: no polynomial coefficients,
/// no boxed exponent dictionary, no parity list.
/// </summary>
internal sealed class PartialLite
{
    private PartialLite(string relationId, BigInteger t, SparseExponentVector exponents, BigInteger[] largePrimes)
    {
        RelationId = relationId;
        T = t;
        Exponents = exponents;
        LargePrimes = largePrimes;
    }

    public string RelationId { get; }
    public BigInteger T { get; }
    public SparseExponentVector Exponents { get; }
    public BigInteger[] LargePrimes { get; }

    public static PartialLite From(RawRelationRecord record)
    {
        return new PartialLite(
            record.RelationId,
            record.T,
            record.FactorExponents,
            RelationCongruence.NormalizeLargePrimes(record).ToArray());
    }
}
