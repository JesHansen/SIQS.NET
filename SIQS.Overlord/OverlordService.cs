using SIQS.Contracts;
using SIQS.Contracts.Distributed;
using SIQS.Pipeline;

namespace SIQS.Overlord;

/// <summary>Tuning for the coordinator.</summary>
public sealed record OverlordOptions
{
    /// <summary>How long a lease is held before it can be reclaimed and re-offered.</summary>
    public TimeSpan LeaseTtl { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Fallback number of A-indices handed to clients that do not report their effective parallelism.
    /// New clients receive a bounded, worker-scaled lease instead.
    /// </summary>
    public int LeaseChunkSize { get; init; } = 64;

    /// <summary>
    /// Target number of A-indices per unit of client sieve parallelism for the baseline C100
    /// workload (2M sieve half-interval and nine A-primes). Heavier jobs scale this down.
    /// </summary>
    public int LeaseItemsPerWorker { get; init; } = 8;

    /// <summary>Lower bound applied to a client-sized lease.</summary>
    public int MinLeaseChunkSize { get; init; } = 1;

    /// <summary>Upper bound applied to a client-sized lease.</summary>
    public int MaxLeaseChunkSize { get; init; } = 384;

    /// <summary>How long durable uploads remain accepted after the relation target is reached.</summary>
    public TimeSpan UploadGracePeriod { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum durable size of one relation chunk.</summary>
    public long MaxRelationChunkBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>
    /// Maximum unprocessed raw relation data retained in the per-job inbox. Processed chunks are
    /// replaced by compact durable receipts, so this bounds transient backlog rather than the whole
    /// distributed relation stream.
    /// </summary>
    public long MaxRelationBacklogBytes { get; init; } = 50L * 1024 * 1024 * 1024;

    /// <summary>Maximum total bytes retained beneath the per-job relation inbox.</summary>
    public long MaxRelationInboxBytes { get; init; } = 50L * 1024 * 1024 * 1024 + 64L * 1024 * 1024;

    /// <summary>Keep upload receipts after a terminal result. Canonical relation artifacts are unaffected.</summary>
    public bool RetainRelationInboxOnCompletion { get; init; }

    internal int ResolveLeaseChunkSize(int? parallelism)
        => ResolveLeaseChunkSize(parallelism, sieving: null);

    internal int ResolveLeaseChunkSize(int? parallelism, SievingParameterSet? sieving)
    {
        if (parallelism is null or <= 0)
        {
            return LeaseChunkSize;
        }

        var itemsPerWorker = sieving is null
            ? LeaseItemsPerWorker
            : ResolveItemsPerWorker(sieving);
        var requested = (long)parallelism.Value * itemsPerWorker;
        return (int)Math.Clamp(requested, MinLeaseChunkSize, MaxLeaseChunkSize);
    }

    private int ResolveItemsPerWorker(SievingParameterSet sieving)
    {
        const double baselineHalfInterval = 2_097_152;
        const int baselineAPrimeCount = 9;

        // Each A produces 2^(s-1) polynomial variants. Sieve work is approximately linear in both
        // that family size and the interval, which is a much better lease-duration predictor than
        // core count alone. Always give each reported worker at least one A-index.
        var intervalScale = baselineHalfInterval / Math.Max(1, sieving.SieveHalfInterval);
        var familyScale = Math.Pow(2, baselineAPrimeCount - sieving.APrimeCount);
        var scaled = Math.Floor(LeaseItemsPerWorker * intervalScale * familyScale);
        return (int)Math.Clamp(scaled, 1, LeaseItemsPerWorker);
    }
}

/// <summary>
/// Singleton application service coordinating the one active distributed factorization. It runs the
/// SIQS pipeline on a background task with a <see cref="DistributedSievingPhaseExecutor"/>, so
/// factor-base building, filtering, linear algebra, and the square root happen centrally while the
/// sieve is farmed out. Endpoints call the handshake / lease / upload methods; the UI observes
/// <see cref="Changed"/> and <see cref="Snapshot"/>.
/// </summary>
public sealed class OverlordService : IAsyncDisposable
{
    private readonly string _runsRoot;
    private readonly OverlordOptions _options;
    private readonly object _gate = new();

    private OverlordJob? _job;
    private CancellationTokenSource? _cts;
    private Task<FactorizationJobResult>? _running;
    private volatile bool _accepting = true;
    private Task? _stopTask;

    public OverlordService(string runsRoot, OverlordOptions? options = null)
    {
        _runsRoot = runsRoot;
        _options = options ?? new OverlordOptions();
        if (_options.LeaseChunkSize < 1 ||
            _options.LeaseItemsPerWorker < 1 ||
            _options.MinLeaseChunkSize < 1 ||
            _options.MaxLeaseChunkSize < _options.MinLeaseChunkSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), "Lease sizes, bounds, and items per worker must be positive and ordered.");
        }

        if (_options.UploadGracePeriod < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(OverlordOptions.UploadGracePeriod), "The upload grace period cannot be negative.");
        }

