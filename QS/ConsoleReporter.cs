using System.Globalization;
using SIQS.Contracts;

namespace QS;

/// <summary>
/// Renders SIQS pipeline progress to the console: a live, overwriting line during sieving and a
/// one-line summary as each phase completes (factor base size, relations found vs needed, matrix
/// sizes before/after pruning, null-space vectors, relations used in the square root).
/// </summary>
internal sealed class ConsoleReporter : IProgress<SiqsProgressEvent>
{
    private const int LabelWidth = 17;

    private readonly object _gate = new();
    private bool _liveLineActive;

    public void Report(SiqsProgressEvent value)
    {
        lock (_gate)
        {
            if (value.Phase == SiqsPhase.Pipeline && value.Message == "job workspace created")
            {
                EndLiveLine();
                Console.WriteLine($"{Prefix("[job]")}artifacts: {Get(value, "run_dir")}");
                return;
            }

            if (value.Phase == SiqsPhase.Pipeline && value.Message.StartsWith("resuming at ", StringComparison.Ordinal))
            {
                EndLiveLine();
                Console.WriteLine($"{Prefix("[job]")}{value.Message}");
                return;
            }

            if (value.Message == "phase completed")
            {
                EndLiveLine();
                Console.WriteLine($"{Summarize(value)}  ({FormatElapsed(value)})");
                if (value.Phase == SiqsPhase.Sieving)
                {
                    var breakdown = SievingCpuBreakdownFormatter.Format(value, Prefix);
                    if (breakdown.Length > 0) Console.WriteLine(breakdown);
                }
                return;
            }

            if (value.Phase == SiqsPhase.Sieving && value.Message == "sieving")
            {
                if (value.Counters.ContainsKey("trial_raw_target"))
                {
                    WriteLive($"{Prefix("trial sieving...")}{Get(value, "raw_relations")}/{Get(value, "trial_raw_target")} raw relations" +
                              $"  (full {Get(value, "full_relations")}, partial {Get(value, "partial_relations")})" +
                              $"  {Get(value, "polynomials")} polynomials");
                }
                else
                {
                    WriteLive($"{Prefix("sieving...")}{Get(value, "usable_relations")}/{Get(value, "relations_needed")} relations" +
                              $"  (full {Get(value, "full_relations")}, partial {Get(value, "partial_relations")})" +
                              $"  {Get(value, "polynomials")} polynomials");
                }
            }

            if (value.Phase == SiqsPhase.LinearAlgebra)
            {
                WriteLive(FormatLinearAlgebraProgress(value));
            }
        }
    }

    private static string Summarize(SiqsProgressEvent e) => e.Phase switch
    {
        SiqsPhase.FactorBase =>
            $"{Prefix("[factor base]")}{Get(e, "factor_base_size")} primes  (multiplier {Get(e, "multiplier")}, bound {Get(e, "bound")})",
        SiqsPhase.Sieving => SummarizeSieving(e),
        SiqsPhase.Filtering =>
            $"{Prefix("[filtering]")}matrix {Get(e, "rows_before_pruning")}x{Get(e, "columns_before_pruning")} -> {Get(e, "final_rows")}x{Get(e, "columns")}  " +
            $"(nonzero {Get(e, "nonzero_rows")}, zero {Get(e, "zero_rows")}, surplus {Get(e, "nonzero_row_surplus")}/{Get(e, "target_nonzero_surplus")}; " +
            $"rows removed {Get(e, "rows_removed")}, columns removed {Get(e, "columns_removed")}; " +
            $"combined partials {Get(e, "combined_partials")}, surplus rows trimmed {Get(e, "surplus_rows_trimmed")}, " +
            $"duplicates {Get(e, "duplicates_removed")}, singletons pruned {Get(e, "singleton_pruned")}; " +
            $"max cycle {Get(e, "max_cycle_length")}, row weight avg {Get(e, "avg_row_weight_before_trim")}->{Get(e, "avg_row_weight_after_trim")}, " +
            $"p90 {Get(e, "p90_row_weight_before_trim")}->{Get(e, "p90_row_weight_after_trim")}, max {Get(e, "max_row_weight_before_trim")}->{Get(e, "max_row_weight_after_trim")})",
        SiqsPhase.LinearAlgebra =>
            $"{Prefix("[linear algebra]")}solver {Get(e, "solver")}  " +
            $"{Get(e, "pivots")} pivots, {Get(e, "dependencies")} null-space vectors  " +
            $"[lanczos runs {Get(e, "lanczos_runs")}, deps {Get(e, "lanczos_dependencies")}, nonzero surplus {Get(e, "nonzero_row_surplus")}]",
        SiqsPhase.SquareRoot =>
            $"{Prefix("[square root]")}{Get(e, "dependencies_attempted")} dependencies attempted, {Get(e, "relations_used")} relations used",
        _ => $"{Prefix($"[{SiqsTokens.ToToken(e.Phase)}]")}{e.Message}",
    };

