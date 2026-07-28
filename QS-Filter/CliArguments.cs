using System.Globalization;

namespace QS_Filter;

/// <summary>
/// Validated command-line input for filtering, including expanded relation file patterns.
/// </summary>
internal sealed record FilteringCommand(
    string FactorBasePath,
    string OutputDirectory,
    IReadOnlyList<string> RelationPaths,
    IReadOnlyList<string> PartialPaths,
    int? MaxPartialsPerPrime,
    string? SpillDirectory)
{
    public static FilteringCommand Parse(string[] args)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new FormatException($"Unexpected argument '{arg}'. Options use --key value form.");
            }

            var key = arg[2..];
            if (i + 1 >= args.Length)
            {
                throw new FormatException($"Missing value for option '--{key}'.");
            }

            if (!values.TryGetValue(key, out var list))
            {
                values[key] = list = new List<string>();
            }

            list.Add(args[++i]);
        }

        return new FilteringCommand(
            GetOptional(values, "factor-base") ?? "factor_base.txt",
            GetOptional(values, "out-dir") ?? ".",
            ExpandFiles(values, "relations"),
            ExpandFiles(values, "partials"),
            GetOptional(values, "max-partials-per-prime") is { } maxPartials
                ? int.Parse(maxPartials, CultureInfo.InvariantCulture)
                : null,
            GetOptional(values, "filter-spill-dir"));
    }

    private static string? GetOptional(IReadOnlyDictionary<string, List<string>> values, string key)
        => values.TryGetValue(key, out var list) ? list[^1] : null;

    private static IReadOnlyList<string> ExpandFiles(IReadOnlyDictionary<string, List<string>> values, string key)
    {
        if (!values.TryGetValue(key, out var patterns))
        {
            return Array.Empty<string>();
        }

        var files = new List<string>();
        foreach (var pattern in patterns)
        {
            if (pattern.Contains('*') || pattern.Contains('?'))
            {
                var dir = Path.GetDirectoryName(pattern);
                dir = string.IsNullOrEmpty(dir) ? "." : dir;
                var name = Path.GetFileName(pattern);
                files.AddRange(Directory.GetFiles(dir, name).OrderBy(f => f, StringComparer.Ordinal));
            }
            else
            {
                files.Add(pattern);
            }
        }

        return files;
    }
}
