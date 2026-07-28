using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Numerics;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Factorbase;
using Sieving;
using SIQS.Contracts;
using SIQS.Contracts.Distributed;
using SIQS.Contracts.Files;

const string clientVersion = "2.0";

var serverUrl = (args.FirstOrDefault() ?? "http://localhost:5000").TrimEnd('/');
using var http = new HttpClient { BaseAddress = new Uri(serverUrl), Timeout = Timeout.InfiniteTimeSpan };

Console.WriteLine($"SIQS distributed sieve client v{clientVersion} -> {serverUrl}");

// Handshake: refuse to sieve for a server we cannot agree with.
try
{
    var helloResult = await GetJson<HelloResponse>(
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
        var descriptor = await GetJson<JobDescriptor>(http, HttpMethod.Get, "/api/dist/job", null, cts.Token);
        if (descriptor is null)
        {
            Console.WriteLine("No job available; waiting...");
            await Task.Delay(idleDelay, cts.Token);
            continue;
        }

        if (context is null || context.JobId != descriptor.JobId)
        {
            context = ClientContext.Build(descriptor);
            Console.WriteLine($"Joined job {descriptor.JobId} (N has {descriptor.N.Length} digits, {descriptor.ACount} A-candidates).");
        }

        var lease = await GetJson<LeaseResponse>(http, HttpMethod.Post, "/api/dist/lease", null, cts.Token);
        if (lease is null)
        {
            await Task.Delay(idleDelay, cts.Token);
            continue;
        }

        var range = new HashSet<int>(Enumerable.Range(lease.AStart, lease.AEnd - lease.AStart));
        using var leaseCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        using var leaseProgress = new ClientLeaseProgress(lease, queueCapacity: 256);
        var sink = new StreamingRawRelationSink(capacity: 256, leaseCts.Token, leaseProgress);
        var uploadTask = UploadRelations(http, lease, sink.Reader, leaseCts, leaseProgress, cts.Token);
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
            if (result.Accepted)
            {
                var acceptedSummary =
                    $"{summary} -> accepted {result.AcceptedCount}, rejected {result.RejectedCount}";
                if (result.Reason is null)
                {
                    Console.WriteLine(acceptedSummary);
                }
                else
                {
                    Console.Error.WriteLine($"[{DateTimeOffset.Now:O}] {acceptedSummary}; {result.Reason}");
                }
            }
            else
            {
                Console.Error.WriteLine($"[{DateTimeOffset.Now:O}] {summary} -> upload declined: {result.Reason}");
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

// Sends an optional JSON body and returns the deserialized response, or null on 204 No Content.
static async Task<T?> GetJson<T>(HttpClient http, HttpMethod method, string path, object? body, CancellationToken ct)
{
    using var request = new HttpRequestMessage(method, path);
    if (body is not null)
    {
        request.Content = JsonContent.Create(body, body.GetType());
    }

    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    if (response.StatusCode == HttpStatusCode.NoContent)
    {
        return default;
    }

    await EnsureSuccess(response, ct);
    return await response.Content.ReadFromJsonAsync<T>(ct);
}

static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken ct)
{
    if (response.IsSuccessStatusCode)
    {
        return;
    }

    var detail = await response.Content.ReadAsStringAsync(ct);
    var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" Response: {detail.Trim()}";
    throw new HttpRequestException(
        $"Server returned {(int)response.StatusCode} {response.ReasonPhrase}.{suffix}",
        null,
        response.StatusCode);
}

static async Task SafeDelay(TimeSpan delay, CancellationToken ct)
{
    try { await Task.Delay(delay, ct); }
    catch (OperationCanceledException) { }
}

static async Task<UploadResponse> UploadRelations(
    HttpClient http,
    LeaseResponse lease,
    ChannelReader<RawRelationRecord> relations,
    CancellationTokenSource leaseCts,
    ClientLeaseProgress progress,
    CancellationToken cancellationToken)
{
    try
    {
        var path = $"/api/dist/relations/{Uri.EscapeDataString(lease.JobId)}/{Uri.EscapeDataString(lease.LeaseId)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new NdjsonRelationContent(relations, progress),
        };
        using var response = await http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccess(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<UploadResponse>(cancellationToken)
            ?? throw new HttpRequestException("The server returned an empty relation-upload response.");
    }
    catch
    {
        leaseCts.Cancel();
        throw;
    }
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

/// <summary>
/// A bounded bridge between the parallel sieve and the HTTP request body. When the network is slower
/// than the sieve, producers apply backpressure instead of allowing relation memory to grow without bound.
/// </summary>
sealed class StreamingRawRelationSink : IRawRelationSink
{
    private readonly Channel<RawRelationRecord> _channel;
    private readonly CancellationToken _cancellationToken;
    private readonly ClientLeaseProgress _progress;

    public StreamingRawRelationSink(
        int capacity, CancellationToken cancellationToken, ClientLeaseProgress progress)
    {
        _cancellationToken = cancellationToken;
        _progress = progress;
        _channel = Channel.CreateBounded<RawRelationRecord>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public ChannelReader<RawRelationRecord> Reader => _channel.Reader;

    public void Add(RawRelationRecord relation)
    {
        _channel.Writer.WriteAsync(relation, _cancellationToken).AsTask().GetAwaiter().GetResult();
        _progress.RecordProduced();
    }

    public void Flush()
    {
    }

    public void Complete() => _channel.Writer.TryComplete();

    public void Fail(Exception exception) => _channel.Writer.TryComplete(exception);
}

/// <summary>Writes one compact JSON object per line without ever computing or buffering a content length.</summary>
sealed class NdjsonRelationContent : HttpContent
{
    private static readonly byte[] NewLine = "\n"u8.ToArray();
    private readonly ChannelReader<RawRelationRecord> _relations;
    private readonly ClientLeaseProgress _progress;

    public NdjsonRelationContent(ChannelReader<RawRelationRecord> relations, ClientLeaseProgress progress)
    {
        _relations = relations;
        _progress = progress;
        Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson");
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(
        Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        await foreach (var relation in _relations.ReadAllAsync(cancellationToken))
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(
                RelationUploadCodec.ToUploadRecord(relation), JsonSerializerOptions.Web);
            await stream.WriteAsync(json, cancellationToken);
            await stream.WriteAsync(NewLine, cancellationToken);
            _progress.RecordStreamed();
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}

/// <summary>Low-frequency client heartbeat that remains useful even when network backpressure stalls workers.</summary>
sealed class ClientLeaseProgress : IProgress<SiqsProgressEvent>, IDisposable
{
    private readonly LeaseResponse _lease;
    private readonly int _queueCapacity;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly Timer _heartbeat;
    private long _polynomials;
    private long _fullRelations;
    private long _partialRelations;
    private long _usableRelations;
    private long _produced;
    private long _streamed;
    private int _disposed;

    public ClientLeaseProgress(LeaseResponse lease, int queueCapacity)
    {
        _lease = lease;
        _queueCapacity = queueCapacity;
        _heartbeat = new Timer(_ => WriteHeartbeat(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public void Report(SiqsProgressEvent value)
    {
        SetCounter(value, "polynomials", ref _polynomials);
        SetCounter(value, "full_relations", ref _fullRelations);
        SetCounter(value, "partial_relations", ref _partialRelations);
        SetCounter(value, "usable_relations", ref _usableRelations);
    }

    public void RecordProduced() => Interlocked.Increment(ref _produced);

    public void RecordStreamed() => Interlocked.Increment(ref _streamed);

    public void Dispose()
    {
        Interlocked.Exchange(ref _disposed, 1);
        _heartbeat.Dispose();
    }

    private void WriteHeartbeat()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var produced = Volatile.Read(ref _produced);
        var streamed = Volatile.Read(ref _streamed);
        var queued = Math.Clamp(produced - streamed, 0, _queueCapacity);
        Console.WriteLine(
            $"Lease {_lease.LeaseId}: {_elapsed.Elapsed:hh\\:mm\\:ss}, " +
            $"{Volatile.Read(ref _polynomials)} polynomials, " +
            $"{Volatile.Read(ref _fullRelations)} full / {Volatile.Read(ref _partialRelations)} partial, " +
            $"{Volatile.Read(ref _usableRelations)} usable; " +
            $"generated {produced}, written to HTTP {streamed}, queue {queued}/{_queueCapacity}.");
    }

    private static void SetCounter(SiqsProgressEvent value, string name, ref long destination)
    {
        if (value.Counters.TryGetValue(name, out var text) && long.TryParse(text, out var parsed))
        {
            Volatile.Write(ref destination, parsed);
        }
    }
}
