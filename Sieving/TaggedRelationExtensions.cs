using SIQS.Contracts;

namespace Sieving;

/// <summary>Behaviour attached to <see cref="TaggedRelation"/> values.</summary>
internal static class TaggedRelationExtensions
{
    /// <summary>
    /// Stamps the deterministic canonical relation and polynomial ids, derived from the tagged
    /// (aIdx, polyIdx, x) coordinates, onto the underlying raw relation record.
    /// </summary>
    public static RawRelationRecord WithStableIds(this TaggedRelation tagged)
        => tagged.Record with
        {
            RelationId = $"R{tagged.AIdx:D6}_{tagged.PolyIdx:D4}_{XKey(tagged.X)}",
            PolyId = $"P{tagged.AIdx:D6}_{tagged.PolyIdx:D4}",
        };

    private static string XKey(long x)
        => x < 0 ? $"M{Math.Abs(x):D12}" : $"P{x:D12}";
}
