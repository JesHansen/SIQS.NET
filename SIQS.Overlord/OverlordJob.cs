using SIQS.Contracts;
using SIQS.Contracts.Distributed;

namespace SIQS.Overlord;

/// <summary>Lifecycle of the single active distributed job.</summary>
public enum OverlordPhase
{
    /// <summary>Factor base is being built; no work is available to clients yet.</summary>
    Preparing,

    /// <summary>Accepting leases and relation uploads.</summary>
    Sieving,

    /// <summary>The target was reached; uploads are accepted for a short grace period.</summary>
    Draining,

    /// <summary>Relation target met; the server is running filtering, linear algebra, and the square root.</summary>
    Finishing,

    /// <summary>The factorization pipeline finished (with or without a factor).</summary>
    Completed,

    /// <summary>The job failed.</summary>
    Faulted,
}

/// <summary>Why distributed sieving stopped.</summary>
public enum SieveOutcome
{
    /// <summary>The relation target was reached.</summary>
    Converged,

    /// <summary>The whole A-space was sieved without reaching the target.</summary>
    Exhausted,
}

/// <summary>A view of the job for the coordination endpoints and the UI.</summary>
public sealed record OverlordJobSnapshot(
    string JobId,
    string TargetN,
    OverlordPhase Phase,
    long UsableRelations,
    int RelationTarget,
    LeaseLedgerSnapshot? Leases,
    DiscoveredFactors Factors,
    string? Error);

/// <summary>
/// Holds the state of the one active distributed job: the descriptor clients need, the lease ledger,
/// the relation ingest, and a completion signal the distributed sieving phase awaits. The pipeline
/// runs on a background task (see <see cref="OverlordService"/>) and drives this through
/// <see cref="BeginSieving"/> / <see cref="Finish"/> / <see cref="Fault"/>; client requests drive it
/// through <see cref="TryLease"/> / <see cref="AcceptUpload"/>.
/// </summary>
public sealed class OverlordJob
{
    private TaskCompletionSource<SieveOutcome> _sieveCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();

    private JobDescriptor? _descriptor;
    private LeaseLedger? _ledger;
    private RelationIngest? _ingest;
    private int _relationTarget;
    private TimeSpan _leaseTtl;
    private TimeSpan _uploadGracePeriod;
    private int _sieveGeneration;
    private DiscoveredFactors _factors = DiscoveredFactors.None;

    public OverlordJob(string jobId, string targetN, string directory)
    {
        JobId = jobId;
        TargetN = targetN;
        Directory = directory;
    }

    public string JobId { get; }
    public string TargetN { get; }
    public string Directory { get; }
    public OverlordPhase Phase { get; private set; } = OverlordPhase.Preparing;
    public string? Error { get; private set; }

    /// <summary>Raised on any observable state change so the UI can refresh.</summary>
    public event Action? Changed;

    /// <summary>The job descriptor, available once sieving has begun (null while preparing).</summary>
    public JobDescriptor? Descriptor { get { lock (_gate) { return _descriptor; } } }

    /// <summary>Publishes the descriptor and opens the job for leases and uploads.</summary>
    internal void BeginSieving(
        JobDescriptor descriptor,
        LeaseLedger ledger,
        RelationIngest ingest,
        int relationTarget,
        TimeSpan uploadGracePeriod)
    {
        lock (_gate)
        {
            _descriptor = descriptor;
            _ledger = ledger;
            _ingest = ingest;
            _relationTarget = relationTarget;
            _uploadGracePeriod = uploadGracePeriod;
            _sieveGeneration++;
            _sieveCompletion = new TaskCompletionSource<SieveOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            Phase = OverlordPhase.Sieving;
        }

        RaiseChanged();
    }

