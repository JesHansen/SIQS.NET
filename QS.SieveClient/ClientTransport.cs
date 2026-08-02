using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Sieving;
using SIQS.Contracts;
using SIQS.Contracts.Distributed;

internal sealed record ClientUploadResult(
    LeaseUploadCompleteResponse Response,
    long Relations,
    long Chunks);

internal static class ClientTransportSettings
{
    // One full relation chunk lets the sieve absorb a transient ACK stall without immediately
    // parking its worker threads. Upload concurrency remains bounded separately below.
    public const int ChannelCapacity = 4_096;
    public const int RelationChunkCapacity = 4_096;
    public const int MaxConcurrentUploads = 3;
}

internal interface IClientTransportProgress
{
    void RecordProduced();
    void RecordDequeued();
    void RecordStreamed();
    void BeginProducerWait();
    void EndProducerWait();
    void RecordUploadStarted();
    void RecordUploadCompleted(int relationCount, long elapsedTimestampTicks, bool durable);
}

/// <summary>
/// Bounded relation-upload pipeline. It drains the sieve channel into immutable chunks and permits a
/// small number of durable HTTP writes to overlap, so one network round trip or server fsync does not
/// immediately stall every sieve worker. The lease completion marker is still sent only after every
/// chunk has received its durable acknowledgement.
/// </summary>
internal static class RelationUploadPipeline
{
    public static async Task<ClientUploadResult> UploadAsync(
        HttpClient http,
        LeaseResponse lease,
        ChannelReader<RawRelationRecord> relations,
        CancellationTokenSource leaseCts,
        IClientTransportProgress progress,
        CancellationToken cancellationToken,
        int relationChunkCapacity = ClientTransportSettings.RelationChunkCapacity,
        int maxConcurrentUploads = ClientTransportSettings.MaxConcurrentUploads)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(relationChunkCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrentUploads, 1);

        using var uploadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pending = new List<Task>(maxConcurrentUploads);
        try
        {
            var batch = new List<RawRelationRecord>(relationChunkCapacity);
            long sequence = 0;
            long relationCount = 0;
            await foreach (var relation in relations.ReadAllAsync(uploadCts.Token))
            {
                progress.RecordDequeued();
                batch.Add(relation);
                if (batch.Count < relationChunkCapacity)
                {
                    continue;
                }

                var chunk = batch.ToArray();
                batch.Clear();
                pending.Add(UploadChunkAsync(
                    http, lease, sequence++, chunk, progress, uploadCts.Token));
                relationCount += chunk.Length;
                if (pending.Count >= maxConcurrentUploads)
                {
                    await AwaitOneAsync(pending);
                }
            }

            if (batch.Count > 0)
            {
                var chunk = batch.ToArray();
                pending.Add(UploadChunkAsync(
                    http, lease, sequence++, chunk, progress, uploadCts.Token));
                relationCount += chunk.Length;
            }

            while (pending.Count > 0)
            {
                await AwaitOneAsync(pending);
            }

            var completionPath =
                $"/api/dist/relations/{Uri.EscapeDataString(lease.JobId)}/{Uri.EscapeDataString(lease.LeaseId)}/complete";
            var completion = await ClientHttp.GetJsonAsync<LeaseUploadCompleteResponse>(
                http,
                HttpMethod.Post,
                completionPath,
                new LeaseUploadCompleteRequest(sequence),
                uploadCts.Token)
                ?? throw new HttpRequestException("The server returned an empty lease-upload response.");
            return new ClientUploadResult(completion, relationCount, sequence);
        }
        catch
        {
            uploadCts.Cancel();
            leaseCts.Cancel();
            await ObservePendingAsync(pending);
            throw;
        }
    }

    private static async Task AwaitOneAsync(List<Task> pending)
    {
        var completed = await Task.WhenAny(pending);
        pending.Remove(completed);
        await completed;
    }

    private static async Task ObservePendingAsync(List<Task> pending)
    {
        try
        {
            await Task.WhenAll(pending);
        }
        catch
        {
            // Preserve the exception that failed the pipeline while still observing every other
            // in-flight request after cancellation.
        }
    }

    private static async Task UploadChunkAsync(
        HttpClient http,
        LeaseResponse lease,
        long sequence,
        IReadOnlyCollection<RawRelationRecord> relations,
        IClientTransportProgress progress,
        CancellationToken cancellationToken)
    {
        var path =
            $"/api/dist/relations/{Uri.EscapeDataString(lease.JobId)}/{Uri.EscapeDataString(lease.LeaseId)}/{sequence}";
        var started = Stopwatch.GetTimestamp();
        var durable = false;
        progress.RecordUploadStarted();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new NdjsonRelationChunkContent(relations, progress),
            };
            using var response = await http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            await ClientHttp.EnsureSuccessAsync(response, cancellationToken);
            var receipt = await response.Content.ReadFromJsonAsync<RelationChunkResponse>(cancellationToken)
                ?? throw new HttpRequestException("The server returned an empty relation-chunk response.");
            if (!receipt.Accepted)
            {
                throw new HttpRequestException($"The server declined relation chunk {sequence}: {receipt.Reason}");
            }

            durable = true;
        }
        finally
        {
            progress.RecordUploadCompleted(
                relations.Count,
                Stopwatch.GetTimestamp() - started,
                durable);
        }
    }
}

internal static class ClientHttp
{
    /// <summary>Sends an optional JSON body and returns the response, or null for 204 No Content.</summary>
    public static async Task<T?> GetJsonAsync<T>(
        HttpClient http,
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }

        using var response = await http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    public static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" Response: {detail.Trim()}";
        throw new HttpRequestException(
            $"Server returned {(int)response.StatusCode} {response.ReasonPhrase}.{suffix}",
            null,
            response.StatusCode);
    }
}

/// <summary>
/// A bounded bridge between parallel sieve workers and the upload pipeline. Backpressure remains a
/// deliberate safety boundary, but the queue now holds one complete upload chunk.
/// </summary>
internal sealed class StreamingRawRelationSink : IRawRelationSink
{
    private readonly Channel<RawRelationRecord> _channel;
    private readonly CancellationToken _cancellationToken;
    private readonly IClientTransportProgress _progress;

    public StreamingRawRelationSink(
        int capacity,
        CancellationToken cancellationToken,
        IClientTransportProgress progress)
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
        if (!_channel.Writer.TryWrite(relation))
        {
            _progress.BeginProducerWait();
            try
            {
                _channel.Writer.WriteAsync(relation, _cancellationToken).AsTask().GetAwaiter().GetResult();
            }
            finally
            {
                _progress.EndProducerWait();
            }
        }

        _progress.RecordProduced();
    }

    public void Flush()
    {
    }

    public void Complete() => _channel.Writer.TryComplete();

    public void Fail(Exception exception) => _channel.Writer.TryComplete(exception);
}

/// <summary>Writes one compact JSON object per line without buffering a complete request body.</summary>
internal sealed class NdjsonRelationChunkContent : HttpContent
{
    private static readonly byte[] NewLine = "\n"u8.ToArray();
    private readonly IReadOnlyCollection<RawRelationRecord> _relations;
    private readonly IClientTransportProgress _progress;

    public NdjsonRelationChunkContent(
        IReadOnlyCollection<RawRelationRecord> relations,
        IClientTransportProgress progress)
    {
        _relations = relations;
        _progress = progress;
        Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson");
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(
        Stream stream,
        TransportContext? context,
        CancellationToken cancellationToken)
    {
        foreach (var relation in _relations)
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
