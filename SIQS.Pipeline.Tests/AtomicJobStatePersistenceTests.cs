using SIQS.Contracts;
using SIQS.Pipeline;

namespace SIQS.Pipeline.Tests;

public sealed class AtomicJobStatePersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "siqs-atomic-state-tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData((int)JobStateWriteStage.TemporaryFileCreated, JobStatus.Created)]
    [InlineData((int)JobStateWriteStage.TemporaryFileFlushed, JobStatus.Created)]
    [InlineData((int)JobStateWriteStage.BeforePrimaryReplace, JobStatus.Created)]
    [InlineData((int)JobStateWriteStage.PrimaryReplaced, JobStatus.Running)]
    public void Interrupted_write_leaves_old_or_new_complete_state_readable(
        int interruptedStageValue,
        JobStatus expectedStatus)
    {
        var interruptedStage = (JobStateWriteStage)interruptedStageValue;
        Directory.CreateDirectory(_directory);
        new AtomicJobStatePersistence().Write(_directory, State(JobStatus.Created));
        var interrupted = new AtomicJobStatePersistence(stage =>
        {
            if (stage == interruptedStage)
            {
                throw new SimulatedInterruptionException();
            }
        });

        Assert.Throws<SimulatedInterruptionException>(() =>
            interrupted.Write(_directory, State(JobStatus.Running)));

        var recovered = new AtomicJobStatePersistence().Load(_directory);
        Assert.Equal(expectedStatus, recovered.Status);
        Assert.Empty(TemporaryFiles());
    }

    [Fact]
    public void Complete_orphan_is_promoted_when_initial_primary_publish_is_interrupted()
    {
        Directory.CreateDirectory(_directory);
        var interrupted = new AtomicJobStatePersistence(stage =>
        {
            if (stage == JobStateWriteStage.BeforePrimaryReplace)
            {
                throw new SimulatedInterruptionException();
            }
        });
        Assert.Throws<SimulatedInterruptionException>(() =>
            interrupted.Write(_directory, State(JobStatus.Created)));

        var recovered = new AtomicJobStatePersistence().Load(_directory);

        Assert.Equal(JobStatus.Created, recovered.Status);
        Assert.True(File.Exists(Path.Combine(_directory, JobStore.FileName)));
        Assert.Empty(TemporaryFiles());
    }

    [Fact]
    public void Corrupt_primary_recovers_last_known_good_backup_and_repairs_primary()
    {
        Directory.CreateDirectory(_directory);
        var persistence = new AtomicJobStatePersistence();
        persistence.Write(_directory, State(JobStatus.Created));
        persistence.Write(_directory, State(JobStatus.Running));
        File.WriteAllText(Path.Combine(_directory, JobStore.FileName), "{ partial");

        var recovered = persistence.Load(_directory);
        var repeated = persistence.Load(_directory);

        Assert.Equal(JobStatus.Created, recovered.Status);
        Assert.Equal(JobStatus.Created, repeated.Status);
        Assert.Empty(TemporaryFiles());
    }

    [Fact]
    public void Repeated_writes_clean_orphaned_temporary_files()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, $"{JobStore.FileName}.{Guid.NewGuid():N}.tmp"), "partial");
        var persistence = new AtomicJobStatePersistence();

        persistence.Write(_directory, State(JobStatus.Created));
        persistence.Write(_directory, State(JobStatus.Running));

        Assert.Empty(TemporaryFiles());
        Assert.True(File.Exists(Path.Combine(_directory, AtomicJobStatePersistence.BackupFileName)));
    }

    private static JobState State(JobStatus status) => new()
    {
        JobId = "J20260818-123456-0001",
        TargetN = "91",
        Status = status,
    };

    private string[] TemporaryFiles() =>
        Directory.GetFiles(_directory, $"{JobStore.FileName}.*.tmp", SearchOption.TopDirectoryOnly);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class SimulatedInterruptionException : Exception;
}
