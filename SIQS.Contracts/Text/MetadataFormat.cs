namespace SIQS.Contracts.Text;

/// <summary>
/// Parses and writes the <c>key=value</c> metadata lines used in artifact headers, in both the
/// commented form (<c># target_n=77</c>) and the bare form (<c>row_count=1</c>). Values are split
/// on the first <c>=</c> only, so values containing <c>=</c> or <c>,</c> (e.g. a <c>columns=</c>
/// list) are preserved intact.
/// </summary>
public static class MetadataFormat
{
    /// <summary>Formats a commented metadata line: <c># key=value</c>.</summary>
    public static string Comment(string key, string value) => $"# {key}={value}";

    /// <summary>Formats a bare metadata line: <c>key=value</c>.</summary>
    public static string KeyValue(string key, string value) => $"{key}={value}";

    /// <summary>True if the line is a comment line (starts with <c>#</c>, ignoring leading space).</summary>
    public static bool IsComment(string line) => line.TrimStart().StartsWith('#');

    /// <summary>
    /// Attempts to parse a metadata line into its key and value. Accepts both commented and bare
    /// forms. Returns false for blank lines, bare comments without <c>=</c>, and data/header rows.
    /// </summary>
    public static bool TryParse(string line, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;

        var trimmed = line.Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..].Trim();
        }

        if (trimmed.Length == 0)
        {
            return false;
        }

        var eq = trimmed.IndexOf('=');
        if (eq <= 0)
        {
            return false;
        }

        key = trimmed[..eq].Trim();
        value = trimmed[(eq + 1)..].Trim();
        return key.Length > 0;
    }

    /// <summary>
    /// Collects every parseable metadata line (commented or bare) into a dictionary, keeping the
    /// last value for a repeated key. Non key-value lines (CSV headers, data rows) are skipped.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseAll(IEnumerable<string> lines)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (TryParse(line, out var key, out var value))
            {
                map[key] = value;
            }
        }

        return map;
    }
}
