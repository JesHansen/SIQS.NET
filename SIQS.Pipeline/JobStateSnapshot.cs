using SIQS.Contracts;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;

namespace SIQS.Pipeline;

/// <summary>Immutable view of one persisted phase state.</summary>
public sealed record PhaseStateSnapshot(
    SiqsPhase Phase,
    PhaseStatus Status,
    int Attempt,
    string? StartedUtc,
    string? CompletedUtc,
    double? ElapsedSeconds,
    string? CurrentOperation,
    double? Percent,
    IReadOnlyDictionary<string, string> Counters,
    IReadOnlyList<string> Artifacts,
    string? Error,
    IReadOnlyList<PhaseAttemptStateSnapshot> Attempts)
{
    /// <summary>The phase start time, parsed from the persisted round-trip string.</summary>
    public DateTimeOffset? StartedAt => JobTimestamp.Parse(StartedUtc);

    /// <summary>The phase completion time, parsed from the persisted round-trip string.</summary>
    public DateTimeOffset? CompletedAt => JobTimestamp.Parse(CompletedUtc);
}

/// <summary>Immutable job projection safe to expose to UI and reporting consumers.</summary>
public sealed record JobStateSnapshot(
    string JobId,
    string TargetN,
    JobStatus Status,
    string? CreatedUtc,
    string? StartedUtc,
    string? CompletedUtc,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyList<PhaseStateSnapshot> PhaseStates,
    IReadOnlyList<string> ArtifactPaths,
    IReadOnlyList<string> FinalFactors,
    IReadOnlyList<TopUpRoundSnapshot> TopUpRounds,
    ErrorSummarySnapshot? ErrorSummary)
{
    /// <summary>Job creation time, parsed from the persisted round-trip string.</summary>
    public DateTimeOffset? CreatedAt => JobTimestamp.Parse(CreatedUtc);

    /// <summary>Job start time, parsed from the persisted round-trip string.</summary>
    public DateTimeOffset? StartedAt => JobTimestamp.Parse(StartedUtc);

    /// <summary>Job completion time, parsed from the persisted round-trip string.</summary>
    public DateTimeOffset? CompletedAt => JobTimestamp.Parse(CompletedUtc);

    /// <summary>The target number, parsed from its persisted decimal string.</summary>
    public BigInteger TargetNValue => BigInteger.Parse(TargetN, CultureInfo.InvariantCulture);

    /// <summary>The discovered factors, parsed from their persisted decimal strings.</summary>
    public IReadOnlyList<BigInteger> FinalFactorValues =>
        FinalFactors.Select(f => BigInteger.Parse(f, CultureInfo.InvariantCulture)).ToArray();
}

public sealed record TopUpRoundSnapshot(
    int Round,
    string? StartedUtc,
    int Deficit,
    long CurrentUsableRelations,
    int Margin,
    int NewRelationTarget);

public sealed record ErrorSummarySnapshot(
    SiqsPhase Phase,
    string Message,
    string? ExceptionType,
    string? ArtifactPath);

/// <summary>Creates defensive immutable projections of the mutable serializer DTOs.</summary>
public static class JobStateSnapshots
{
    public static JobStateSnapshot From(JobState state)
        => new(
            state.JobId,
            state.TargetN,
            state.Status,
            state.CreatedUtc,
            state.StartedUtc,
            state.CompletedUtc,
            ReadOnly(state.Parameters),
            Array.AsReadOnly(state.PhaseStates.Select(From).ToArray()),
            Array.AsReadOnly(state.ArtifactPaths.ToArray()),
            Array.AsReadOnly(state.FinalFactors.ToArray()),
            Array.AsReadOnly(state.TopUpRounds.Select(round => new TopUpRoundSnapshot(
                round.Round,
                round.StartedUtc,
                round.Deficit,
                round.CurrentUsableRelations,
                round.Margin,
                round.NewRelationTarget)).ToArray()),
            state.ErrorSummary is null ? null : new ErrorSummarySnapshot(
                state.ErrorSummary.Phase,
                state.ErrorSummary.Message,
                state.ErrorSummary.ExceptionType,
                state.ErrorSummary.ArtifactPath));

    private static PhaseStateSnapshot From(PhaseState state)
        => new(
            state.Phase,
            state.Status,
            state.Attempt,
            state.StartedUtc,
            state.CompletedUtc,
            state.ElapsedSeconds,
            state.CurrentOperation,
            state.Percent,
            ReadOnly(state.Counters),
            Array.AsReadOnly(state.Artifacts.ToArray()),
            state.Error,
            Array.AsReadOnly(state.Attempts.Select(attempt => new PhaseAttemptStateSnapshot(
                attempt.Attempt,
                attempt.Status,
                attempt.StartedUtc,
                attempt.CompletedUtc,
                attempt.ElapsedSeconds,
                ReadOnly(attempt.Counters),
                Array.AsReadOnly(attempt.Artifacts.ToArray()),
                attempt.Error)).ToArray()));

    private static IReadOnlyDictionary<string, string> ReadOnly(IReadOnlyDictionary<string, string> values)
        => new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(values));
}

public sealed record PhaseAttemptStateSnapshot(
    int Attempt,
    PhaseStatus Status,
    string? StartedUtc,
    string? CompletedUtc,
    double? ElapsedSeconds,
    IReadOnlyDictionary<string, string> Counters,
    IReadOnlyList<string> Artifacts,
    string? Error);

internal sealed class JobStateRepository
{
    public JobState Load(string directory) => JobStore.Load(directory);

    public JobStateSnapshot LoadSnapshot(string directory) => JobStateSnapshots.From(Load(directory));

    public void Save(string directory, JobState state) => JobStore.Write(directory, state);
}
