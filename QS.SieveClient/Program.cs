using System.Net;
using System.Net.Http.Json;
using System.Numerics;
using Factorbase;
using Sieving;
using SIQS.Contracts;
using SIQS.Contracts.Distributed;
using SIQS.Contracts.Files;

const string clientVersion = "1.0";

var serverUrl = (args.FirstOrDefault() ?? "http://localhost:5000").TrimEnd('/');
using var http = new HttpClient { BaseAddress = new Uri(serverUrl), Timeout = TimeSpan.FromMinutes(10) };

Console.WriteLine($"SIQS distributed sieve client v{clientVersion} -> {serverUrl}");

// Handshake: refuse to sieve for a server we cannot agree with.
try
{
    var hello = await http.PostAsJsonAsync("/api/dist/hello", new HelloRequest(clientVersion, DistProtocol.Version));
    var helloResult = await hello.Content.ReadFromJsonAsync<HelloResponse>();
    if (helloResult is null || !helloResult.Accepted)
    {
        Console.Error.WriteLine($"Handshake rejected: {helloResult?.Reason ?? "no response"}.");
        return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Could not reach server: {ex.Message}");
    return 1;
}

Console.WriteLine("Handshake accepted. Waiting for work - press Ctrl+C to stop.");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

ClientContext? context = null;
var idleDelay = TimeSpan.FromSeconds(2);

// The upload of the previous lease runs in the background so it overlaps the next sieve, keeping all
// cores busy instead of idling during the serialize + HTTP round trip. At most one upload is in flight.
Task<string?> pendingUpload = Task.FromResult<string?>(null);

async Task DrainUploadLog()
{
    var line = await pendingUpload;
    if (line is not null)
    {
        Console.WriteLine(line);
    }

    pendingUpload = Task.FromResult<string?>(null);
}

while (!cts.IsCancellationRequested)
{
    try
    {
        var descriptor = await GetJson<JobDescriptor>(http, HttpMethod.Get, "/api/dist/job", null, cts.Token);
        if (descriptor is null)
        {
            await DrainUploadLog();
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
            await DrainUploadLog();
            await Task.Delay(idleDelay, cts.Token);
            continue;
        }

        var range = new HashSet<int>(Enumerable.Range(lease.AStart, lease.AEnd - lease.AStart));
        var sink = new InMemoryRawRelationSink();
        SievingEngine.Sieve(context.FactorBase, context.Parameters, sink, null, cts.Token, null, range);

        // Print the previous lease's outcome, then upload this one in the background while we go straight
        // back to leasing and sieving the next range.
        await DrainUploadLog();
        pendingUpload = UploadAndDescribe(http, lease, context.Metadata, sink.FullRelations, sink.Partials, cts.Token);
    }
    catch (OperationCanceledException) when (cts.IsCancellationRequested)
    {
        break;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Transient error: {ex.Message}; retrying...");
        await SafeDelay(idleDelay, cts.Token);
    }
}

await DrainUploadLog();
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

    using var response = await http.SendAsync(request, ct);
    if (response.StatusCode == HttpStatusCode.NoContent)
    {
        return default;
    }

    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<T>(ct);
}

static async Task SafeDelay(TimeSpan delay, CancellationToken ct)
{
    try { await Task.Delay(delay, ct); }
    catch (OperationCanceledException) { }
}

// Serializes and uploads one lease's relations, returning a log line. Never throws: a failed upload is
// logged and the lease is simply reissued by the server after its TTL.
static async Task<string?> UploadAndDescribe(
    HttpClient http, LeaseResponse lease, RawRelationsMetadata metadata,
    IReadOnlyList<RawRelationRecord> fullRelations, IReadOnlyList<RawRelationRecord> partials, CancellationToken ct)
{
    try
    {
        var upload = RelationUploadCodec.ToUpload(lease.JobId, lease.LeaseId, metadata, fullRelations, partials);
        var result = await GetJson<UploadResponse>(http, HttpMethod.Post, "/api/dist/relations", upload, ct);
        return $"Lease {lease.LeaseId} [{lease.AStart}..{lease.AEnd}): sieved {fullRelations.Count} full / {partials.Count} partial -> " +
            (result is { Accepted: true } ? $"accepted {result.AcceptedCount}, rejected {result.RejectedCount}" : $"upload declined: {result?.Reason}");
    }
    catch (OperationCanceledException)
    {
        return null;
    }
    catch (Exception ex)
    {
        return $"Lease {lease.LeaseId}: upload failed ({ex.Message}); will be reissued.";
    }
}

/// <summary>The client's local reconstruction of a job: the factor base, parameters, and relation
/// metadata rebuilt from the descriptor, with the determinism checks that guard against divergence.</summary>
sealed record ClientContext(
    string JobId, FactorBaseDocument FactorBase, SievingParameters Parameters, RawRelationsMetadata Metadata)
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

        var metadata = new RawRelationsMetadata(
            factorBase.Metadata.TargetN, factorBase.Metadata.Multiplier, factorBase.Metadata.ScaledN,
            factorBase.Metadata.Bound, parameters.LargePrimeBound,
            parameters.EnableTwoLargePrimes ? parameters.LargePrime2Bound : null);
        return new ClientContext(d.JobId, factorBase, parameters, metadata);
    }
}
