namespace SIQS.Pipeline;

/// <summary>Reads and writes <c>job.json</c> using lower_snake_case property and enum tokens.</summary>
public static class JobStore
{
    private static readonly AtomicJobStatePersistence Persistence = new();

    public const string FileName = "job.json";

    public static void Write(string jobDirectory, JobState state)
        => Persistence.Write(jobDirectory, state);

    public static JobState Load(string jobDirectory) => Persistence.Load(jobDirectory);

    public static JobStateSnapshot LoadSnapshot(string jobDirectory)
        => JobStateSnapshots.From(Load(jobDirectory));
}
