using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIQS.Contracts;

/// <summary>
/// Serializes <see cref="SiqsProgressEvent"/> to and from single-line JSON suitable for the
/// newline-delimited <c>events.log</c>. Property names and enum values use lower_snake_case.
/// </summary>
public static class SiqsProgressEventJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }

    /// <summary>Serializes an event to a single JSON line (no embedded newlines).</summary>
    public static string Serialize(SiqsProgressEvent evt) => JsonSerializer.Serialize(evt, Options);

    /// <summary>Deserializes a single JSON line into an event.</summary>
    public static SiqsProgressEvent Deserialize(string json)
        => JsonSerializer.Deserialize<SiqsProgressEvent>(json, Options)
           ?? throw new FormatException("Progress event JSON deserialized to null.");
}
