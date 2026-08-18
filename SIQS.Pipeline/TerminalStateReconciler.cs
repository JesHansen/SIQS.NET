using SIQS.Contracts;
using SIQS.Contracts.Files;

namespace SIQS.Pipeline;

/// <summary>Repairs a phase-complete job whose final whole-job transition was interrupted.</summary>
internal sealed class TerminalStateReconciler
{
    private readonly JobStateRepository _repository;

    public TerminalStateReconciler(JobStateRepository repository)
    {
        _repository = repository;
    }

    public bool TryReconcile(string directory, JobState state, FactorizationRequest request)
    {
        if (TryReadEarlyOutcome(directory, state, request, out var earlyStatus, out var earlyFactor))
        {
            SkipAfter(state, SiqsPhase.FactorBase);
            JobStateMachine.Completed(state, earlyStatus, earlyFactor);
            AddArtifact(state, "factors.txt");
            _repository.Save(directory, state);
            return true;
        }

        if (request.TrialSievePercent is not null &&
            IsCompletedAndValid(SiqsPhase.Sieving, directory, state, request))
        {
            SkipAfter(state, SiqsPhase.Sieving);
            JobStateMachine.Completed(state, JobStatus.CompletedNoFactor, null);
            _repository.Save(directory, state);
            return true;
        }

        if (!PhaseSequence.All.All(phase => IsCompletedAndValid(phase, directory, state, request)))
        {
            return false;
        }

        var factors = TryParseFactors(directory);
        if (factors is null)
        {
            return false;
        }

        var winning = factors.Results.FirstOrDefault(result => result.Status == FactorizationStatus.FactorFound);
        PhaseFactorOutcome? factor = null;
        var status = JobStatus.CompletedNoFactor;
        if (winning is not null)
        {
            if (!TryCreateProperFactor(request, winning, out factor))
            {
                return false;
            }

            status = JobStatus.CompletedFactorFound;
        }

        var squareRoot = state.PhaseStates[PhaseSequence.IndexOf(SiqsPhase.SquareRoot)];
        squareRoot.Counters[CounterKeys.DependenciesAttempted] = CounterFormat.Count(factors.DependencyCount);
        JobStateMachine.Completed(state, status, factor);
        AddArtifact(state, "factors.txt");
        _repository.Save(directory, state);
        return true;
    }

    private static bool TryReadEarlyOutcome(
        string directory,
        JobState state,
        FactorizationRequest request,
        out JobStatus status,
        out PhaseFactorOutcome? factor)
    {
        status = default;
        factor = null;
        var factorBaseState = state.PhaseStates[PhaseSequence.IndexOf(SiqsPhase.FactorBase)];
        if (factorBaseState.Status != PhaseStatus.Completed ||
            File.Exists(Path.Combine(directory, "factor_base.txt")) ||
            !ArtifactValidator.Validate(SiqsPhase.FactorBase, directory, request, result: null).IsValid)
        {
            return false;
        }

        var factors = TryParseFactors(directory);
        if (factors?.Results is not [var outcome])
        {
            return false;
        }

        if (outcome.Status == FactorizationStatus.InputPrime)
        {
            status = JobStatus.CompletedPrime;
            return true;
        }

        if (outcome.Status == FactorizationStatus.InputProbablePrime)
        {
            status = JobStatus.CompletedProbablePrime;
            return true;
        }

        if (outcome.Status != FactorizationStatus.FactorFound ||
            !TryCreateProperFactor(request, outcome, out factor))
        {
            return false;
        }

        status = JobStatus.CompletedTrivialFactor;
        return true;
    }

    private static bool TryCreateProperFactor(
        FactorizationRequest request,
        FactorResultRecord outcome,
        out PhaseFactorOutcome? factor)
    {
        factor = null;
        if (outcome.Factor1 is not { } factor1 || outcome.Factor2 is not { } factor2 ||
            factor1 <= 1 || factor2 <= 1 || factor1 >= request.TargetN || factor2 >= request.TargetN ||
            factor1 * factor2 != request.TargetN)
        {
            return false;
        }

        factor = new PhaseFactorOutcome(factor1, factor2);
        return true;
    }

    private static bool IsCompletedAndValid(
        SiqsPhase phase,
        string directory,
        JobState state,
        FactorizationRequest request)
    {
        var phaseState = state.PhaseStates[PhaseSequence.IndexOf(phase)];
        return phaseState.Status == PhaseStatus.Completed &&
               ArtifactValidator.Validate(phase, directory, request, result: null).IsValid;
    }

    private static FactorsDocument? TryParseFactors(string directory)
    {
        try
        {
            return FactorsFile.Parse(ArtifactFileIO.ReadAllText(Path.Combine(directory, "factors.txt")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static void SkipAfter(JobState state, SiqsPhase phase)
    {
        for (var index = PhaseSequence.IndexOf(phase) + 1; index < state.PhaseStates.Count; index++)
        {
            PhaseStateMachine.Skip(state.PhaseStates[index]);
        }
    }

    private static void AddArtifact(JobState state, string artifact)
    {
        if (!state.ArtifactPaths.Contains(artifact, StringComparer.Ordinal))
        {
            state.ArtifactPaths.Add(artifact);
        }
    }
}
