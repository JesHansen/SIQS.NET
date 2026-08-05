using SIQS.Contracts;

namespace QS.Presentation;

/// <summary>
/// Short, user-facing phrasing for each pipeline phase: a display label and the one-line summary
/// shown when the phase completes. This is the trimmed-down counterpart to the verbose per-phase
/// lines the debug reporter emits.
/// </summary>
internal static class PhaseText
{
    public static string DisplayName(SiqsPhase phase) => phase switch
    {
        SiqsPhase.FactorBase => "factor base",
        SiqsPhase.Sieving => "sieving",
        SiqsPhase.Filtering => "filtering",
        SiqsPhase.LinearAlgebra => "linear algebra",
        SiqsPhase.SquareRoot => "square root",
        _ => SiqsTokens.ToToken(phase).Replace('_', ' '),
    };

    /// <summary>Builds the completed-phase summary from its reported counters.</summary>
    public static string Summarize(SiqsPhase phase, IReadOnlyDictionary<string, string> counters)
    {
        string Get(string key) => counters.GetValueOrDefault(key, "?");

        return phase switch
        {
            // No factor base is built when the pre-check settles N outright (a small prime divides it,
            // it is prime, it is a perfect power). Reporting the reason beats reporting "? primes".
            SiqsPhase.FactorBase => counters.TryGetValue("factor_base_size", out var size)
                ? $"{size} primes"
                : EarlyOutcome(counters),
            SiqsPhase.Sieving => counters.ContainsKey("trial_raw_target")
                ? $"{Get("raw_relations")}/{Get("trial_raw_target")} raw relations "
                  + $"(full {Get("full_relations")}, partial {Get("partial_relations")})"
                : $"{Get("usable_relations")}/{Get("relations_needed")} relations "
                  + $"(full {Get("full_relations")}, partial {Get("partial_relations")})",
            SiqsPhase.Filtering =>
                $"matrix {Get("rows_before_pruning")}×{Get("columns_before_pruning")} "
                + $"→ {Get("final_rows")}×{Get("columns")}",
            SiqsPhase.LinearAlgebra => $"{Get("dependencies")} null-space vectors",
            SiqsPhase.SquareRoot =>
                $"{Get("dependencies_attempted")} dependencies attempted, {Get("relations_used")} relations used",
            _ => string.Empty,
        };
    }

    /// <summary>Describes why the factor-base phase finished without building a factor base.</summary>
    private static string EarlyOutcome(IReadOnlyDictionary<string, string> counters)
        => counters.ContainsKey(CounterKeys.InputIsPrime) ? "not needed — input is prime"
            : counters.GetValueOrDefault("reason") is { Length: > 0 } reason
                ? $"not needed — {reason.Replace('_', ' ')}"
                : "not needed";
}
