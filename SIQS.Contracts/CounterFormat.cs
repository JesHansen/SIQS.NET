using System.Globalization;

namespace SIQS.Contracts;

/// <summary>
/// The single (de)serializer for progress- and phase-counter values, which are carried as strings
/// in the <c>counters</c> maps and persisted to <c>job.json</c>. Every value is formatted and
/// parsed with the invariant culture so the on-disk representation is stable regardless of the
/// host locale, and reads fall back to a caller-supplied default rather than throwing on a missing
/// or malformed value.
/// </summary>
public static class CounterFormat
{
    /// <summary>Formats an integer counter value.</summary>
    public static string Count(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Formats a wall-clock duration in seconds to three decimal places.</summary>
    public static string Seconds(double seconds) => seconds.ToString("F3", CultureInfo.InvariantCulture);

    /// <summary>Formats a boolean counter value as the lowercase tokens the readers expect.</summary>
    public static string Bool(bool value) => value ? "true" : "false";

    /// <summary>Reads an integer counter, returning <paramref name="fallback"/> when absent or malformed.</summary>
    public static long ReadLong(IReadOnlyDictionary<string, string> counters, string key, long fallback = 0)
        => counters.TryGetValue(key, out var value)
            && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    /// <summary>Reads an integer counter as <see cref="int"/>, returning <paramref name="fallback"/> when absent or malformed.</summary>
    public static int ReadInt(IReadOnlyDictionary<string, string> counters, string key, int fallback = 0)
        => counters.TryGetValue(key, out var value)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    /// <summary>Reads a seconds counter, returning <paramref name="fallback"/> when absent or malformed.</summary>
    public static double ReadSeconds(IReadOnlyDictionary<string, string> counters, string key, double fallback = 0.0)
        => counters.TryGetValue(key, out var value)
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    /// <summary>Reads a boolean counter, returning <paramref name="fallback"/> when absent.</summary>
    public static bool ReadBool(IReadOnlyDictionary<string, string> counters, string key, bool fallback = false)
        => counters.TryGetValue(key, out var value) ? value == "true" : fallback;
}
