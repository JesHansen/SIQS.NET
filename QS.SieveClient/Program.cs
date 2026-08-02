using System.Numerics;
using System.Diagnostics;
using Factorbase;
using Sieving;
using SIQS.Contracts;
using SIQS.Contracts.Distributed;
using SIQS.Contracts.Files;

const string clientVersion = "3.3";

var serverUrl = (args.FirstOrDefault() ?? "http://localhost:5000").TrimEnd('/');
using var http = new HttpClient { BaseAddress = new Uri(serverUrl), Timeout = Timeout.InfiniteTimeSpan };

Console.WriteLine($"SIQS distributed sieve client v{clientVersion} -> {serverUrl}");

// Handshake: refuse to sieve for a server we cannot agree with.
try
{
    var helloResult = await ClientHttp.GetJsonAsync<HelloResponse>(
        http, HttpMethod.Post, "/api/dist/hello", new HelloRequest(clientVersion, DistProtocol.Version), CancellationToken.None);
    if (helloResult is null || !helloResult.Accepted)
    {
        Console.Error.WriteLine($"Handshake rejected: {helloResult?.Reason ?? "no response"}.");
        return 1;
    }
}
catch (Exception ex)
{
    ReportError("Could not complete the server handshake", ex);
    return 1;
}

Console.WriteLine("Handshake accepted. Waiting for work - press Ctrl+C to stop.");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

ClientContext? context = null;
var idleDelay = TimeSpan.FromSeconds(2);

while (!cts.IsCancellationRequested)
{
    try
    {
        var descriptor = await ClientHttp.GetJsonAsync<JobDescriptor>(http, HttpMethod.Get, "/api/dist/job", null, cts.Token);
        if (descriptor is null)
        {
            Console.WriteLine("No job available; waiting...");
            await Task.Delay(idleDelay, cts.Token);
            continue;
        }

        if (context is null || context.JobId != descriptor.JobId)
        {
            context = ClientContext.Build(descriptor);
            Console.WriteLine(
                $"Joined job {descriptor.JobId} (N has {descriptor.N.Length} digits, " +
                $"{descriptor.ACount} A-candidates, {context.Parameters.EffectiveParallelism} sieve workers).");
        }

        var leasePath = $"/api/dist/lease?parallelism={context.Parameters.EffectiveParallelism}";
        var lease = await ClientHttp.GetJsonAsync<LeaseResponse>(
            http, HttpMethod.Post, leasePath, null, cts.Token);
        if (lease is null)
        {
            await Task.Delay(idleDelay, cts.Token);
            continue;
        }

        var range = new HashSet<int>(Enumerable.Range(lease.AStart, lease.AEnd - lease.AStart));
        using var leaseCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        using var leaseProgress = new ClientLeaseProgress(
            lease,
            ClientTransportSettings.ChannelCapacity,
            ClientTransportSettings.RelationChunkCapacity,
            ClientTransportSettings.MaxConcurrentUploads,
            context.Parameters.EffectiveParallelism);
        var sink = new StreamingRawRelationSink(
            ClientTransportSettings.ChannelCapacity,
            leaseCts.Token,
            leaseProgress);
        var uploadTask = RelationUploadPipeline.UploadAsync(
            http, lease, sink.Reader, leaseCts, leaseProgress, cts.Token);
        Console.WriteLine(
            $"Lease {lease.LeaseId} [{lease.AStart}..{lease.AEnd}) received; sieving and streaming relations...");

        try
        {
            var counters = SievingEngine.Sieve(
                context.FactorBase, context.Parameters, sink, leaseProgress, leaseCts.Token, null, range);
            var result = await uploadTask;
            var summary =
                $"Lease {lease.LeaseId} [{lease.AStart}..{lease.AEnd}): " +
                $"sieved {counters.FullRelations} full / {counters.Partials} partial";
            if (result.Response.Accepted)
            {
                Console.WriteLine(
                    $"{summary} -> durably uploaded {result.Relations} relations in {result.Chunks} chunks; " +
                    $"server verification continues in the background; {leaseProgress.TransportSummary()}");
            }
            else
            {
                Console.Error.WriteLine(
                    $"[{DateTimeOffset.Now:O}] {summary} -> upload declined: {result.Response.Reason}");
            }
        }
        catch (Exception sieveException)
        {
            sink.Fail(sieveException);

            // A transport failure cancels the sieve to release bounded-channel backpressure. Prefer
            // that original transport exception over the resulting cancellation from the sieve.
            try
            {
                await uploadTask;
            }
            catch (Exception uploadException)
            {
                var sieveWasCancelled = sieveException is OperationCanceledException ||
                    sieveException is AggregateException aggregate && IsCancellationAggregate(aggregate);
                if (sieveWasCancelled && !cts.IsCancellationRequested)
                {
                    throw new HttpRequestException($"Lease {lease.LeaseId} upload failed.", uploadException);
                }
            }

            throw;
        }
    }
    catch (OperationCanceledException) when (cts.IsCancellationRequested)
    {
        break;
    }
    catch (AggregateException ex) when (cts.IsCancellationRequested && IsCancellationAggregate(ex))
    {
        break;
    }
    catch (Exception ex)
    {
        ReportError("Transient client error; retrying", ex);
        await SafeDelay(idleDelay, cts.Token);
    }
}

