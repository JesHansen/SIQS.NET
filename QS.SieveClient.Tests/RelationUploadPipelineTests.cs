using System.Collections.Concurrent;
using System.Net;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using SIQS.Contracts;
using SIQS.Contracts.Distributed;

namespace QS.SieveClient.Tests;

public sealed class RelationUploadPipelineTests
{
    [Fact]
    public async Task Uploads_bounded_chunks_concurrently_before_completing_the_lease()
    {
        var handler = new RecordingHandler(requiredConcurrency: 2);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://coordinator") };
        using var leaseCts = new CancellationTokenSource();
        var progress = new RecordingProgress();
        var relations = Relations(7);

        var result = await RelationUploadPipeline.UploadAsync(
            http,
            Lease(),
            relations,
            leaseCts,
            progress,
            CancellationToken.None,
            relationChunkCapacity: 2,
            maxConcurrentUploads: 2);

        Assert.True(result.Response.Accepted);
        Assert.Equal(7, result.Relations);
        Assert.Equal(4, result.Chunks);
        Assert.Equal(2, handler.MaxActiveChunks);
        Assert.Equal([0, 1, 2, 3], handler.Sequences.Order().ToArray());
        Assert.Equal(1, handler.CompletionRequests);
        Assert.False(handler.CompletionWhileChunkActive);
        Assert.Equal(7, progress.DurableRelations);
        Assert.Equal(4, progress.DurableChunks);
        Assert.Equal(2, progress.MaxUploadsInFlight);
        Assert.False(leaseCts.IsCancellationRequested);
    }

    [Fact]
    public async Task A_failed_chunk_cancels_the_lease_and_suppresses_completion()
    {
        var handler = new RecordingHandler(requiredConcurrency: 2, failedSequence: 1);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://coordinator") };
        using var leaseCts = new CancellationTokenSource();

        await Assert.ThrowsAsync<HttpRequestException>(() => RelationUploadPipeline.UploadAsync(
            http,
            Lease(),
            Relations(8),
            leaseCts,
            new RecordingProgress(),
            CancellationToken.None,
            relationChunkCapacity: 2,
            maxConcurrentUploads: 2));

        Assert.True(leaseCts.IsCancellationRequested);
        Assert.Equal(0, handler.CompletionRequests);
    }

    private static LeaseResponse Lease()
        => new("D0001", "L00000001", 0, 64, DateTimeOffset.UtcNow.AddMinutes(5));

    private static ChannelReader<RawRelationRecord> Relations(int count)
    {
        var channel = Channel.CreateUnbounded<RawRelationRecord>();
        for (var i = 0; i < count; i++)
        {
            Assert.True(channel.Writer.TryWrite(new RawRelationRecord(
                $"R{i:D6}",
                RelationKind.Full,
                "P000001_0000",
                BigInteger.One,
                BigInteger.One,
                BigInteger.One,
                i,
                BigInteger.One,
                1,
                new Dictionary<int, int> { [i] = 1 },
                [i],
                null)));
        }

        channel.Writer.Complete();
        return channel.Reader;
    }

    private sealed class RecordingHandler(int requiredConcurrency, long? failedSequence = null)
        : HttpMessageHandler
    {
        private readonly TaskCompletionSource _concurrencyReached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeChunks;
        private int _maxActiveChunks;
        private int _completionRequests;
        private int _completionWhileChunkActive;

        public ConcurrentBag<long> Sequences { get; } = [];
        public int MaxActiveChunks => Volatile.Read(ref _maxActiveChunks);
        public int CompletionRequests => Volatile.Read(ref _completionRequests);
        public bool CompletionWhileChunkActive => Volatile.Read(ref _completionWhileChunkActive) != 0;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/complete", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _completionRequests);
                if (Volatile.Read(ref _activeChunks) != 0)
                {
                    Interlocked.Exchange(ref _completionWhileChunkActive, 1);
                }

                return JsonResponse(new LeaseUploadCompleteResponse(true, null));
            }

            var sequence = long.Parse(path[(path.LastIndexOf('/') + 1)..]);
            Sequences.Add(sequence);
            var active = Interlocked.Increment(ref _activeChunks);
            SetMaximum(ref _maxActiveChunks, active);
            if (active >= requiredConcurrency)
            {
                _concurrencyReached.TrySetResult();
            }

            try
            {
                _ = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                await _concurrencyReached.Task.WaitAsync(cancellationToken);
                await Task.Delay(10, cancellationToken);
                if (sequence == failedSequence)
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent("deliberate test failure"),
                    };
                }

                return JsonResponse(new RelationChunkResponse(true, sequence, 1, false, null));
            }
            finally
            {
                Interlocked.Decrement(ref _activeChunks);
            }
        }

        private static HttpResponseMessage JsonResponse<T>(T value)
            => new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(value, JsonSerializerOptions.Web),
                    Encoding.UTF8,
                    "application/json"),
            };

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
    }

    private sealed class RecordingProgress : IClientTransportProgress
    {
        private int _uploadsInFlight;
        private int _maxUploadsInFlight;
        private int _durableRelations;
        private int _durableChunks;

        public int DurableRelations => Volatile.Read(ref _durableRelations);
        public int DurableChunks => Volatile.Read(ref _durableChunks);
        public int MaxUploadsInFlight => Volatile.Read(ref _maxUploadsInFlight);

        public void RecordProduced()
        {
        }

        public void RecordDequeued()
        {
        }

        public void RecordStreamed()
        {
        }

        public void BeginProducerWait()
        {
        }

        public void EndProducerWait()
        {
        }

        public void RecordUploadStarted()
        {
            var current = Interlocked.Increment(ref _uploadsInFlight);
            var maximum = Volatile.Read(ref _maxUploadsInFlight);
            while (current > maximum)
            {
                var observed = Interlocked.CompareExchange(ref _maxUploadsInFlight, current, maximum);
                if (observed == maximum)
                {
                    break;
                }

                maximum = observed;
            }
        }

        public void RecordUploadCompleted(int relationCount, long elapsedTimestampTicks, bool durable)
        {
            Interlocked.Decrement(ref _uploadsInFlight);
            if (durable)
            {
                Interlocked.Add(ref _durableRelations, relationCount);
                Interlocked.Increment(ref _durableChunks);
            }
        }
    }
}
