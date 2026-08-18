using System.Numerics;
using SIQS.Contracts;
using SIQS.Overlord;
using SIQS.Pipeline;

namespace SIQS.Overlord.Tests;

public sealed class DistributedRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "siqs-distributed-recovery", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Lease_journal_reclaims_interrupted_uploads_but_preserves_pending_ingest()
    {
        Directory.CreateDirectory(_root);
        var issued = DateTimeOffset.UtcNow;
        var ledger = new LeaseLedger(10, 2, _root);
        var completed = ledger.TryLease(TimeSpan.FromMinutes(5), issued)!;
        Assert.True(ledger.Complete(completed.LeaseId));
        var interrupted = ledger.TryLease(TimeSpan.FromMinutes(5), issued)!;
        var pending = ledger.TryLease(TimeSpan.FromMinutes(5), issued)!;
        Assert.True(ledger.ProtectPendingIngest(pending.LeaseId, issued));

        var recovered = new LeaseLedger(10, 2, _root);
        var reassigned = recovered.TryLease(TimeSpan.FromMinutes(5), issued.AddMinutes(1))!;

        Assert.Equal(interrupted.Start, reassigned.Start);
        Assert.Equal(interrupted.End, reassigned.End);
        Assert.DoesNotContain(pending.Start,
            Enumerable.Range(reassigned.Start, reassigned.End - reassigned.Start));
        Assert.True(recovered.Complete(pending.LeaseId));
        Assert.Equal(4, recovered.Snapshot(issued.AddMinutes(1)).Completed);
    }

    [Fact]
    public async Task Interrupted_job_is_operator_visible_and_recovers_same_identity_and_ranges()
    {
        var options = new OverlordOptions
        {
            LeaseChunkSize = 2,
            UploadGracePeriod = TimeSpan.Zero,
        };
        string jobId;
        int interruptedStart;
        await using (var first = new OverlordService(_root, options))
        {
            first.Submit(Request());
            await WaitUntil(() => first.TryGetJob() is not null);
            jobId = first.Current!.JobId;
            interruptedStart = first.TryLease()!.AStart;
            await first.StopAsync(CancellationToken.None);
        }

        await using var recovered = new OverlordService(_root, options);
        var choice = Assert.Single(recovered.ListRecoverableJobs());
        Assert.Equal(jobId, choice.JobId);
        Assert.True(choice.IsEligible);

        recovered.Recover(jobId);
        await WaitUntil(() => recovered.TryGetJob() is not null);
        var lease = recovered.TryLease();

        Assert.NotNull(lease);
        Assert.Equal(jobId, recovered.Current!.JobId);
        Assert.Equal(interruptedStart, lease.AStart);
        await recovered.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Multiple_interrupted_jobs_are_listed_deterministically_without_auto_activation()
    {
        var firstId = await CreateInterruptedJob();
        await Task.Delay(2);
        var secondId = await CreateInterruptedJob();

        await using var observer = new OverlordService(_root);
        var jobs = observer.ListRecoverableJobs();

        Assert.Null(observer.Current);
        Assert.Equal(new[] { firstId, secondId }.Order(StringComparer.Ordinal), jobs.Select(job => job.JobId));
    }

    private async Task<string> CreateInterruptedJob()
    {
        await using var service = new OverlordService(_root);
        service.Submit(Request());
        await WaitUntil(() => service.Current is not null);
        var id = service.Current!.JobId;
        await service.StopAsync(CancellationToken.None);
        return id;
    }

    private static FactorizationRequest Request()
        => new(BigInteger.Parse("1022117"))
        {
            FactorBase = new FactorBaseRunOptions { Bound = 1000, Multiplier = 1 },
            Sieving = new SievingRunOptions
            {
                HalfInterval = 20_000,
                APrimeCount = 2,
                APrimeWindowSize = 24,
                RelationTarget = 150,
                PolynomialCount = 200_000,
            },
        };

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
