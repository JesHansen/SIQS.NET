using System.Globalization;
using Filtering;
using SIQS.Contracts;
using SIQS.Contracts.Files;

namespace SIQS.Pipeline;

internal sealed class FilteringPhaseRunner
{
    public Task<PhaseResult> RunAsync(PhaseContext context) => Task.Run(() =>
    {
        var factorBase = FactorBaseFile.Parse(PhaseArtifactStore.Read(context, "factor_base.txt"));
        var partialMetadata = PhaseArtifactStore.ReadFirstRawMetadata(context.JobDirectory, "partials_*.txt");
        var fulls = PhaseArtifactStore.RawRelationSource(context.JobDirectory, "relations_*.txt");
        var partials = PhaseArtifactStore.RawRelationSource(context.JobDirectory, "partials_*.txt");
        var options = new FilteringOptions(
            LargePrimeBound: partialMetadata?.LargePrimeBound,
            LargePrime2Bound: partialMetadata?.LargePrime2Bound);
        var result = FilteringEngine.Run(factorBase, fulls, partials, options, context.Progress);

        PhaseArtifactStore.Write(context, "relations_filtered.txt",
            FilteredRelationsFile.Write(result.Relations, PhaseArtifactStore.FilteredRelationsFormat(result.Relations)));
        PhaseArtifactStore.Write(context, "filtered_matrix.txt", FilteredMatrixFile.Write(result.Matrix));
        PhaseArtifactStore.Write(context, "matrix_meta.txt", MatrixMetaFile.Write(result.Meta));

        return PhaseResult.Completed(SiqsPhase.Filtering,
            new[] { "relations_filtered.txt", "filtered_matrix.txt", "matrix_meta.txt" },
            new Dictionary<string, string>
            {
                ["rows_before_pruning"] = result.Counters.RowsBeforePruning.ToString(),
                ["columns_before_pruning"] = result.Counters.ColumnsBeforePruning.ToString(),
                ["rows_removed"] = result.Counters.RowsRemoved.ToString(),
                ["columns_removed"] = result.Counters.ColumnsRemoved.ToString(),
                ["final_rows"] = result.Meta.RowCount.ToString(),
                ["columns"] = result.Meta.ColumnCount.ToString(),
                ["factor_base_columns"] = result.Meta.FactorBaseCount.ToString(),
                ["combined_partials"] = result.Counters.CombinedPartials.ToString(),
                ["rejected_cycles"] = result.Counters.RejectedCycles.ToString(),
                ["surplus_rows_trimmed"] = result.Counters.SurplusRowsTrimmed.ToString(),
                ["duplicates_removed"] = result.Counters.DuplicatesRemoved.ToString(),
                ["singleton_pruned"] = result.Counters.SingletonPruned.ToString(),
                ["zero_rows"] = result.Counters.ZeroRows.ToString(),
                ["nonzero_rows"] = result.Counters.NonZeroRows.ToString(),
                ["nonzero_row_surplus"] = result.Counters.NonZeroRowSurplus.ToString(),
                ["target_nonzero_surplus"] = result.Counters.TargetNonZeroSurplus.ToString(),
                ["max_cycle_length"] = result.Counters.MaxCycleLength.ToString(),
                ["total_cycle_length"] = result.Counters.TotalCycleLength.ToString(),
                ["max_row_weight_before_trim"] = result.Counters.MaxRowWeightBeforeTrim.ToString(),
                ["max_row_weight_after_trim"] = result.Counters.MaxRowWeightAfterTrim.ToString(),
                ["total_row_weight_before_trim"] = result.Counters.TotalRowWeightBeforeTrim.ToString(),
                ["total_row_weight_after_trim"] = result.Counters.TotalRowWeightAfterTrim.ToString(),
                ["avg_row_weight_before_trim"] = result.Counters.AverageRowWeightBeforeTrim.ToString("F3", CultureInfo.InvariantCulture),
                ["avg_row_weight_after_trim"] = result.Counters.AverageRowWeightAfterTrim.ToString("F3", CultureInfo.InvariantCulture),
                ["p50_row_weight_before_trim"] = result.Counters.P50RowWeightBeforeTrim.ToString(),
                ["p50_row_weight_after_trim"] = result.Counters.P50RowWeightAfterTrim.ToString(),
                ["p90_row_weight_before_trim"] = result.Counters.P90RowWeightBeforeTrim.ToString(),
                ["p90_row_weight_after_trim"] = result.Counters.P90RowWeightAfterTrim.ToString(),
                ["p99_row_weight_before_trim"] = result.Counters.P99RowWeightBeforeTrim.ToString(),
                ["p99_row_weight_after_trim"] = result.Counters.P99RowWeightAfterTrim.ToString(),
            });
    }, context.CancellationToken);
}
