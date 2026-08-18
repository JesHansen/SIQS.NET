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
    string? Error,
    RelationInboxSnapshot? Inbox);

/// <summary>
/// Holds the state of the one active distributed job: the descriptor clients need, the lease ledger,
/// the relation ingest, and a completion signal the distributed sieving phase awaits. The pipeline
/// runs on a background task (see <see cref="OverlordService"/>) and drives this through
/// <see cref="BeginSieving"/> / <see cref="Finish"/> / <see cref="Fault"/>; client requests drive it
/// through <see cref="TryLease"/> and the durable chunk-upload methods.
/// </summary>
public sealed class OverlordJob
{
    private TaskCompletionSource<SieveOutcome> _sieveCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();

    private JobDescriptor? _descriptor;
    private LeaseLedger? _ledger;
    private RelationIngest? _ingest;
    private DurableRelationInbox? _inbox;
    private int _relationTarget;
    private TimeSpan _uploadGracePeriod;
    private int _sieveGeneration;
    private DiscoveredFactors _factors = DiscoveredFactors.None;
    private bool _acceptingRequests = true;
    private CancellationTokenSource _finishCancellation = new();
    private Task? _finishingTask;

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
        DurableRelationInbox inbox,
        int relationTarget,
        TimeSpan uploadGracePeriod)
    {
        lock (_gate)
        {
            _finishCancellation.Cancel();
            _finishCancellation.Dispose();
            _finishCancellation = new CancellationTokenSource();
            _finishingTask = null;
            _descriptor = descriptor;
            _ledger = ledger;
            _ingest = ingest;
            _inbox = inbox;
            _relationTarget = relationTarget;
            _uploadGracePeriod = uploadGracePeriod;
            _sieveGeneration++;
            _sieveCompletion = new TaskCompletionSource<SieveOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            Phase = OverlordPhase.Sieving;
        }

        inbox.Start();
        RaiseChanged();
        if (ingest.UsableCount >= relationTarget)
        {
            var generation = 0;
            lock (_gate)
            {
                if (Phase == OverlordPhase.Sieving)
                {
                    Phase = OverlordPhase.Draining;
                    generation = _sieveGeneration;
                }
            }

            if (generation != 0)
            {
                RaiseChanged();
                StartFinishAfterInboxDrain(generation, uploadGracePeriod, SieveOutcome.Converged);
            }
        }
    }

    /// <summary>Completed by durable background ingestion or lease exhaustion; cancelled with the token.</summary>
    public Task<SieveOutcome> WaitForSieveCompletionAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return _sieveCompletion.Task.WaitAsync(cancellationToken);
        }
    }

    public LeaseResponse? TryLease(TimeSpan ttl, int chunkSize)
    {
        LeaseLedger.Lease? lease;
        lock (_gate)
        {
            if (Phase != OverlordPhase.Sieving ||
                !_acceptingRequests ||
                _ledger is null ||
                _inbox is null ||
                !_inbox.CanAcceptLease)
            {
                return null;
            }

            lease = _ledger.TryLease(ttl, DateTimeOffset.UtcNow, chunkSize);
        }

        if (lease is null)
        {
            CheckExhaustion();
            return null;
        }

        RaiseChanged();
        return new LeaseResponse(JobId, lease.LeaseId, lease.Start, lease.End, lease.ExpiresUtc);
    }

    internal DurableRelationInbox? BeginChunkUpload(string leaseId, TimeSpan ttl)
    {
        lock (_gate)
        {
            if (Phase is not (OverlordPhase.Sieving or OverlordPhase.Draining) ||
                !_acceptingRequests ||
                _ledger is null ||
                _inbox is null ||
                !_ledger.BeginUpload(leaseId, ttl, DateTimeOffset.UtcNow))
            {
                return null;
            }

            return _inbox;
        }
    }

    internal void EndChunkUpload(string leaseId, TimeSpan ttl)
    {
        lock (_gate)
        {
            _ledger?.EndUpload(leaseId, ttl, DateTimeOffset.UtcNow);
        }

        RaiseChanged();
    }

    internal void AbandonLease(string leaseId)
    {
        lock (_gate)
        {
            _ledger?.Abandon(leaseId);
        }

        RaiseChanged();
    }

    internal DurableRelationInbox? ProtectLeaseForIngest(string leaseId)
    {
        lock (_gate)
        {
            if (Phase is not (OverlordPhase.Sieving or OverlordPhase.Draining) ||
                !_acceptingRequests ||
                _ledger is null ||
                _inbox is null ||
                !_ledger.ProtectPendingIngest(leaseId, DateTimeOffset.UtcNow))
            {
                return null;
            }

            return _inbox;
        }
    }

    internal void CancelLeaseIngestProtection(string leaseId, TimeSpan ttl)
    {
        lock (_gate)
        {
            _ledger?.CancelPendingIngest(leaseId, ttl, DateTimeOffset.UtcNow);
        }
    }

    internal (int Accepted, int Rejected) IngestDurableChunk(
        IReadOnlyCollection<RawRelationRecord> relations)
    {
        RelationIngest ingest;
        lock (_gate)
        {
            if (Phase is not (OverlordPhase.Sieving or OverlordPhase.Draining) || _ingest is null)
            {
                return (0, relations.Count);
            }

            ingest = _ingest;
        }

        var result = ingest.Ingest(relations);
        var startGracePeriod = false;
        var generation = 0;
        lock (_gate)
        {
            if (Phase == OverlordPhase.Sieving && ingest.UsableCount >= _relationTarget)
            {
                Phase = OverlordPhase.Draining;
                generation = _sieveGeneration;
                startGracePeriod = true;
            }
        }

        RaiseChanged();
        if (startGracePeriod)
        {
            StartFinishAfterInboxDrain(generation, _uploadGracePeriod, SieveOutcome.Converged);
        }

        return result;
    }

    internal void CompleteDurableLease(string leaseId, bool succeeded)
    {
        LeaseLedger? ledger;
        lock (_gate)
        {
            ledger = _ledger;
        }

        if (ledger is not null)
        {
            if (succeeded)
            {
                ledger.Complete(leaseId);
            }
            else
            {
                ledger.FailPendingIngest(leaseId);
            }
        }

        CheckExhaustion();
        RaiseChanged();
    }

    /// <summary>Marks the pipeline finished and records the factors it found (empty if none).</summary>
    public void Finish(DiscoveredFactors factors)
    {
        lock (_gate)
        {
            _acceptingRequests = false;
            _finishCancellation.Cancel();
            _factors = factors;
            Phase = OverlordPhase.Completed;
        }

        RaiseChanged();
    }

    public void Fault(string error)
    {
        TaskCompletionSource<SieveOutcome> completion;
        lock (_gate)
        {
            _acceptingRequests = false;
            _finishCancellation.Cancel();
            Phase = OverlordPhase.Faulted;
            Error = error;
            completion = _sieveCompletion;
        }

        completion.TrySetException(new InvalidOperationException(error));
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
                Error,
                _inbox?.Snapshot());
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

        var generation = 0;
        lock (_gate)
        {
            if (Phase != OverlordPhase.Sieving)
            {
                return;
            }

            Phase = OverlordPhase.Draining;
            generation = _sieveGeneration;
        }

        RaiseChanged();
        StartFinishAfterInboxDrain(generation, TimeSpan.Zero, SieveOutcome.Exhausted);
    }

    /// <summary>Closes network admission and joins accepted inbox and delayed drain work.</summary>
    internal async Task StopAcceptingAndDrainAsync()
    {
        DurableRelationInbox? inbox;
        Task? finishing;
        lock (_gate)
        {
            _acceptingRequests = false;
            _finishCancellation.Cancel();
            inbox = _inbox;
            finishing = _finishingTask;
        }

        if (inbox is not null)
        {
            await inbox.SealAndDrainAsync().ConfigureAwait(false);
        }

        if (finishing is not null)
        {
            await finishing.ConfigureAwait(false);
        }
    }

    /// <summary>Applies the configured terminal retention policy to transport-only inbox data.</summary>
    internal Task CleanupInboxAsync(bool retain)
    {
        DurableRelationInbox? inbox;
        lock (_gate)
        {
            inbox = _inbox;
        }

        return inbox?.CleanupAsync(retain) ?? Task.CompletedTask;
    }

    private void StartFinishAfterInboxDrain(int generation, TimeSpan delay, SieveOutcome outcome)
    {
        lock (_gate)
        {
            _finishingTask ??= FinishAfterInboxDrainAsync(
                generation, delay, outcome, _finishCancellation.Token);
        }
    }

    private async Task FinishAfterInboxDrainAsync(
        int generation,
        TimeSpan delay,
        SieveOutcome outcome,
        CancellationToken cancellationToken)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        DurableRelationInbox? inbox;
        lock (_gate)
        {
            if (generation != _sieveGeneration || Phase != OverlordPhase.Draining)
            {
                return;
            }

            inbox = _inbox;
        }

        if (inbox is not null)
        {
            await inbox.SealAndDrainAsync().ConfigureAwait(false);
        }

        TaskCompletionSource<SieveOutcome> completion;
        lock (_gate)
        {
            if (generation != _sieveGeneration || Phase != OverlordPhase.Draining)
            {
                return;
            }

            Phase = OverlordPhase.Finishing;
            completion = _sieveCompletion;
        }

        completion.TrySetResult(outcome);
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke();
}
