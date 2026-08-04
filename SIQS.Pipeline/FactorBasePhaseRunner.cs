using Factorbase;
using SIQS.Contracts;
using SIQS.Contracts.Files;

namespace SIQS.Pipeline;

internal sealed class FactorBasePhaseRunner
{
    public Task<PhaseResult> RunAsync(PhaseContext context) => Task.Run(() =>
    {
        var request = context.Request;
        var options = new FactorBaseOptions(
            request.TargetN,
            request.FactorBase.Bound,
            request.FactorBase.Multiplier,
            request.FactorBase.AllowTinyInputTrialDivision);
        var result = FactorBaseGenerator.Generate(options, context.Progress);

        if (result.HasEarlyOutcome)
        {
            var factors = result.EarlyOutcome!;
            PhaseArtifactStore.Write(context, "factors.txt", FactorsFile.Write(factors));
            var row = factors.Results[0];
            if (row.Status == FactorizationStatus.InputPrime)
            {
                return PhaseResult.Completed(SiqsPhase.FactorBase, new[] { "factors.txt" },
                    new Dictionary<string, string>
                    {
                        ["reason"] = row.Reason ?? string.Empty,
                        [CounterKeys.InputIsPrime] = CounterFormat.Bool(true),
                    });
            }

            return PhaseResult.Completed(SiqsPhase.FactorBase, new[] { "factors.txt" },
                new Dictionary<string, string> { ["reason"] = row.Reason ?? string.Empty },
                new PhaseFactorOutcome(row.Factor1!.Value, row.Factor2!.Value));
        }

        var doc = result.FactorBase!;
        PhaseArtifactStore.Write(context, "factor_base.txt", FactorBaseFile.Write(doc));
        return PhaseResult.Completed(SiqsPhase.FactorBase, new[] { "factor_base.txt" },
            new Dictionary<string, string>
            {
                ["bound"] = doc.Metadata.Bound.ToString(),
                ["multiplier"] = doc.Metadata.Multiplier.ToString(),
                ["factor_base_size"] = doc.Entries.Count.ToString(),
            });
    }, context.CancellationToken);
}
