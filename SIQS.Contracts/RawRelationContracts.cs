using System.Numerics;

namespace SIQS.Contracts;

/// <summary>A raw relation as emitted by the sieving phase (full or single-large-prime partial).</summary>
public sealed record RawRelationRecord : IEquatable<RawRelationRecord>
{
    public RawRelationRecord(
        string RelationId,
        RelationKind Kind,
        string PolyId,
        BigInteger A,
        BigInteger B,
        BigInteger C,
        long X,
        BigInteger T,
        int Sign,
        IReadOnlyDictionary<int, int> FactorExponents,
        IReadOnlyList<int> ParityColumns,
        BigInteger? LargePrime)
    {
        this.RelationId = RelationId;
        this.Kind = Kind;
        this.PolyId = PolyId;
        this.A = A;
        this.B = B;
        this.C = C;
        this.X = X;
        this.T = T;
        this.Sign = Sign;
        this.FactorExponents = new SparseExponentVector(FactorExponents);
        this.ParityColumns = new ParityColumnSet(ParityColumns);
        this.LargePrime = LargePrime;
        _largePrimes = LargePrime is { } q ? new[] { q } : Array.Empty<BigInteger>();
    }

    /// <summary>Adopts already validated vectors directly, avoiding the defensive re-copy above.</summary>
    public RawRelationRecord(
        string RelationId,
        RelationKind Kind,
        string PolyId,
        BigInteger A,
        BigInteger B,
        BigInteger C,
        long X,
        BigInteger T,
        int Sign,
        SparseExponentVector FactorExponents,
        ParityColumnSet ParityColumns,
        BigInteger? LargePrime)
    {
        this.RelationId = RelationId;
        this.Kind = Kind;
        this.PolyId = PolyId;
        this.A = A;
        this.B = B;
        this.C = C;
        this.X = X;
        this.T = T;
        this.Sign = Sign;
        this.FactorExponents = FactorExponents;
        this.ParityColumns = ParityColumns;
        this.LargePrime = LargePrime;
        _largePrimes = LargePrime is { } q ? new[] { q } : Array.Empty<BigInteger>();
    }

    public string RelationId { get; init; }
    public RelationKind Kind { get; init; }
    public string PolyId { get; init; }
    public BigInteger A { get; init; }
    public BigInteger B { get; init; }
    public BigInteger C { get; init; }
    public long X { get; init; }
    public BigInteger T { get; init; }
    public int Sign { get; init; }
    public SparseExponentVector FactorExponents { get; init; }
    public ParityColumnSet ParityColumns { get; init; }
    public BigInteger? LargePrime { get; init; }

    private IReadOnlyList<BigInteger> _largePrimes;

    public IReadOnlyList<BigInteger> LargePrimes
    {
        get => _largePrimes;
        init => _largePrimes = value.ToArray();
    }

    public bool Equals(RawRelationRecord? other)
        => other is not null
            && RelationId == other.RelationId
            && Kind == other.Kind
            && PolyId == other.PolyId
            && A == other.A && B == other.B && C == other.C
            && X == other.X && T == other.T && Sign == other.Sign
            && LargePrime == other.LargePrime
            && StructuralEquality.SequenceEqual(LargePrimes, other.LargePrimes)
            && FactorExponents.Equals(other.FactorExponents)
            && ParityColumns.Equals(other.ParityColumns);

    public override int GetHashCode()
        => HashCode.Combine(RelationId, Kind, PolyId, T, Sign, FactorExponents.Count, ParityColumns.Count, LargePrimes.Count);
}
