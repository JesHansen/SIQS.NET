using SIQS.Contracts.Cli;

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
    string? SpillDirectory,
    int? MaxCycleLength,
    bool EnableTwoMerge,
    int? TwoMergeSlack)
{
    public static FilteringCommand Parse(string[] args)
    {
        var cli = CommandLine.Parse(args, CommandLineSyntax.Strict);
        return new FilteringCommand(
            cli.GetOptional("factor-base") ?? "factor_base.txt",
            cli.GetOptional("out-dir") ?? ".",
            ExpandFiles(cli.GetAll("relations")),
            ExpandFiles(cli.GetAll("partials")),
            cli.GetInt("max-partials-per-prime"),
            cli.GetOptional("filter-spill-dir"),
            cli.GetInt("max-cycle-length"),
            cli.GetBool("enable-two-merge") ?? true,
            cli.GetInt("two-merge-slack"));
    }

    private static IReadOnlyList<string> ExpandFiles(IReadOnlyList<string> patterns)
    {
        if (patterns.Count == 0)
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
