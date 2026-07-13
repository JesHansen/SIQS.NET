namespace QsSqrt;

/// <summary>
/// Validated command-line input for the square-root CLI.
/// </summary>
internal sealed record SquareRootCommand(
    string FactorBasePath,
    string RelationsPath,
    string DependenciesPath,
    string OutputPath,
    bool ContinueAfterFactor)
{
    public static SquareRootCommand Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new FormatException($"Unexpected argument '{arg}'. Options use --key value form.");
            }

            var key = arg[2..];
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                values[key] = args[++i];
            }
            else
            {
                values[key] = "true"; // valueless flag
            }
        }

        return new SquareRootCommand(
            GetOptional(values, "factor-base") ?? "factor_base.txt",
            GetOptional(values, "relations") ?? "relations_filtered.txt",
            GetOptional(values, "dependencies") ?? "dependencies.txt",
            GetOptional(values, "out") ?? "factors.txt",
            values.ContainsKey("continue-after-factor"));
    }

    private static string? GetOptional(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) ? value : null;
}
