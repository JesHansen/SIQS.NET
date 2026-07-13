namespace Sieving;

/// <summary>Thread-safe accumulation of per-worker telemetry into solution-wide totals.</summary>
internal sealed class WorkerTotals
{
    private readonly object _gate = new();
    public RelationCounts Relations;
    public TwoLargePrimeStats TwoLargePrime;
    public CofactorStats Cofactor;
    public PhaseTicks Ticks;
    public long BucketOverflowHits;
    public long BucketSlabBytesPerWorker; // max, not sum

    public void Add(PolynomialSieveWorker worker)
    {
        lock (_gate)
        {
            Relations.Fulls += worker.Metrics.Relations.Fulls;
            Relations.Partials += worker.Metrics.Relations.Partials;
            Relations.OneLargePrimePartials += worker.Metrics.Relations.OneLargePrimePartials;
            Relations.TwoLargePrimePartials += worker.Metrics.Relations.TwoLargePrimePartials;
            Relations.PolyCount += worker.Metrics.Relations.PolyCount;
            Relations.Candidates += worker.Metrics.Relations.Candidates;
            Relations.Blocks += worker.Metrics.Relations.Blocks;
            Relations.Discarded += worker.Metrics.Relations.Discarded;

            TwoLargePrime.SplitAttempts += worker.Metrics.TwoLargePrime.SplitAttempts;
            TwoLargePrime.SplitSuccesses += worker.Metrics.TwoLargePrime.SplitSuccesses;
            TwoLargePrime.ResidualTooSmall += worker.Metrics.TwoLargePrime.ResidualTooSmall;
            TwoLargePrime.ResidualTooLarge += worker.Metrics.TwoLargePrime.ResidualTooLarge;
            TwoLargePrime.ResidualPrime += worker.Metrics.TwoLargePrime.ResidualPrime;
            TwoLargePrime.ResidualSmallFactor += worker.Metrics.TwoLargePrime.ResidualSmallFactor;
            TwoLargePrime.ResidualBitsLe32 += worker.Metrics.TwoLargePrime.ResidualBitsLe32;
            TwoLargePrime.ResidualBitsLe48 += worker.Metrics.TwoLargePrime.ResidualBitsLe48;
            TwoLargePrime.ResidualBitsLe64 += worker.Metrics.TwoLargePrime.ResidualBitsLe64;
            TwoLargePrime.ResidualBitsGt64 += worker.Metrics.TwoLargePrime.ResidualBitsGt64;

            Cofactor.SqufofAttempts += worker.Metrics.Cofactor.SqufofAttempts;
            Cofactor.SqufofSuccesses += worker.Metrics.Cofactor.SqufofSuccesses;
            Cofactor.RhoAttempts += worker.Metrics.Cofactor.RhoAttempts;
            Cofactor.RhoSuccesses += worker.Metrics.Cofactor.RhoSuccesses;

            Ticks.Setup += worker.Metrics.Ticks.Setup;
            Ticks.SieveFill += worker.Metrics.Ticks.SieveFill;
            Ticks.SieveInit += worker.Metrics.Ticks.SieveInit;
            Ticks.Scan += worker.Metrics.Ticks.Scan;
            Ticks.PolyEval += worker.Metrics.Ticks.PolyEval;
            Ticks.TrialDiv += worker.Metrics.Ticks.TrialDiv;
            Ticks.TrialDivPre += worker.Metrics.Ticks.TrialDivPre;
            Ticks.TrialDivPost += worker.Metrics.Ticks.TrialDivPost;
            Ticks.TrialDivPostAPos += worker.Metrics.Ticks.TrialDivPostAPos;
            Ticks.TrialDivPostCheck += worker.Metrics.Ticks.TrialDivPostCheck;
            Ticks.TrialDivPostParity += worker.Metrics.Ticks.TrialDivPostParity;

            BucketOverflowHits += worker.Metrics.Buckets.OverflowHits;
            BucketSlabBytesPerWorker = Math.Max(BucketSlabBytesPerWorker, worker.Metrics.Buckets.SlabBytesPerWorker);
        }
    }
}
