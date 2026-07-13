using System.Globalization;

namespace SIQS.PerformanceSpy;

internal sealed record PerformanceSpyOptions(
    int StartDigits,
    int EndDigits,
    double TargetSecondsPerSize,
    int MaxCompositesPerSize,
    double RegressionTolerance,
    string OutputPath,
    string ScratchDirectory)
{
    public static PerformanceSpyOptions Parse(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new FormatException($"Unexpected argument '{arg}'.");
            }

            var key = arg[2..];
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new FormatException($"Option '{arg}' requires a value.");
            }

            options[key] = args[++i];
        }

        var start = GetInt(options, "start-digits", 20);
        var end = GetInt(options, "end-digits", 75);
        if (start < 1)
        {
            throw new FormatException("--start-digits must be at least 1.");
        }

        if (end < start)
        {
            throw new FormatException("--end-digits must be greater than or equal to --start-digits.");
        }

        var targetSeconds = GetDouble(options, "target-seconds-per-size", 1.0);
        if (targetSeconds <= 0)
        {
            throw new FormatException("--target-seconds-per-size must be greater than 0.");
        }

        var maxComposites = GetInt(options, "max-composites-per-size", 256);
        if (maxComposites < 1)
        {
            throw new FormatException("--max-composites-per-size must be at least 1.");
        }

        var tolerance = GetDouble(options, "regression-tolerance", 1.10);
        if (tolerance < 1.0)
        {
            throw new FormatException("--regression-tolerance must be at least 1.0.");
        }

        return new PerformanceSpyOptions(
            start,
            end,
            targetSeconds,
            maxComposites,
            tolerance,
            GetString(options, "output", Path.Combine("SIQS.Pipeline.Tests", "GeneratedPerformanceSpyTests.cs")),
            GetString(options, "scratch", Path.Combine(".tmp", "performance-spy")));
    }

    private static string GetString(IReadOnlyDictionary<string, string> options, string key, string defaultValue) =>
        options.TryGetValue(key, out var value) ? value : defaultValue;

    private static int GetInt(IReadOnlyDictionary<string, string> options, string key, int defaultValue) =>
        options.TryGetValue(key, out var value)
            ? int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)
            : defaultValue;

    private static double GetDouble(IReadOnlyDictionary<string, string> options, string key, double defaultValue) =>
        options.TryGetValue(key, out var value)
            ? double.Parse(value, CultureInfo.InvariantCulture)
            : defaultValue;
}
