namespace SIQS.Overlord;

/// <summary>Flushes small inbox records and atomically publishes them under the total retention quota.</summary>
internal sealed class DurableFilePublisher(RelationInboxQuota quota)
{
    public async Task WriteAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            return;
        }

        if (!quota.TryReserveRetained(content.Length))
        {
            throw new InboxLimitException(
                $"The total relation inbox quota of {quota.MaxInboxBytes} bytes is full.");
        }

        var temporaryPath = path + $".{Guid.NewGuid():N}.part";
        try
        {
            var stream = new FileStream(
                temporaryPath,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    BufferSize = 4096,
                    Options = FileOptions.Asynchronous,
                });
            await using (stream.ConfigureAwait(false))
            {
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(temporaryPath, path);
            }
            catch (IOException) when (File.Exists(path))
            {
                File.Delete(temporaryPath);
                quota.ReleaseRetained(content.Length);
            }
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            quota.ReleaseRetained(content.Length);
            throw;
        }
    }
}

internal sealed class InboxLimitException(string message) : Exception(message);
