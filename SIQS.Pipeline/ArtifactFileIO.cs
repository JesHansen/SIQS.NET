using System.Text;

namespace SIQS.Pipeline;

internal static class ArtifactFileIO
{
    private const int MaxAttempts = 8;

    public static string ReadAllText(string path)
    {
        var delayMs = 10;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            }
            catch (Exception ex) when (IsRetryableFileFailure(ex) && attempt < MaxAttempts)
            {
                Thread.Sleep(delayMs);
                delayMs = Math.Min(delayMs * 2, 250);
            }
        }
    }

    internal static void MoveWithRetry(
        string sourcePath,
        string destinationPath,
        bool overwrite,
        Action<string, string, bool> moveFile,
        Action<int> delay)
    {
        var delayMs = 10;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                moveFile(sourcePath, destinationPath, overwrite);
                return;
            }
            catch (Exception ex) when (IsRetryableFileFailure(ex) && attempt < MaxAttempts)
            {
                delay(delayMs);
                delayMs = Math.Min(delayMs * 2, 250);
            }
        }
    }

    private static bool IsRetryableFileFailure(Exception ex)
        => ex is IOException or UnauthorizedAccessException;
}
