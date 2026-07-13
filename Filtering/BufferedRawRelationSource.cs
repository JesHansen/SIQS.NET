using SIQS.Contracts;

namespace Filtering;

/// <summary>
/// Wraps an in-memory record sequence as a source. The sequence is enumerated exactly once (during
/// <see cref="Enumerate"/>) and buffered so <see cref="Materialize"/> can serve locators; this keeps
/// the historical in-memory behavior for callers that do not have file-backed input.
/// </summary>
public sealed class BufferedRawRelationSource : IRawRelationSource
{
    private readonly IEnumerable<RawRelationRecord> _records;
    private readonly List<RawRelationRecord> _buffer = [];

    public BufferedRawRelationSource(IEnumerable<RawRelationRecord> records) => _records = records;

    public IEnumerable<(RawRelationLocator Locator, RawRelationRecord Record)> Enumerate()
    {
        foreach (var record in _records)
        {
            _buffer.Add(record);
            yield return (new RawRelationLocator(0, _buffer.Count - 1), record);
        }
    }

    public IEnumerable<(RawRelationLocator Locator, RawRelationRecord Record)> Materialize(
        IReadOnlyList<RawRelationLocator> ascendingLocators)
    {
        foreach (var locator in ascendingLocators)
        {
            yield return (locator, _buffer[locator.Ordinal]);
        }
    }
}