Console.WriteLine("Stopped.");
return 0;

static async Task SafeDelay(TimeSpan delay, CancellationToken ct)
{
    try { await Task.Delay(delay, ct); }
    catch (OperationCanceledException) { }
}

static bool IsCancellationAggregate(AggregateException exception)
    => exception.Flatten().InnerExceptions.All(inner => inner is OperationCanceledException);

static void ReportError(string context, Exception exception)
{
    Console.Error.WriteLine($"[{DateTimeOffset.Now:O}] {context}:");
    Console.Error.WriteLine(exception);
}

/// <summary>The client's local reconstruction of a job, with the determinism checks that guard
/// against divergence.</summary>
sealed record ClientContext(string JobId, FactorBaseDocument FactorBase, SievingParameters Parameters)
{
    public static ClientContext Build(JobDescriptor d)
    {
        var generation = FactorBaseGenerator.Generate(new FactorBaseOptions(
            BigInteger.Parse(d.N), d.FactorBaseBound, BigInteger.Parse(d.Multiplier), d.AllowTinyTrialDivision));
        if (generation.FactorBase is not { } factorBase)
        {
            throw new InvalidOperationException("Server assigned a number that resolves to a trivial factor.");
        }

        var parameters = d.Sieving.ToParameters();

        var localACount = PolynomialGenerator.SelectAPositions(FactorBaseData.From(factorBase), parameters).Count;
        var localHash = DistProtocol.ComputeParamHash(
            DistProtocol.Version, d.N, d.FactorBaseBound, d.Multiplier, d.AllowTinyTrialDivision, d.Sieving);
        if (localACount != d.ACount || localHash != d.ParamHash)
        {
            throw new InvalidOperationException(
                "Local job reconstruction disagrees with the server (A-count or parameter hash mismatch); refusing to sieve.");
        }

        return new ClientContext(d.JobId, factorBase, parameters);
    }
}

/// <summary>Low-frequency client heartbeat that remains useful even when network backpressure stalls workers.</summary>
sealed class ClientLeaseProgress : IProgress<SiqsProgressEvent>, IClientTransportProgress, IDisposable
{
    private readonly LeaseResponse _lease;
    private readonly int _queueCapacity;
    private readonly int _chunkCapacity;
    private readonly int _maxConcurrentUploads;
    private readonly int _sieveParallelism;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly Timer _heartbeat;
    private long _polynomials;
    private long _fullRelations;
    private long _partialRelations;
    private long _usableRelations;
    private long _activeAFamilies;
    private long _produced;
    private long _dequeued;
    private long _streamed;
    private long _durable;
    private long _durableChunks;
    private long _durableAckTimestampTicks;
    private long _maxDurableAckTimestampTicks;
    private long _producerBackpressureTimestampTicks;
    private long _producerBackpressureStarted;
    private long _producerBackpressureEpisodes;
    private int _blockedProducers;
    private int _uploadsInFlight;
    private int _maxUploadsInFlight;
    private int _disposed;