    private static string SummarizeSieving(SiqsProgressEvent e)
    {
        var prefix = e.Counters.ContainsKey("trial_raw_target")
            ? $"{Prefix("[sieving]")}trial {Get(e, "raw_relations")}/{Get(e, "trial_raw_target")} raw relations"
            : $"{Prefix("[sieving]")}{Get(e, "usable_relations")}/{Get(e, "relations_needed")} relations";

        return prefix +
               $"  (full {Get(e, "full_relations")}, partial {Get(e, "partial_relations")})  from {Get(e, "polynomials")} polynomials  " +
               $"[{Get(e, "candidates")} candidates, {Get(e, "discarded")} discarded]";
    }

    private void WriteLive(string text)
    {
        // Pad to clear any longer previous line, then return to start.
        Console.Write("\r" + text.PadRight(Math.Max(text.Length, 78)));
        _liveLineActive = true;
    }

    private void EndLiveLine()
    {
        if (_liveLineActive)
        {
            Console.WriteLine();
            _liveLineActive = false;
        }
    }

    private static string Get(SiqsProgressEvent e, string key) => e.Counters.GetValueOrDefault(key, "?");

    private static string Prefix(string label) => $"  {label.PadRight(LabelWidth)}";

    private static string FormatLinearAlgebraProgress(SiqsProgressEvent e)
    {
        var elapsed = FormatProgressElapsed(Get(e, "elapsed_seconds"));
        var prefix = Prefix("linear algebra...");
        return e.Message switch
        {
            "run-start" =>
                $"{prefix}block-lanczos run {Get(e, "run")} started target-dims={Get(e, "target_dimensions")}",
            "iteration" =>
                $"{prefix}block-lanczos run {Get(e, "run")} iter={Get(e, "iteration")} " +
                $"dims={Get(e, "dimensions_solved")}/{Get(e, "target_dimensions")} elapsed={elapsed}",
            "extracting" =>
                $"{prefix}block-lanczos run {Get(e, "run")} extracting dependencies " +
                $"dims={Get(e, "dimensions_solved")}/{Get(e, "target_dimensions")} elapsed={elapsed}",
            "dependencies-found" =>
                $"{prefix}block-lanczos run {Get(e, "run")} accepted dependencies={Get(e, "iteration")} " +
                $"dims={Get(e, "dimensions_solved")}/{Get(e, "target_dimensions")} elapsed={elapsed}",
            "no-dependencies" =>
                $"{prefix}block-lanczos run {Get(e, "run")} found no verified dependencies; retrying " +
                $"dims={Get(e, "dimensions_solved")}/{Get(e, "target_dimensions")} elapsed={elapsed}",
            "run-complete" =>
                $"{prefix}block-lanczos run {Get(e, "run")} complete " +
                $"dims={Get(e, "dimensions_solved")}/{Get(e, "target_dimensions")} elapsed={elapsed}",
            "run-failed" =>
                $"{prefix}block-lanczos run {Get(e, "run")} failed " +
                $"dims={Get(e, "dimensions_solved")}/{Get(e, "target_dimensions")} elapsed={elapsed}",
            _ => $"{prefix}{e.Message}",
        };
    }

    private static string FormatElapsed(SiqsProgressEvent e)
        => double.TryParse(Get(e, "elapsed_seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? seconds < 60
                ? $"{seconds:F1}s"
                : FormatElapsedTimeSpan(TimeSpan.FromSeconds(seconds))
            : "?";

    private static string FormatElapsedTimeSpan(TimeSpan elapsed)
        => elapsed.TotalHours >= 1.0
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{elapsed.Minutes}:{elapsed.Seconds:00}";

    private static string FormatProgressElapsed(string secondsText)
        => double.TryParse(secondsText, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? FormatElapsedTimeSpan(TimeSpan.FromSeconds(seconds))
            : "?";
}
