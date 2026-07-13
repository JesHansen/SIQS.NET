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
    private readonly TaskCompletionSource<SieveOutcome> _sieveCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();

    private JobDescriptor? _descriptor;
    private LeaseLedger? _ledger;
    private RelationIngest? _ingest;
    private int _relationTarget;
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
    internal void BeginSieving(JobDescriptor descriptor, LeaseLedger ledger, RelationIngest ingest, int relationTarget)
    {
        lock (_gate)
        {
            _descriptor = descriptor;
            _ledger = ledger;
            _ingest = ingest;
            _relationTarget = relationTarget;
            Phase = OverlordPhase.Sieving;
        }

        RaiseChanged();
    }

    /// <summary>Completed by <see cref="AcceptUpload"/> (converged) or lease exhaustion; cancelled with the token.</summary>
    public Task<SieveOutcome> WaitForSieveCompletionAsync(CancellationToken cancellationToken)
        => _sieveCompletion.Task.WaitAsync(cancellationToken);

    public LeaseResponse? TryLease(TimeSpan ttl)
    {
        LeaseLedger.Lease? lease;
        lock (_gate)
        {
            if (Phase != OverlordPhase.Sieving || _ledger is null)
            {
                return null;
            }

            lease = _ledger.TryLease(ttl, DateTimeOffset.UtcNow);
        }

        if (lease is null)
        {
            CheckExhaustion();
            return null;
        }

        return new LeaseResponse(JobId, lease.LeaseId, lease.Start, lease.End, lease.ExpiresUtc);
    }

    public UploadResponse AcceptUpload(string leaseId, IReadOnlyCollection<RawRelationRecord> relations)
    {
        RelationIngest ingest;
        LeaseLedger ledger;
        lock (_gate)
        {
            if (Phase != OverlordPhase.Sieving || _ingest is null || _ledger is null)
            {
                return new UploadResponse(false, 0, 0, "Job is not accepting relations.");
            }

            ingest = _ingest;
            ledger = _ledger;
        }

        var (accepted, rejected) = ingest.Ingest(relations);
        ledger.Complete(leaseId);
        RaiseChanged();

        if (ingest.UsableCount >= _relationTarget)
        {
            lock (_gate)
            {
                if (Phase == OverlordPhase.Sieving)
                {
                    Phase = OverlordPhase.Finishing;
                }
            }

            _sieveCompletion.TrySetResult(SieveOutcome.Converged);
            RaiseChanged();
        }
        else
        {
            CheckExhaustion();
        }

        return new UploadResponse(true, accepted, rejected, null);
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

        if (ledger is not null && ledger.IsExhausted)
        {
            _sieveCompletion.TrySetResult(SieveOutcome.Exhausted);
        }
    }

    private void RaiseChanged() => Changed?.Invoke();
}
