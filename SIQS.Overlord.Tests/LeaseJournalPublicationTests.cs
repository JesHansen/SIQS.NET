namespace SIQS.Overlord.Tests;

/// <summary>
/// leases.json is republished on every lease operation. A brief external open of the destination
/// makes the Windows replace fail with access denied, so publication retries within a bound and
/// reports a failure that outlasts it.
/// </summary>
public sealed class LeaseJournalPublicationTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("siqs-lease-journal-").FullName;

    [Fact]
    public void Transient_access_denied_on_replace_is_retried()
    {
        var attempts = 0;
        var delays = new List<int>();
        var journal = new LeaseJournal(_directory, (source, destination, overwrite) =>
        {
            if (++attempts <= 2)
            {
                throw new UnauthorizedAccessException("Simulated scanner handle on leases.json.");
            }

            File.Move(source, destination, overwrite);
        }, delays.Add);

        journal.Save(State(completed: 4));

        Assert.Equal(3, attempts);
        Assert.Equal([10, 20], delays);
        Assert.Equal(4, new LeaseJournal(_directory).Load()!.Completed);
        Assert.Empty(TemporaryFiles());
    }

    [Fact]
    public void Persistent_publication_failure_is_reported_and_keeps_the_last_saved_state()
    {
        new LeaseJournal(_directory).Save(State(completed: 2));
        var journal = new LeaseJournal(_directory,
            (_, _, _) => throw new UnauthorizedAccessException("Destination stays locked."), _ => { });

        Assert.Throws<UnauthorizedAccessException>(() => journal.Save(State(completed: 4)));

        Assert.Equal(2, new LeaseJournal(_directory).Load()!.Completed);
        Assert.Empty(TemporaryFiles());
    }

    [Fact]
    public async Task Save_succeeds_once_a_brief_external_open_of_the_journal_closes()
    {
        var journal = new LeaseJournal(_directory);
        journal.Save(State(completed: 1));
        var path = Path.Combine(_directory, LeaseJournal.DirectoryName, LeaseJournal.FileName);

        var holder = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var save = Task.Run(() => journal.Save(State(completed: 3)));
        await Task.Delay(50);
        await holder.DisposeAsync();
        await save;

        Assert.Equal(3, new LeaseJournal(_directory).Load()!.Completed);
    }

    private static LeaseJournalState State(int completed)
        => new(ACount: 10, ChunkSize: 2, Cursor: completed, Completed: completed, LeaseSequence: completed, [], []);

    private string[] TemporaryFiles()
        => Directory.GetFiles(Path.Combine(_directory, LeaseJournal.DirectoryName), "*.tmp");

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
