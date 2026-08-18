namespace SIQS.Contracts;

/// <summary>Identifies the SIQS algorithmic phase a piece of work or output belongs to.</summary>
public enum SiqsPhase
{
    Pipeline,
    FactorBase,
    Sieving,
    Filtering,
    LinearAlgebra,
    SquareRoot,
    Ui,
}

/// <summary>Durable status of a whole factorization job.</summary>
public enum JobStatus
{
    Created,
    Running,
    Canceling,
    Canceled,
    Failed,
    CompletedNoFactor,
    CompletedPrime,
    CompletedProbablePrime,
    CompletedFactorFound,
    CompletedTrivialFactor,
}

/// <summary>Status of a single phase within a job.</summary>
public enum PhaseStatus
{
    Pending,
    Running,
    Completed,
    Skipped,
    Failed,
    Canceled,
}

/// <summary>Severity level for a progress event.</summary>
public enum ProgressLevel
{
    Trace,
    Info,
    Warning,
    Error,
}

/// <summary>Categorises a produced artifact file.</summary>
public enum ArtifactKind
{
    FactorBase,
    RawRelations,
    RawPartials,
    SieveCheckpoint,
    FilteredRelations,
    FilteredMatrix,
    MatrixMeta,
    Dependencies,
    Factors,
    JobState,
    EventLog,
}

/// <summary>Classifies a relation record.</summary>
public enum RelationKind
{
    Full,
    Partial,
    CombinedPartial,
}

/// <summary>Per-dependency outcome of square-root extraction (and early prechecks).</summary>
public enum FactorizationStatus
{
    FactorFound,
    Trivial,
    Invalid,
    NoFactor,
    InputPrime,
    InputProbablePrime,
}