        if (_options.MaxRelationChunkBytes < 1 ||
            _options.MaxRelationBacklogBytes < _options.MaxRelationChunkBytes ||
            _options.MaxRelationInboxBytes < _options.MaxRelationBacklogBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), "Relation inbox quotas must be positive, ordered, and hold one maximum-size chunk.");
        }

        Directory.CreateDirectory(_runsRoot);
    }

    public event Action? Changed;

    public OverlordJob? Current
    {
        get { lock (_gate) { return _job; } }
    }

    public bool IsBusy => Current is { Phase: not (OverlordPhase.Completed or OverlordPhase.Faulted) };

    public OverlordJobSnapshot? Snapshot() => Current?.Snapshot();

    /// <summary>The background pipeline task, for callers that want to await the terminal result.</summary>
    public Task<FactorizationJobResult>? Completion => _running;

    /// <summary>Maximum application payload bytes accepted by one relation-upload request.</summary>
    public long MaxRelationChunkBytes => _options.MaxRelationChunkBytes;

    /// <summary>Lists interrupted distributed jobs for explicit operator selection.</summary>
    public IReadOnlyList<RecoverableDistributedJob> ListRecoverableJobs()
    {
        var jobs = new List<RecoverableDistributedJob>();
        foreach (var directory in Directory.EnumerateDirectories(_runsRoot, "D*"))
        {
            var jobId = Path.GetFileName(directory);
            if (!IsDistributedJobId(jobId)) continue;
            try
            {
                var state = JobStore.LoadSnapshot(directory);
                if (IsTerminal(state.Status)) continue;
                jobs.Add(new RecoverableDistributedJob(jobId, state.TargetN, state.Status, true));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
            {
                jobs.Add(new RecoverableDistributedJob(jobId, string.Empty, JobStatus.Failed, false, ex.Message));
            }
        }

        return jobs.OrderBy(job => job.JobId, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Starts a distributed factorization. Throws if one is already active.</summary>
    public void Submit(FactorizationRequest request)
    {
        var normalizedRequest = new SiqsPipeline().NormalizeAndValidate(request);
        lock (_gate)
        {
            if (!_accepting)
            {
                throw new InvalidOperationException(
                    "The application is shutting down and no longer accepts distributed jobs.");
            }

            if (_job is { Phase: not (OverlordPhase.Completed or OverlordPhase.Faulted) })
            {
                throw new InvalidOperationException("A distributed job is already active.");
            }

            var jobId = $"D{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-ffff}";
            var directory = Path.Combine(_runsRoot, jobId);
            var job = new OverlordJob(jobId, request.TargetN.ToString(), directory);
            job.Changed += RaiseChanged;

            var executor = new DistributedSievingPhaseExecutor(
                new RealPhaseExecutor(),
                job,
                _options.LeaseChunkSize,
                _options.UploadGracePeriod,
                _options.MaxRelationChunkBytes,
                _options.MaxRelationBacklogBytes,
                _options.MaxRelationInboxBytes);
            var pipeline = new SiqsPipeline(executor);
            var normalized = normalizedRequest with { RunDirectory = directory };

            _job = job;
            _cts = new CancellationTokenSource();
            _running = Task.Run(() => RunPipelineAsync(pipeline, job, normalized, jobId, _cts.Token));
        }

        RaiseChanged();
    }

    /// <summary>Explicitly resumes one interrupted distributed job discovered under the runs root.</summary>
    public void Recover(string jobId)
    {
        if (!IsDistributedJobId(jobId))
        {
            throw new ArgumentException("Distributed job id has an invalid format.", nameof(jobId));
        }

        var directory = Path.Combine(_runsRoot, jobId);
        var state = JobStore.LoadSnapshot(directory);
        if (state.JobId != jobId || IsTerminal(state.Status))
        {
            throw new InvalidOperationException("The selected distributed job is not recoverable.");
        }

        lock (_gate)
        {
            if (!_accepting)
            {
                throw new InvalidOperationException(
                    "The application is shutting down and no longer accepts distributed jobs.");
            }

            if (_job is { Phase: not (OverlordPhase.Completed or OverlordPhase.Faulted) })
            {
                throw new InvalidOperationException("A distributed job is already active.");
            }

            var job = new OverlordJob(jobId, state.TargetN, directory);
            job.Changed += RaiseChanged;
            var executor = new DistributedSievingPhaseExecutor(
                new RealPhaseExecutor(), job, _options.LeaseChunkSize, _options.UploadGracePeriod,
                _options.MaxRelationChunkBytes, _options.MaxRelationBacklogBytes,
                _options.MaxRelationInboxBytes);
            var pipeline = new SiqsPipeline(executor);

            _job = job;
            _cts = new CancellationTokenSource();
            _running = Task.Run(() => RunRecoveredPipelineAsync(
                pipeline, job, directory, _cts.Token));
        }

        RaiseChanged();
    }

    /// <summary>Handshake: accept the client only when it speaks the same protocol version.</summary>
    public HelloResponse Hello(HelloRequest request)
        => request.ProtocolVersion == DistProtocol.Version
            ? new HelloResponse(true, DistProtocol.Version, null)
            : new HelloResponse(false, DistProtocol.Version,
                $"Protocol mismatch: server speaks v{DistProtocol.Version}, client v{request.ProtocolVersion}.");

    /// <summary>The active job descriptor, or null when nothing is available to sieve.</summary>
    public JobDescriptor? TryGetJob()
        => _accepting && Current is { Phase: OverlordPhase.Sieving } job ? job.Descriptor : null;

    /// <summary>Leases a slice of work, or null when none is currently available.</summary>
    public LeaseResponse? TryLease(int? parallelism = null)
    {
        if (!_accepting)
        {
            return null;
        }

        if (Current is not { Phase: OverlordPhase.Sieving } job)
        {
            return null;
        }

        var chunkSize = _options.ResolveLeaseChunkSize(parallelism, job.Descriptor?.Sieving);
        return job.TryLease(_options.LeaseTtl, chunkSize);
    }

    /// <summary>Copies one opaque relation chunk to the durable inbox without parsing or verifying it.</summary>
    public async Task<RelationChunkResponse> UploadChunkAsync(
        string jobId,
        string leaseId,
        long sequence,
        Stream body,
        CancellationToken cancellationToken = default)
    {
        var job = Current;
        if (job is null || job.JobId != jobId)
        {
            return new RelationChunkResponse(false, sequence, 0, false, "Unknown or inactive job.");
        }

        var inbox = job.BeginChunkUpload(leaseId, _options.LeaseTtl);
        if (inbox is null)
        {
            return new RelationChunkResponse(
                false, sequence, 0, false, "The job or lease is no longer accepting relation chunks.");
        }

        RelationChunkResponse response;
        try
        {
            response = await inbox.StoreAsync(leaseId, sequence, body, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            job.EndChunkUpload(leaseId, _options.LeaseTtl);
        }

        if (!response.Accepted)
        {
            job.AbandonLease(leaseId);
        }

        return response;
    }

    /// <summary>
    /// Durably records that a client has sent every chunk for a lease. The lease remains protected
    /// until the inbox worker has parsed and verified those chunks.
    /// </summary>
    public async Task<LeaseUploadCompleteResponse> CompleteLeaseUploadAsync(
        string jobId,
        string leaseId,
        long chunkCount,
        CancellationToken cancellationToken = default)
    {
        var job = Current;
        if (job is null || job.JobId != jobId)
        {
            return new LeaseUploadCompleteResponse(false, "Unknown or inactive job.");
        }

        var inbox = job.ProtectLeaseForIngest(leaseId);
        if (inbox is null)
        {
            return new LeaseUploadCompleteResponse(false, "The job or lease is no longer accepting completion markers.");
        }

        var response = await inbox.CompleteLeaseAsync(leaseId, chunkCount, cancellationToken).ConfigureAwait(false);
        if (!response.Accepted)
        {
            job.CancelLeaseIngestProtection(leaseId, _options.LeaseTtl);
        }

        return response;
    }

    public void Cancel() => _cts?.Cancel();

    /// <summary>Stops admission, seals accepted uploads, cancels the pipeline, and joins all work.</summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Task stopTask;
        lock (_gate)
        {
            _accepting = false;
            _stopTask ??= StopCoreAsync(_job, _cts, _running);
            stopTask = _stopTask;
        }

        return stopTask.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Cancels and joins the background pipeline so its event log and artifact files are closed
    /// before the owning application or test removes the run directory.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        lock (_gate)
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task<FactorizationJobResult> RunPipelineAsync(
        SiqsPipeline pipeline, OverlordJob job, FactorizationRequest request, string jobId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await pipeline.RunAsync(request, progress: null, cancellationToken, jobId).ConfigureAwait(false);
            await job.CleanupInboxAsync(_options.RetainRelationInboxOnCompletion).ConfigureAwait(false);
            job.Finish(DiscoveredFactors.From(result.Factors));
            return result;
        }
        catch (Exception ex)
        {
            job.Fault(ex.Message);
            throw;
        }
    }

    private async Task<FactorizationJobResult> RunRecoveredPipelineAsync(
        SiqsPipeline pipeline,
        OverlordJob job,
        string directory,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await pipeline.ResumeAsync(
                directory, overrides: null, progress: null, cancellationToken).ConfigureAwait(false);
            await job.CleanupInboxAsync(_options.RetainRelationInboxOnCompletion).ConfigureAwait(false);
            job.Finish(DiscoveredFactors.From(result.Factors));
            return result;
        }
        catch (Exception ex)
        {
            job.Fault($"Distributed recovery failed: {ex.Message}");
            throw;
        }
    }

    private void RaiseChanged() => Changed?.Invoke();

    private static bool IsTerminal(JobStatus status)
        => status is JobStatus.CompletedNoFactor or JobStatus.CompletedPrime or JobStatus.CompletedProbablePrime or
            JobStatus.CompletedFactorFound or JobStatus.CompletedTrivialFactor;

    private static bool IsDistributedJobId(string jobId)
        => jobId.Length == 21 && jobId[0] == 'D' &&
           DateTimeOffset.TryParseExact(
               jobId[1..], "yyyyMMdd-HHmmss-ffff", null,
               System.Globalization.DateTimeStyles.None, out _);

    private static async Task StopCoreAsync(
        OverlordJob? job,
        CancellationTokenSource? cancellation,
        Task<FactorizationJobResult>? running)
    {
        var drain = job?.StopAcceptingAndDrainAsync() ?? Task.CompletedTask;
        cancellation?.Cancel();
        try
        {
            await Task.WhenAll(drain, running ?? Task.CompletedTask).ConfigureAwait(false);
        }
        catch (Exception) when (running?.IsCompleted == true)
        {
            // Pipeline failures remain observable through Completion; shutdown still joined it.
        }
    }
}

public sealed record RecoverableDistributedJob(
    string JobId,
    string TargetN,
    JobStatus Status,
    bool IsEligible,
    string? Error = null);
