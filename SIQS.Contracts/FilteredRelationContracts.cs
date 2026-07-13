using System.Numerics;

namespace SIQS.Contracts;

/// <summary>A relation after filtering (original full relation or a combined partial).</summary>
public sealed record FilteredRelationRecord : IEquatable<FilteredRelationRecord>
{
    public FilteredRelationRecord(
        string RelationId,
        RelationKind Kind,
        IReadOnlyList<string> SourceRelationIds,
        BigInteger T,
        int Sign,
        IReadOnlyDictionary<int, int> Exponents,
        IReadOnlyList<int> ParityColumns,
        BigInteger? LargePrime)
    {
        this.RelationId = RelationId;
        this.Kind = Kind;
        this.SourceRelationIds = SourceRelationIds.ToArray();
        this.T = T;
        this.Sign = Sign;
        this.Exponents = new SparseExponentVector(Exponents);
        this.ParityColumns = new ParityColumnSet(ParityColumns);
        this.LargePrime = LargePrime;
        _largePrimes = LargePrime is { } q ? new[] { q } : Array.Empty<BigInteger>();
    }

    public string RelationId { get; init; }
    public RelationKind Kind { get; init; }
    public IReadOnlyList<string> SourceRelationIds { get; init; }
    public BigInteger T { get; init; }
    public int Sign { get; init; }
    public SparseExponentVector Exponents { get; init; }
    public ParityColumnSet ParityColumns { get; init; }
    public BigInteger? LargePrime { get; init; }

    private IReadOnlyList<BigInteger> _largePrimes;

    public IReadOnlyList<BigInteger> LargePrimes
    {
        get => _largePrimes;
        init => _largePrimes = value.ToArray();
    }

    public bool Equals(FilteredRelationRecord? other)
        => other is not null
            && RelationId == other.RelationId
            && Kind == other.Kind
            && T == other.T && Sign == other.Sign
            && LargePrime == other.LargePrime
            && StructuralEquality.SequenceEqual(LargePrimes, other.LargePrimes)
            && StructuralEquality.SequenceEqual(SourceRelationIds, other.SourceRelationIds)
            && Exponents.Equals(other.Exponents)
            && ParityColumns.Equals(other.ParityColumns);

    public override int GetHashCode()
        => HashCode.Combine(RelationId, Kind, T, Sign, LargePrime, Exponents.Count, ParityColumns.Count, LargePrimes.Count);
}
