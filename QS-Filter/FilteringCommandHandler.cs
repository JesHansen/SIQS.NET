using System.Globalization;
using Filtering;
using SIQS.Contracts;
using SIQS.Contracts.Files;
using SIQS.Contracts.Text;

namespace QS_Filter;

/// <summary>Application service for loading, filtering, and writing filter artifacts.</summary>
internal sealed class FilteringCommandHandler
{
    public FilteringCommandResult Execute(FilteringCommand command, IProgress<SiqsProgressEvent>? progress)
    {
        if (!File.Exists(command.FactorBasePath))
        {
            throw new FormatException($"Factor base file not found: {command.FactorBasePath}");
        }

        var factorBase = FactorBaseFile.Parse(File.ReadAllText(command.FactorBasePath));
        var relationFiles = KeepRawRelationFiles(command.RelationPaths);
        var partialFiles = KeepRawRelationFiles(command.PartialPaths);
        if (relationFiles.Count == 0 && partialFiles.Count == 0)
        {
            throw new FormatException("No raw relation records supplied across --relations and --partials.");
        }

        var duplicateIds = new DuplicateRelationIdDetector();
        var partialMetadata = ReadFirstRawMetadata(partialFiles, factorBase.Metadata);
        var fulls = FileSource(relationFiles, factorBase.Metadata, duplicateIds);
        var partials = FileSource(partialFiles, factorBase.Metadata, duplicateIds);
        Directory.CreateDirectory(command.OutputDirectory);

        var options = new FilteringOptions(
            MaxPartialsPerPrime: command.MaxPartialsPerPrime,
            LargePrimeBound: partialMetadata?.LargePrimeBound,
            LargePrime2Bound: partialMetadata?.LargePrime2Bound,
            SpillDirectory: command.SpillDirectory,
            MaxCycleLength: command.MaxCycleLength,
            EnableTwoMerge: command.EnableTwoMerge,
            TwoMergeSlack: command.TwoMergeSlack);
        var result = FilteringEngine.Run(factorBase, fulls, partials, options, progress);
        var format = result.Relations.Relations.Any(r => r.LargePrimes.Count > 1
            || (r.LargePrimes.Count == 1 && r.LargePrime is null))
            ? FileFormats.FilteredRelationsV2
            : FileFormats.FilteredRelationsV1;

        File.WriteAllText(Path.Combine(command.OutputDirectory, "relations_filtered.txt"),
            FilteredRelationsFile.Write(result.Relations, format));
        File.WriteAllText(Path.Combine(command.OutputDirectory, "filtered_matrix.txt"),
            FilteredMatrixFile.Write(result.Matrix));
        File.WriteAllText(Path.Combine(command.OutputDirectory, "matrix_meta.txt"),
            MatrixMetaFile.Write(result.Meta));

        return new FilteringCommandResult(result, duplicateIds.DuplicateRowsDropped);
    }

    private static IRawRelationSource FileSource(
        IReadOnlyList<string> paths,
        FactorBaseMetadata factorBase,
        DuplicateRelationIdDetector duplicateIds)
        => new RawRelationFileSource(
            paths,
            validateMetadata: (metadata, path) => EnsureMetadataAgrees(metadata, factorBase, path),
            recordFilter: duplicateIds.ShouldForward);

    private static IReadOnlyList<string> KeepRawRelationFiles(IReadOnlyList<string> paths)
        => paths.Where(path => ReadFormat(path) is not (FileFormats.FilteredRelationsV1 or FileFormats.FilteredRelationsV2)).ToArray();

    private static string? ReadFormat(string path)
    {
        using var reader = new StreamReader(path);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (MetadataFormat.TryParse(line, out var key, out var value) && key == "format")
            {
                return value;
            }

            if (line.Length > 0 && !MetadataFormat.IsComment(line))
            {
                return null;
            }
        }

        return null;
    }

    private static RawRelationsMetadata? ReadFirstRawMetadata(
        IReadOnlyList<string> paths, FactorBaseMetadata factorBase)
    {
        var path = paths.FirstOrDefault();
        if (path is null)
        {
            return null;
        }

        using var reader = new StreamReader(path);
        var metadata = RawRelationsFile.ReadMetadata(reader);
        EnsureMetadataAgrees(metadata, factorBase, path);
        return metadata;
    }

    private static void EnsureMetadataAgrees(RawRelationsMetadata raw, FactorBaseMetadata factorBase, string path)
    {
        if (raw.TargetN != factorBase.TargetN || raw.Multiplier != factorBase.Multiplier || raw.ScaledN != factorBase.ScaledN)
        {
            throw new FormatException($"Relation file '{path}' metadata disagrees with the factor base.");
        }
    }
}

