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

/// <summary>
/// Re-enters the real <see cref="DistributedSievingPhaseExecutor"/> with only a larger relation
/// target, exactly as <c>TopUpCoordinator</c> does after an underdetermined matrix.
/// </summary>
public sealed class DistributedTopUpTests : IDisposable
{
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromMinutes(5);
    private readonly string _directory = Directory.CreateTempSubdirectory("siqs-distributed-topup-").FullName;

    [Fact]
    public async Task Relation_target_top_up_reenters_sieving_with_prior_relations_and_leases()
    {
        var factorBase = FactorBaseGenerator.Generate(new FactorBaseOptions(1022117, 1000, 1)).FactorBase!;
        File.WriteAllText(Path.Combine(_directory, "factor_base.txt"), FactorBaseFile.Write(factorBase));
        var job = new OverlordJob("D20261012-000000-0000", "1022117", _directory);
        var executor = new DistributedSievingPhaseExecutor(
            new RealPhaseExecutor(), job, leaseChunkSize: 1, uploadGracePeriod: TimeSpan.Zero,
            maxRelationChunkBytes: 16 * 1024 * 1024,
            maxRelationBacklogBytes: 1024L * 1024 * 1024,
            maxRelationInboxBytes: 1024L * 1024 * 1024 + 64L * 1024 * 1024);
        var request = Request(relationTarget: 1);
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var firstCompleted = new List<(int Start, int End)>();
        var first = await RunRoundAsync(executor, job, request, firstCompleted, stop.Token);
        var firstDescriptor = job.Descriptor!;
        var firstUsable = int.Parse(first.Counters[CounterKeys.UsableRelations]);
        var firstIds = RelationIdsOnDisk().ToHashSet(StringComparer.Ordinal);

        var topUp = request with
        {
            Sieving = request.Sieving with { RelationTarget = firstUsable + 2_000 },
        };
        var secondCompleted = new List<(int Start, int End)>();
        var second = await RunRoundAsync(executor, job, topUp, secondCompleted, stop.Token);

        Assert.Equal(PhaseStatus.Completed, second.Status);
        Assert.True(int.Parse(second.Counters[CounterKeys.UsableRelations]) >= firstUsable + 2_000);
        Assert.Equal(firstDescriptor.ParamHash, job.Descriptor!.ParamHash);
        Assert.Equal(firstDescriptor.ACount, job.Descriptor.ACount);
        Assert.NotEmpty(firstCompleted);
        Assert.NotEmpty(secondCompleted);
        Assert.Empty(firstCompleted.Intersect(secondCompleted));
        Assert.Subset(RelationIdsOnDisk().ToHashSet(StringComparer.Ordinal), firstIds);
    }

    private async Task<PhaseResult> RunRoundAsync(
        DistributedSievingPhaseExecutor executor,
        OverlordJob job,
        FactorizationRequest request,
        List<(int Start, int End)> completed,
        CancellationToken cancellationToken)
    {
        using var roundDone = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var round = executor.RunSievingAsync(new PhaseContext("topup", _directory, request, null, cancellationToken));
        var client = Task.Run(() => RunClientAsync(job, completed, roundDone.Token), CancellationToken.None);
        var result = await round;
        await roundDone.CancelAsync();
        await client;
        return result;
    }

    private static async Task RunClientAsync(
        OverlordJob job, List<(int Start, int End)> completed, CancellationToken cancellationToken)
    {
        ClientContext? context = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (job.Phase != OverlordPhase.Sieving || job.Descriptor is null ||
                job.TryLease(LeaseTtl, chunkSize: 1) is not { } lease)
            {
                await Task.Delay(5, CancellationToken.None);
                continue;
            }

            context ??= ClientContext.FromDescriptor(job.Descriptor);
            var sink = new InMemoryRawRelationSink();
            SievingEngine.Sieve(context.FactorBase, context.Parameters, sink, null, CancellationToken.None, null,
                Enumerable.Range(lease.AStart, lease.AEnd - lease.AStart).ToHashSet());
            using var upload = await SerializeUploadAsync(sink.FullRelations.Concat(sink.Partials));

            var inbox = job.BeginChunkUpload(lease.LeaseId, LeaseTtl);
            if (inbox is null) continue;
            try
            {
                if (!(await inbox.StoreAsync(lease.LeaseId, 0, upload, CancellationToken.None)).Accepted) continue;
            }
            finally
            {
                job.EndChunkUpload(lease.LeaseId, LeaseTtl);
            }

            if (job.ProtectLeaseForIngest(lease.LeaseId) is { } protectedInbox &&
                (await protectedInbox.CompleteLeaseAsync(lease.LeaseId, 1, CancellationToken.None)).Accepted)
            {
                completed.Add((lease.AStart, lease.AEnd));
            }
        }
    }

    private IEnumerable<string> RelationIdsOnDisk()
        => new[] { "relations", "partials" }
            .SelectMany(prefix => Directory.EnumerateFiles(_directory, $"{prefix}_*.txt"))
            .SelectMany(path => RawRelationsFile.Parse(File.ReadAllText(path)).Relations)
            .Select(relation => relation.RelationId);

    private static FactorizationRequest Request(int relationTarget)
        => new(BigInteger.Parse("1022117")) // 1009 * 1013
        {
            FactorBase = new FactorBaseRunOptions { Bound = 1000, Multiplier = 1 },
            Sieving = new SievingRunOptions
            {
                HalfInterval = 20_000,
                APrimeCount = 2,
                APrimeWindowSize = 24,
                ErrorMargin = 20,
                RelationTarget = relationTarget,
                PolynomialCount = 200_000,
                Parallelism = 1,
            },
        };

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

    private sealed record ClientContext(FactorBaseDocument FactorBase, SievingParameters Parameters)
    {
        public static ClientContext FromDescriptor(JobDescriptor descriptor)
            => new(
                FactorBaseGenerator.Generate(new FactorBaseOptions(
                    BigInteger.Parse(descriptor.N), descriptor.FactorBaseBound,
                    BigInteger.Parse(descriptor.Multiplier), descriptor.AllowTinyTrialDivision)).FactorBase!,
                descriptor.Sieving.ToParameters());
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
