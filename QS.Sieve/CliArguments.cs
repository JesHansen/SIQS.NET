namespace QsSieve;

/// <summary>Minimal <c>--key value</c> command-line argument reader for the qs-sieve tool.</summary>
internal sealed class CliArguments
{
    private readonly Dictionary<string, string> _values;

    private CliArguments(Dictionary<string, string> values) => _values = values;

    public static CliArguments Parse(string[] args)
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

        return new CliArguments(values);
    }

    public string GetRequired(string key)
        => _values.TryGetValue(key, out var value)
            ? value
            : throw new FormatException($"Required option '--{key}' was not supplied.");

    public string? GetOptional(string key) => _values.TryGetValue(key, out var value) ? value : null;

    public long? GetOptionalLong(string key)
        => GetOptional(key) is { } v ? long.Parse(v) : null;

    public int? GetOptionalInt(string key)
        => GetOptional(key) is { } v ? int.Parse(v) : null;

    public double? GetOptionalDouble(string key)
        => GetOptional(key) is { } v ? double.Parse(v) : null;

    public bool? GetOptionalBool(string key)
        => GetOptional(key) is { } v ? bool.Parse(v) : null;
}
