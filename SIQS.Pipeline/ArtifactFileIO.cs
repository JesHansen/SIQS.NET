using System.Text;

namespace SIQS.Pipeline;

internal static class ArtifactFileIO
{
    private const int MaxReadAttempts = 8;

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
            catch (Exception ex) when (IsRetryableReadFailure(ex) && attempt < MaxReadAttempts)
            {
                Thread.Sleep(delayMs);
                delayMs = Math.Min(delayMs * 2, 250);
            }
        }
    }

    private static bool IsRetryableReadFailure(Exception ex)
        => ex is IOException or UnauthorizedAccessException;
}
