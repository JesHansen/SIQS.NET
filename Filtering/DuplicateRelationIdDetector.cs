using SIQS.Contracts;
using SIQS.Contracts.Files;

namespace Filtering;

/// <summary>
/// Tracks seen relation ids and canonical record fingerprints so duplicate detection over
/// millions of records does not retain the id strings. A false duplicate needs full 128-bit
/// collisions in both the id and content fingerprints.
/// </summary>
public sealed class DuplicateRelationIdDetector
{
    private readonly Dictionary<(ulong, ulong), (ulong, ulong)> _seen = [];
    public long DuplicateRowsDropped { get; private set; }

    /// <summary>Returns false if the id was already seen.</summary>
    public bool Add(string relationId) => _seen.TryAdd(Fingerprint(relationId), default);

    /// <summary>
    /// Returns true when the record should be forwarded. Identical duplicate rows are counted and
    /// skipped; same-id rows with different parsed content throw.
    /// </summary>
    public bool ShouldForward(RawRelationRecord record)
    {
        var id = Fingerprint(record.RelationId);
        var content = Fingerprint(RawRelationCanonicalForm.Content(record));
        if (!_seen.TryGetValue(id, out var existing))
        {
            _seen[id] = content;
            return true;
        }

        if (existing == content)
        {
            DuplicateRowsDropped++;
            return false;
        }

        throw new FormatException($"Duplicate raw relation id '{record.RelationId}' has different content.");
    }

    private static (ulong, ulong) Fingerprint(string value)
    {
        var h1 = 0xcbf29ce484222325UL;
        var h2 = 0x9E3779B97F4A7C15UL;
        foreach (var ch in value)
        {
            h1 = (h1 ^ ch) * 0x100000001B3UL;
            h2 = (h2 ^ ch) * 0xD1B54A32D192ED03UL;
        }

        return (h1, h2);
    }
}
