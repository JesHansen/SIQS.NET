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
        if (interruptedStage is JobStateWriteStage.TemporaryFileCreated or
            JobStateWriteStage.TemporaryFileFlushed or JobStateWriteStage.BeforePrimaryReplace)
        {
            Assert.NotEmpty(TemporaryFiles());
        }
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
        Assert.NotEmpty(TemporaryFiles());
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
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reader_during_temporary_file_flush_does_not_break_active_writer(bool hasPrimary)
    {
        Directory.CreateDirectory(_directory);
        if (hasPrimary)
        {
            new AtomicJobStatePersistence().Write(_directory, State(JobStatus.Created));
        }

        var writer = new AtomicJobStatePersistence(stage =>
        {
            if (stage == JobStateWriteStage.TemporaryFileFlushed)
            {
                _ = new AtomicJobStatePersistence().Load(_directory);
            }
        });

        writer.Write(_directory, State(JobStatus.Running));

        Assert.Equal(JobStatus.Running, new AtomicJobStatePersistence().Load(_directory).Status);
    }

    [Fact]
    public void Snapshot_read_does_not_delete_pending_publication()
    {
        Directory.CreateDirectory(_directory);
        new AtomicJobStatePersistence().Write(_directory, State(JobStatus.Created));
        var observed = new List<JobStatus>();
        var writer = new AtomicJobStatePersistence(stage =>
        {
            if (stage == JobStateWriteStage.BeforePrimaryReplace)
            {
                observed.Add(JobStore.LoadSnapshot(_directory).Status);
            }
        });

        writer.Write(_directory, State(JobStatus.Running));

        Assert.Equal([JobStatus.Created], observed);
        Assert.Equal(JobStatus.Running, JobStore.Load(_directory).Status);
    }

    [Fact]
    public void Write_does_not_delete_another_writers_pending_publication()
    {
        Directory.CreateDirectory(_directory);
        new AtomicJobStatePersistence().Write(_directory, State(JobStatus.Created));
        var concurrentWriter = new AtomicJobStatePersistence(stage =>
        {
            if (stage == JobStateWriteStage.BeforePrimaryReplace)
            {
                new AtomicJobStatePersistence().Write(_directory, State(JobStatus.Running));
            }
        });

        concurrentWriter.Write(_directory, State(JobStatus.CompletedFactorFound));

        Assert.Equal(JobStatus.CompletedFactorFound, JobStore.Load(_directory).Status);
    }

    [Fact]
    public void Repeated_writes_clean_orphaned_temporary_files()
    {
        Directory.CreateDirectory(_directory);
        var orphan = Path.Combine(_directory, $"{JobStore.FileName}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(orphan, "partial");
        File.SetLastWriteTimeUtc(orphan, DateTime.UtcNow - AtomicJobStatePersistence.OrphanAge - TimeSpan.FromMinutes(1));
        var persistence = new AtomicJobStatePersistence();

        persistence.Write(_directory, State(JobStatus.Created));
        persistence.Write(_directory, State(JobStatus.Running));

        Assert.Empty(TemporaryFiles());
        Assert.True(File.Exists(Path.Combine(_directory, AtomicJobStatePersistence.BackupFileName)));
    }

    [Fact]
    public void Transient_primary_publish_failures_are_retried()
    {
        Directory.CreateDirectory(_directory);
        var attempts = 0;
        var delays = new List<int>();
        var persistence = PersistenceWithMove((source, destination, overwrite) =>
        {
            attempts++;
            if (attempts <= 2)
            {
                throw new UnauthorizedAccessException("Simulated transient destination lock.");
            }

            File.Move(source, destination, overwrite);
        }, delays);

        persistence.Write(_directory, State(JobStatus.Created));

        Assert.Equal(3, attempts);
        Assert.Equal([10, 20], delays);
        Assert.Equal(JobStatus.Created, new AtomicJobStatePersistence().Load(_directory).Status);
    }

    [Fact]
    public void Transient_backup_publish_failures_are_retried()
    {
        Directory.CreateDirectory(_directory);
        new AtomicJobStatePersistence().Write(_directory, State(JobStatus.Created));
        var backupAttempts = 0;
        var delays = new List<int>();
        var persistence = PersistenceWithMove((source, destination, overwrite) =>
        {
            if (Path.GetFileName(destination) == AtomicJobStatePersistence.BackupFileName
                && ++backupAttempts <= 2)
            {
                throw new IOException("Simulated transient destination lock.");
            }

            File.Move(source, destination, overwrite);
        }, delays);

        persistence.Write(_directory, State(JobStatus.Running));

        Assert.Equal(3, backupAttempts);
        Assert.Equal([10, 20], delays);
        Assert.Equal(JobStatus.Running, persistence.Load(_directory).Status);
    }

    [Fact]
    public void Transient_restore_failures_are_retried()
    {
        Directory.CreateDirectory(_directory);
        var initial = new AtomicJobStatePersistence();
        initial.Write(_directory, State(JobStatus.Created));
        initial.Write(_directory, State(JobStatus.Running));
        File.WriteAllText(Path.Combine(_directory, JobStore.FileName), "{ partial");
        var restoreAttempts = 0;
        var delays = new List<int>();
        var persistence = PersistenceWithMove((source, destination, overwrite) =>
        {
            if (Path.GetFileName(destination) == JobStore.FileName && ++restoreAttempts <= 2)
            {
                throw new UnauthorizedAccessException("Simulated transient destination lock.");
            }

            File.Move(source, destination, overwrite);
        }, delays);

        var recovered = persistence.Load(_directory);

        Assert.Equal(3, restoreAttempts);
        Assert.Equal([10, 20], delays);
        Assert.Equal(JobStatus.Created, recovered.Status);
    }

    private static AtomicJobStatePersistence PersistenceWithMove(
        Action<string, string, bool> moveFile,
        List<int> delays)
        => new(onStage: null, moveFile, delays.Add);

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
