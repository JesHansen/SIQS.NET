using System.Text.Json;

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

/// <summary>Atomic durable storage for distributed A-range ownership.</summary>
internal sealed class LeaseJournal
{
    public const string FileName = "leases.json";
    private readonly string _path;

    public LeaseJournal(string jobDirectory)
    {
        var directory = Path.Combine(jobDirectory, ".distributed-state");
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
            File.Move(temporary, _path, overwrite: true);
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
