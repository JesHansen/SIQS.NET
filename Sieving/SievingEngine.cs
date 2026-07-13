using SIQS.Contracts;
using SIQS.Contracts.Files;

namespace Sieving;

/// <summary>
/// Public entry point to the SIQS sieving phase. Sieving generates polynomials, sieves the interval
/// <c>[-M, M]</c>, trial-divides promising values against the factor base, and classifies full and
/// large-prime partial relations. See <c>Instructions/Sieving.md</c>.
/// <para>
/// This type is only the facade; the run itself is driven by <see cref="SieveRunCoordinator"/> and
/// the thread-local <see cref="PolynomialSieveWorker"/>.
/// </para>
/// </summary>
public static class SievingEngine
{
    public static SievingResult Sieve(
        FactorBaseDocument factorBase,
        SievingParameters parameters,
        IProgress<SiqsProgressEvent>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sink = new InMemoryRawRelationSink();
        var counters = Sieve(factorBase, parameters, sink, progress, cancellationToken);
        var metadata = new RawRelationsMetadata(
            factorBase.Metadata.TargetN,
            factorBase.Metadata.Multiplier,
            factorBase.Metadata.ScaledN,
            factorBase.Metadata.Bound,
            parameters.LargePrimeBound);

        return new SievingResult(metadata, sink.FullRelations, sink.Partials, counters);
    }

    public static SievingCounters Sieve(
        FactorBaseDocument factorBase,
        SievingParameters parameters,
        IRawRelationSink sink,
        IProgress<SiqsProgressEvent>? progress = null,
        CancellationToken cancellationToken = default,
        SievingResumeState? resumeState = null,
        IReadOnlySet<int>? assignedAIndices = null)
        => SieveRunCoordinator.Run(factorBase, parameters, sink, progress, cancellationToken, resumeState, assignedAIndices);

    /// <summary>
    /// The scan cutoff for a sieve value: a value survives when its log-credit total exceeds this
    /// threshold, allowing for one large-prime cofactor plus the error margin.
    /// </summary>
    internal static double CandidateThreshold(
        double logScale,
        double vEstimate,
        double largePrimeLogAllowance,
        int errorMargin)
        => logScale * Math.Log(Math.Max(1.0, vEstimate)) - largePrimeLogAllowance - errorMargin;
}
