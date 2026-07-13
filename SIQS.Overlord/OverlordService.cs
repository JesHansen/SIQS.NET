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
    /// Number of A-indices handed out per lease. The sieve parallelizes across the A-indices in a
    /// lease, so this also caps a single client's parallelism; keep it at or above typical core counts
    /// so multi-core clients stay saturated, while small enough that many volunteers can share a job.
    /// </summary>
    public int LeaseChunkSize { get; init; } = 64;
}

/// <summary>
/// Singleton application service coordinating the one active distributed factorization. It runs the
/// SIQS pipeline on a background task with a <see cref="DistributedSievingPhaseExecutor"/>, so
/// factor-base building, filtering, linear algebra, and the square root happen centrally while the
/// sieve is farmed out. Endpoints call the handshake / lease / upload methods; the UI observes
/// <see cref="Changed"/> and <see cref="Snapshot"/>.
/// </summary>
public sealed class OverlordService
{
    private readonly string _runsRoot;
    private readonly OverlordOptions _options;
    private readonly object _gate = new();

    private OverlordJob? _job;
    private CancellationTokenSource? _cts;
    private Task<FactorizationJobResult>? _running;

    public OverlordService(string runsRoot, OverlordOptions? options = null)
    {
        _runsRoot = runsRoot;
        _options = options ?? new OverlordOptions();
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

    /// <summary>Starts a distributed factorization. Throws if one is already active.</summary>
    public void Submit(FactorizationRequest request)
    {
        lock (_gate)
        {
            if (_job is { Phase: not (OverlordPhase.Completed or OverlordPhase.Faulted) })
            {
                throw new InvalidOperationException("A distributed job is already active.");
            }

            var jobId = $"D{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-ffff}";
            var directory = Path.Combine(_runsRoot, jobId);
            var job = new OverlordJob(jobId, request.TargetN.ToString(), directory);
            job.Changed += RaiseChanged;

            var executor = new DistributedSievingPhaseExecutor(new RealPhaseExecutor(), job, _options.LeaseChunkSize);
            var pipeline = new SiqsPipeline(executor);
            var normalized = request with { RunDirectory = directory };

            _job = job;
            _cts = new CancellationTokenSource();
            _running = Task.Run(() => RunPipelineAsync(pipeline, job, normalized, jobId, _cts.Token));
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
        => Current is { Phase: OverlordPhase.Sieving } job ? job.Descriptor : null;

    /// <summary>Leases a slice of work, or null when none is currently available.</summary>
    public LeaseResponse? TryLease()
        => Current is { Phase: OverlordPhase.Sieving } job ? job.TryLease(_options.LeaseTtl) : null;

    /// <summary>Verifies and ingests an uploaded relation batch.</summary>
    public UploadResponse Upload(UploadRequest request)
    {
        var job = Current;
        if (job is null || job.JobId != request.JobId)
        {
            return new UploadResponse(false, 0, 0, "Unknown or inactive job.");
        }

        IReadOnlyList<RawRelationRecord> records;
        try
        {
            records = RelationUploadCodec.Parse(request);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return new UploadResponse(false, 0, 0, $"Malformed upload: {ex.Message}");
        }

        return job.AcceptUpload(request.LeaseId, records);
    }

    public void Cancel() => _cts?.Cancel();

    private async Task<FactorizationJobResult> RunPipelineAsync(
        SiqsPipeline pipeline, OverlordJob job, FactorizationRequest request, string jobId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await pipeline.RunAsync(request, progress: null, cancellationToken, jobId);
            job.Finish(DiscoveredFactors.From(result.Factors));
            return result;
        }
        catch (Exception ex)
        {
            job.Fault(ex.Message);
            throw;
        }
    }

    private void RaiseChanged() => Changed?.Invoke();
}
