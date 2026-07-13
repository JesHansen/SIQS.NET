using SIQS.Contracts;
using SIQS.Overlord;
using SIQS.Pipeline;
using SIQS.UI.Services;

namespace SIQS.UI.Tests;

public sealed class JobCatalogTests
{
    [Fact]
    public void Lists_completed_and_failed_jobs_and_skips_invalid_directories()
    {
        var root = Path.Combine(Path.GetTempPath(), $"siqs-ui-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            Write(root, new JobState
            {
                JobId = "J20260713-101500-1234",
                TargetN = "10403",
                Status = JobStatus.CompletedFactorFound,
                CreatedUtc = "2026-07-13T10:15:00.0000000+00:00",
                CompletedUtc = "2026-07-13T10:16:00.0000000+00:00",
                FinalFactors = ["101", "103"],
            });
            Write(root, new JobState
            {
                JobId = "D20260713-111500-1234",
                TargetN = "15347",
                Status = JobStatus.Failed,
                CreatedUtc = "2026-07-13T11:15:00.0000000+00:00",
            });
            Directory.CreateDirectory(Path.Combine(root, "garbage"));

            var runs = new RunsDirectory(root);
            var catalog = new JobCatalog(runs, new FactorizationJobService(new SiqsPipeline(), runs), new OverlordService(root));
            var entries = catalog.List();

            Assert.Equal(2, entries.Count);
            Assert.Equal("D20260713-111500-1234", entries[0].JobId);
            Assert.Equal(JobKind.Distributed, entries[0].Kind);
            Assert.Equal(JobStatus.Failed, entries[0].Status);
            Assert.Equal(JobKind.OneShot, entries[1].Kind);
            Assert.Equal(["101", "103"], entries[1].Factors);
            Assert.Equal(2, catalog.CompletedCount());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Write(string root, JobState state)
    {
        var directory = Path.Combine(root, state.JobId);
        Directory.CreateDirectory(directory);
        JobStore.Write(directory, state);
    }
}
