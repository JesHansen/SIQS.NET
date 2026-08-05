using System.Globalization;
using System.Numerics;

namespace SIQS.Contracts.Cli;

/// <summary>
/// Parsing discipline for <see cref="CommandLine"/>, capturing the two shapes the qs tools use.
/// </summary>
/// <param name="AllowPositional">
/// When <c>true</c>, the first bare (non <c>--</c>) token is captured as <see cref="CommandLine.Positional"/>;
/// otherwise a bare token is an error.
/// </param>
/// <param name="RequireOptionValues">
/// When <c>true</c> (strict <c>--key value</c> tools), every option consumes the following token as its
/// value and a trailing option with no following token is an error. When <c>false</c> (flag-aware tools),
/// an option is treated as a valueless flag whenever it is listed in <see cref="ValuelessFlags"/> or the
/// next token is another option or the end of input.
/// </param>
/// <param name="ValuelessFlags">
/// Options that never take a value; only consulted when <see cref="RequireOptionValues"/> is <c>false</c>.
/// </param>
public sealed record CommandLineSyntax(
    bool AllowPositional = false,
    bool RequireOptionValues = true,
    IReadOnlySet<string>? ValuelessFlags = null)
{
    /// <summary>Strict <c>--key value</c> with no positional and no valueless flags.</summary>
    public static readonly CommandLineSyntax Strict = new();

    /// <summary>Flag-aware parsing: unknown trailing options become valueless flags.</summary>
    public static CommandLineSyntax FlagAware(bool allowPositional = false, params string[] valuelessFlags)
        => new(allowPositional, RequireOptionValues: false,
            new HashSet<string>(valuelessFlags, StringComparer.OrdinalIgnoreCase));
}

/// <summary>
/// One parser shared by every qs command-line tool. Values are keyed case-insensitively and every key
/// keeps all supplied values (last wins for the single-value accessors); typed accessors parse with the
/// invariant culture so behaviour does not depend on the host locale.
/// </summary>
public sealed class CommandLine
{
    private readonly Dictionary<string, List<string>> _values;

    private CommandLine(Dictionary<string, List<string>> values, string? positional)
    {
        _values = values;
        Positional = positional;
    }

    /// <summary>The single bare argument, when the syntax allows one.</summary>
    public string? Positional { get; }

    public static CommandLine Parse(string[] args, CommandLineSyntax syntax)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(syntax);
        var valuelessFlags = syntax.ValuelessFlags ?? EmptySet;
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? positional = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (syntax.AllowPositional && positional is null)
                {
                    positional = arg;
                    continue;
                }

                throw new FormatException($"Unexpected argument '{arg}'. Options use --key value form.");
            }

            var key = arg[2..];
            if (syntax.RequireOptionValues)
            {
                if (i + 1 >= args.Length)
                {
                    throw new FormatException($"Missing value for option '--{key}'.");
                }

                Add(values, key, args[++i]);
                continue;
            }

            if (!valuelessFlags.Contains(key)
                && i + 1 < args.Length
                && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                Add(values, key, args[++i]);
            }
            else
            {
                Add(values, key, "true");
            }
        }

        return new CommandLine(values, positional);
    }

    /// <summary>The last value supplied for <paramref name="key"/>, or <c>null</c> when absent.</summary>
    public string? GetOptional(string key) => _values.TryGetValue(key, out var list) ? list[^1] : null;

    /// <summary>The last value supplied for <paramref name="key"/>, or throws when absent.</summary>
    public string GetRequired(string key)
        => GetOptional(key) ?? throw new FormatException($"Required option '--{key}' was not supplied.");

    /// <summary>Every value supplied for <paramref name="key"/>, in argument order.</summary>
    public IReadOnlyList<string> GetAll(string key)
        => _values.TryGetValue(key, out var list) ? list : Array.Empty<string>();

    /// <summary>Whether <paramref name="key"/> was supplied at all (its presence is the flag).</summary>
    public bool GetFlag(string key) => _values.ContainsKey(key);

    /// <summary>Whether any of <paramref name="keys"/> was supplied.</summary>
    public bool HasAny(params string[] keys) => keys.Any(_values.ContainsKey);

    public int? GetInt(string key)
        => GetOptional(key) is { } v ? int.Parse(v, CultureInfo.InvariantCulture) : null;

    public long? GetLong(string key)
        => GetOptional(key) is { } v ? long.Parse(v, CultureInfo.InvariantCulture) : null;

    public ulong? GetULong(string key)
        => GetOptional(key) is { } v ? ulong.Parse(v, CultureInfo.InvariantCulture) : null;

    public double? GetDouble(string key)
        => GetOptional(key) is { } v ? double.Parse(v, CultureInfo.InvariantCulture) : null;

    public bool? GetBool(string key)
        => GetOptional(key) is { } v ? bool.Parse(v) : null;

    public BigInteger? GetBigInteger(string key)
        => GetOptional(key) is { } v ? BigInteger.Parse(v, CultureInfo.InvariantCulture) : null;

    private static void Add(Dictionary<string, List<string>> values, string key, string value)
    {
        if (!values.TryGetValue(key, out var list))
        {
            values[key] = list = new List<string>();
        }

        list.Add(value);
    }

    private static readonly IReadOnlySet<string> EmptySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
