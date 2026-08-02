using Sieving;
using SIQS.Contracts;

namespace SIQS.Overlord;

/// <summary>
/// Verifies uploaded relations, deduplicates them, appends the survivors to the job's raw batch files,
/// and tracks the usable-relation count using the sieve engine's own <see cref="RelationTally"/> — so
/// distributed convergence uses the identical "useful fulls + usable partial pairs" condition the
/// local sieve stops on. Thread-safe: concurrent client uploads are serialized here.
/// </summary>
internal sealed class RelationIngest
{
    private readonly RelationVerifier _verifier;
    private readonly RelationTally _tally = new();
    private readonly RawRelationBatchFileSink _sink;
    private readonly object _gate = new();

    /// <param name="existing">
    /// Relations already on disk from a previous sieving pass (e.g. a matrix top-up re-enters the
    /// Sieving phase with batches kept). They are folded into the tally for the usable count but not
    /// re-written, since the sink continues numbering after them.
    /// </param>
    public RelationIngest(RelationVerifier verifier, RawRelationBatchFileSink sink, IEnumerable<RawRelationRecord>? existing = null)
    {
        _verifier = verifier;
        _sink = sink;
        if (existing is not null)
        {
            foreach (var record in existing)
            {
                if (Remember(record))
                {
                    _tally.Count(record, includeInTotals: true);
                }
            }
        }
    }

    /// <summary>Useful full relations plus usable partial-prime pairs — the sieve's stop metric.</summary>
    public long UsableCount
    {
        get
        {
            lock (_gate)
            {
                return _tally.UsefulFullCount + _tally.UsablePairs;
            }
        }
    }

    /// <summary>
    /// Ingests one transport batch. Before returning, accepted relations cross the canonical sink's
    /// physical durability boundary so the inbox can safely replace its raw write-ahead payload with
    /// a compact receipt.
    /// </summary>
    public (int Accepted, int Rejected) Ingest(IEnumerable<RawRelationRecord> relations)
    {
        var accepted = 0;
        var rejected = 0;
        lock (_gate)
        {
            foreach (var record in relations)
            {
                if (!_verifier.IsValid(record) || !Remember(record))
                {
                    rejected++;
                    continue;
                }

                _tally.Count(record, includeInTotals: false);
                _sink.Add(record);
                accepted++;
            }

            // Once this method returns, the inbox may compact its raw write-ahead chunk. Make every
            // accepted record crash-durable first; replay remains safe if a crash occurs before the
            // inbox publishes its compact receipt because existing canonical records are deduplicated.
            _sink.FlushDurable();
        }

        return (accepted, rejected);
    }

    /// <summary>Records the relation's fingerprint, returning true when it is new. A duplicate (or a
    /// forged id colliding with different content) returns false and is dropped.</summary>
    private bool Remember(RawRelationRecord record)
    {
        try
        {
            return _tally.TryRemember(record, out _);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
