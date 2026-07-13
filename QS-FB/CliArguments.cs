using System.Globalization;
using System.Numerics;

namespace QS_FB;

/// <summary>Validated command-line input for the factor-base CLI.</summary>
internal sealed record FactorBaseCommand(BigInteger TargetN, long? Bound, BigInteger? Multiplier, string OutputPath)
{
    public static FactorBaseCommand Parse(string[] args)
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
            if (i + 1 >= args.Length)
            {
                throw new FormatException($"Missing value for option '--{key}'.");
            }

            values[key] = args[++i];
        }

        return new FactorBaseCommand(
            BigInteger.Parse(GetRequired(values, "n"), CultureInfo.InvariantCulture),
            GetOptional(values, "bound") is { } bound ? long.Parse(bound, CultureInfo.InvariantCulture) : null,
            GetOptional(values, "multiplier") is { } multiplier ? BigInteger.Parse(multiplier, CultureInfo.InvariantCulture) : null,
            GetOptional(values, "out") ?? "factor_base.txt");
    }

    private static string GetRequired(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value)
            ? value
            : throw new FormatException($"Required option '--{key}' was not supplied.");

    private static string? GetOptional(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) ? value : null;
}
