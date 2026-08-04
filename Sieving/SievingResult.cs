using SIQS.Contracts;
using SIQS.Contracts.Files;

namespace Sieving;

/// <summary>Running counts reported by the sieving engine.</summary>
public sealed class SievingCounters
{
    internal IReadOnlyList<ulong>? CapturedCompositeResiduals { get; set; }
    public long Polynomials { get; set; }
    public long Candidates { get; set; }
    public long SmallPrimeVariationReports { get; set; }
    public long SmallPrimeVariationRejected { get; set; }
    public int SmallPrimeVariationCount { get; set; }
    public int SmallPrimeVariationAllowance { get; set; }
    public long PreliminaryReports { get; set; }
    public long ExactThresholdRejects { get; set; }
    public long DirectGatedCandidateBlocks { get; set; }
    public long ResievedCandidateBlocks { get; set; }
    public long Blocks { get; set; }
    public long FullRelations { get; set; }
    public long Partials { get; set; }
    public long Discarded { get; set; }
    public long UsableRelations { get; set; }
    public long UsablePartialPairs { get; set; }
    public long ZeroParityFullRelations { get; set; }
    public long ZeroParityPartialPairs { get; set; }
    public long ProjectedMatrixRows { get; set; }
    public long ProjectedMatrixColumns { get; set; }
    public long OneLargePrimePartials { get; set; }
    public long TwoLargePrimePartials { get; set; }
    public long TwoLargePrimeSplitAttempts { get; set; }
    public long TwoLargePrimeSplitSuccesses { get; set; }
    public long TwoLargePrimeResidualTooSmall { get; set; }
    public long TwoLargePrimeResidualTooLarge { get; set; }
    public long TwoLargePrimeResidualPrime { get; set; }
    public long TwoLargePrimeResidualSmallFactor { get; set; }
    public long TwoLargePrimeResidualBitsLe32 { get; set; }
    public long TwoLargePrimeResidualBitsLe48 { get; set; }
    public long TwoLargePrimeResidualBitsLe64 { get; set; }
    public long TwoLargePrimeResidualBitsGt64 { get; set; }
    public long CofactorSqufofAttempts { get; set; }
    public long CofactorSqufofSuccesses { get; set; }
    public long CofactorMicroEcmAttempts { get; set; }
    public long CofactorMicroEcmSuccesses { get; set; }
    public long CofactorRhoAttempts { get; set; }
    public long CofactorRhoSuccesses { get; set; }
    public long BucketOverflowHits { get; set; }
    public long BucketSlabBytesPerWorker { get; set; }
    public long BucketMaximumHitsPerBucket { get; set; }
    public long BucketCapacityPerBucket { get; set; }
    public long BucketBinaryCandidateBlocks { get; set; }
    public long BucketCandidateMajorBlocks { get; set; }
    public long BucketOffsetMapBlocks { get; set; }
    public long BucketCandidateHitInspections { get; set; }
    public long BucketCandidateVectorGroups { get; set; }
    public long BucketCandidateMatchingMasks { get; set; }
    public long BucketOffsetMapProbes { get; set; }
    public long BucketDecodedPrimeHits { get; set; }
    public long RelationTarget { get; set; }
    public long RawRelations => FullRelations + Partials;
    public int? TrialRawRelationTarget { get; set; }
    public long SetupCpuMs { get; set; }
    public long SieveFillCpuMs { get; set; }
    public long SieveInitCpuMs { get; set; }
    public long SieveClearCpuMs { get; set; }
    public long SmallPrimeFillCpuMs { get; set; }
    public long DirectFillCpuMs { get; set; }
    public long BucketScatterCpuMs { get; set; }
    public long BucketReplayCpuMs { get; set; }
    public long ScanCpuMs { get; set; }
    public long SmallPrimeVariationCpuMs { get; set; }
    public long PolyEvalCpuMs { get; set; }
    public long KnownHitCollectionCpuMs { get; set; }
    public long DirectKnownHitCollectionCpuMs { get; set; }
    public long BucketKnownHitCollectionCpuMs { get; set; }
    public long TrialDivCpuMs { get; set; }
    public long TrialDivPreCpuMs { get; set; }
    public long TrialDivPostCpuMs { get; set; }
    public long TrialDivPostAPosCpuMs { get; set; }
    public long TrialDivPostCheckCpuMs { get; set; }
    public long TrialDivPostParityCpuMs { get; set; }
}

/// <summary>The full output of a sieving run.</summary>
public sealed record SievingResult(
    RawRelationsMetadata Metadata,
    IReadOnlyList<RawRelationRecord> FullRelations,
    IReadOnlyList<RawRelationRecord> Partials,
    SievingCounters Counters);
