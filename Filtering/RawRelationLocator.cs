namespace Filtering;

/// <summary>
/// Addresses one raw relation record by its file and zero-based data-row ordinal, so the engine
/// can hold an 8-byte handle during graph construction and re-read the record later instead of
/// retaining the parsed record.
/// </summary>
public readonly record struct RawRelationLocator(int FileIndex, int Ordinal) : IComparable<RawRelationLocator>
{
    public int CompareTo(RawRelationLocator other)
    {
        var byFile = FileIndex.CompareTo(other.FileIndex);
        return byFile != 0 ? byFile : Ordinal.CompareTo(other.Ordinal);
    }
}
