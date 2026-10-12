using System.Text.Json;
using SIQS.Contracts.Files;

namespace SIQS.Overlord;

internal sealed record LeaseRange(int Start, int End);

internal sealed record LeaseJournalState(
    int ACount,
    int ChunkSize,
    int Cursor,
    int Completed,
    long LeaseSequence,
    IReadOnlyList<LeaseLedger.Lease> Outstanding,
    IReadOnlyList<LeaseRange> Reclaimed);

/// <summary>
/// Atomic durable storage for distributed A-range ownership. The journal is republished on every
/// lease operation, so its rename uses <see cref="FilePublication"/>'s bounded retry: a brief
/// external open of <c>leases.json</c> otherwise fails the replace with access denied on Windows.
/// </summary>
internal sealed class LeaseJournal
{
    public const string FileName = "leases.json";
    public const string DirectoryName = ".distributed-state";
    private readonly string _path;
    private readonly Action<string, string, bool> _moveFile;
    private readonly Action<int> _delay;

    public LeaseJournal(string jobDirectory)
        : this(jobDirectory, File.Move, Thread.Sleep)
    {
    }

    internal LeaseJournal(string jobDirectory, Action<string, string, bool> moveFile, Action<int> delay)
    {
        _moveFile = moveFile;
        _delay = delay;
        var directory = Path.Combine(jobDirectory, DirectoryName);
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, FileName);
    }

    public LeaseJournalState? Load()
        => !File.Exists(_path)
            ? null
            : JsonSerializer.Deserialize<LeaseJournalState>(File.ReadAllText(_path), JsonOptions)
              ?? throw new FormatException("leases.json is empty or invalid.");

    public void Save(LeaseJournalState state)
    {
        var temporary = _path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, JsonOptions));
            FilePublication.Move(temporary, _path, overwrite: true, _moveFile, _delay);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };
}
