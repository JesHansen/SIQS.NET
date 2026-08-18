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
        IProgress<SiqsProgressEvent>? progress = null,
        CancellationToken cancellationToken = default)
        => Run(
            factorBase,
            new BufferedRawRelationSource(fullRelations),
            new BufferedRawRelationSource(partials),
            options,
            progress,
            cancellationToken);

    /// <summary>Runs filtering over re-readable record sources (multi-pass, low retention).</summary>
    public static FilteringResult Run(
        FactorBaseDocument factorBase,
        IRawRelationSource fullRelations,
        IRawRelationSource partials,
        FilteringOptions? options = null,
        IProgress<SiqsProgressEvent>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
            cancellationToken.ThrowIfCancellationRequested();
            counters.RawFull++;
            RelationValidation.ValidateColumns(full.FactorExponents, factorBaseCount);
            RelationValidation.ValidateDeclaredParity(full);
            candidates.Add(store.Add(CandidateParts.FromFull(full, meta.ScaledN)));
        }

        candidates.AddRange(PartialCycleCombiner.Combine(
            partials, meta.ScaledN, meta.Bound, factorBaseCount, options, counters, store, cancellationToken));

        // Output ordering: full relations by raw id, then combined partials by candidate key.
        var ordered = candidates
            .OrderBy(c => c.Kind == RelationKind.Full ? 0 : 1)
            .ThenBy(c => c.OrderKey, StringComparer.Ordinal)
            .ToList();

        var deduped = CandidateDuplicateRemover.RemoveDuplicates(ordered, counters, cancellationToken);
        CandidateReductionTelemetry.RecordPrePruningTelemetry(deduped, factorBaseCount, counters, cancellationToken);
        var survivors = CandidateSingletonPruner.PruneSingletons(deduped, factorBaseCount, counters, cancellationToken);
        if (options.EnableTwoMerge)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mergesBefore = counters.TwoMerges;
                survivors = CandidateWeightTwoMerger.MergeWeightTwoColumns(
                    survivors, factorBaseCount, meta.ScaledN, options.TwoMergeSlack, counters, cancellationToken);
                if (counters.TwoMerges == mergesBefore)
                {
                    break;
                }

                survivors = CandidateSingletonPruner.PruneSingletons(survivors, factorBaseCount, counters, cancellationToken);
            }
        }

        CandidateReductionTelemetry.RecordRowWeightTelemetry(survivors, beforeTrim: true, counters, cancellationToken);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var surplusRowsTrimmedBefore = counters.SurplusRowsTrimmed;
            survivors = CandidateRowTrimmer.TrimHeavyRows(survivors, factorBaseCount, options, counters, cancellationToken);
            if (counters.SurplusRowsTrimmed == surplusRowsTrimmedBefore)
            {
                break;
            }

            survivors = CandidateSingletonPruner.PruneSingletons(survivors, factorBaseCount, counters, cancellationToken);
        }

        CandidateReductionTelemetry.RecordRowWeightTelemetry(survivors, beforeTrim: false, counters, cancellationToken);

        var result = FilteredResultBuilder.Build(meta, factorBaseCount, survivors, counters, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        FilteringProgressReporter.Report(progress, "filtering complete", counters);
        return result;
    }
}
