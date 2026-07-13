using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIQS.Pipeline;

/// <summary>Reads and writes <c>job.json</c> using lower_snake_case property and enum tokens.</summary>
public static class JobStore
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }

    public const string FileName = "job.json";

    public static void Write(string jobDirectory, JobState state)
        => File.WriteAllText(Path.Combine(jobDirectory, FileName), JsonSerializer.Serialize(state, Options));

    public static JobState Load(string jobDirectory)
    {
        var path = Path.Combine(jobDirectory, FileName);
        return JsonSerializer.Deserialize<JobState>(ArtifactFileIO.ReadAllText(path), Options)
               ?? throw new FormatException("job.json deserialized to null.");
    }

    public static JobStateSnapshot LoadSnapshot(string jobDirectory)
        => JobStateSnapshots.From(Load(jobDirectory));
}
