using System.Globalization;
using SIQS.Contracts;

namespace Filtering;

/// <summary>Translates <see cref="FilteringCounters"/> into an <see cref="SiqsProgressEvent"/>.</summary>
internal static class FilteringProgressReporter
{
    public static void Report(IProgress<SiqsProgressEvent>? progress, string message, FilteringCounters counters)
    {
        progress?.Report(new SiqsProgressEvent(
            DateTimeOffset.UtcNow, null, SiqsPhase.Filtering, ProgressLevel.Info, message, null,
            new Dictionary<string, string>
            {
                ["raw_full"] = counters.RawFull.ToString(),
                ["raw_partials"] = counters.RawPartials.ToString(),
                ["combined_partials"] = counters.CombinedPartials.ToString(),
                ["rejected_cycles"] = counters.RejectedCycles.ToString(),
                ["two_merges"] = counters.TwoMerges.ToString(),
                ["surplus_rows_trimmed"] = counters.SurplusRowsTrimmed.ToString(),
                ["duplicates_removed"] = counters.DuplicatesRemoved.ToString(),
                ["redundant_columns_merged"] = counters.RedundantColumnsMerged.ToString(),
                ["singleton_pruned"] = counters.SingletonPruned.ToString(),
                ["rows_before_pruning"] = counters.RowsBeforePruning.ToString(),
                ["columns_before_pruning"] = counters.ColumnsBeforePruning.ToString(),
                ["rows_removed"] = counters.RowsRemoved.ToString(),
                ["columns_removed"] = counters.ColumnsRemoved.ToString(),
                ["final_rows"] = counters.FinalRows.ToString(),
                ["matrix_columns"] = counters.MatrixColumns.ToString(),
                ["zero_rows"] = counters.ZeroRows.ToString(),
                ["nonzero_rows"] = counters.NonZeroRows.ToString(),
                ["nonzero_row_surplus"] = counters.NonZeroRowSurplus.ToString(),
                ["target_nonzero_surplus"] = counters.TargetNonZeroSurplus.ToString(),
                ["max_cycle_length"] = counters.MaxCycleLength.ToString(),
                ["total_cycle_length"] = counters.TotalCycleLength.ToString(),
                ["max_row_weight_before_trim"] = counters.MaxRowWeightBeforeTrim.ToString(),
                ["max_row_weight_after_trim"] = counters.MaxRowWeightAfterTrim.ToString(),
                ["total_row_weight_before_trim"] = counters.TotalRowWeightBeforeTrim.ToString(),
                ["total_row_weight_after_trim"] = counters.TotalRowWeightAfterTrim.ToString(),
                ["avg_row_weight_before_trim"] = counters.AverageRowWeightBeforeTrim.ToString("F3", CultureInfo.InvariantCulture),
                ["avg_row_weight_after_trim"] = counters.AverageRowWeightAfterTrim.ToString("F3", CultureInfo.InvariantCulture),
                ["p50_row_weight_before_trim"] = counters.P50RowWeightBeforeTrim.ToString(),
                ["p50_row_weight_after_trim"] = counters.P50RowWeightAfterTrim.ToString(),
                ["p90_row_weight_before_trim"] = counters.P90RowWeightBeforeTrim.ToString(),
                ["p90_row_weight_after_trim"] = counters.P90RowWeightAfterTrim.ToString(),
                ["p99_row_weight_before_trim"] = counters.P99RowWeightBeforeTrim.ToString(),
                ["p99_row_weight_after_trim"] = counters.P99RowWeightAfterTrim.ToString(),
            },
            null));
    }
}
