using System.Diagnostics;
using System.Globalization;
using SIQS.Contracts;

namespace SIQS.Pipeline;

/// <summary>Owns the asynchronous phase loop, retries, transitions, and terminal result policy.</summary>
internal sealed class PipelineRunCoordinator
{
    private readonly IPhaseExecutor _executor;
    private readonly JobStateRepository _repository;
    private readonly TopUpCoordinator _topUp = new();

    public PipelineRunCoordinator(IPhaseExecutor executor, JobStateRepository repository)
    {
        _executor = executor;
        _repository = repository;
    }

    public async Task<FactorizationJobResult> RunAsync(
        string directory,
        JobState state,
        FactorizationRequest initialRequest,
        int startIndex,
        EventLog events,
        CancellationToken cancellationToken)
    {
        var currentRequest = initialRequest;
        PhaseResult? squareRootResult = null;
        for (var i = startIndex; i < PhaseSequence.All.Length; i++)
        {
            var phase = PhaseSequence.All[i];
            var phaseState = state.PhaseStates[i];
            if (cancellationToken.IsCancellationRequested)
            {
                return Cancel(directory, state, phaseState, currentRequest);
            }

            PhaseStateMachine.Begin(phaseState);
            _repository.Save(directory, state);
            var stopwatch = Stopwatch.StartNew();
            PhaseResult result;
            try
            {
                result = await RunPhase(phase, new PhaseContext(state.JobId, directory, currentRequest, events, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                phaseState.ElapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                return Cancel(directory, state, phaseState, currentRequest);
            }
            catch (UnderdeterminedMatrixException ex) when (phase == SiqsPhase.LinearAlgebra)
            {
                stopwatch.Stop();
                phaseState.ElapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                if (!TryPrepareTopUp(directory, state, phaseState, currentRequest, ex, events, out var toppedUp, out var failure))
                {
                    return failure!;
                }

                currentRequest = toppedUp!;
                i = PhaseSequence.IndexOf(SiqsPhase.Sieving) - 1;
                continue;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                phaseState.ElapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                return Fail(directory, state, phaseState, phase, ex.Message, ex.GetType().Name, currentRequest);
            }

            stopwatch.Stop();
            phaseState.ElapsedSeconds = stopwatch.Elapsed.TotalSeconds;
            if (result.Status == PhaseStatus.Failed)
            {
                return Fail(directory, state, phaseState, phase, result.Error ?? "phase failed", null, currentRequest);
            }

            var validation = ArtifactValidator.Validate(phase, directory, currentRequest, result);
            if (!validation.IsValid)
            {
                return Fail(directory, state, phaseState, phase,
                    string.Join("; ", validation.Issues.Select(issue => issue.Message)), null, currentRequest);
            }

            PhaseStateMachine.Complete(phaseState, stopwatch.Elapsed.TotalSeconds, result);
            foreach (var artifact in result.Artifacts)
            {
                if (!state.ArtifactPaths.Contains(artifact)) state.ArtifactPaths.Add(artifact);
            }
            PhaseStateMachine.RecordAttempt(phaseState);
            _repository.Save(directory, state);

            var counters = new Dictionary<string, string>(result.Counters)
            {
                ["elapsed_seconds"] = stopwatch.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture),
            };
            events.Report(new SiqsProgressEvent(DateTimeOffset.UtcNow, state.JobId, phase,
                ProgressLevel.Info, "phase completed", null, counters, null));

            if (phase == SiqsPhase.FactorBase)
            {
                if (result.Factor is { } early)
                    return CompleteTrivial(directory, state, i, early, currentRequest);
                if (result.Counters.TryGetValue("input_is_prime", out var inputIsPrime) && inputIsPrime == "true")
                    return CompletePrime(directory, state, i, currentRequest);
            }
            if (phase == SiqsPhase.Sieving && currentRequest.TrialSievePercent is not null)
                return CompleteSieveTrial(directory, state, i, currentRequest);
            if (phase == SiqsPhase.SquareRoot) squareRootResult = result;
        }

        return Complete(directory, state, squareRootResult, currentRequest);
    }

    public FactorizationJobResult BuildStoredResult(JobState state, FactorizationRequest request)
    {
        var phase = state.PhaseStates.ElementAtOrDefault(PhaseSequence.IndexOf(SiqsPhase.SquareRoot));
        var attempted = (int)ReadLongCounter(phase, "dependencies_attempted");
        var found = state.Status is JobStatus.CompletedFactorFound or JobStatus.CompletedTrivialFactor;
        return JobResultFactory.BuildResult(state, request, found, attempted);
    }

    public void ResetForResume(string directory, JobState state, int startIndex, bool keepSievingArtifacts, EventLog events)
        => ResetPhasesFrom(directory, state, startIndex, keepSievingArtifacts, events);

    private Task<PhaseResult> RunPhase(SiqsPhase phase, PhaseContext context) => phase switch
    {
        SiqsPhase.FactorBase => _executor.RunFactorBaseAsync(context),
        SiqsPhase.Sieving => _executor.RunSievingAsync(context),
        SiqsPhase.Filtering => _executor.RunFilteringAsync(context),
        SiqsPhase.LinearAlgebra => _executor.RunLinearAlgebraAsync(context),
        SiqsPhase.SquareRoot => _executor.RunSquareRootAsync(context),
        _ => throw new ArgumentOutOfRangeException(nameof(phase)),
    };

    private bool TryPrepareTopUp(
        string directory, JobState state, PhaseState phaseState, FactorizationRequest currentRequest,
        UnderdeterminedMatrixException failure, EventLog events,
        out FactorizationRequest? toppedUpRequest, out FactorizationJobResult? terminalFailure)
    {
        toppedUpRequest = null;
        terminalFailure = null;
        PhaseStateMachine.Fail(phaseState, failure.Message, new Dictionary<string, string>
        {
            ["nonzero_rows"] = failure.NonZeroRows.ToString(CultureInfo.InvariantCulture),
            ["columns"] = failure.ColumnCount.ToString(CultureInfo.InvariantCulture),
            ["underdetermined_deficit"] = failure.Deficit.ToString(CultureInfo.InvariantCulture),
        });
        TopUpPlan? plan;
        try { plan = _topUp.TryPlan(state, currentRequest, failure); }
        catch (OverflowException ex)
        {
            terminalFailure = Fail(directory, state, phaseState, SiqsPhase.LinearAlgebra, ex.Message, ex.GetType().Name, currentRequest);
            return false;
        }
        if (plan is null)
        {
            terminalFailure = Fail(directory, state, phaseState, SiqsPhase.LinearAlgebra,
                $"Underdetermined matrix still short after {TopUpCoordinator.MaxRounds} relation top-up round(s).",
                nameof(UnderdeterminedMatrixException), currentRequest);
            return false;
        }

        PhaseStateMachine.RecordAttempt(phaseState);
        var round = new TopUpRoundState
        {
            Round = plan.Round,
            StartedUtc = Now(),
            Deficit = plan.Deficit,
            CurrentUsableRelations = plan.CurrentUsableRelations,
            Margin = plan.Margin,
            NewRelationTarget = plan.NewRelationTarget,
        };
        state.TopUpRounds.Add(round);
        state.Parameters[RunParameterKeys.RelationTarget] = round.NewRelationTarget.ToString(CultureInfo.InvariantCulture);
        toppedUpRequest = plan.Request;
        ResetPhasesFrom(directory, state, PhaseSequence.IndexOf(SiqsPhase.Sieving), true, events);
        JobStateMachine.Start(state);
        _repository.Save(directory, state);
        events.Report(new SiqsProgressEvent(DateTimeOffset.UtcNow, state.JobId, SiqsPhase.Pipeline,
            ProgressLevel.Warning, "matrix underdetermined; topping up relations", null,
            new Dictionary<string, string>
            {
                ["top_up_round"] = round.Round.ToString(CultureInfo.InvariantCulture),
                ["underdetermined_deficit"] = round.Deficit.ToString(CultureInfo.InvariantCulture),
                ["new_relation_target"] = round.NewRelationTarget.ToString(CultureInfo.InvariantCulture),
            }, null));
        return true;
    }

    private static void ResetPhasesFrom(string directory, JobState state, int startIndex, bool keepSieving, EventLog? events)
    {
        new JobWorkspace(directory, state.JobId).DeletePhaseArtifacts(startIndex, keepSieving, events);
        for (var i = startIndex; i < state.PhaseStates.Count; i++) PhaseStateMachine.Reset(state.PhaseStates[i]);
        state.ArtifactPaths = state.ArtifactPaths.Where(path => File.Exists(Path.Combine(directory, path)))
            .Distinct(StringComparer.Ordinal).ToList();
    }

    private FactorizationJobResult Cancel(string directory, JobState state, PhaseState phase, FactorizationRequest request)
    {
        JobStateMachine.Canceling(state);
        _repository.Save(directory, state);
        if (phase.Status == PhaseStatus.Running)
        {
            PhaseStateMachine.Cancel(phase);
            PhaseStateMachine.RecordAttempt(phase);
        }
        JobStateMachine.Canceled(state);
        _repository.Save(directory, state);
        return JobResultFactory.BuildResult(state, request, false, 0);
    }

    private FactorizationJobResult Fail(string directory, JobState state, PhaseState phase, SiqsPhase phaseKind,
        string message, string? exceptionType, FactorizationRequest request)
    {
        PhaseStateMachine.Fail(phase, message);
        PhaseStateMachine.RecordAttempt(phase);
        JobStateMachine.Failed(state, phaseKind, message, exceptionType);
        _repository.Save(directory, state);
        return JobResultFactory.BuildResult(state, request, false, 0);
    }

    private FactorizationJobResult CompleteTrivial(string directory, JobState state, int phaseIndex,
        PhaseFactorOutcome factor, FactorizationRequest request)
    {
        for (var i = phaseIndex + 1; i < state.PhaseStates.Count; i++) PhaseStateMachine.Skip(state.PhaseStates[i]);
        JobStateMachine.Completed(state, JobStatus.CompletedTrivialFactor, factor);
        _repository.Save(directory, state);
        return JobResultFactory.BuildResult(state, request, true, 0);
    }

    private FactorizationJobResult CompleteSieveTrial(string directory, JobState state, int phaseIndex, FactorizationRequest request)
    {
        for (var i = phaseIndex + 1; i < state.PhaseStates.Count; i++) PhaseStateMachine.Skip(state.PhaseStates[i]);
        JobStateMachine.Completed(state, JobStatus.CompletedNoFactor, null);
        _repository.Save(directory, state);
        return JobResultFactory.BuildResult(state, request, false, 0);
    }

    private FactorizationJobResult CompletePrime(string directory, JobState state, int phaseIndex, FactorizationRequest request)
    {
        for (var i = phaseIndex + 1; i < state.PhaseStates.Count; i++) PhaseStateMachine.Skip(state.PhaseStates[i]);
        JobStateMachine.Completed(state, JobStatus.CompletedPrime, null);
        _repository.Save(directory, state);
        return JobResultFactory.BuildResult(state, request, false, 0);
    }

    private FactorizationJobResult Complete(string directory, JobState state, PhaseResult? squareRoot,
        FactorizationRequest request)
    {
        var attempted = squareRoot?.Counters.TryGetValue("dependencies_attempted", out var value) == true
            && int.TryParse(value, out var parsed) ? parsed : 0;
        if (squareRoot?.Factor is { } factor)
        {
            JobStateMachine.Completed(state, JobStatus.CompletedFactorFound, factor);
            _repository.Save(directory, state);
            return JobResultFactory.BuildResult(state, request, true, attempted);
        }

        JobStateMachine.Completed(state, JobStatus.CompletedNoFactor, null);
        _repository.Save(directory, state);
        return JobResultFactory.BuildResult(state, request, false, attempted);
    }

    private static long ReadLongCounter(PhaseState? state, string key)
        => state is not null && state.Counters.TryGetValue(key, out var value)
            && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static string Now() => JobTimestamp.Now();
}