    public ClientLeaseProgress(
        LeaseResponse lease,
        int queueCapacity,
        int chunkCapacity,
        int maxConcurrentUploads,
        int sieveParallelism)
    {
        _lease = lease;
        _queueCapacity = queueCapacity;
        _chunkCapacity = chunkCapacity;
        _maxConcurrentUploads = maxConcurrentUploads;
        _sieveParallelism = sieveParallelism;
        _heartbeat = new Timer(_ => WriteHeartbeat(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public void Report(SiqsProgressEvent value)
    {
        SetCounter(value, "polynomials", ref _polynomials);
        SetCounter(value, "full_relations", ref _fullRelations);
        SetCounter(value, "partial_relations", ref _partialRelations);
        SetCounter(value, "usable_relations", ref _usableRelations);
        SetCounter(value, "active_a_families", ref _activeAFamilies);
    }

    public void RecordProduced() => Interlocked.Increment(ref _produced);

    public void RecordDequeued() => Interlocked.Increment(ref _dequeued);

    public void RecordStreamed() => Interlocked.Increment(ref _streamed);

    public void BeginProducerWait()
    {
        if (Interlocked.Increment(ref _blockedProducers) == 1)
        {
            Interlocked.Exchange(ref _producerBackpressureStarted, Stopwatch.GetTimestamp());
            Interlocked.Increment(ref _producerBackpressureEpisodes);
        }
    }

    public void EndProducerWait()
    {
        if (Interlocked.Decrement(ref _blockedProducers) == 0)
        {
            var started = Interlocked.Exchange(ref _producerBackpressureStarted, 0);
            if (started > 0)
            {
                Interlocked.Add(
                    ref _producerBackpressureTimestampTicks,
                    Stopwatch.GetTimestamp() - started);
            }
        }
    }

    public void RecordUploadStarted()
    {
        var current = Interlocked.Increment(ref _uploadsInFlight);
        SetMaximum(ref _maxUploadsInFlight, current);
    }

    public void RecordUploadCompleted(int relationCount, long elapsedTimestampTicks, bool durable)
    {
        Interlocked.Decrement(ref _uploadsInFlight);
        if (!durable)
        {
            return;
        }

        Interlocked.Add(ref _durable, relationCount);
        Interlocked.Increment(ref _durableChunks);
        Interlocked.Add(ref _durableAckTimestampTicks, elapsedTimestampTicks);
        SetMaximum(ref _maxDurableAckTimestampTicks, elapsedTimestampTicks);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _disposed, 1);
        _heartbeat.Dispose();
    }

    public string TransportSummary()
    {
        var durableChunks = Volatile.Read(ref _durableChunks);
        var averageAckMilliseconds = durableChunks == 0
            ? 0.0
            : TimestampTicksToMilliseconds(Volatile.Read(ref _durableAckTimestampTicks)) / durableChunks;
        return $"transport backpressure {BackpressureSeconds():F2}s in " +
               $"{Volatile.Read(ref _producerBackpressureEpisodes)} episodes, durable ACK " +
               $"avg {averageAckMilliseconds:F1}ms / max " +
               $"{TimestampTicksToMilliseconds(Volatile.Read(ref _maxDurableAckTimestampTicks)):F1}ms, " +
               $"max {Volatile.Read(ref _maxUploadsInFlight)} uploads in flight";
    }

    private void WriteHeartbeat()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var produced = Volatile.Read(ref _produced);
        var dequeued = Volatile.Read(ref _dequeued);
        var streamed = Volatile.Read(ref _streamed);
        var durable = Volatile.Read(ref _durable);
        var queued = Math.Clamp(produced - dequeued, 0, _queueCapacity);
        var staged = Math.Clamp(dequeued - streamed, 0, _chunkCapacity);
        var awaitingAck = Math.Clamp(
            streamed - durable,
            0,
            (long)_chunkCapacity * _maxConcurrentUploads);
        Console.WriteLine(
            $"Lease {_lease.LeaseId}: {_elapsed.Elapsed:hh\\:mm\\:ss}, " +
            $"{Volatile.Read(ref _polynomials)} polynomials, " +
            $"{Volatile.Read(ref _fullRelations)} full / {Volatile.Read(ref _partialRelations)} partial, " +
            $"{Volatile.Read(ref _usableRelations)} usable; " +
            $"A workers {Volatile.Read(ref _activeAFamilies)}/{_sieveParallelism}; " +
            $"generated {produced}; transport channel {queued}/{_queueCapacity}, " +
            $"staged {staged}/{_chunkCapacity}, awaiting durable ACK {awaitingAck}, " +
            $"uploads {Volatile.Read(ref _uploadsInFlight)}/{_maxConcurrentUploads}, " +
            $"backpressure {BackpressureSeconds():F2}s, " +
            $"durable {durable} ({Volatile.Read(ref _durableChunks)} chunks).");
    }

    private static void SetCounter(SiqsProgressEvent value, string name, ref long destination)
    {
        if (value.Counters.TryGetValue(name, out var text) && long.TryParse(text, out var parsed))
        {
            Volatile.Write(ref destination, parsed);
        }
    }

    private double BackpressureSeconds()
    {
        var timestampTicks = Volatile.Read(ref _producerBackpressureTimestampTicks);
        if (Volatile.Read(ref _blockedProducers) > 0)
        {
            var started = Volatile.Read(ref _producerBackpressureStarted);
            if (started > 0)
            {
                timestampTicks += Stopwatch.GetTimestamp() - started;
            }
        }

        return timestampTicks / (double)Stopwatch.Frequency;
    }

    private static double TimestampTicksToMilliseconds(long timestampTicks)
        => timestampTicks * 1_000.0 / Stopwatch.Frequency;

    private static void SetMaximum(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }

    private static void SetMaximum(ref long target, long value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }
}
