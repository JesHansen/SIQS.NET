using SIQS.Contracts;
using SIQS.Contracts.Files;
using SquareRoot;

namespace SIQS.Pipeline;

internal sealed class SquareRootPhaseRunner
{
    public Task<PhaseResult> RunAsync(PhaseContext context) => Task.Run(() =>
    {
        var factorBase = FactorBaseFile.Parse(PhaseArtifactStore.Read(context, "factor_base.txt"));
        var relations = FilteredRelationsFile.Parse(PhaseArtifactStore.Read(context, "relations_filtered.txt"));
        var dependencies = DependenciesFile.Parse(PhaseArtifactStore.Read(context, "dependencies.txt"));
        var result = SquareRootEngine.Run(
            factorBase,
            relations,
            dependencies,
            new SquareRootOptions(context.Request.SquareRoot.ContinueAfterFactor),
            context.Progress,
            context.CancellationToken);

        PhaseArtifactStore.Write(context, "factors.txt", FactorsFile.Write(result.Factors));
        var factor = result.Factor1 is { } f1 && result.Factor2 is { } f2
            ? new PhaseFactorOutcome(f1, f2)
            : null;
        var relationsUsed = 0;
        var winning = result.Factors.Results.FirstOrDefault(r => r.Status == FactorizationStatus.FactorFound);
        if (winning is not null && int.TryParse(winning.DependencyId, out var dependencyId))
        {
            relationsUsed = dependencies.Dependencies.FirstOrDefault(d => d.DependencyId == dependencyId)?.RelationIds.Count ?? 0;
        }

        return PhaseResult.Completed(SiqsPhase.SquareRoot, new[] { "factors.txt" },
            new Dictionary<string, string>
            {
                [CounterKeys.DependenciesAttempted] = CounterFormat.Count(result.Factors.Results.Count),
                ["relations_used"] = relationsUsed.ToString(),
                ["factor_found"] = factor is not null ? "true" : "false",
            }, factor);
    }, context.CancellationToken);
}
