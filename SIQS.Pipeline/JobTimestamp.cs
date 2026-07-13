using System.Globalization;

namespace SIQS.Pipeline;

/// <summary>
/// The single formatter/parser for persisted <c>job.json</c> timestamps. Values are written in
/// invariant round-trip (<c>O</c>) format and parsed back to <see cref="DateTimeOffset"/>; an
/// unparseable persisted value is a clear format error rather than a silent null.
/// </summary>
internal static class JobTimestamp
{
    public static string Now() => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Parses a persisted timestamp; null/empty is "not set", anything else must be valid round-trip format.</summary>
    public static DateTimeOffset? Parse(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            throw new FormatException($"Invalid persisted timestamp '{value}'; expected round-trip (O) format.");
        }

        return parsed;
    }
}
