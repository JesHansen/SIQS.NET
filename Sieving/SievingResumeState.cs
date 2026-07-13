using SIQS.Contracts;

namespace Sieving;

/// <summary>Optional state used to continue an interrupted sieving run from durable raw batches.</summary>
public sealed record SievingResumeState(
    IReadOnlySet<int> CompletedAIndices,
    IEnumerable<RawRelationRecord> ExistingRelations,
    Action<int>? MarkAIndexCompleted = null);
