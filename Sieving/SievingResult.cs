using SIQS.Contracts;
using SIQS.Contracts.Files;

namespace Sieving;

/// <summary>Running counts reported by the sieving engine.</summary>
public sealed class SievingCounters
{
    public long Polynomials { get; set; }
    public long Candidates { get; set; }
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
    public long CofactorRhoAttempts { get; set; }
    public long CofactorRhoSuccesses { get; set; }
    public long BucketOverflowHits { get; set; }
    public long BucketSlabBytesPerWorker { get; set; }
    public long RelationTarget { get; set; }
    public long RawRelations => FullRelations + Partials;
    public int? TrialRawRelationTarget { get; set; }
    public long SetupCpuMs { get; set; }
    public long SieveFillCpuMs { get; set; }
    public long SieveInitCpuMs { get; set; }
    public long ScanCpuMs { get; set; }
    public long PolyEvalCpuMs { get; set; }
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
