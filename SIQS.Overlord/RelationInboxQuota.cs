namespace SIQS.Overlord;

/// <summary>Race-safe reservations for raw payload backlog and all retained inbox files.</summary>
internal sealed class RelationInboxQuota
{
    private readonly long _maxBacklogBytes;
    private readonly long _maxInboxBytes;
    private long _rawBacklogBytes;
    private long _totalBytes;

    public RelationInboxQuota(
        long maxBacklogBytes,
        long maxInboxBytes,
        long initialRawBacklogBytes,
        long initialTotalBytes)
    {
        _maxBacklogBytes = maxBacklogBytes;
        _maxInboxBytes = maxInboxBytes;
        _rawBacklogBytes = initialRawBacklogBytes;
        _totalBytes = initialTotalBytes;
        if (initialRawBacklogBytes > maxBacklogBytes || initialTotalBytes > maxInboxBytes)
        {
            throw new InvalidOperationException("Recovered relation inbox exceeds its configured quota.");
        }
    }

    public long RawBacklogBytes => Interlocked.Read(ref _rawBacklogBytes);
    public long TotalBytes => Interlocked.Read(ref _totalBytes);
    public long MaxBacklogBytes => _maxBacklogBytes;
    public long MaxInboxBytes => _maxInboxBytes;

    public bool CanAcceptPayload(long maximumChunkBytes)
        => RawBacklogBytes <= _maxBacklogBytes - maximumChunkBytes &&
           TotalBytes <= _maxInboxBytes - maximumChunkBytes;

    public bool TryReservePayload(long bytes, out string? reason)
    {
        if (!TryReserve(ref _rawBacklogBytes, bytes, _maxBacklogBytes))
        {
            reason = $"The raw relation backlog quota of {_maxBacklogBytes} bytes is full.";
            return false;
        }

        if (!TryReserve(ref _totalBytes, bytes, _maxInboxBytes))
        {
            Interlocked.Add(ref _rawBacklogBytes, -bytes);
            reason = $"The total relation inbox quota of {_maxInboxBytes} bytes is full.";
            return false;
        }

        reason = null;
        return true;
    }

    public bool TryReserveRetained(long bytes)
        => TryReserve(ref _totalBytes, bytes, _maxInboxBytes);

    public void ReleasePayload(long bytes)
    {
        Interlocked.Add(ref _rawBacklogBytes, -bytes);
        Interlocked.Add(ref _totalBytes, -bytes);
    }

    public void ReleaseRetained(long bytes) => Interlocked.Add(ref _totalBytes, -bytes);

    public void Reset()
    {
        Interlocked.Exchange(ref _rawBacklogBytes, 0);
        Interlocked.Exchange(ref _totalBytes, 0);
    }

    private static bool TryReserve(ref long counter, long amount, long maximum)
    {
        while (true)
        {
            var current = Interlocked.Read(ref counter);
            if (amount > maximum - current)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref counter, current + amount, current) == current)
            {
                return true;
            }
        }
    }
}
