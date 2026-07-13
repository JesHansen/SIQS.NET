using System.Text.Json;

namespace SIQS.Contracts;

/// <summary>
/// Converts enum values to and from the canonical lower_snake_case string tokens used in both
/// JSON progress events and the text artifact files (e.g. <c>SiqsPhase.LinearAlgebra</c>
/// &lt;-&gt; <c>"linear_algebra"</c>). A single token convention keeps file formats and the
/// event log consistent.
/// </summary>
public static class SiqsTokens
{
    /// <summary>Returns the canonical token for an enum value.</summary>
    public static string ToToken<TEnum>(TEnum value) where TEnum : struct, Enum
        => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    /// <summary>Parses a token back to its enum value (case-insensitive).</summary>
    /// <exception cref="FormatException">The token does not match any value of <typeparamref name="TEnum"/>.</exception>
    public static TEnum Parse<TEnum>(string token) where TEnum : struct, Enum
    {
        if (TryParse<TEnum>(token, out var value))
        {
            return value;
        }

        throw new FormatException($"'{token}' is not a valid {typeof(TEnum).Name} token.");
    }

    /// <summary>Attempts to parse a token back to its enum value (case-insensitive).</summary>
    public static bool TryParse<TEnum>(string token, out TEnum value) where TEnum : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(ToToken(candidate), token, StringComparison.OrdinalIgnoreCase))
            {
                value = candidate;
                return true;
            }
        }

        value = default;
        return false;
    }
}