    /// <summary>Completed by <see cref="AcceptUpload"/> (converged) or lease exhaustion; cancelled with the token.</summary>
    public Task<SieveOutcome> WaitForSieveCompletionAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return _sieveCompletion.Task.WaitAsync(cancellationToken);
        }
    }

    public LeaseResponse? TryLease(TimeSpan ttl)
    {
        LeaseLedger.Lease? lease;
        lock (_gate)
        {
            if (Phase != OverlordPhase.Sieving || _ledger is null)
            {
                return null;
            }

            _leaseTtl = ttl;
            lease = _ledger.TryLease(ttl, DateTimeOffset.UtcNow);
        }

        if (lease is null)
        {
            CheckExhaustion();
            return null;
        }

        RaiseChanged();
        return new LeaseResponse(JobId, lease.LeaseId, lease.Start, lease.End, lease.ExpiresUtc);
    }

    public UploadResponse AcceptUpload(string leaseId, IReadOnlyCollection<RawRelationRecord> relations)
    {
        var upload = BeginUpload(leaseId);
        if (upload is null)
        {
            return new UploadResponse(false, 0, 0, "Job is not accepting relations.");
        }

        upload.Ingest(relations);
        return upload.Complete();
    }

    internal UploadSession? BeginUpload(string leaseId)
    {
        lock (_gate)
        {
            if (Phase is not (OverlordPhase.Sieving or OverlordPhase.Draining) || _ingest is null || _ledger is null)
            {
                return null;
            }

            _ledger.Renew(leaseId, _leaseTtl, DateTimeOffset.UtcNow);
            return new UploadSession(this, _sieveGeneration, leaseId, _ingest, _ledger);
        }
    }

    private UploadResponse CompleteUpload(
        int generation, string leaseId, LeaseLedger ledger, int accepted, int rejected, bool discarded)
    {
        var leaseCompleted = ledger.Complete(leaseId);
        if (IsCurrentSieveGeneration(generation))
        {
            CheckExhaustion();
        }

        RaiseChanged();
        return new UploadResponse(
            !discarded || accepted > 0,
            accepted,
            rejected,
            discarded
                ? "The upload grace period expired; late relations were discarded."
                : !leaseCompleted
                    ? "Relations were accepted, but the lease expired before completion; its A-range may be reassigned."
                    : null);
    }

    private (int Accepted, int Rejected, bool Discarded) IngestUploadBatch(
        int generation,
        string leaseId,
        RelationIngest ingest,
        LeaseLedger ledger,
        IReadOnlyCollection<RawRelationRecord> relations)
    {
        var startGracePeriod = false;
        (int Accepted, int Rejected) result;
        lock (_gate)
        {
            // This gate is deliberately held through ingest. The grace-period timer takes the same
            // gate, so filtering cannot start while a final accepted batch is writing to disk.
            if (generation != _sieveGeneration || Phase is not (OverlordPhase.Sieving or OverlordPhase.Draining))
            {
                return (0, 0, true);
            }

            // A large-number lease can run well beyond the original TTL. Every received transport
            // batch proves liveness, so extend the lease before a UI snapshot can reclaim its range.
            ledger.Renew(leaseId, _leaseTtl, DateTimeOffset.UtcNow);
            result = ingest.Ingest(relations);
            if (Phase == OverlordPhase.Sieving && ingest.UsableCount >= _relationTarget)
            {
                Phase = OverlordPhase.Draining;
                startGracePeriod = true;
            }
        }

        // The UI already throttles Changed notifications. Publishing every bounded ingest batch
        // keeps live relation counts visible without coupling rendering to individual records.
        RaiseChanged();
        if (startGracePeriod)
        {
            _ = CompleteUploadGracePeriodAsync(generation, _uploadGracePeriod);
        }

        return (result.Accepted, result.Rejected, false);
    }

    private async Task CompleteUploadGracePeriodAsync(int generation, TimeSpan gracePeriod)
    {
        await Task.Delay(gracePeriod).ConfigureAwait(false);

        lock (_gate)
        {
            if (generation != _sieveGeneration || Phase != OverlordPhase.Draining)
            {
                return;
            }

            // Any later Ingest call observes Finishing under this same gate and discards its batch.
            Phase = OverlordPhase.Finishing;
        }

        _sieveCompletion.TrySetResult(SieveOutcome.Converged);
        RaiseChanged();
    }

    internal sealed class UploadSession
    {
        private readonly OverlordJob _job;
        private readonly int _generation;
        private readonly string _leaseId;
        private readonly RelationIngest _ingest;
        private readonly LeaseLedger _ledger;
        private int _accepted;
        private int _rejected;
        private bool _discarded;
        private int _state;

        internal UploadSession(
            OverlordJob job,
            int generation,
            string leaseId,
            RelationIngest ingest,
            LeaseLedger ledger)
        {
            _job = job;
            _generation = generation;
            _leaseId = leaseId;
            _ingest = ingest;
            _ledger = ledger;
        }

        public bool IsAccepting => _job.IsAcceptingUploadRecords(_generation);

        public void Ingest(IReadOnlyCollection<RawRelationRecord> relations)
        {
            if (Volatile.Read(ref _state) != 0)
            {
                throw new InvalidOperationException("The relation upload is no longer active.");
            }

            var (accepted, rejected, discarded) = _job.IngestUploadBatch(
                _generation, _leaseId, _ingest, _ledger, relations);
            _accepted = checked(_accepted + accepted);
            _rejected = checked(_rejected + rejected);
            _discarded |= discarded;
        }

        public void DiscardRemaining() => _discarded = true;

        public UploadResponse Complete()
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            {
                throw new InvalidOperationException("The relation upload is no longer active.");
            }

            return _job.CompleteUpload(
                _generation, _leaseId, _ledger, _accepted, _rejected, _discarded);
        }
    }

    private bool IsAcceptingUploadRecords(int generation)
    {
        lock (_gate)
        {
            return generation == _sieveGeneration &&
                Phase is (OverlordPhase.Sieving or OverlordPhase.Draining);
        }
    }

    private bool IsCurrentSieveGeneration(int generation)
    {
        lock (_gate)
        {
            return generation == _sieveGeneration;
        }
    }

    /// <summary>Marks the pipeline finished and records the factors it found (empty if none).</summary>
    public void Finish(DiscoveredFactors factors)
    {
        lock (_gate)
        {
            _factors = factors;
            Phase = OverlordPhase.Completed;
        }

        RaiseChanged();
    }

    public void Fault(string error)
    {
        lock (_gate)
        {
            Phase = OverlordPhase.Faulted;
            Error = error;
        }

        _sieveCompletion.TrySetException(new InvalidOperationException(error));
        RaiseChanged();
    }

    public OverlordJobSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new OverlordJobSnapshot(
                JobId, TargetN, Phase,
                _ingest?.UsableCount ?? 0,
                _relationTarget,
                _ledger?.Snapshot(DateTimeOffset.UtcNow),
                _factors,
                Error);
        }
    }

    private void CheckExhaustion()
    {
        LeaseLedger? ledger;
        lock (_gate)
        {
            ledger = _ledger;
        }

        if (ledger is null || !ledger.IsExhausted)
        {
            return;
        }

        lock (_gate)
        {
            if (Phase != OverlordPhase.Sieving)
            {
                return;
            }

            // Taking the gate closes ingestion atomically. Existing request bodies can continue to
            // arrive, but their sessions will discard rather than mutate the completed sieve output.
            Phase = OverlordPhase.Finishing;
        }

        _sieveCompletion.TrySetResult(SieveOutcome.Exhausted);
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke();
}
