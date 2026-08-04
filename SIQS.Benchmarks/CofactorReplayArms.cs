using Sieving;

namespace SIQS.Benchmarks;

/// <summary>
/// The registry of named cofactor splitters that <see cref="ReplayCofactorTool"/> can score. Each arm
/// is a pure <c>ulong → ulong</c> function returning one nontrivial factor (or ≤ 1 for "no split").
/// Experiments 37/38 extend <see cref="BuildArms"/> with their candidate splitters so every variant
/// is measured on the same corpus with the same harness.
/// </summary>
internal static class CofactorReplayArms
{
    public static IEnumerable<(string Name, Func<ulong, ulong> Split)> Resolve(string token)
    {
        var arms = BuildArms();
        if (string.Equals(token, "all", StringComparison.OrdinalIgnoreCase))
        {
            return arms.Select(static a => (a.Key, a.Value));
        }

        // Comma-separated list of arm names, preserving order and skipping unknowns silently.
        var resolved = new List<(string, Func<ulong, ulong>)>();
        foreach (var name in token.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (arms.TryGetValue(name, out var split))
            {
                resolved.Add((name, split));
            }
        }

        return resolved;
    }

    private static Dictionary<string, Func<ulong, ulong>> BuildArms()
    {
        var arms = new Dictionary<string, Func<ulong, ulong>>(StringComparer.OrdinalIgnoreCase)
        {
            ["squfof"] = CofactorFactorizer.Squfof64,
            ["squfof-rho"] = static value =>
            {
                var factor = CofactorFactorizer.Squfof64(value);
                return factor > 1 ? factor : CofactorFactorizer.PollardRho64(value);
            },
            // The production MicroEcmSqufof arm: one stage-1 curve at B1=47, then SQUFOF.
            ["micro-ecm-squfof"] = static value =>
            {
                var factor = MicroEcm64.TryFactor(value, stage1Bound: 47, curves: 1);
                return factor > 1 ? factor : CofactorFactorizer.Squfof64(value);
            },
        };

        MicroEcmStage2Arms.Register(arms);
        BatchedScreenArms.Register(arms);
        return arms;
    }
}
