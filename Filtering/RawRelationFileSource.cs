using SIQS.Contracts;
using SIQS.Contracts.Files;

namespace Filtering;

/// <summary>
/// Streams raw relation records from <c>relations_*.txt</c> / <c>partials_*.txt</c> files without
/// retaining them, and re-reads selected records positionally for Pass 2. Files must not change
/// between the two passes.
/// </summary>
public sealed class RawRelationFileSource : IRawRelationSource
{
    private readonly IReadOnlyList<string> _paths;
    private readonly Action<RawRelationsMetadata, string>? _validateMetadata;
    private readonly Func<RawRelationRecord, bool>? _recordFilter;

    public RawRelationFileSource(
        IReadOnlyList<string> paths,
        Action<RawRelationsMetadata, string>? validateMetadata = null,
        Func<RawRelationRecord, bool>? recordFilter = null)
    {
        _paths = paths;
        _validateMetadata = validateMetadata;
        _recordFilter = recordFilter;
    }

    public IEnumerable<(RawRelationLocator Locator, RawRelationRecord Record)> Enumerate()
    {
        for (var fileIndex = 0; fileIndex < _paths.Count; fileIndex++)
        {
            ValidateMetadata(_paths[fileIndex]);
            using var reader = new StreamReader(_paths[fileIndex]);
            foreach (var (ordinal, record) in RawRelationsFile.EnumerateWithOrdinals(reader))
            {
                if (_recordFilter is not null && !_recordFilter(record))
                {
                    continue;
                }

                yield return (new RawRelationLocator(fileIndex, ordinal), record);
            }
        }
    }

    public IEnumerable<(RawRelationLocator Locator, RawRelationRecord Record)> Materialize(
        IReadOnlyList<RawRelationLocator> ascendingLocators)
    {
        var i = 0;
        while (i < ascendingLocators.Count)
        {
            var fileIndex = ascendingLocators[i].FileIndex;
            var ordinals = new List<int>();
            while (i < ascendingLocators.Count && ascendingLocators[i].FileIndex == fileIndex)
            {
                ordinals.Add(ascendingLocators[i].Ordinal);
                i++;
            }

            using var reader = new StreamReader(_paths[fileIndex]);
            foreach (var (ordinal, record) in RawRelationsFile.ParseRecordsAt(reader, ordinals))
            {
                yield return (new RawRelationLocator(fileIndex, ordinal), record);
            }
        }
    }

    private void ValidateMetadata(string path)
    {
        if (_validateMetadata is null)
        {
            return;
        }

        using var reader = new StreamReader(path);
        _validateMetadata(RawRelationsFile.ReadMetadata(reader), path);
    }
}
