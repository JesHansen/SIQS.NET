using System.Diagnostics;
using System.Numerics;
using SIQS.Contracts;
using SIQS.Contracts.Files;
using SIQS.Contracts.Numerics;

namespace Sieving;

/// <summary>Coordinates one sieve run, including resume state, parallel worker scheduling, and result aggregation.</summary>
internal static class SieveRunCoordinator
{
    internal static SievingCounters Run(
        FactorBaseDocument factorBase,
        SievingParameters parameters,
        IRawRelationSink sink,
        IProgress<SiqsProgressEvent>? progress = null,
        CancellationToken cancellationToken = default,
        SievingResumeState? resumeState = null,
        IReadOnlySet<int>? assignedAIndices = null)
    {
        var fb = FactorBaseData.From(factorBase);
        var aCandidates = PolynomialGenerator.SelectAPositions(fb, parameters);
        if (aCandidates.Count == 0)
        {
            throw new InvalidOperationException(
                "No valid polynomial A candidates could be generated from the factor base.");
        }

        // Slice mode: a distributed client is assigned a fixed subset of A-indices and must sieve
        // its entire assignment (the lease size bounds the work, not the relation target). Any index
        // outside [0, aCandidates.Count) means the client and server disagree on the A-candidate
        // ordering — a determinism bug we surface immediately rather than silently mis-sieving.
        var sliceMode = assignedAIndices is not null;
        if (assignedAIndices is not null)
        {
            foreach (var index in assignedAIndices)
            {
                if (index < 0 || index >= aCandidates.Count)
                {
                    throw new InvalidOperationException(
                        $"Assigned A-index {index} is outside the A-candidate range [0, {aCandidates.Count}).");
                }
            }
        }

        if (resumeState is not null && parameters.TrialRawRelationTarget is not null)
        {
            throw new InvalidOperationException("Trial-sieve runs cannot use A-index checkpoint resume.");
        }

        var completedAIndices = resumeState?.CompletedAIndices.ToHashSet() ?? new HashSet<int>();
        foreach (var index in completedAIndices)
        {
            if (index < 0 || index >= aCandidates.Count)
            {
                throw new InvalidOperationException($"Checkpoint A-index {index} is outside the current A-candidate range.");
            }
        }

        var m = parameters.SieveHalfInterval;
        var blockLength = checked((int)(2 * m + 1));

        // Byte-sieve rescaling: for a smooth Q(x) of size ≈ Q_max, the ushort log-credit totals
        // logScale × log(Q_max) ≈ 1600–2400.  Scale everything by byteRescale = 200 / that total
        // so that the maximum meaningful sieve value fits in a byte, and the fill uses byte arithmetic
        // (halving memory traffic relative to ushort).
        var qMaxEstimate = (double)m * Math.Sqrt(2.0 * (double)fb.TargetN) / 2.0;
        var effectiveMaxLogCredit = fb.LogScale * Math.Log(Math.Max(1.0, qMaxEstimate));
        var byteRescale = effectiveMaxLogCredit > 0.0 ? 200.0 / effectiveMaxLogCredit : 1.0;
        var byteLogP = new byte[fb.Count];
        for (var i = 0; i < fb.Count; i++)
            byteLogP[i] = (byte)Math.Max(1, (int)Math.Round(fb.LogP[i] * byteRescale));

        var smallPrimeMasks = SmallPrimeFillMasks.Build(fb, byteLogP);

        var totals = new WorkerTotals();
        var tally = new RelationTally();

        if (resumeState is not null)
        {
            foreach (var record in resumeState.ExistingRelations)
            {
                if (!tally.TryRemember(record, out var duplicate) && duplicate)
                {
                    continue;
                }

                tally.Count(record, includeInTotals: true);
            }

            foreach (var aIndex in completedAIndices)
            {
                var familySize = PolynomialGenerator.BuildFamily(fb, aCandidates[aIndex]).Members.Count;
                tally.AddResumedPolynomials(familySize);
            }
        }

        using var loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pOpts = new ParallelOptions
        {
            MaxDegreeOfParallelism = parameters.EffectiveParallelism,
            CancellationToken = loopCts.Token,
        };

        try
        {
            var candidateOrder = assignedAIndices is not null
                ? assignedAIndices.OrderBy(i => i)
                : parameters.TrialRawRelationTarget is null
                    ? Enumerable.Range(0, aCandidates.Count)
                    : TrialSpreadOrder(aCandidates.Count);

            Parallel.ForEach(
                candidateOrder
                    .Where(i => !completedAIndices.Contains(i))
                    .Select(i => (AIdx: i, Positions: aCandidates[i])),
                pOpts,
                () => new PolynomialSieveWorker(Math.Min(blockLength, parameters.EffectiveSieveBlockSize)),
                (item, state, worker) =>
                {
                    if (!sliceMode &&
                        (StopReached(parameters, tally.FullCount, tally.UsefulFullCount, tally.PartialCount, tally.UsablePairs) ||
                        tally.PolyCount >= parameters.PolynomialCount))
                    {
                        state.Stop();
                        return worker;
                    }

                    var t0 = Stopwatch.GetTimestamp();
                    var family = PolynomialGenerator.BuildFamily(fb, item.Positions);
                    var isAPrime = new bool[fb.Count];
                    foreach (var pos in item.Positions) isAPrime[pos] = true;

                    // inverse(A) mod p is fixed for the whole B-family of this A.
                    var invA = new long[fb.Count];
                    for (var i = 0; i < fb.Count; i++)
                    {
                        if (!isAPrime[i] && fb.Primes[i] != 2)
                            invA[i] = IntegerMath.ModInverse((long)IntegerMath.Mod(family.A, fb.Primes[i]), fb.Primes[i]);
                    }

                    var grayRootDeltas = PrecomputeGrayRootDeltas(fb, family.BTerms, isAPrime, invA);
                    worker.Metrics.Ticks.Setup += Stopwatch.GetTimestamp() - t0;

                    var polyInFamily = 0;
                    foreach (var member in family.Members)
                    {
                        if (state.ShouldExitCurrentIteration) break;
                        var poly = member.Polynomial;

                        var polySeq = tally.IncrementPolyCount();
                        if (!sliceMode && polySeq > parameters.PolynomialCount)
                        {
                            state.Stop();
                            break;
                        }

                        var currentPolyInFamily = polyInFamily;
                        var sieveContext = new PolynomialSieveContext(
                            fb, blockLength, m, poly, isAPrime, invA, member, grayRootDeltas,
                            parameters, item.AIdx, currentPolyInFamily, byteLogP, byteRescale,
                            smallPrimeMasks.Masks, smallPrimeMasks.NextPhase, smallPrimeMasks.Count);
                        worker.SieveAndScanBlocked(sieveContext);
                        polyInFamily++;

                        // Update shared counters for every new relation found this poly.
                        // Full relations: increment the shared full-relation count.
                        // Partial relations: update the shared large-prime dictionary; when a second
                        // partial with the same large prime is seen, increment the shared usable-pair count.
                        foreach (var tagged in worker.Pending)
                        {
                            var record = tagged.WithStableIds();
                            if (!tally.TryRemember(record, out var duplicate))
                            {
                                if (duplicate)
                                {
                                    if (tagged.IsPartial)
                                    {
                                        worker.Metrics.Relations.Partials--;
                                        if (record.LargePrimes.Count == 1) worker.Metrics.Relations.OneLargePrimePartials--;
                                        else if (record.LargePrimes.Count == 2) worker.Metrics.Relations.TwoLargePrimePartials--;
                                    }
                                    else
                                    {
                                        worker.Metrics.Relations.Fulls--;
                                    }

                                    continue;
                                }
                            }

                            tally.Count(record, includeInTotals: false);
                            sink.Add(record);
                        }
                        worker.Pending.Clear();

                        var usable = tally.UsefulFullCount + tally.UsablePairs;
                        if (!sliceMode && StopReached(parameters, tally.FullCount, tally.UsefulFullCount, tally.PartialCount, tally.UsablePairs))
                        {
                            state.Stop();
                            break;
                        }

                        if (polySeq % 256 == 0)
                        {
                            SievingProgressReporter.ReportShared(progress, tally.PolyCount, tally.FullCount, tally.PartialCount, usable, fb.Count, parameters);
                        }
                    }

                    if (polyInFamily == family.Members.Count)
                    {
                        sink.Flush();
                        resumeState?.MarkAIndexCompleted?.Invoke(item.AIdx);
                    }

                    return worker;
                },
                worker => totals.Add(worker));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        sink.Complete();
        var usablePairs = tally.UsablePairs;
        var zeroParityFulls = tally.ZeroParityFullCount;
        var zeroParityPairs = tally.ZeroParityPairs;
        static long ToMs(long ticks) => (long)(ticks * 1000.0 / Stopwatch.Frequency);

        // Resumed relations were replayed above (not produced by any worker in this run), so their
        // counts must be added back on top of the worker-summed totals.
        var finalPolys = tally.ResumedPolys + totals.Relations.PolyCount;
        var finalFulls = tally.ResumedFulls + totals.Relations.Fulls;
        var finalPartials = tally.ResumedPartials + totals.Relations.Partials;
        var finalOneLargePrimePartials = tally.ResumedOneLargePrimePartials + totals.Relations.OneLargePrimePartials;
        var finalTwoLargePrimePartials = tally.ResumedTwoLargePrimePartials + totals.Relations.TwoLargePrimePartials;

        var counters = new SievingCounters
        {
            Polynomials = finalPolys,
            Candidates = totals.Relations.Candidates,
            Blocks = totals.Relations.Blocks,
            FullRelations = finalFulls,
            Partials = finalPartials,
            OneLargePrimePartials = finalOneLargePrimePartials,
            TwoLargePrimePartials = finalTwoLargePrimePartials,
            TwoLargePrimeSplitAttempts = totals.TwoLargePrime.SplitAttempts,
            TwoLargePrimeSplitSuccesses = totals.TwoLargePrime.SplitSuccesses,
            TwoLargePrimeResidualTooSmall = totals.TwoLargePrime.ResidualTooSmall,
            TwoLargePrimeResidualTooLarge = totals.TwoLargePrime.ResidualTooLarge,
            TwoLargePrimeResidualPrime = totals.TwoLargePrime.ResidualPrime,
            TwoLargePrimeResidualSmallFactor = totals.TwoLargePrime.ResidualSmallFactor,
            TwoLargePrimeResidualBitsLe32 = totals.TwoLargePrime.ResidualBitsLe32,
            TwoLargePrimeResidualBitsLe48 = totals.TwoLargePrime.ResidualBitsLe48,
            TwoLargePrimeResidualBitsLe64 = totals.TwoLargePrime.ResidualBitsLe64,
            TwoLargePrimeResidualBitsGt64 = totals.TwoLargePrime.ResidualBitsGt64,
            CofactorSqufofAttempts = totals.Cofactor.SqufofAttempts,
            CofactorSqufofSuccesses = totals.Cofactor.SqufofSuccesses,
            CofactorRhoAttempts = totals.Cofactor.RhoAttempts,
            CofactorRhoSuccesses = totals.Cofactor.RhoSuccesses,
            BucketOverflowHits = totals.BucketOverflowHits,
            BucketSlabBytesPerWorker = totals.BucketSlabBytesPerWorker,
            Discarded = totals.Relations.Discarded,
            RelationTarget = parameters.RelationTarget,
            TrialRawRelationTarget = parameters.TrialRawRelationTarget,
            UsableRelations = finalFulls - zeroParityFulls + usablePairs,
            UsablePartialPairs = usablePairs,
            ZeroParityFullRelations = zeroParityFulls,
            ZeroParityPartialPairs = zeroParityPairs,
            ProjectedMatrixRows = finalFulls - zeroParityFulls + usablePairs,
            ProjectedMatrixColumns = fb.Count + 1L,
            SetupCpuMs = ToMs(totals.Ticks.Setup),
            SieveFillCpuMs = ToMs(totals.Ticks.SieveFill),
            SieveInitCpuMs = ToMs(totals.Ticks.SieveInit),
            ScanCpuMs = ToMs(totals.Ticks.Scan),
            PolyEvalCpuMs = ToMs(totals.Ticks.PolyEval),
            TrialDivCpuMs = ToMs(totals.Ticks.TrialDiv),
            TrialDivPreCpuMs = ToMs(totals.Ticks.TrialDivPre),
            TrialDivPostCpuMs = ToMs(totals.Ticks.TrialDivPost),
            TrialDivPostAPosCpuMs = ToMs(totals.Ticks.TrialDivPostAPos),
            TrialDivPostCheckCpuMs = ToMs(totals.Ticks.TrialDivPostCheck),
            TrialDivPostParityCpuMs = ToMs(totals.Ticks.TrialDivPostParity),
        };

        SievingProgressReporter.Report(progress, counters, current: null);
        return counters;
    }

    /// <summary>Stops sieving once the raw-relation trial target or the usable-relation target is met.</summary>
    private static bool StopReached(
        SievingParameters parameters,
        long sharedFullCount,
        long sharedUsefulFullCount,
        long sharedPartialCount,
        long sharedUsablePairs)
    {
        var full = Volatile.Read(ref sharedFullCount);
        if (parameters.TrialRawRelationTarget is { } rawTarget &&
            full + Volatile.Read(ref sharedPartialCount) >= rawTarget)
        {
            return true;
        }

        return Volatile.Read(ref sharedUsefulFullCount) + Volatile.Read(ref sharedUsablePairs) >= parameters.RelationTarget;
    }

    /// <summary>
    /// For each Gray-code B-term, the per-prime root shift (2·bTerm·A⁻¹ mod p) applied when advancing
    /// to the next polynomial in the family.
    /// </summary>
    private static int[][] PrecomputeGrayRootDeltas(
        FactorBaseData fb,
        BigInteger[] bTerms,
        bool[] isAPrime,
        long[] invA)
    {
        var deltas = new int[bTerms.Length][];
        for (var termIndex = 0; termIndex < bTerms.Length; termIndex++)
        {
            var termDeltas = new int[fb.Count];
            var twiceTerm = 2 * bTerms[termIndex];
            for (var i = 0; i < fb.Count; i++)
            {
                var p = fb.Primes[i];
                if (isAPrime[i] || p == 2)
                {
                    continue;
                }

                var termModP = (long)IntegerMath.Mod(twiceTerm, p);
                termDeltas[i] = (int)((termModP * invA[i]) % p);
            }

            deltas[termIndex] = termDeltas;
        }

        return deltas;
    }

    /// <summary>
    /// A bisection ordering of <c>[0, count)</c>: endpoints first, then repeatedly the midpoints of
    /// the widest gaps. Trial sieve runs use it to sample A-candidates evenly across the range.
    /// </summary>
    internal static IReadOnlyList<int> TrialSpreadOrder(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (count == 0)
        {
            return Array.Empty<int>();
        }

        var order = new List<int>(count) { 0 };
        if (count == 1)
        {
            return order;
        }

        order.Add(count - 1);
        var seen = new bool[count];
        seen[0] = true;
        seen[count - 1] = true;

        var intervals = new Queue<(int Left, int Right)>();
        intervals.Enqueue((0, count - 1));
        while (intervals.Count > 0)
        {
            var (left, right) = intervals.Dequeue();
            if (right - left <= 1)
            {
                continue;
            }

            var mid = left + (right - left) / 2;
            if (!seen[mid])
            {
                seen[mid] = true;
                order.Add(mid);
            }

            intervals.Enqueue((left, mid));
            intervals.Enqueue((mid, right));
        }

        return order;
    }
}
