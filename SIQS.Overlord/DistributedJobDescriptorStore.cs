using System.Text.Json;
using SIQS.Contracts.Distributed;

namespace SIQS.Overlord;

/// <summary>Persists and verifies the immutable identity clients use for a distributed sieve.</summary>
internal static class DistributedJobDescriptorStore
{
    public const string FileName = "distributed-job.json";

    public static void ValidateOrCreate(string jobDirectory, JobDescriptor descriptor)
    {
        var path = Path.Combine(jobDirectory, FileName);
        if (File.Exists(path))
        {
            var stored = JsonSerializer.Deserialize<JobDescriptor>(File.ReadAllText(path), JsonOptions)
                ?? throw new FormatException($"{FileName} is empty or invalid.");
            if (stored.JobId != descriptor.JobId || stored.ParamHash != descriptor.ParamHash ||
                stored.ACount != descriptor.ACount || stored.N != descriptor.N)
            {
                throw new InvalidOperationException(
                    $"{FileName} does not match the recovered job, factor base, and sieving parameters.");
            }

            return;
        }

        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(descriptor, JsonOptions));
            File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };
}
