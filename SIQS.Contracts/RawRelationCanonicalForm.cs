using System.Globalization;
using System.Numerics;

namespace SIQS.Contracts;

/// <summary>Canonical parsed-record serialization used for duplicate raw-relation checks.</summary>
public static class RawRelationCanonicalForm
{
    public static string Content(RawRelationRecord record)
        => string.Join('|',
            record.RelationId,
            SiqsTokens.ToToken(record.Kind),
            record.PolyId,
            Dec(record.A),
            Dec(record.B),
            Dec(record.C),
            record.X.ToString(CultureInfo.InvariantCulture),
            Dec(record.T),
            record.Sign.ToString(CultureInfo.InvariantCulture),
            string.Join(' ', record.FactorExponents
                .OrderBy(kv => kv.Key)
                .Select(kv => $"{kv.Key.ToString(CultureInfo.InvariantCulture)}:{kv.Value.ToString(CultureInfo.InvariantCulture)}")),
            string.Join(' ', record.ParityColumns.Select(c => c.ToString(CultureInfo.InvariantCulture))),
            string.Join(' ', record.LargePrimes.Select(Dec)));

    private static string Dec(BigInteger value) => value.ToString(CultureInfo.InvariantCulture);
}
