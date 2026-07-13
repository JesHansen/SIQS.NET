namespace SIQS.Contracts;

/// <summary>
/// Describes an artifact produced during a factorization run. Paths are relative to the job
/// directory; absolute paths belong to runtime services, not persisted contracts.
/// </summary>
public sealed record ArtifactDescriptor(
    string Name,
    ArtifactKind Kind,
    SiqsPhase ProducedBy,
    string RelativePath,
    bool Required);
