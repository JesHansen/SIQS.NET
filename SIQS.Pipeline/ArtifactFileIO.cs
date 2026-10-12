using System.Text;
using SIQS.Contracts.Files;

namespace SIQS.Pipeline;

internal static class ArtifactFileIO
{
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
            catch (Exception ex) when (FilePublication.IsRetryable(ex) && attempt < FilePublication.MaxAttempts)
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
        => FilePublication.Move(sourcePath, destinationPath, overwrite, moveFile, delay);
}
