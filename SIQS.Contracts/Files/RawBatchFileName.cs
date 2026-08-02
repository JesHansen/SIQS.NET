using System.Globalization;

namespace SIQS.Contracts.Files;

/// <summary>
/// A canonical numbered raw sieve batch name such as <c>relations_0000.txt</c> or
/// <c>partials_10000.txt</c>.
/// </summary>
public readonly record struct RawBatchFileName
{
    private RawBatchFileName(string prefix, int index)
    {
        Prefix = prefix;
        Index = index;
    }

    public string Prefix { get; }
    public int Index { get; }
    public string FileName => $"{Prefix}_{Index:D4}.txt";

    public static RawBatchFileName Create(string prefix, int index)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return new RawBatchFileName(prefix, index);
    }

    public static bool TryParse(string fileName, string expectedPrefix, out RawBatchFileName batch)
    {
        batch = default;
        var start = expectedPrefix.Length + 1;
        const int suffixLength = 4;
        if (!fileName.StartsWith(expectedPrefix + "_", StringComparison.Ordinal) ||
            !fileName.EndsWith(".txt", StringComparison.Ordinal) ||
            fileName.Length < start + 4 + suffixLength)
        {
            return false;
        }

        var digits = fileName.AsSpan(start, fileName.Length - start - suffixLength);
        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
        {
            return false;
        }

        var parsed = new RawBatchFileName(expectedPrefix, index);
        if (!fileName.Equals(parsed.FileName, StringComparison.Ordinal))
        {
            return false;
        }

        batch = parsed;
        return true;
    }
}
