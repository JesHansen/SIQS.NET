using SIQS.Contracts;

namespace Sieving;

/// <summary>Deduplicates raw relations by id/content fingerprint and tallies usable/zero-parity outcomes.</summary>
internal sealed class RelationTally
{
    private readonly Dictionary<(ulong, ulong), (ulong, ulong)> _seenRelations = new();
    private readonly object _seenRelationsGate = new();
    private readonly LargePrimeCycleTracker _largePrimeCycles = new();
    private readonly object _largePrimeGate = new();

    // Shared counters for the stop condition. Empty-parity full relations and combined
    // partial pairs are valid dependencies, but they do not add useful matrix surplus, so
    // they are not allowed to stop sieving early.
    private long _sharedPolyCount;
    private long _sharedFullCount;
    private long _sharedUsefulFullCount;
    private long _sharedPartialCount;
    private long _sharedUsablePairs;
    private long _sharedZeroParityFullCount;
    private long _sharedZeroParityPairs;

    // Relations replayed from resumeState.ExistingRelations are historical: no worker in this
    // run produces them, so they cannot flow through WorkerTotals. Tracked separately here.
    private long _resumedPolys;
    private long _resumedFulls;
    private long _resumedPartials;
    private long _resumedOneLargePrimePartials;
    private long _resumedTwoLargePrimePartials;

    public long PolyCount => Volatile.Read(ref _sharedPolyCount);

    public long FullCount => Volatile.Read(ref _sharedFullCount);

    public long UsefulFullCount => Volatile.Read(ref _sharedUsefulFullCount);

    public long PartialCount => Volatile.Read(ref _sharedPartialCount);

    public long UsablePairs => Volatile.Read(ref _sharedUsablePairs);

    public long ZeroParityFullCount => Volatile.Read(ref _sharedZeroParityFullCount);

    public long ZeroParityPairs => Volatile.Read(ref _sharedZeroParityPairs);

    public long ResumedPolys => _resumedPolys;

    public long ResumedFulls => _resumedFulls;

    public long ResumedPartials => _resumedPartials;

    public long ResumedOneLargePrimePartials => _resumedOneLargePrimePartials;

    public long ResumedTwoLargePrimePartials => _resumedTwoLargePrimePartials;

    public bool TryRemember(RawRelationRecord record, out bool duplicate)
    {
        var idFingerprint = Fingerprint(record.RelationId);
        var contentFingerprint = Fingerprint(RawRelationCanonicalForm.Content(record));
        lock (_seenRelationsGate)
        {
            if (!_seenRelations.TryGetValue(idFingerprint, out var existing))
            {
                _seenRelations[idFingerprint] = contentFingerprint;
                duplicate = false;
                return true;
            }

            if (existing == contentFingerprint)
            {
                duplicate = true;
                return false;
            }

            throw new FormatException($"Raw relation id '{record.RelationId}' appears with different content.");
        }
    }

    public void Count(RawRelationRecord record, bool includeInTotals)
    {
        if (record.Kind != RelationKind.Partial)
        {
            Interlocked.Increment(ref _sharedFullCount);
            if (includeInTotals)
            {
                _resumedFulls++;
            }

            if (record.ParityColumns.Count == 0)
            {
                Interlocked.Increment(ref _sharedZeroParityFullCount);
            }
            else
            {
                Interlocked.Increment(ref _sharedUsefulFullCount);
            }

            return;
        }

        Interlocked.Increment(ref _sharedPartialCount);
        if (includeInTotals)
        {
            _resumedPartials++;
            if (record.LargePrimes.Count == 1)
            {
                _resumedOneLargePrimePartials++;
            }
            else if (record.LargePrimes.Count == 2)
            {
                _resumedTwoLargePrimePartials++;
            }
        }

        if (record.LargePrimes.Count is 1 or 2)
        {
            LargePrimeCycleAddResult cycle;
            lock (_largePrimeGate)
            {
                cycle = _largePrimeCycles.Add(record.LargePrimes, record.ParityColumns);
            }

            if (cycle.ClosedCycle)
            {
                if (cycle.CombinedParityIsZero)
                {
                    Interlocked.Increment(ref _sharedZeroParityPairs);
                }
                else
                {
                    Interlocked.Increment(ref _sharedUsablePairs);
                }
            }
        }
    }

    /// <summary>Adds a completed A-index's polynomial count from a resumed checkpoint (single-threaded, pre-loop).</summary>
    public void AddResumedPolynomials(long familySize)
    {
        _sharedPolyCount += familySize;
        _resumedPolys += familySize;
    }

    /// <summary>Atomically increments the shared polynomial sequence counter and returns the new value.</summary>
    public long IncrementPolyCount() => Interlocked.Increment(ref _sharedPolyCount);

    private static (ulong, ulong) Fingerprint(string key)
    {
        var h1 = 0xcbf29ce484222325UL;
        var h2 = 0x9E3779B97F4A7C15UL;
        foreach (var ch in key)
        {
            h1 = (h1 ^ ch) * 0x100000001B3UL;
            h2 = (h2 ^ ch) * 0xD1B54A32D192ED03UL;
        }

        return (h1, h2);
    }
}
