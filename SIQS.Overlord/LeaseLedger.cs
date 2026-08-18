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
    private readonly LeaseJournal? _journal;

    public LeaseLedger(int aCount, int chunkSize, string? jobDirectory = null)
    {
        if (aCount < 0) throw new ArgumentOutOfRangeException(nameof(aCount));
        if (chunkSize < 1) throw new ArgumentOutOfRangeException(nameof(chunkSize));
        _aCount = aCount;
        _chunkSize = chunkSize;
        if (jobDirectory is not null)
        {
            _journal = new LeaseJournal(jobDirectory);
            Restore(_journal.Load());
            Persist();
        }
    }

    public sealed record Lease(
        string LeaseId,
        int Start,
        int End,
        DateTimeOffset ExpiresUtc,
        int ActiveUploads = 0,
        bool PendingIngest = false);

    /// <summary>Leases the next available range, or null when no work is currently available.</summary>
    public Lease? TryLease(TimeSpan ttl, DateTimeOffset now, int? chunkSize = null)
    {
        var requestedChunkSize = chunkSize ?? _chunkSize;
        ArgumentOutOfRangeException.ThrowIfLessThan(requestedChunkSize, 1);

        lock (_gate)
        {
            SweepExpired(now);

            (int Start, int End) range;
            if (_reclaimed.Count > 0)
            {
                var reclaimed = _reclaimed.Dequeue();
                range = (reclaimed.Start, (int)Math.Min((long)reclaimed.Start + requestedChunkSize, reclaimed.End));
                if (range.End < reclaimed.End)
                {
                    _reclaimed.Enqueue((range.End, reclaimed.End));
                }
            }
            else if (_cursor < _aCount)
            {
                range = (_cursor, (int)Math.Min((long)_cursor + requestedChunkSize, _aCount));
                _cursor = range.End;
            }
            else
            {
                return null;
            }

            var lease = new Lease($"L{Interlocked.Increment(ref _leaseSeq):D8}", range.Start, range.End, now + ttl);
            _outstanding[lease.LeaseId] = lease;
            Persist();
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
            Persist();
            return true;
        }
    }

    /// <summary>Immediately returns a declined client's range to the unassigned queue.</summary>
    public bool Abandon(string leaseId)
    {
        lock (_gate)
        {
            if (!_outstanding.Remove(leaseId, out var lease))
            {
                return false;
            }

            _reclaimed.Enqueue((lease.Start, lease.End));
            Persist();
            return true;
        }
    }

    /// <summary>
    /// Protects a lease from expiry while an HTTP request is copying a chunk into the durable inbox.
    /// Returns false when the lease was already expired, completed, or unknown.
    /// </summary>
    public bool BeginUpload(string leaseId, TimeSpan ttl, DateTimeOffset now)
    {
        lock (_gate)
        {
            SweepExpired(now);
            if (!_outstanding.TryGetValue(leaseId, out var lease) || lease.PendingIngest)
            {
                return false;
            }

            _outstanding[leaseId] = lease with
            {
                ExpiresUtc = now + ttl,
                ActiveUploads = checked(lease.ActiveUploads + 1),
            };
            Persist();
            return true;
        }
    }

    /// <summary>Ends an active chunk transfer and gives the client a fresh TTL for its next chunk.</summary>
    public void EndUpload(string leaseId, TimeSpan ttl, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!_outstanding.TryGetValue(leaseId, out var lease))
            {
                return;
            }

            _outstanding[leaseId] = lease with
            {
                ExpiresUtc = now + ttl,
                ActiveUploads = Math.Max(0, lease.ActiveUploads - 1),
            };
            Persist();
        }
    }

    /// <summary>
    /// Marks the client upload complete. The range remains protected without a TTL until the inbox
    /// worker has processed all of the lease's durable chunks.
    /// </summary>
    public bool ProtectPendingIngest(string leaseId, DateTimeOffset now)
    {
        lock (_gate)
        {
            SweepExpired(now);
            if (!_outstanding.TryGetValue(leaseId, out var lease))
            {
                return false;
            }

            _outstanding[leaseId] = lease with { PendingIngest = true };
            Persist();
            return true;
        }
    }

    /// <summary>Restores normal TTL expiry when the durable completion marker could not be written.</summary>
    public void CancelPendingIngest(string leaseId, TimeSpan ttl, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_outstanding.TryGetValue(leaseId, out var lease))
            {
                _outstanding[leaseId] = lease with
                {
                    PendingIngest = false,
                    ExpiresUtc = now + ttl,
                };
                Persist();
            }
        }
    }

    /// <summary>
    /// Releases a lease whose durable upload could not be parsed. Its A-range becomes available for
    /// reassignment while valid relations from any successfully processed chunks remain ingested.
    /// </summary>
    public bool FailPendingIngest(string leaseId)
    {
        lock (_gate)
        {
            if (!_outstanding.Remove(leaseId, out var lease))
            {
                return false;
            }

            _reclaimed.Enqueue((lease.Start, lease.End));
            Persist();
            return true;
        }
    }

    /// <summary>
    /// Extends an outstanding lease while its client is actively uploading. Returns false when the
    /// lease has already expired or is unknown; verified relations may still be ingested, but the
    /// range is no longer protected from reassignment.
    /// </summary>
    public bool Renew(string leaseId, TimeSpan ttl, DateTimeOffset now)
    {
        lock (_gate)
        {
            SweepExpired(now);
            if (!_outstanding.TryGetValue(leaseId, out var lease))
            {
                return false;
            }

            _outstanding[leaseId] = lease with { ExpiresUtc = now + ttl };
            Persist();
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

        var changed = false;
        foreach (var lease in _outstanding.Values
                     .Where(lease => lease.ActiveUploads == 0 && !lease.PendingIngest && lease.ExpiresUtc <= now)
                     .ToArray())
        {
            _outstanding.Remove(lease.LeaseId);
            _reclaimed.Enqueue((lease.Start, lease.End));
            changed = true;
        }

        if (changed) Persist();
    }

    private void Restore(LeaseJournalState? state)
    {
        if (state is null) return;
        if (state.ACount != _aCount || state.ChunkSize != _chunkSize)
        {
            throw new InvalidOperationException(
                "The persisted lease journal does not match the recovered A-domain configuration.");
        }

        _cursor = state.Cursor;
        _completed = state.Completed;
        _leaseSeq = state.LeaseSequence;
        foreach (var range in state.Reclaimed)
        {
            _reclaimed.Enqueue((range.Start, range.End));
        }

        // A process restart terminates active HTTP uploads. Reclaim those known ranges; leases with
        // a durable completion marker remain protected until inbox replay completes them.
        foreach (var lease in state.Outstanding)
        {
            if (lease.PendingIngest)
            {
                _outstanding[lease.LeaseId] = lease with { ActiveUploads = 0 };
            }
            else
            {
                _reclaimed.Enqueue((lease.Start, lease.End));
            }
        }
    }

    private void Persist()
        => _journal?.Save(new LeaseJournalState(
            _aCount,
            _chunkSize,
            _cursor,
            _completed,
            _leaseSeq,
            _outstanding.Values.OrderBy(lease => lease.LeaseId, StringComparer.Ordinal).ToArray(),
            _reclaimed.Select(range => new LeaseRange(range.Start, range.End)).ToArray()));

    private static int RangeLength(Queue<(int Start, int End)> ranges)
        => ranges.Sum(r => r.End - r.Start);
}
