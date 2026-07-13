namespace QS;

/// <summary>
/// Argument reader for the capstone qs tool. Accepts the target number either positionally
/// (first argument) or as <c>--n</c>, plus <c>--key value</c> options and valueless flags.
/// </summary>
internal sealed class CliArguments
{
    private readonly Dictionary<string, string> _values;
    public string? Positional { get; }

    private CliArguments(string? positional, Dictionary<string, string> values)
    {
        Positional = positional;
        _values = values;
    }

    public static CliArguments Parse(string[] args)
    {
        string? positional = null;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var i = 0;
        if (args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal))
        {
            positional = args[0];
            i = 1;
        }

        for (; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new FormatException($"Unexpected argument '{arg}'.");
            }

            var key = arg[2..];
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                values[key] = args[++i];
            }
            else
            {
                values[key] = "true";
            }
        }

        return new CliArguments(positional, values);
    }

    public string? GetOptional(string key) => _values.TryGetValue(key, out var value) ? value : null;
    public bool GetFlag(string key) => _values.ContainsKey(key);
    public bool HasAny(params string[] keys) => keys.Any(key => _values.ContainsKey(key));
    public long? GetLong(string key) => GetOptional(key) is { } v ? long.Parse(v) : null;
    public int? GetInt(string key) => GetOptional(key) is { } v ? int.Parse(v) : null;
    public double? GetDouble(string key) => GetOptional(key) is { } v ? double.Parse(v) : null;
    public bool? GetBool(string key) => GetOptional(key) is { } v ? bool.Parse(v) : null;
}
