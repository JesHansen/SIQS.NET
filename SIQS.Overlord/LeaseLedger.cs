namespace SIQS.Overlord;

/// <summary>A snapshot of the ledger's leasing progress for display.</summary>
public sealed record LeaseLedgerSnapshot(int ACount, int Assigned, int Completed, int Outstanding, bool Exhausted);

/// <summary>
/// Hands out disjoint slices of the A-index space [0, ACount) to clients and tracks their fate.
/// Leasing is greedy: each request takes the next unassigned range, or a range reclaimed from an
/// expired lease. Because sieving stops on the relation target (not on lease completion), ranges left
/// unassigned when the pool converges are simply never handed out — exactly as the local sieve stops
/// early. Thread-safe: HTTP requests call it concurrently.
/// </summary>
public sealed class LeaseLedger
{
    private readonly int _aCount;
    private readonly int _chunkSize;
    private readonly object _gate = new();
    private readonly Dictionary<string, Lease> _outstanding = new();
    private readonly Queue<(int Start, int End)> _reclaimed = new();
    private int _cursor;
    private int _completed;
    private long _leaseSeq;

    public LeaseLedger(int aCount, int chunkSize)
    {
        if (aCount < 0) throw new ArgumentOutOfRangeException(nameof(aCount));
        if (chunkSize < 1) throw new ArgumentOutOfRangeException(nameof(chunkSize));
        _aCount = aCount;
        _chunkSize = chunkSize;
    }

    public sealed record Lease(string LeaseId, int Start, int End, DateTimeOffset ExpiresUtc);

    /// <summary>Leases the next available range, or null when no work is currently available.</summary>
    public Lease? TryLease(TimeSpan ttl, DateTimeOffset now)
    {
        lock (_gate)
        {
            SweepExpired(now);

            (int Start, int End) range;
            if (_reclaimed.Count > 0)
            {
                range = _reclaimed.Dequeue();
            }
            else if (_cursor < _aCount)
            {
                range = (_cursor, Math.Min(_cursor + _chunkSize, _aCount));
                _cursor = range.End;
            }
            else
            {
                return null;
            }

            var lease = new Lease($"L{Interlocked.Increment(ref _leaseSeq):D8}", range.Start, range.End, now + ttl);
            _outstanding[lease.LeaseId] = lease;
            return lease;
        }
    }

    /// <summary>Marks a lease's range done. Returns false for an unknown/expired lease (its upload is
    /// still accepted — the ledger just no longer credits it).</summary>
    public bool Complete(string leaseId)
    {
        lock (_gate)
        {
            if (!_outstanding.Remove(leaseId, out var lease))
            {
                return false;
            }

            _completed = Math.Min(_aCount, _completed + (lease.End - lease.Start));
            return true;
        }
    }

    /// <summary>True when every range has been handed out and none remain outstanding or reclaimable —
    /// i.e. the whole A-space was sieved and no further work can be produced.</summary>
    public bool IsExhausted
    {
        get
        {
            lock (_gate)
            {
                SweepExpired(DateTimeOffset.UtcNow);
                return _cursor >= _aCount && _reclaimed.Count == 0 && _outstanding.Count == 0;
            }
        }
    }

    public LeaseLedgerSnapshot Snapshot(DateTimeOffset now)
    {
        lock (_gate)
        {
            SweepExpired(now);
            var assigned = Math.Min(_aCount, _cursor - RangeLength(_reclaimed));
            return new LeaseLedgerSnapshot(_aCount, assigned, _completed, _outstanding.Count,
                _cursor >= _aCount && _reclaimed.Count == 0 && _outstanding.Count == 0);
        }
    }

    private void SweepExpired(DateTimeOffset now)
    {
        if (_outstanding.Count == 0)
        {
            return;
        }

        foreach (var lease in _outstanding.Values.Where(l => l.ExpiresUtc <= now).ToArray())
        {
            _outstanding.Remove(lease.LeaseId);
            _reclaimed.Enqueue((lease.Start, lease.End));
        }
    }

    private static int RangeLength(Queue<(int Start, int End)> ranges)
        => ranges.Sum(r => r.End - r.Start);
}
