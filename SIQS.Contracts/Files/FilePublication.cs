namespace SIQS.Contracts.Files;

/// <summary>
/// Bounded retry for publishing a file by atomic rename. On Windows, replacing a file fails with
/// <see cref="UnauthorizedAccessException"/> (HRESULT 0x80070005) while any other handle has the
/// destination open, whatever its sharing mode; an antivirus or indexer scan of a freshly written
/// file is enough. Such opens are brief, so the move is retried with capped exponential backoff
/// (about 0.8 s in total) and a failure that outlasts the budget is rethrown.
/// </summary>
public static class FilePublication
{
    public const int MaxAttempts = 8;

    public static void Move(
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
            catch (Exception ex) when (IsRetryable(ex) && attempt < MaxAttempts)
            {
                delay(delayMs);
                delayMs = Math.Min(delayMs * 2, 250);
            }
        }
    }

    public static bool IsRetryable(Exception ex) => ex is IOException or UnauthorizedAccessException;
}
