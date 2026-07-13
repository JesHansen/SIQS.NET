using System.Globalization;
using System.Threading;
using SIQS.Contracts;

namespace SIQS.Pipeline;

/// <summary>Application-facing API for running a complete SIQS factorization.</summary>
public interface ISiqsPipeline
{
    Task<FactorizationJobResult> RunAsync(FactorizationRequest request, IProgress<SiqsProgressEvent>? progress, CancellationToken cancellationToken, string? requestedJobId = null);
    Task<FactorizationJobResult> ResumeAsync(string jobDirectory, FactorizationRequest? overrides, IProgress<SiqsProgressEvent>? progress, CancellationToken cancellationToken);
    FactorizationRequest NormalizeAndValidate(FactorizationRequest request);
    IReadOnlyList<ArtifactDescriptor> GetExpectedArtifacts();
    JobState LoadJob(string jobDirectory);
}

/// <summary>
/// Thin facade that validates requests, opens a workspace, and delegates orchestration to composed
/// pipeline collaborators. Phase execution and transition policy live outside this class.
/// </summary>
public sealed class SiqsPipeline : ISiqsPipeline
{
    private static int _sequence;
    private readonly IPhaseExecutor _executor;
    private readonly JobStateRepository _stateRepository = new();
    private readonly ResumePlanner _resumePlanner = new();
    private readonly PipelineRunCoordinator _coordinator;

    public SiqsPipeline(IPhaseExecutor? executor = null)
    {
        _executor = executor ?? new RealPhaseExecutor();
        _coordinator = new PipelineRunCoordinator(_executor, _stateRepository);
    }

    public FactorizationRequest NormalizeAndValidate(FactorizationRequest request)
        => FactorizationRequestValidator.NormalizeAndValidate(request);

    public IReadOnlyList<ArtifactDescriptor> GetExpectedArtifacts() => new[]
    {
        new ArtifactDescriptor("factor_base.txt", ArtifactKind.FactorBase, SiqsPhase.FactorBase, "factor_base.txt", true),
        new ArtifactDescriptor("relations_0000.txt", ArtifactKind.RawRelations, SiqsPhase.Sieving, "relations_0000.txt", false),
        new ArtifactDescriptor("partials_0000.txt", ArtifactKind.RawPartials, SiqsPhase.Sieving, "partials_0000.txt", false),
        new ArtifactDescriptor("sieve_checkpoint.txt", ArtifactKind.SieveCheckpoint, SiqsPhase.Sieving, "sieve_checkpoint.txt", false),
        new ArtifactDescriptor("matrix_meta.txt", ArtifactKind.MatrixMeta, SiqsPhase.Filtering, "matrix_meta.txt", true),
        new ArtifactDescriptor("filtered_matrix.txt", ArtifactKind.FilteredMatrix, SiqsPhase.Filtering, "filtered_matrix.txt", true),
        new ArtifactDescriptor("relations_filtered.txt", ArtifactKind.FilteredRelations, SiqsPhase.Filtering, "relations_filtered.txt", true),
        new ArtifactDescriptor("dependencies.txt", ArtifactKind.Dependencies, SiqsPhase.LinearAlgebra, "dependencies.txt", true),
        new ArtifactDescriptor("factors.txt", ArtifactKind.Factors, SiqsPhase.SquareRoot, "factors.txt", false),
        new ArtifactDescriptor("job.json", ArtifactKind.JobState, SiqsPhase.Pipeline, "job.json", true),
        new ArtifactDescriptor("events.log", ArtifactKind.EventLog, SiqsPhase.Pipeline, "events.log", true),
    };

    public JobState LoadJob(string jobDirectory) => _stateRepository.Load(jobDirectory);

    public async Task<FactorizationJobResult> RunAsync(
        FactorizationRequest request,
        IProgress<SiqsProgressEvent>? progress,
        CancellationToken cancellationToken,
        string? requestedJobId = null)
    {
        var normalized = NormalizeAndValidate(request);
        var jobId = requestedJobId ?? GenerateJobId();
        var directory = normalized.RunDirectory ?? Path.Combine("runs", jobId);
        var workspace = new JobWorkspace(directory, jobId);
        workspace.EnsureNew();

        var state = new JobState
        {
            JobId = jobId,
            TargetN = normalized.TargetN.ToString(CultureInfo.InvariantCulture),
            Status = JobStatus.Created,
            CreatedUtc = Now(),
            Parameters = JobResultFactory.BuildParameters(normalized),
        };
        PhaseSequence.EnsureStateOrder(state);
        _stateRepository.Save(directory, state);

        using var events = new EventLog(directory, jobId, progress);
        events.Report(new SiqsProgressEvent(DateTimeOffset.UtcNow, jobId, SiqsPhase.Pipeline,
            ProgressLevel.Info, "job workspace created", null,
            new Dictionary<string, string> { ["run_dir"] = directory }, directory));
        JobStateMachine.Start(state);
        _stateRepository.Save(directory, state);
        return await _coordinator.RunAsync(directory, state, normalized, 0, events, cancellationToken);
    }

    public async Task<FactorizationJobResult> ResumeAsync(
        string jobDirectory,
        FactorizationRequest? overrides,
        IProgress<SiqsProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(jobDirectory))
            throw new InvalidOperationException($"Job workspace '{jobDirectory}' does not exist.");

        JobState state;
        try { state = _stateRepository.Load(jobDirectory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException)
        {
            throw new InvalidOperationException($"Could not load job.json from '{jobDirectory}': {ex.Message}", ex);
        }

        PhaseSequence.EnsureStateOrder(state);
        if (state.Parameters.TryGetValue(RunParameterKeys.TrialSievePercent, out var trial)
            && !trial.Equals("off", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Trial-sieve jobs cannot be resumed because A-index checkpoint semantics do not apply.");

        var storedRequest = NormalizeAndValidate(StoredParameterReader.BuildRequest(state, jobDirectory));
        ResumeOverrideValidator.Validate(state, storedRequest, overrides);
        if (IsCompleted(state.Status)) return _coordinator.BuildStoredResult(state, storedRequest);

        var resumeIndex = _resumePlanner.FindResumePoint(jobDirectory, state, storedRequest);
        if (resumeIndex >= PhaseSequence.All.Length) return _coordinator.BuildStoredResult(state, storedRequest);

        using var events = new EventLog(jobDirectory, state.JobId, progress);
        events.Report(new SiqsProgressEvent(DateTimeOffset.UtcNow, state.JobId, SiqsPhase.Pipeline,
            ProgressLevel.Info, $"resuming at {SiqsTokens.ToToken(PhaseSequence.All[resumeIndex])}", null,
            new Dictionary<string, string>(), null));
        _coordinator.ResetForResume(jobDirectory, state, resumeIndex,
            resumeIndex == PhaseSequence.IndexOf(SiqsPhase.Sieving), events);
        JobStateMachine.Start(state);
        _stateRepository.Save(jobDirectory, state);
        return await _coordinator.RunAsync(jobDirectory, state, storedRequest, resumeIndex, events, cancellationToken);
    }

    private static bool IsCompleted(JobStatus status)
        => status is JobStatus.CompletedNoFactor or JobStatus.CompletedFactorFound or JobStatus.CompletedTrivialFactor;

    private static string Now() => DateTimeOffset.UtcNow.ToString("O");

    private static string GenerateJobId()
    {
        var sequence = Interlocked.Increment(ref _sequence) % 10000;
        return $"J{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{sequence:D4}";
    }
}
