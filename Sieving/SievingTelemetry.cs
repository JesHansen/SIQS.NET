namespace Sieving;

/// <summary>Relation tallies accumulated by one worker across its polynomials.</summary>
internal struct RelationCounts
{
    public int Fulls;
    public int Partials;
    public int OneLargePrimePartials;
    public int TwoLargePrimePartials;
    public long PolyCount;
    public long Candidates;
    public long SmallPrimeVariationReports;
    public long SmallPrimeVariationRejected;
    public long PreliminaryReports;
    public long ExactThresholdRejects;
    public long DirectGatedCandidateBlocks;
    public long ResievedCandidateBlocks;
    public long Blocks;
    public long Discarded;
}

/// <summary>Two-large-prime split attempt/outcome tallies.</summary>
internal struct TwoLargePrimeStats
{
    public long SplitAttempts;
    public long SplitSuccesses;
    public long ResidualTooSmall;
    public long ResidualTooLarge;
    public long ResidualPrime;
    public long ResidualSmallFactor;
    public long ResidualBitsLe32;
    public long ResidualBitsLe48;
    public long ResidualBitsLe64;
    public long ResidualBitsGt64;
}

/// <summary>Cofactor-factorization engine tallies.</summary>
internal struct CofactorStats
{
    public long MicroEcmAttempts;
    public long MicroEcmSuccesses;
    public long SqufofAttempts;
    public long SqufofSuccesses;
    public long RhoAttempts;
    public long RhoSuccesses;
}

/// <summary>Raw Stopwatch tick accumulators for each sieving sub-phase.</summary>
internal struct PhaseTicks
{
    public long Setup;
    public long SieveFill;
    public long SieveInit;
    public long SieveClear;
    public long SmallPrimeFill;
    public long DirectFill;
    public long BucketScatter;
    public long BucketReplay;
    public long Scan;
    public long SmallPrimeVariation;
    public long PolyEval;
    public long KnownHitCollection;
    public long DirectKnownHitCollection;
    public long BucketKnownHitCollection;
    public long TrialDiv;
    public long TrialDivPre;
    public long TrialDivPost;
    public long TrialDivPostAPos;
    public long TrialDivPostCheck;
    public long TrialDivPostParity;
}

/// <summary>Large-prime bucket diagnostics for one worker.</summary>
internal struct BucketStats
{
    public long OverflowHits;
    public long SlabBytesPerWorker;
    public long MaximumHitsPerBucket;
    public long CapacityPerBucket;
    public long BinaryCandidateBlocks;
    public long CandidateMajorBlocks;
    public long OffsetMapBlocks;
    public long CandidateHitInspections;
    public long CandidateVectorGroups;
    public long CandidateMatchingMasks;
    public long OffsetMapProbes;
    public long DecodedPrimeHits;
}

/// <summary>
/// All telemetry accumulated by one <see cref="PolynomialSieveWorker"/>. Bundling the grouped stat
/// structs keeps the worker free of loose counter fields; the mutable struct fields are incremented
/// in place on the hot path (a class field access, so no allocation and no boxing).
/// </summary>
internal sealed class SieveWorkerMetrics
{
    public RelationCounts Relations;
    public TwoLargePrimeStats TwoLargePrime;
    public CofactorStats Cofactor;
    public BucketStats Buckets;
    public PhaseTicks Ticks;
}
