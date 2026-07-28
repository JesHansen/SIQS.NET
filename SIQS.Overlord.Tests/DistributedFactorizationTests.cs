using System.Numerics;
using System.Text;
using System.Text.Json;
using Factorbase;
using Sieving;
using SIQS.Contracts;
using SIQS.Contracts.Distributed;
using SIQS.Contracts.Files;
using SIQS.Pipeline;

namespace SIQS.Overlord.Tests;

public class DistributedFactorizationTests : IDisposable
{
    private readonly string _runsRoot = Path.Combine(Path.GetTempPath(), "siqs-overlord-e2e", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_runsRoot))
        {
            Directory.Delete(_runsRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Distributed_run_factors_a_small_composite_with_in_proc_clients()
    {
        var service = new OverlordService(_runsRoot, new OverlordOptions
        {
            LeaseChunkSize = 16,
            UploadGracePeriod = TimeSpan.Zero,
        });
        var request = new FactorizationRequest(BigInteger.Parse("1022117")) // 1009 * 1013
        {
            FactorBase = new FactorBaseRunOptions { Bound = 1000, Multiplier = 1 },
            Sieving = new SievingRunOptions
            {
                HalfInterval = 20_000,
                APrimeCount = 2,
                APrimeWindowSize = 24,
                ErrorMargin = 20,
                RelationTarget = 150,
                PolynomialCount = 200_000,
            },
        };

        service.Submit(request);

        // Three volunteers lease slices, sieve them, and upload — exactly the real client loop minus HTTP.
        var clients = Enumerable.Range(0, 3).Select(_ => Task.Run(() => RunClientAsync(service))).ToArray();

        var result = await service.Completion!;
        await Task.WhenAll(clients);

        Assert.Equal(JobStatus.CompletedFactorFound, result.Status);
        Assert.True(result.FactorFound);
        Assert.Equal(new BigInteger(1022117), result.Factors[0] * result.Factors[1]);
        Assert.Contains(new BigInteger(1009), result.Factors);
        Assert.Contains(new BigInteger(1013), result.Factors);
        Assert.Equal(OverlordPhase.Completed, service.Current!.Phase);

        // The distributed sieve produced the same on-disk artifacts a local run would, so the whole
        // pipeline (filtering, linear algebra, square root) ran centrally against them.
        foreach (var name in new[] { "factor_base.txt", "matrix_meta.txt", "filtered_matrix.txt",
                     "relations_filtered.txt", "dependencies.txt", "factors.txt", "job.json" })
        {
            Assert.True(File.Exists(Path.Combine(service.Current.Directory, name)), $"missing {name}");
        }

        Assert.True(Directory.EnumerateFiles(service.Current.Directory, "relations_*.txt").Any());

        // Fix 1: the run directory, the persisted job.json, and the Overlord job all share one id,
        // so the job is reachable at /jobs/<id>.
        var jobId = service.Current.JobId;
        Assert.Equal(jobId, Path.GetFileName(service.Current.Directory));
        Assert.Equal(jobId, JobStore.LoadSnapshot(service.Current.Directory).JobId);

        // Fix 2: the snapshot surfaces the factors, so the distributed page can show the result.
        var snapshot = service.Snapshot()!;
        Assert.True(snapshot.Factors.Any);
        Assert.Contains("1009", snapshot.Factors.Values);
        Assert.Contains("1013", snapshot.Factors.Values);
    }

    [Fact]
    public async Task Handshake_rejects_a_client_on_a_different_protocol_version()
    {
        var service = new OverlordService(_runsRoot);

        Assert.True(service.Hello(new HelloRequest("test", DistProtocol.Version)).Accepted);

        var mismatch = service.Hello(new HelloRequest("test", DistProtocol.Version + 1));
        Assert.False(mismatch.Accepted);
        Assert.Equal(DistProtocol.Version, mismatch.ProtocolVersion);
        Assert.Contains("mismatch", mismatch.Reason);
        await Task.CompletedTask;
    }

    [Fact]
    public void Active_upload_renewal_keeps_a_lease_outstanding_past_its_original_expiry()
    {
        var ledger = new LeaseLedger(aCount: 250_000, chunkSize: 64);
        var issuedAt = new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
        var ttl = TimeSpan.FromMinutes(5);
        var lease = Assert.IsType<LeaseLedger.Lease>(ledger.TryLease(ttl, issuedAt));

        Assert.True(ledger.Renew(lease.LeaseId, ttl, issuedAt + TimeSpan.FromMinutes(4)));

        var afterOriginalExpiry = ledger.Snapshot(issuedAt + TimeSpan.FromMinutes(6));
        Assert.Equal(64, afterOriginalExpiry.Assigned);
        Assert.Equal(0, afterOriginalExpiry.Completed);
        Assert.Equal(1, afterOriginalExpiry.Outstanding);
    }

    [Fact]
    public void A_renewed_lease_is_not_reclaimed_and_reassigned_after_its_original_expiry()
    {
        var ledger = new LeaseLedger(aCount: 4, chunkSize: 1);
        var t0 = new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
        var ttl = TimeSpan.FromMinutes(5);
        var held = Assert.IsType<LeaseLedger.Lease>(ledger.TryLease(ttl, t0));
        Assert.Equal(0, held.Start);

        // The client keeps uploading, so the lease is renewed just before its original expiry.
        Assert.True(ledger.Renew(held.LeaseId, ttl, t0 + TimeSpan.FromMinutes(4)));

        // A later lease request past the *original* expiry must take fresh ground, not steal the
        // renewed lease's still-active range — otherwise two clients sieve A-index 0 at once.
        var next = Assert.IsType<LeaseLedger.Lease>(ledger.TryLease(ttl, t0 + TimeSpan.FromMinutes(6)));
        Assert.Equal(1, next.Start);
    }

    [Fact]
    public async Task A_streaming_upload_outliving_the_lease_ttl_stays_credited_against_a_polling_ui()
    {
        // Short TTL, so a multi-batch upload can outlast it within the test. Chunk size 4 gives the
        // first slice enough polynomials to yield a useful relation to replay.
        var service = new OverlordService(_runsRoot, new OverlordOptions
        {
            LeaseChunkSize = 4,
            LeaseTtl = TimeSpan.FromMilliseconds(300),
        });
        service.Submit(new FactorizationRequest(BigInteger.Parse("1022117"))
        {
            FactorBase = new FactorBaseRunOptions { Bound = 1000, Multiplier = 1 },
            Sieving = new SievingRunOptions
            {
                HalfInterval = 20_000,
                APrimeCount = 2,
                APrimeWindowSize = 24,
                ErrorMargin = 20,
                RelationTarget = 150,
                PolynomialCount = 200_000,
            },
        });

        while (service.Current!.Phase == OverlordPhase.Preparing)
        {
            await Task.Delay(10);
        }

        var context = ClientContext.FromDescriptor(service.Current.Descriptor!);
        var lease = Assert.IsType<LeaseResponse>(service.TryLease());
        var relation = SieveUsefulRelations(context, lease, count: 1)[0];
        var line = JsonSerializer.Serialize(
            RelationUploadCodec.ToUploadRecord(relation), JsonSerializerOptions.Web);

        // 13 transport batches (each the 128-line ingest boundary) drip-fed 50 ms apart: ~600 ms of
        // streaming against a 300 ms TTL. Each batch arrives well within a TTL of the last, so only
        // the per-batch renewal in IngestUploadBatch can keep the lease outstanding to the end.
        var segment = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(line + "\n", 128)));
        var segments = Enumerable.Range(0, 13).Select(_ => segment).ToArray();
        using var body = new DripStream(segments, TimeSpan.FromMilliseconds(50));

        // The UI polls snapshots throughout; a snapshot sweeps expired leases, so without renewal one
        // of these polls past the 300 ms mark would reclaim the range mid-upload.
        using var pollCts = new CancellationTokenSource();
        var poller = Task.Run(async () =>
        {
            while (!pollCts.IsCancellationRequested)
            {
                service.Snapshot();
                try { await Task.Delay(15, pollCts.Token); } catch (OperationCanceledException) { }
            }
        });

        var response = await service.UploadAsync(lease.JobId, lease.LeaseId, body);

        // Tear the run down before asserting, so a failed assertion still releases the pipeline's
        // file handles and leaves a clean error rather than a cascading directory-cleanup IOException.
        await pollCts.CancelAsync();
        await poller;
        service.Cancel();
        try { await service.Completion!; } catch { }

        Assert.True(response.Accepted);
        // A null reason means ledger.Complete found the lease still outstanding — the streaming
        // renewal outran the polling reclaim. A reclaimed lease reports "expired before completion".
        Assert.Null(response.Reason);
    }

    [Fact]
    public async Task Malformed_stream_does_not_complete_the_lease()
    {
        var service = new OverlordService(_runsRoot, new OverlordOptions { LeaseChunkSize = 1 });
        service.Submit(new FactorizationRequest(BigInteger.Parse("1022117"))
        {
            FactorBase = new FactorBaseRunOptions { Bound = 1000, Multiplier = 1 },
            Sieving = new SievingRunOptions
            {
                HalfInterval = 20_000,
                APrimeCount = 2,
                APrimeWindowSize = 24,
                ErrorMargin = 20,
                RelationTarget = 150,
                PolynomialCount = 200_000,
            },
        });

        while (service.Current!.Phase == OverlordPhase.Preparing)
        {
            await Task.Delay(10);
        }

        var notifications = 0;
        service.Changed += () => Interlocked.Increment(ref notifications);
        var notificationsBeforeLease = Volatile.Read(ref notifications);
        var lease = Assert.IsType<LeaseResponse>(service.TryLease());
        Assert.True(Volatile.Read(ref notifications) > notificationsBeforeLease);
        using var malformed = new MemoryStream(Encoding.UTF8.GetBytes("{not-json}\n"));

        var response = await service.UploadAsync(lease.JobId, lease.LeaseId, malformed);

        Assert.False(response.Accepted);
        Assert.Contains("Malformed upload", response.Reason);
        Assert.Equal(0, service.Snapshot()!.Leases!.Completed);
        Assert.Equal(1, service.Snapshot()!.Leases!.Outstanding);

        service.Cancel();
        await service.Completion!;
    }

    [Fact]
    public async Task Upload_transport_batches_do_not_force_partial_disk_batches()
    {
        var service = new OverlordService(_runsRoot, new OverlordOptions { LeaseChunkSize = 16 });
        service.Submit(new FactorizationRequest(BigInteger.Parse("1022117"))
        {
            FactorBase = new FactorBaseRunOptions { Bound = 1000, Multiplier = 1 },
            Sieving = new SievingRunOptions
            {
                HalfInterval = 20_000,
                APrimeCount = 2,
                APrimeWindowSize = 24,
                ErrorMargin = 20,
                RelationTarget = 150,
                PolynomialCount = 200_000,
                OutputBatchSize = 10,
            },
        });

        while (service.Current!.Phase == OverlordPhase.Preparing)
        {
            await Task.Delay(10);
        }

        var lease = Assert.IsType<LeaseResponse>(service.TryLease());
        var context = ClientContext.FromDescriptor(service.Current.Descriptor!);
        var relations = SieveUsefulRelations(context, lease, count: 2);
        using var body = await SerializeUploadAsync(relations);

        var response = await service.UploadAsync(lease.JobId, lease.LeaseId, body);

        Assert.True(response.Accepted);
        Assert.Equal(2, response.AcceptedCount);
        Assert.Empty(Directory.EnumerateFiles(service.Current.Directory, "relations_*.txt"));

        service.Cancel();
        await service.Completion!;
    }

    [Fact]
    public async Task Grace_period_stops_leasing_then_discards_late_stream_data()
    {
        var service = new OverlordService(_runsRoot, new OverlordOptions
        {
            LeaseChunkSize = 16,
            UploadGracePeriod = TimeSpan.FromMilliseconds(500),
        });
        service.Submit(new FactorizationRequest(BigInteger.Parse("1022117"))
        {
            FactorBase = new FactorBaseRunOptions { Bound = 1000, Multiplier = 1 },
            Sieving = new SievingRunOptions
            {
                HalfInterval = 20_000,
                APrimeCount = 2,
                APrimeWindowSize = 24,
                ErrorMargin = 20,
                RelationTarget = 1,
                PolynomialCount = 200_000,
            },
        });

        while (service.Current!.Phase == OverlordPhase.Preparing)
        {
            await Task.Delay(10);
        }

        var context = ClientContext.FromDescriptor(service.Current.Descriptor!);
        var productiveLease = Assert.IsType<LeaseResponse>(service.TryLease());
        var graceLease = Assert.IsType<LeaseResponse>(service.TryLease());
        var lateLease = Assert.IsType<LeaseResponse>(service.TryLease());

        using var lateBody = new GatedReadStream(Encoding.UTF8.GetBytes("{not-json}\n"));
        var lateUpload = service.UploadAsync(lateLease.JobId, lateLease.LeaseId, lateBody);

        try
        {
            var usefulRelations = SieveUsefulRelations(context, productiveLease, count: 2);
            using var productiveBody = await SerializeUploadAsync([usefulRelations[0]]);
            var productiveResponse = await service.UploadAsync(
                productiveLease.JobId, productiveLease.LeaseId, productiveBody);

            Assert.True(productiveResponse.Accepted);
            Assert.Equal(OverlordPhase.Draining, service.Current.Phase);
            Assert.Null(service.TryLease());
            Assert.False(service.Completion!.IsCompleted);

            using var graceBody = await SerializeUploadAsync([usefulRelations[1]]);
            var graceResponse = await service.UploadAsync(graceLease.JobId, graceLease.LeaseId, graceBody);
            Assert.True(graceResponse.Accepted);
            Assert.Equal(1, graceResponse.AcceptedCount);

            await WaitUntilAsync(() => service.Current.Phase != OverlordPhase.Draining, TimeSpan.FromSeconds(5));
            lateBody.Release();
            var lateResponse = await lateUpload;

            Assert.False(lateResponse.Accepted);
            Assert.Contains("grace period expired", lateResponse.Reason);
        }
        finally
        {
            lateBody.Release();
            try { await lateUpload; } catch { }
            service.Cancel();
            try { await service.Completion!; } catch { }
        }
    }

    private static async Task RunClientAsync(OverlordService service)
    {
        ClientContext? context = null;
        while (service.Current is { } job && job.Phase is not (OverlordPhase.Completed or OverlordPhase.Faulted))
        {
            if (job.Phase != OverlordPhase.Sieving || job.Descriptor is null)
            {
                await Task.Delay(10);
                continue;
            }

            context ??= ClientContext.FromDescriptor(job.Descriptor);
            var lease = service.TryLease();
            if (lease is null)
            {
                await Task.Delay(10);
                continue;
            }

            var range = new HashSet<int>(Enumerable.Range(lease.AStart, lease.AEnd - lease.AStart));
            var sink = new InMemoryRawRelationSink();
            SievingEngine.Sieve(context.FactorBase, context.Parameters, sink, null, CancellationToken.None, null, range);

            using var upload = await SerializeUploadAsync(sink.FullRelations.Concat(sink.Partials));
            await service.UploadAsync(lease.JobId, lease.LeaseId, upload);
        }
    }

    private static async Task<MemoryStream> SieveLeaseAsync(ClientContext context, LeaseResponse lease)
    {
        var range = new HashSet<int>(Enumerable.Range(lease.AStart, lease.AEnd - lease.AStart));
        var sink = new InMemoryRawRelationSink();
        SievingEngine.Sieve(context.FactorBase, context.Parameters, sink, null, CancellationToken.None, null, range);
        return await SerializeUploadAsync(sink.FullRelations.Concat(sink.Partials));
    }

    private static IReadOnlyList<RawRelationRecord> SieveUsefulRelations(
        ClientContext context, LeaseResponse lease, int count)
    {
        var range = new HashSet<int>(Enumerable.Range(lease.AStart, lease.AEnd - lease.AStart));
        var sink = new InMemoryRawRelationSink();
        SievingEngine.Sieve(context.FactorBase, context.Parameters, sink, null, CancellationToken.None, null, range);
        var useful = sink.FullRelations.Where(relation => relation.ParityColumns.Count > 0).Take(count).ToArray();
        Assert.Equal(count, useful.Length);
        return useful;
    }

    private static async Task<MemoryStream> SerializeUploadAsync(IEnumerable<RawRelationRecord> relations)
    {
        var upload = new MemoryStream();
        await using (var writer = new StreamWriter(
            upload, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: true))
        {
            foreach (var relation in relations)
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(
                    RelationUploadCodec.ToUploadRecord(relation), JsonSerializerOptions.Web));
            }
        }

        upload.Position = 0;
        return upload;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!condition())
        {
            await Task.Delay(10, cts.Token);
        }
    }

    private sealed class GatedReadStream(byte[] content) : Stream
    {
        private readonly MemoryStream _inner = new(content);
        private readonly TaskCompletionSource _released =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _released.TrySetResult();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _released.Task.WaitAsync(cancellationToken);
            return await _inner.ReadAsync(buffer, cancellationToken);
        }

        public override async Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _released.Task.WaitAsync(cancellationToken);
            return await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            _released.Task.GetAwaiter().GetResult();
            return _inner.Read(buffer, offset, count);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>A read-only stream that serves a fixed set of byte segments, pausing for a fixed delay
    /// before each segment after the first — so a reader observes the payload arriving in timed bursts,
    /// modelling a client that streams relation batches over a slow connection.</summary>
    private sealed class DripStream(byte[][] segments, TimeSpan delay) : Stream
    {
        private int _segment;
        private int _offset;

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (_segment < segments.Length && _offset >= segments[_segment].Length)
            {
                _segment++;
                _offset = 0;
            }

            if (_segment >= segments.Length)
            {
                return 0;
            }

            if (_offset == 0 && _segment > 0)
            {
                await Task.Delay(delay, cancellationToken);
            }

            var current = segments[_segment];
            var take = Math.Min(buffer.Length, current.Length - _offset);
            current.AsMemory(_offset, take).CopyTo(buffer);
            _offset += take;
            return take;
        }

        public override async Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => await ReadAsync(buffer.AsMemory(offset, count), cancellationToken);

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>The client-side reconstruction of the job: rebuild the factor base and parameters from
    /// the descriptor, asserting the determinism checks a real client makes.</summary>
    private sealed record ClientContext(FactorBaseDocument FactorBase, SievingParameters Parameters, RawRelationsMetadata Metadata)
    {
        public static ClientContext FromDescriptor(JobDescriptor d)
        {
            var factorBase = FactorBaseGenerator.Generate(new FactorBaseOptions(
                BigInteger.Parse(d.N), d.FactorBaseBound, BigInteger.Parse(d.Multiplier), d.AllowTinyTrialDivision)).FactorBase!;
            var parameters = d.Sieving.ToParameters();

            Assert.Equal(d.ACount, PolynomialGenerator.SelectAPositions(FactorBaseData.From(factorBase), parameters).Count);
            Assert.Equal(d.ParamHash, DistProtocol.ComputeParamHash(
                DistProtocol.Version, d.N, d.FactorBaseBound, d.Multiplier, d.AllowTinyTrialDivision, d.Sieving));

            var metadata = new RawRelationsMetadata(
                factorBase.Metadata.TargetN, factorBase.Metadata.Multiplier, factorBase.Metadata.ScaledN,
                factorBase.Metadata.Bound, parameters.LargePrimeBound,
                parameters.EnableTwoLargePrimes ? parameters.LargePrime2Bound : null);
            return new ClientContext(factorBase, parameters, metadata);
        }
    }
}
