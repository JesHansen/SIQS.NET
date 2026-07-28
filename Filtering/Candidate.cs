using System.Numerics;
using SIQS.Contracts;

namespace Filtering;

/// <summary>
/// A filtered relation candidate — either a full relation or a combined partial cycle — split into the
/// light structural half (<see cref="CandidateParts"/>) that reduction works on and a heavy arithmetic
/// payload the output build reads back at the end. A <see cref="CandidateStore"/> decides whether the
/// payload stays resident or is spilled to disk.
/// </summary>
internal sealed class Candidate
{
    private readonly CandidatePayload _resident;
    private readonly CandidateStore? _store;
    private readonly long _token;

    private Candidate(CandidateParts parts, CandidatePayload resident, CandidateStore? store, long token)
    {
        Kind = parts.Kind;
        Parity = parts.Parity;
        OrderKey = parts.OrderKey;
        CycleLength = parts.CycleLength;
        DuplicateFingerprint = parts.DuplicateFingerprint;
        _resident = resident;
        _store = store;
        _token = token;
    }

    public RelationKind Kind { get; }
    public ParityColumnSet Parity { get; }
    public string OrderKey { get; }
    public int CycleLength { get; }

    /// <summary>Precomputed 128-bit fingerprint of the canonical congruence key used for deduplication,
    /// so duplicate removal never needs to reload the arithmetic payload.</summary>
    public (ulong, ulong) DuplicateFingerprint { get; }

    /// <summary>Retrieves the arithmetic payload, reading it back from the store when it was spilled.</summary>
    public CandidatePayload LoadPayload() => _store is null ? _resident : _store.Load(_token);

    public static Candidate Resident(CandidateParts parts) => new(parts, parts.Payload, store: null, token: 0);

    public static Candidate Spilled(CandidateParts parts, CandidateStore store, long token)
        => new(parts, default, store, token);
}

/// <summary>The structural half of a candidate plus its payload, as produced before the store decides
/// on residency. Kind, parity, order key, cycle length, and the duplicate fingerprint stay resident on
/// the <see cref="Candidate"/>; <see cref="Payload"/> may be spilled.</summary>
internal readonly record struct CandidateParts(
    RelationKind Kind,
    ParityColumnSet Parity,
    string OrderKey,
    int CycleLength,
    (ulong, ulong) DuplicateFingerprint,
    CandidatePayload Payload)
{
    public static CandidateParts FromFull(RawRelationRecord full, BigInteger scaledN)
    {
        var payload = new CandidatePayload(
            new[] { full.RelationId }, full.T, full.FactorExponents, Array.Empty<BigInteger>());
        return Build(RelationKind.Full, full.RelationId, cycleLength: 1, payload, scaledN);
    }

    public static CandidateParts FromCombined(IReadOnlyList<PartialLite> sourceRelations, BigInteger scaledN)
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

            t = SIQS.Contracts.Numerics.IntegerMath.Mod(t * relation.T, scaledN);
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

        var payload = new CandidatePayload(
            sourceIds, t, SparseExponentVector.FromOwned(columns, values), largePrimes.ToArray());
        return Build(RelationKind.CombinedPartial, string.Join(' ', sourceIds), sourceIds.Length, payload, scaledN);
    }

    private static CandidateParts Build(
        RelationKind kind, string orderKey, int cycleLength, CandidatePayload payload, BigInteger scaledN)
    {
        // Key on the congruence itself, not on source ids: the sieve can rediscover the same lattice
        // point under a mirrored polynomial (t' = N - t, identical factorization), and such re-finds
        // only yield trivial x = +-y dependencies.
        var fingerprint = Fingerprint.Of(
            RelationCongruence.CanonicalKey(payload.T, payload.Exponents, payload.LargePrimes, scaledN));
        return new CandidateParts(kind, payload.Exponents.DeriveParity(), orderKey, cycleLength, fingerprint, payload);
    }
}
