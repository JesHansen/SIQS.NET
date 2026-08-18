using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIQS.Pipeline;

internal enum JobStateWriteStage
{
    TemporaryFileCreated,
    TemporaryFileFlushed,
    BeforePrimaryReplace,
    PrimaryReplaced,
}

/// <summary>
/// Persists job state through a flushed same-directory temporary file and an atomic rename. A
/// last-known-good backup is retained, while recovery can promote that backup or a complete orphan.
/// </summary>
internal sealed class AtomicJobStatePersistence
{
    internal const string BackupFileName = JobStore.FileName + ".bak";
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions Options = CreateOptions();
    private readonly Action<JobStateWriteStage>? _onStage;

    public AtomicJobStatePersistence(Action<JobStateWriteStage>? onStage = null)
    {
        _onStage = onStage;
    }

    public void Write(string jobDirectory, JobState state)
    {
        Directory.CreateDirectory(jobDirectory);
        var primaryPath = Path.Combine(jobDirectory, JobStore.FileName);
        var temporaryPath = TemporaryPath(jobDirectory, JobStore.FileName);
        var bytes = Utf8.GetBytes(JsonSerializer.Serialize(state, Options));

        using (var stream = new FileStream(
                   temporaryPath,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.Read,
                   bufferSize: 16 * 1024,
                   FileOptions.WriteThrough))
        {
            _onStage?.Invoke(JobStateWriteStage.TemporaryFileCreated);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        _onStage?.Invoke(JobStateWriteStage.TemporaryFileFlushed);
        PreserveLastKnownGood(jobDirectory, primaryPath);
        _onStage?.Invoke(JobStateWriteStage.BeforePrimaryReplace);
        File.Move(temporaryPath, primaryPath, overwrite: true);
        _onStage?.Invoke(JobStateWriteStage.PrimaryReplaced);
        CleanupTemporaryFiles(jobDirectory);
    }

    public JobState Load(string jobDirectory)
    {
        var primaryPath = Path.Combine(jobDirectory, JobStore.FileName);
        if (TryLoad(primaryPath, out var primary, out var primaryError))
        {
            CleanupTemporaryFiles(jobDirectory);
            return primary!;
        }

        var backupPath = Path.Combine(jobDirectory, BackupFileName);
        if (TryLoad(backupPath, out var backup, out _))
        {
            RestorePrimary(jobDirectory, backup!);
            CleanupTemporaryFiles(jobDirectory);
            return backup!;
        }

        foreach (var temporaryPath in EnumerateTemporaryFiles(jobDirectory)
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            if (!TryLoad(temporaryPath, out var temporary, out _))
            {
                continue;
            }

            RestorePrimary(jobDirectory, temporary!);
            CleanupTemporaryFiles(jobDirectory);
            return temporary!;
        }

        if (primaryError is not null)
        {
            throw new JsonException(
                "No complete job state could be recovered from job.json, its backup, or temporary files.",
                primaryError);
        }

        throw new FileNotFoundException("No recoverable job state exists.", primaryPath);
    }

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

    private static void PreserveLastKnownGood(string jobDirectory, string primaryPath)
    {
        if (!TryLoad(primaryPath, out var current, out _))
        {
            return;
        }

        var backupTemporaryPath = TemporaryPath(jobDirectory, BackupFileName);
        WriteFlushed(backupTemporaryPath, Utf8.GetBytes(JsonSerializer.Serialize(current, Options)));
        File.Move(backupTemporaryPath, Path.Combine(jobDirectory, BackupFileName), overwrite: true);
    }

    private static void RestorePrimary(string jobDirectory, JobState state)
    {
        var temporaryPath = TemporaryPath(jobDirectory, JobStore.FileName);
        WriteFlushed(temporaryPath, Utf8.GetBytes(JsonSerializer.Serialize(state, Options)));
        File.Move(temporaryPath, Path.Combine(jobDirectory, JobStore.FileName), overwrite: true);
    }

    private static void WriteFlushed(string path, ReadOnlySpan<byte> bytes)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static bool TryLoad(string path, out JobState? state, out Exception? error)
    {
        state = null;
        error = null;
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            state = JsonSerializer.Deserialize<JobState>(ArtifactFileIO.ReadAllText(path), Options)
                ?? throw new FormatException($"{Path.GetFileName(path)} deserialized to null.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or JsonException or FormatException)
        {
            error = ex;
            return false;
        }
    }

    private static string TemporaryPath(string directory, string baseName)
        => Path.Combine(directory, $"{baseName}.{Guid.NewGuid():N}.tmp");

    private static IEnumerable<string> EnumerateTemporaryFiles(string directory)
        => Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, $"{JobStore.FileName}.*.tmp", SearchOption.TopDirectoryOnly)
            : Array.Empty<string>();

    private static void CleanupTemporaryFiles(string directory)
    {
        foreach (var path in EnumerateTemporaryFiles(directory))
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A concurrent process may still own an orphan. A later successful operation retries.
            }
        }
    }
}
