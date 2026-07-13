using System.Numerics;
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
        var service = new OverlordService(_runsRoot, new OverlordOptions { LeaseChunkSize = 16 });
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

            var upload = RelationUploadCodec.ToUpload(
                lease.JobId, lease.LeaseId, context.Metadata, sink.FullRelations, sink.Partials);
            service.Upload(upload);
        }
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
