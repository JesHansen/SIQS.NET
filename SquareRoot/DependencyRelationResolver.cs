using SIQS.Contracts;

namespace SquareRoot;

/// <summary>Resolves a dependency's stable relation identifiers to filtered relation records.</summary>
internal static class DependencyRelationResolver
{
    public static bool TryResolve(
        IReadOnlyList<string> relationIds,
        IReadOnlyDictionary<string, FilteredRelationRecord> relationsById,
        out IReadOnlyList<FilteredRelationRecord> relations,
        CancellationToken cancellationToken = default)
    {
        var resolved = new FilteredRelationRecord[relationIds.Count];
        for (var i = 0; i < relationIds.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!relationsById.TryGetValue(relationIds[i], out var relation))
            {
                relations = Array.Empty<FilteredRelationRecord>();
                return false;
            }

            resolved[i] = relation;
        }

        relations = resolved;
        return true;
    }
}
