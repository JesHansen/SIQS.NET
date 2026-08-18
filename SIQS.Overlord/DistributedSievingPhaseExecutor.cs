using Sieving;
using SIQS.Contracts;
using SIQS.Contracts.Distributed;
using SIQS.Contracts.Files;
using SIQS.Pipeline;

namespace SIQS.Overlord;

/// <summary>
/// An <see cref="IPhaseExecutor"/> that runs every phase in-process except Sieving, which it replaces
/// with distributed collection: it publishes the job to clients and blocks until verified uploads
/// reach the relation target, writing them into the standard raw batch files. Because the outputs are
/// byte-compatible with a local sieve, the surrounding pipeline (job.json, filtering, linear algebra,
/// square root, and even matrix top-up) is entirely unchanged.
/// </summary>
public sealed class DistributedSievingPhaseExecutor : IPhaseExecutor
{
    private readonly IPhaseExecutor _inner;
    private readonly OverlordJob _job;
    private readonly int _leaseChunkSize;
    private readonly TimeSpan _uploadGracePeriod;
    private readonly long _maxRelationChunkBytes;
    private readonly long _maxRelationBacklogBytes;
    private readonly long _maxRelationInboxBytes;

    public DistributedSievingPhaseExecutor(
        IPhaseExecutor inner,
        OverlordJob job,
        int leaseChunkSize,
        TimeSpan uploadGracePeriod,
        long maxRelationChunkBytes,
        long maxRelationBacklogBytes,
        long maxRelationInboxBytes)
    {
        _inner = inner;
        _job = job;
        _leaseChunkSize = leaseChunkSize;
        _uploadGracePeriod = uploadGracePeriod;
        _maxRelationChunkBytes = maxRelationChunkBytes;
        _maxRelationBacklogBytes = maxRelationBacklogBytes;
        _maxRelationInboxBytes = maxRelationInboxBytes;
    }

    public Task<PhaseResult> RunFactorBaseAsync(PhaseContext context) => _inner.RunFactorBaseAsync(context);
    public Task<PhaseResult> RunFilteringAsync(PhaseContext context) => _inner.RunFilteringAsync(context);
    public Task<PhaseResult> RunLinearAlgebraAsync(PhaseContext context) => _inner.RunLinearAlgebraAsync(context);
    public Task<PhaseResult> RunSquareRootAsync(PhaseContext context) => _inner.RunSquareRootAsync(context);

    public async Task<PhaseResult> RunSievingAsync(PhaseContext context)
    {
        var factorBase = FactorBaseFile.Parse(File.ReadAllText(Path.Combine(context.JobDirectory, "factor_base.txt")));
        var parameters = SievingParameterResolver.Resolve(context.Request, factorBase);
        var aCount = ADomain.Count(factorBase, parameters);

        var metadata = new RawRelationsMetadata(
            factorBase.Metadata.TargetN,
            factorBase.Metadata.Multiplier,
            factorBase.Metadata.ScaledN,
            factorBase.Metadata.Bound,
            parameters.LargePrimeBound,
            parameters.EnableTwoLargePrimes ? parameters.LargePrime2Bound : null);

        var verifier = new RelationVerifier(
            factorBase, parameters.LargePrimeBound, parameters.EnableTwoLargePrimes ? parameters.LargePrime2Bound : null);
        var sink = new RawRelationBatchFileSink(context.JobDirectory, metadata, parameters.OutputBatchSize);
        var ingest = new RelationIngest(verifier, sink, ReadExistingRelations(context.JobDirectory));
        var descriptor = BuildDescriptor(_job.JobId, factorBase, parameters, aCount);
        DistributedJobDescriptorStore.ValidateOrCreate(context.JobDirectory, descriptor);
        var ledger = new LeaseLedger(aCount, _leaseChunkSize, context.JobDirectory);
        var inbox = new DurableRelationInbox(
            context.JobDirectory,
            _maxRelationChunkBytes,
            _maxRelationBacklogBytes,
            _maxRelationInboxBytes,
            _job.IngestDurableChunk,
            _job.CompleteDurableLease,
            exception => _job.Fault($"Relation inbox failed: {exception.Message}"));

        // Use the Overlord job id (also the run-directory name) so the descriptor, leases, and uploads
        // all share one id; the pipeline's internal job id in job.json is an implementation detail.
        _job.BeginSieving(
            descriptor,
            ledger,
            ingest,
            inbox,
            parameters.RelationTarget,
            _uploadGracePeriod);

        SieveOutcome outcome;
        try
        {
            outcome = await _job.WaitForSieveCompletionAsync(context.CancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Stop the worker before flushing the canonical sink so no background ingest can append
            // after the sieving artifacts have been declared complete.
            await inbox.SealAndDrainAsync().ConfigureAwait(false);
            sink.Complete();
        }

        if (outcome == SieveOutcome.Exhausted && ingest.UsableCount < parameters.RelationTarget)
        {
            throw new InvalidOperationException(
                $"Distributed polynomial supply exhausted before relation target: {ingest.UsableCount}/{parameters.RelationTarget} usable relations.");
        }

        var counters = new Dictionary<string, string>
        {
            [CounterKeys.UsableRelations] = ingest.UsableCount.ToString(),
            ["relations_needed"] = parameters.RelationTarget.ToString(),
            ["a_candidates"] = aCount.ToString(),
        };
        return PhaseResult.Completed(SiqsPhase.Sieving, sink.Artifacts, counters);
    }

    private static JobDescriptor BuildDescriptor(
        string jobId, FactorBaseDocument factorBase, SievingParameters parameters, int aCount)
    {
        var set = parameters.ToSet();
        var n = factorBase.Metadata.TargetN.ToString();
        var multiplier = factorBase.Metadata.Multiplier.ToString();
        const bool allowTiny = false; // bound and multiplier are explicit, so tiny trial division never applies.
        var hash = DistProtocol.ComputeParamHash(DistProtocol.Version, n, factorBase.Metadata.Bound, multiplier, allowTiny, set);
        return new JobDescriptor(jobId, n, factorBase.Metadata.Bound, multiplier, allowTiny, set, aCount, hash);
    }

    /// <summary>Relations already on disk (top-up re-entry keeps prior batches); empty on a fresh run.</summary>
    private static IEnumerable<RawRelationRecord> ReadExistingRelations(string jobDirectory)
    {
        foreach (var prefix in new[] { "relations", "partials" })
        {
            foreach (var path in Directory.EnumerateFiles(jobDirectory, $"{prefix}_*.txt").OrderBy(p => p, StringComparer.Ordinal))
            {
                foreach (var record in RawRelationsFile.Parse(File.ReadAllText(path)).Relations)
                {
                    yield return record;
                }
            }
        }
    }
}
