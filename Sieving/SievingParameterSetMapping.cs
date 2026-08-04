using SIQS.Contracts.Distributed;

namespace Sieving;

/// <summary>
/// Bridges the engine's <see cref="SievingParameters"/> to the transport-friendly
/// <see cref="SievingParameterSet"/> and back, so a server can ship a resolved configuration to a
/// client that reconstructs it verbatim.
/// </summary>
public static class SievingParameterSetMapping
{
    public static SievingParameterSet ToSet(this SievingParameters p) => new(
        SieveHalfInterval: p.SieveHalfInterval,
        PolynomialCount: p.PolynomialCount,
        RelationTarget: p.RelationTarget,
        LargePrimeBound: p.LargePrimeBound,
        ErrorMargin: p.ErrorMargin,
        OutputBatchSize: p.OutputBatchSize,
        APrimeCount: p.APrimeCount,
        APrimeWindowSize: p.APrimeWindowSize,
        Parallelism: p.Parallelism,
        SieveBlockSize: p.SieveBlockSize,
        BucketLargePrimeCutoff: p.BucketLargePrimeCutoff,
        ResieveLargePrimeCutoff: p.ResieveLargePrimeCutoff,
        SmallPrimeVariationBound: p.SmallPrimeVariationBound,
        TrialRawRelationTarget: p.TrialRawRelationTarget,
        EnableTwoLargePrimes: p.EnableTwoLargePrimes,
        LargePrime2Bound: p.LargePrime2Bound,
        LargePrime2ThresholdBound: p.LargePrime2ThresholdBound,
        CofactorSplitter: p.CofactorSplitter.ToToken());

    public static SievingParameters ToParameters(this SievingParameterSet s) => new(
        SieveHalfInterval: s.SieveHalfInterval,
        PolynomialCount: s.PolynomialCount,
        RelationTarget: s.RelationTarget,
        LargePrimeBound: s.LargePrimeBound,
        ErrorMargin: s.ErrorMargin,
        OutputBatchSize: s.OutputBatchSize,
        APrimeCount: s.APrimeCount,
        APrimeWindowSize: s.APrimeWindowSize,
        Parallelism: s.Parallelism,
        SieveBlockSize: s.SieveBlockSize,
        BucketLargePrimeCutoff: s.BucketLargePrimeCutoff,
        ResieveLargePrimeCutoff: s.ResieveLargePrimeCutoff,
        SmallPrimeVariationBound: s.SmallPrimeVariationBound,
        TrialRawRelationTarget: s.TrialRawRelationTarget,
        EnableTwoLargePrimes: s.EnableTwoLargePrimes,
        LargePrime2Bound: s.LargePrime2Bound,
        LargePrime2ThresholdBound: s.LargePrime2ThresholdBound,
        CofactorSplitter: CofactorSplitterKinds.TryParse(s.CofactorSplitter, out var kind) ? kind : CofactorSplitterKinds.Default);
}