internal sealed record FilteringCommandResult(FilteringResult Result, long DuplicateRowsDropped);

internal static class FilteringReportFormatter
{
    public static IEnumerable<string> Format(FilteringCommandResult command)
    {
        var result = command.Result;
        var c = result.Counters;
        yield return $"matrix {c.RowsBeforePruning}x{c.ColumnsBeforePruning} -> {result.Meta.RowCount}x{result.Meta.ColumnCount}, nonzero={c.NonZeroRows}, zero={c.ZeroRows}, surplus={c.NonZeroRowSurplus}/{c.TargetNonZeroSurplus}";
        yield return $"rows-removed={c.RowsRemoved}, columns-removed={c.ColumnsRemoved}, two-merges={c.TwoMerges}, surplus-rows-trimmed={c.SurplusRowsTrimmed}, singleton-pruned={c.SingletonPruned}, duplicates={c.DuplicatesRemoved}, duplicate-rows-dropped={command.DuplicateRowsDropped}";
        yield return $"max={c.MaxRowWeightBeforeTrim}->{c.MaxRowWeightAfterTrim}, avg-row-weight={c.AverageRowWeightBeforeTrim.ToString("F3", CultureInfo.InvariantCulture)}->{c.AverageRowWeightAfterTrim.ToString("F3", CultureInfo.InvariantCulture)}, combined-partials={c.CombinedPartials}, max-cycle={c.MaxCycleLength}";
        var counters = new Dictionary<string, string>
        {
            ["raw_full"] = c.RawFull.ToString(),
            ["raw_partials"] = c.RawPartials.ToString(),
            ["combined_partials"] = c.CombinedPartials.ToString(),
            ["rejected_cycles"] = c.RejectedCycles.ToString(),
            ["two_merges"] = c.TwoMerges.ToString(),
            ["surplus_rows_trimmed"] = c.SurplusRowsTrimmed.ToString(),
            ["duplicates_removed"] = c.DuplicatesRemoved.ToString(),
            ["singleton_pruned"] = c.SingletonPruned.ToString(),
            ["rows_before_pruning"] = c.RowsBeforePruning.ToString(),
            ["columns_before_pruning"] = c.ColumnsBeforePruning.ToString(),
            ["rows_removed"] = c.RowsRemoved.ToString(),
            ["columns_removed"] = c.ColumnsRemoved.ToString(),
            ["final_rows"] = c.FinalRows.ToString(),
            ["matrix_columns"] = c.MatrixColumns.ToString(),
            ["zero_rows"] = c.ZeroRows.ToString(),
            ["nonzero_rows"] = c.NonZeroRows.ToString(),
            ["nonzero_row_surplus"] = c.NonZeroRowSurplus.ToString(),
            ["target_nonzero_surplus"] = c.TargetNonZeroSurplus.ToString(),
            ["max_cycle_length"] = c.MaxCycleLength.ToString(),
            ["total_cycle_length"] = c.TotalCycleLength.ToString(),
            ["max_row_weight_before_trim"] = c.MaxRowWeightBeforeTrim.ToString(),
            ["max_row_weight_after_trim"] = c.MaxRowWeightAfterTrim.ToString(),
            ["total_row_weight_before_trim"] = c.TotalRowWeightBeforeTrim.ToString(),
            ["total_row_weight_after_trim"] = c.TotalRowWeightAfterTrim.ToString(),
            ["avg_row_weight_before_trim"] = c.AverageRowWeightBeforeTrim.ToString("F3", CultureInfo.InvariantCulture),
            ["avg_row_weight_after_trim"] = c.AverageRowWeightAfterTrim.ToString("F3", CultureInfo.InvariantCulture),
            ["p50_row_weight_before_trim"] = c.P50RowWeightBeforeTrim.ToString(),
            ["p50_row_weight_after_trim"] = c.P50RowWeightAfterTrim.ToString(),
            ["p90_row_weight_before_trim"] = c.P90RowWeightBeforeTrim.ToString(),
            ["p90_row_weight_after_trim"] = c.P90RowWeightAfterTrim.ToString(),
            ["p99_row_weight_before_trim"] = c.P99RowWeightBeforeTrim.ToString(),
            ["p99_row_weight_after_trim"] = c.P99RowWeightAfterTrim.ToString(),
        };
        foreach (var (key, value) in counters.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            yield return $"{key}={value}";
        }
        yield return $"duplicate_rows_dropped={command.DuplicateRowsDropped}";
    }
}
