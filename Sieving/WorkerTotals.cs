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
    public long BucketMaximumHitsPerBucket;
    public long BucketCapacityPerBucket;
    public long BucketBinaryCandidateBlocks;
    public long BucketCandidateMajorBlocks;
    public long BucketOffsetMapBlocks;
    public long BucketCandidateHitInspections;
    public long BucketCandidateVectorGroups;
    public long BucketCandidateMatchingMasks;
    public long BucketOffsetMapProbes;
    public long BucketDecodedPrimeHits;
    public List<ulong>? CompositeResiduals;

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
            Relations.SmallPrimeVariationReports += worker.Metrics.Relations.SmallPrimeVariationReports;
            Relations.SmallPrimeVariationRejected += worker.Metrics.Relations.SmallPrimeVariationRejected;
            Relations.PreliminaryReports += worker.Metrics.Relations.PreliminaryReports;
            Relations.ExactThresholdRejects += worker.Metrics.Relations.ExactThresholdRejects;
            Relations.DirectGatedCandidateBlocks += worker.Metrics.Relations.DirectGatedCandidateBlocks;
            Relations.ResievedCandidateBlocks += worker.Metrics.Relations.ResievedCandidateBlocks;
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
            Cofactor.MicroEcmAttempts += worker.Metrics.Cofactor.MicroEcmAttempts;
            Cofactor.MicroEcmSuccesses += worker.Metrics.Cofactor.MicroEcmSuccesses;
            Cofactor.RhoAttempts += worker.Metrics.Cofactor.RhoAttempts;
            Cofactor.RhoSuccesses += worker.Metrics.Cofactor.RhoSuccesses;

            Ticks.Setup += worker.Metrics.Ticks.Setup;
            Ticks.SieveFill += worker.Metrics.Ticks.SieveFill;
            Ticks.SieveInit += worker.Metrics.Ticks.SieveInit;
            Ticks.SieveClear += worker.Metrics.Ticks.SieveClear;
            Ticks.SmallPrimeFill += worker.Metrics.Ticks.SmallPrimeFill;
            Ticks.DirectFill += worker.Metrics.Ticks.DirectFill;
            Ticks.BucketScatter += worker.Metrics.Ticks.BucketScatter;
            Ticks.BucketReplay += worker.Metrics.Ticks.BucketReplay;
            Ticks.Scan += worker.Metrics.Ticks.Scan;
            Ticks.SmallPrimeVariation += worker.Metrics.Ticks.SmallPrimeVariation;
            Ticks.PolyEval += worker.Metrics.Ticks.PolyEval;
            Ticks.KnownHitCollection += worker.Metrics.Ticks.KnownHitCollection;
            Ticks.DirectKnownHitCollection += worker.Metrics.Ticks.DirectKnownHitCollection;
            Ticks.BucketKnownHitCollection += worker.Metrics.Ticks.BucketKnownHitCollection;
            Ticks.TrialDiv += worker.Metrics.Ticks.TrialDiv;
            Ticks.TrialDivPre += worker.Metrics.Ticks.TrialDivPre;
            Ticks.TrialDivPost += worker.Metrics.Ticks.TrialDivPost;
            Ticks.TrialDivPostAPos += worker.Metrics.Ticks.TrialDivPostAPos;
            Ticks.TrialDivPostCheck += worker.Metrics.Ticks.TrialDivPostCheck;
            Ticks.TrialDivPostParity += worker.Metrics.Ticks.TrialDivPostParity;

            BucketOverflowHits += worker.Metrics.Buckets.OverflowHits;
            BucketSlabBytesPerWorker = Math.Max(BucketSlabBytesPerWorker, worker.Metrics.Buckets.SlabBytesPerWorker);
            BucketMaximumHitsPerBucket = Math.Max(
                BucketMaximumHitsPerBucket, worker.Metrics.Buckets.MaximumHitsPerBucket);
            BucketCapacityPerBucket = Math.Max(
                BucketCapacityPerBucket, worker.Metrics.Buckets.CapacityPerBucket);
            BucketBinaryCandidateBlocks += worker.Metrics.Buckets.BinaryCandidateBlocks;
            BucketCandidateMajorBlocks += worker.Metrics.Buckets.CandidateMajorBlocks;
            BucketOffsetMapBlocks += worker.Metrics.Buckets.OffsetMapBlocks;
            BucketCandidateHitInspections += worker.Metrics.Buckets.CandidateHitInspections;
            BucketCandidateVectorGroups += worker.Metrics.Buckets.CandidateVectorGroups;
            BucketCandidateMatchingMasks += worker.Metrics.Buckets.CandidateMatchingMasks;
            BucketOffsetMapProbes += worker.Metrics.Buckets.OffsetMapProbes;
            BucketDecodedPrimeHits += worker.Metrics.Buckets.DecodedPrimeHits;

            if (worker.CompositeResiduals is { Count: > 0 } residuals)
            {
                (CompositeResiduals ??= []).AddRange(residuals);
            }
        }
    }
}
