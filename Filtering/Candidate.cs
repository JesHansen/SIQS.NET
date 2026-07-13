using System.Numerics;
using SIQS.Contracts;
using SIQS.Contracts.Numerics;

namespace Filtering;

/// <summary>
/// A filtered relation candidate — either a full relation or a combined partial cycle — carrying the
/// data filtering needs to dedupe, prune, order, and emit it.
/// </summary>
internal sealed class Candidate
{
    private Candidate(
        RelationKind kind,
        string[] sourceIds,
        BigInteger t,
        SparseExponentVector exponents,
        BigInteger[] largePrimes,
        string orderKey,
        int cycleLength)
    {
        Kind = kind;
        SourceIds = sourceIds;
        T = t;
        Exponents = exponents;
        Parity = exponents.DeriveParity();
        LargePrimes = largePrimes;
        LargePrime = largePrimes.Length == 1 ? largePrimes[0] : null;
        OrderKey = orderKey;
        CycleLength = cycleLength;
    }

    public RelationKind Kind { get; }
    public string[] SourceIds { get; }
    public BigInteger T { get; }
    public SparseExponentVector Exponents { get; }
    public ParityColumnSet Parity { get; }
    public BigInteger? LargePrime { get; }
    public BigInteger[] LargePrimes { get; }
    public string OrderKey { get; }
    public int CycleLength { get; }

    public int ExponentAtColumnZero()
        => Exponents.TryGetExponent(0, out var exponent) ? exponent : 0;

    public IReadOnlyDictionary<int, int> ExponentMap() => Exponents.ToDictionary();

    // Key on the congruence itself, not on source ids: the sieve can rediscover the same
    // lattice point under a mirrored polynomial (t' = N - t, identical factorization), and
    // such re-finds only yield trivial x = +-y dependencies.
    public string DuplicateKey(BigInteger scaledN) =>
        RelationCongruence.CanonicalKey(T, Exponents, LargePrimes, scaledN);

    public static Candidate FromFull(RawRelationRecord full)
    {
        return new Candidate(
            RelationKind.Full,
            new[] { full.RelationId },
            full.T,
            full.FactorExponents,
            Array.Empty<BigInteger>(),
            full.RelationId,
            1);
    }

    public static Candidate FromCombined(IReadOnlyList<PartialLite> sourceRelations, BigInteger scaledN)
    {
        sourceRelations = sourceRelations.OrderBy(r => r.RelationId, StringComparer.Ordinal).ToArray();
        var exponents = new Dictionary<int, int>();
        var largePrimeCounts = new Dictionary<BigInteger, int>();
        var t = BigInteger.One;
        foreach (var relation in sourceRelations)
        {
            var relationColumns = relation.Exponents.ColumnsSpan;
            var relationValues = relation.Exponents.ValuesSpan;
            for (var i = 0; i < relationColumns.Length; i++)
            {
                var col = relationColumns[i];
                exponents[col] = exponents.GetValueOrDefault(col) + relationValues[i];
            }

            foreach (var q in relation.LargePrimes)
            {
                largePrimeCounts[q] = largePrimeCounts.GetValueOrDefault(q) + 1;
            }

            t = IntegerMath.Mod(t * relation.T, scaledN);
        }

        var largePrimes = new List<BigInteger>();
        foreach (var (q, count) in largePrimeCounts.OrderBy(kv => kv.Key))
        {
            if ((count & 1) != 0)
            {
                throw new FormatException($"Large prime {q} has odd multiplicity in a graph cycle.");
            }

            for (var i = 0; i < count / 2; i++)
            {
                largePrimes.Add(q);
            }
        }

        var sourceIds = sourceRelations.Select(r => r.RelationId).ToArray();
        var columns = exponents
            .Where(pair => pair.Value != 0)
            .Select(pair => pair.Key)
            .ToArray();
        Array.Sort(columns);
        var values = new int[columns.Length];
        for (var i = 0; i < columns.Length; i++)
        {
            values[i] = exponents[columns[i]];
        }

        return new Candidate(
            RelationKind.CombinedPartial,
            sourceIds,
            t,
            SparseExponentVector.FromOwned(columns, values),
            largePrimes.ToArray(),
            string.Join(' ', sourceIds),
            sourceIds.Length);
    }
}
