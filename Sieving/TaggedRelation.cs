using SIQS.Contracts;

namespace Sieving;

/// <summary>
/// A raw relation tagged with the canonical coordinates (A-index, polynomial-in-family index, x)
/// from which its stable relation and polynomial ids are derived by
/// <see cref="TaggedRelationExtensions.WithStableIds"/>.
/// </summary>
internal readonly record struct TaggedRelation(
    RawRelationRecord Record,
    int AIdx,
    int PolyIdx,
    long X,
    bool IsPartial);
