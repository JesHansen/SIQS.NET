using SIQS.Contracts;
using SIQS.Contracts.Files;

namespace Filtering;

/// <summary>
/// Combines single-large-prime partials into full relations, removes duplicates, prunes relations
/// that cannot contribute to a dependency (singleton columns), and emits the sparse GF(2) matrix
/// for linear algebra.
///
/// The engine makes two passes over the partial input. Pass 1 streams every record once, keeping
/// only the large-prime graph skeleton: two <c>ulong</c> vertices and a record locator per forest
/// edge, never the parsed record. Pass 2 re-reads only the records that participate in a cycle.
/// This keeps peak memory proportional to the graph, not to the raw relation volume.
/// </summary>
public static class FilteringEngine
{
    /// <summary>Runs filtering over in-memory record sequences (each enumerated exactly once).</summary>
    public static FilteringResult Run(
        FactorBaseDocument factorBase,
        IEnumerable<RawRelationRecord> fullRelations,
        IEnumerable<RawRelationRecord> partials,
        FilteringOptions? options = null,
        IProgress<SiqsProgressEvent>? progress = null)
        => Run(
            factorBase,
            new BufferedRawRelationSource(fullRelations),
            new BufferedRawRelationSource(partials),
            options,
            progress);

    /// <summary>Runs filtering over re-readable record sources (multi-pass, low retention).</summary>
    public static FilteringResult Run(
        FactorBaseDocument factorBase,
        IRawRelationSource fullRelations,
        IRawRelationSource partials,
        FilteringOptions? options = null,
        IProgress<SiqsProgressEvent>? progress = null)
    {
        options ??= new FilteringOptions();
        var meta = factorBase.Metadata;
        var factorBaseCount = factorBase.Entries.Count;
        var counters = new FilteringCounters();

        // The store owns the heavy payload half of every candidate. With a spill directory it streams
        // payloads to a scratch file, so reduction runs against only the light structural half.
        using var store = options.SpillDirectory is { } spillDir
            ? new SpillCandidateStore(spillDir)
            : (CandidateStore)new InMemoryCandidateStore();

        var candidates = new List<Candidate>();

        foreach (var (_, full) in fullRelations.Enumerate())
        {
            counters.RawFull++;
            RelationValidation.ValidateColumns(full.FactorExponents, factorBaseCount);
            RelationValidation.ValidateDeclaredParity(full);
            candidates.Add(store.Add(CandidateParts.FromFull(full, meta.ScaledN)));
        }

        candidates.AddRange(PartialCycleCombiner.Combine(
            partials, meta.ScaledN, meta.Bound, factorBaseCount, options, counters, store));

        // Output ordering: full relations by raw id, then combined partials by candidate key.
        var ordered = candidates
            .OrderBy(c => c.Kind == RelationKind.Full ? 0 : 1)
            .ThenBy(c => c.OrderKey, StringComparer.Ordinal)
            .ToList();

        var deduped = CandidateReducer.RemoveDuplicates(ordered, counters);
        CandidateReducer.RecordPrePruningTelemetry(deduped, factorBaseCount, counters);
        var survivors = CandidateReducer.PruneSingletons(deduped, factorBaseCount, counters);
        if (options.EnableTwoMerge)
        {
            while (true)
            {
                var mergesBefore = counters.TwoMerges;
                survivors = CandidateReducer.MergeWeightTwoColumns(
                    survivors, factorBaseCount, meta.ScaledN, options.TwoMergeSlack, counters);
                if (counters.TwoMerges == mergesBefore)
                {
                    break;
                }

                survivors = CandidateReducer.PruneSingletons(survivors, factorBaseCount, counters);
            }
        }

        CandidateReducer.RecordRowWeightTelemetry(survivors, beforeTrim: true, counters);

        while (true)
        {
            var surplusRowsTrimmedBefore = counters.SurplusRowsTrimmed;
            survivors = CandidateReducer.TrimHeavyRows(survivors, factorBaseCount, options, counters);
            if (counters.SurplusRowsTrimmed == surplusRowsTrimmedBefore)
            {
                break;
            }

            survivors = CandidateReducer.PruneSingletons(survivors, factorBaseCount, counters);
        }

        CandidateReducer.RecordRowWeightTelemetry(survivors, beforeTrim: false, counters);

        var result = FilteredResultBuilder.Build(meta, factorBaseCount, survivors, counters);
        FilteringProgressReporter.Report(progress, "filtering complete", counters);
        return result;
    }
}
