using System.Globalization;
using System.Numerics;

namespace SIQS.Contracts.Distributed;

/// <summary>
/// Maps raw relations to and from the primitive-only records used by the streaming wire protocol.
/// </summary>
public static class RelationUploadCodec
{
    public static RelationUploadRecord ToUploadRecord(RawRelationRecord relation) => new(
        relation.RelationId,
        relation.Kind,
        relation.PolyId,
        Dec(relation.A),
        Dec(relation.B),
        Dec(relation.C),
        relation.X,
        Dec(relation.T),
        relation.Sign,
        relation.FactorExponents.ToDictionary(),
        relation.ParityColumns.ToArray(),
        relation.LargePrimes.Select(Dec).ToArray());

    public static RawRelationRecord FromUploadRecord(RelationUploadRecord relation)
        => new(
            relation.RelationId,
            relation.Kind,
            relation.PolyId,
            ParseBigInteger(relation.A),
            ParseBigInteger(relation.B),
            ParseBigInteger(relation.C),
            relation.X,
            ParseBigInteger(relation.T),
            relation.Sign,
            relation.FactorExponents,
            relation.ParityColumns,
            relation.LargePrimes.Count == 1 ? ParseBigInteger(relation.LargePrimes[0]) : null)
        {
            LargePrimes = relation.LargePrimes.Select(ParseBigInteger).ToArray(),
        };

    private static string Dec(BigInteger value) => value.ToString(CultureInfo.InvariantCulture);

    private static BigInteger ParseBigInteger(string value) => BigInteger.Parse(value, CultureInfo.InvariantCulture);
}
