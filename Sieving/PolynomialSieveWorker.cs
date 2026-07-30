using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using SIQS.Contracts;
using SIQS.Contracts.Numerics;

namespace Sieving;

internal readonly record struct BlockCandidate(long X, BigInteger V, int J, int Offset);

/// <summary>Per-polynomial context for <see cref="PolynomialSieveWorker.SieveAndScanBlocked"/>: fixed for the whole block loop.</summary>
internal readonly record struct PolynomialSieveContext(
    FactorBaseData Fb, int FullBlockLength, long M, PolynomialCandidate Poly,
    bool[] IsAPrime, long[] InvA, PolynomialFamilyMember FamilyMember,
    int[][]? GrayRootDeltas, SievingParameters Parameters, int AIdx, int PolyInFamily,
    byte[] ByteLogP, double ByteRescale,
    Vector256<byte>[][]? SpMasks, int[][]? SpNext, int SpCount);

// Thread-local worker: owns the sieve buffer and accumulates relations for one thread.
internal sealed class PolynomialSieveWorker(int sieveBufferLength)
{
    private const int RootCrossingScanLength = 32;

    public readonly byte[] Sieve = new byte[sieveBufferLength];
    public readonly List<TaggedRelation> Pending = [];
    public readonly Dictionary<int, int> ExponentScratch = [];

    // All per-worker telemetry (relation/two-large-prime/cofactor/bucket counts and phase timings).
    // See PhaseTicks in SievingTelemetry.cs for the meaning of each Ticks.* accumulator.
    public readonly SieveWorkerMetrics Metrics = new();

    private readonly List<BlockCandidate> _blockCandidates = [];

    // Reused per-worker scratch, owned by dedicated state objects: the root/position arrays, the
    // candidate-hit lists, and the large-prime bucket allocation.
    private readonly SieveRootState _roots = new();
    private readonly CandidateHitWorkspace _candidates = new();
    private readonly LargePrimeBucketWorkspace _buckets = new();

    /// <summary>Non-negative remainder of <paramref name="a"/> modulo <paramref name="p"/>.</summary>
    private static long Mod(long a, long p) { var r = a % p; return r < 0 ? r + p : r; }

    /// <summary>
    /// Sieves one polynomial in cache-sized blocks and scans each block for relations
    /// immediately after filling it, so the filled data stays hot in L1/L2 throughout.
    /// Per-prime hit positions are tracked across blocks so each block only processes
    /// the hits that actually fall within its range.
    /// </summary>
    public void SieveAndScanBlocked(in PolynomialSieveContext ctx)
    {
        var fb = ctx.Fb;
        var fullBlockLength = ctx.FullBlockLength;
        var m = ctx.M;
        var poly = ctx.Poly;
        var isAPrime = ctx.IsAPrime;
        var invA = ctx.InvA;
        var familyMember = ctx.FamilyMember;
        var grayRootDeltas = ctx.GrayRootDeltas;
        var parameters = ctx.Parameters;
        var aIdx = ctx.AIdx;
        var polyInFamily = ctx.PolyInFamily;
        var byteLogP = ctx.ByteLogP;
        var byteRescale = ctx.ByteRescale;
        var spMasks = ctx.SpMasks;
        var spNext = ctx.SpNext;
        var spCount = ctx.SpCount;

        var blockSize = parameters.EffectiveSieveBlockSize;

        // Root/position arrays are grown once and reused across polynomials so Gray-code neighbours
        // keep the previous saved residues (see SieveRootState).
        _roots.EnsureCapacity(fb.Count);
        var pos1 = _roots.Pos1;
        var pos2 = _roots.Pos2;
        var root1Res = _roots.Root1Res;
        var root2Res = _roots.Root2Res;

        // ── Init: compute first hit index in [0, fullBlockLength) for each prime ──
        // The first polynomial in an A-family computes roots directly. Later Gray-code
        // neighbors update the saved residues by one precomputed delta plus any +/-A
        // normalization correction in B, avoiding a full BigInteger mod per prime.
        var tFill = Stopwatch.GetTimestamp();

        ComputeRootPositions(
            fb, poly, isAPrime, invA, familyMember, grayRootDeltas, m, fullBlockLength,
            pos1, pos2, root1Res, root2Res);

        // Pre-compute scan constants shared across all blocks for this polynomial.
        var ad = (double)poly.A;
        var bd = (double)poly.B;
        var cd = (double)poly.C;

        // Snapshot root residues from the just-computed pos1/pos2.
        // pos1[i] = (x1 + m) % p and pos2[i] = (x2 + m) % p immediately after init,
        // before any block advancement.  We capture them now so TryBuildRelation can
        // check j ≡ root (mod p) without touching the running quotient remU.
        var largePrimeLogAllowance = parameters.EnableTwoLargePrimes
            ? fb.LogScale * Math.Log((double)parameters.LargePrime2ThresholdBound * parameters.LargePrime2ThresholdBound)
            : fb.LogScale * Math.Log(parameters.LargePrimeBound);

        // Split: everything above (init + residue snapshot) is counted as SieveInit,
        // and the per-block fill starts fresh from here.
        var tAfterInit = Stopwatch.GetTimestamp();
        Metrics.Ticks.SieveInit += tAfterInit - tFill;
        tFill = tAfterInit;

        // ── Block loop: fill each block then scan it before moving on ──
        // Pin a ref to the first element of Sieve once: all per-prime writes use
        // Unsafe.Add(ref sieve0, j) which compiles to a single offset-load/store
        // with no bounds check, eliminating the redundant range validation that the
        // JIT emits for array indexers when it can't prove j < Sieve.Length.
        ref byte sieve0 = ref MemoryMarshal.GetArrayDataReference(Sieve);

        // Large-prime bucket fill: primes above this cutoff are sparse in each block,
        // so looping over them for every block mostly pays branch/loop overhead.  Instead
        // build per-block hit lists once for this polynomial, then replay only the hits
        // belonging to the current block while its sieve buffer is hot.
        var (bucketStart, resieveStart) = ResolvePrimeBands(ctx);

        var bucketCount = (fullBlockLength + blockSize - 1) / blockSize;
        LargePrimeBuckets? largePrimeBuckets = null;
        if (bucketStart < fb.Count)
        {
            var bucketCapacity = _buckets.EstimateCapacity(fb, bucketStart, blockSize);
            largePrimeBuckets = _buckets.Ensure(bucketCount, bucketCapacity, blockSize);
            largePrimeBuckets.Clear();
            Metrics.Buckets.SlabBytesPerWorker = Math.Max(Metrics.Buckets.SlabBytesPerWorker, largePrimeBuckets.SlabSize.Bytes);

            ScatterBandPrimeHits(fb, bucketStart, fullBlockLength, blockSize, byteLogP, pos1, pos2, largePrimeBuckets);
            Metrics.Buckets.OverflowHits += largePrimeBuckets.OverflowHitCount;
        }

        Span<(int From, int To)> scanSegments = stackalloc (int, int)[64];
        for (var blockStart = 0; blockStart < fullBlockLength; blockStart += blockSize)
        {
            Metrics.Relations.Blocks++;
            var blockEnd = Math.Min(blockStart + blockSize, fullBlockLength);

            // Fill: clear this block and mark every prime's hits within [blockStart, blockEnd).
            Array.Clear(Sieve, 0, blockEnd - blockStart);

            // Compute the extent of full 32-byte chunks within this block.
            var chunkEnd = blockStart + ((blockEnd - blockStart) / 32) * 32;

            // ── AVX2 SIMD fill for small primes (p ≤ SmallPrimeThreshold) ─────
            // Each prime contributes logp at positions ≡ root (mod p).  For small p
            // this repeats frequently; we process 32 bytes per iteration using a
            // precomputed phase mask, replacing O(blockSize/p) scalar writes with
            // O(blockSize/32) vector adds.
            for (var i = 0; i < spCount; i++)
            {
                if (pos1[i] == fullBlockLength) continue; // A-prime sentinel: no hits
                var p      = (int)fb.Primes[i];
                var logp   = byteLogP[i];
                var s1     = pos1[i] - blockStart; // starting phase ∈ [0, p-1]
                var masks_i = spMasks![i];
                var next_i  = spNext![i];

                if (pos2[i] < fullBlockLength)
                {
                    // Two-root prime: add both roots' masks in each chunk.
                    var s2 = pos2[i] - blockStart;
                    for (var b = blockStart; b < chunkEnd; b += 32)
                    {
                        ref var cr = ref Unsafe.Add(ref sieve0, b - blockStart);
                        var v = Vector256.Add(Vector256.LoadUnsafe(ref cr), masks_i[s1]);
                        v = Vector256.Add(v, masks_i[s2]);
                        v.StoreUnsafe(ref cr);
                        s1 = next_i[s1];
                        s2 = next_i[s2];
                    }
                    // Scalar tail for remaining ≤31 bytes (root 2).
                    var j2t = chunkEnd + s2;
                    while (j2t < blockEnd) { Unsafe.Add(ref sieve0, j2t - blockStart) += logp; j2t += p; }
                    pos2[i] = j2t;
                }
                else
                {
                    // Single-root prime (p=2 or same-root).
                    for (var b = blockStart; b < chunkEnd; b += 32)
                    {
                        ref var cr = ref Unsafe.Add(ref sieve0, b - blockStart);
                        var v = Vector256.Add(Vector256.LoadUnsafe(ref cr), masks_i[s1]);
                        v.StoreUnsafe(ref cr);
                        s1 = next_i[s1];
                    }
                }
                // Scalar tail for remaining ≤31 bytes (root 1).
                var j1t = chunkEnd + s1;
                while (j1t < blockEnd) { Unsafe.Add(ref sieve0, j1t - blockStart) += logp; j1t += p; }
                pos1[i] = j1t;
            }

            // ── Scalar (two-root interleaved) fill for large primes ────────────
            for (var i = spCount; i < bucketStart; i++)
            {
                var p    = (int)fb.Primes[i];
                var logp = byteLogP[i];

                var j1 = pos1[i];
                var j2 = pos2[i];

                if (j2 < fullBlockLength)
                {
                    // Two-root prime: sort j1 ≤ j2, advance both in one loop.
                    if (j1 > j2) { (j1, j2) = (j2, j1); }
                    while (j2 < blockEnd)
                    {
                        Unsafe.Add(ref sieve0, j1 - blockStart) += logp; j1 += p;
                        Unsafe.Add(ref sieve0, j2 - blockStart) += logp; j2 += p;
                    }
                    if (j1 < blockEnd) { Unsafe.Add(ref sieve0, j1 - blockStart) += logp; j1 += p; }
                }
                else
                {
                    // Single-root prime (p=2 or A-prime sentinel handled above).
                    while (j1 < blockEnd) { Unsafe.Add(ref sieve0, j1 - blockStart) += logp; j1 += p; }
                }

                pos1[i] = j1;
                pos2[i] = j2;
            }

            if (largePrimeBuckets is not null)
            {
                largePrimeBuckets.PrepareBlock(new(blockStart / blockSize), ref sieve0);
            }

            Metrics.Ticks.SieveFill += Stopwatch.GetTimestamp() - tFill;

            // Scan: walk this block for relation candidates while it is still hot in cache.
            // Most sieve values are small byte totals (3-20); ranges far from polynomial roots
            // have minThreshByte ≈ 130-155, so Avx2.SubtractSaturate(chunk, minThreshVec) == 0
            // for ~99% of 32-byte chunks, letting VPTEST skip them without any log() call.
            // A range that crosses Q(x)=0 needs a zero lower bound. Recursively isolate that
            // crossing first so the zero gate applies to at most RootCrossingScanLength positions
            // rather than disabling the fast scan for an entire cache-sized sieve block.
            var tScan = Stopwatch.GetTimestamp();
            var prevTrialDivTicks = Metrics.Ticks.TrialDiv;
            _blockCandidates.Clear();

            void ScanRange(int from, int to, byte minThreshByte)
            {
                for (var k = from; k < to; k++)
                {
                    var ko = k - blockStart;
                    if (Sieve[ko] < minThreshByte) continue;
                    var x = k - m;
                    var estimate = Math.Max(1.0, Math.Abs(ad * x * x + 2.0 * bd * x + cd));
                    var thresholdByte = (byte)Math.Max(0.0, Math.Floor(byteRescale *
                        SievingEngine.CandidateThreshold(fb.LogScale, estimate, largePrimeLogAllowance, parameters.ErrorMargin)));
                    if (Sieve[ko] < thresholdByte) continue;
                    var tPE = Stopwatch.GetTimestamp();
                    var bx = new BigInteger(x);
                    var v = poly.A * bx * bx + 2 * poly.B * bx + poly.C;
                    Metrics.Ticks.PolyEval += Stopwatch.GetTimestamp() - tPE;
                    if (v.IsZero) continue;
                    Metrics.Relations.Candidates++;
                    _blockCandidates.Add(new BlockCandidate(x, v, k, ko));
                }
            }

            void ScanSegment(int from, int to)
            {
                var minThreshByte = MinThresholdByte(
                    ad, bd, cd, m, from, to, byteRescale, fb.LogScale,
                    largePrimeLogAllowance, parameters.ErrorMargin);
                var si = from;
                ref var scanSieve0 = ref MemoryMarshal.GetArrayDataReference(Sieve);

                if (minThreshByte > 0 && Avx2.IsSupported && !parameters.DisableVectorScan)
                {
                    // Subtract one less than the inclusive threshold: SubtractSaturate(value, gate)
                    // is positive exactly when value >= minThreshByte. At threshold zero every byte
                    // qualifies, so no vector chunk can be skipped safely.
                    var minThreshVec = Vector256.Create(InclusiveVectorGate(minThreshByte));

                    // AVX2: process 32 bytes per iteration (Vector256<byte>).
                    for (; si + 32 <= to; si += 32)
                    {
                        var chunk = Vector256.LoadUnsafe(ref Unsafe.Add(ref scanSieve0, si - blockStart));
                        // VPSUBUSB + VPTEST: all-zero iff every byte is below minThreshByte.
                        var sat = Avx2.SubtractSaturate(chunk, minThreshVec);
                        if (Avx2.TestZ(sat.AsInt32(), sat.AsInt32())) continue;

                        ScanRange(si, si + 32, minThreshByte);
                    }
                }

                // Scalar tail for the remaining <32 entries, or the whole range when AVX2 is unavailable.
                ScanRange(si, to, minThreshByte);
            }

            var segmentCount = 1;
            scanSegments[0] = (blockStart, blockEnd);
            while (segmentCount > 0)
            {
                var (from, to) = scanSegments[--segmentCount];
                if (to - from > RootCrossingScanLength &&
                    CrossesZeroInRange(ad, bd, cd, m, from, to))
                {
                    var middle = from + (to - from) / 2;
                    scanSegments[segmentCount++] = (middle, to);
                    scanSegments[segmentCount++] = (from, middle);
                    continue;
                }

                ScanSegment(from, to);
            }

            var knownPrimeHits = CollectKnownPrimeHits(
                fb, isAPrime, pos1, pos2, root2Res, largePrimeBuckets, blockStart, blockSize, resieveStart, bucketStart);

            // Root-gated trial division only runs below the resieve/bucket bands;
            // resieveStart degrades to bucketStart and then fb.Count as each is disabled.
            var directTrialLimit = knownPrimeHits is null ? bucketStart : resieveStart;

            for (var candidateIndex = 0; candidateIndex < _blockCandidates.Count; candidateIndex++)
            {
                var candidate = _blockCandidates[candidateIndex];
                var t1 = Stopwatch.GetTimestamp();
                var knownHits = knownPrimeHits is null ? null : knownPrimeHits[candidateIndex];
                var record2 = RelationBuilder.Build(fb, poly, isAPrime, parameters, candidate.X, candidate.V,
                    candidate.J, root1Res, root2Res, knownHits, directTrialLimit, this);
                Metrics.Ticks.TrialDiv += Stopwatch.GetTimestamp() - t1;
                if (record2 is null) { Metrics.Relations.Discarded++; continue; }
                Pending.Add(new TaggedRelation(record2, aIdx, polyInFamily, candidate.X, record2.Kind == RelationKind.Partial));
                if (record2.Kind == RelationKind.Full)
                {
                    Metrics.Relations.Fulls++;
                }
                else
                {
                    Metrics.Relations.Partials++;
                    if (record2.LargePrimes.Count == 1) Metrics.Relations.OneLargePrimePartials++;
                    else if (record2.LargePrimes.Count == 2) Metrics.Relations.TwoLargePrimePartials++;
                }
            }

            Metrics.Ticks.Scan += Stopwatch.GetTimestamp() - tScan - (Metrics.Ticks.TrialDiv - prevTrialDivTicks);

            tFill = Stopwatch.GetTimestamp(); // reset for next block's fill timing
        }

        Metrics.Relations.PolyCount++;
    }

    /// <summary>
    /// Splits the factor base above <paramref name="ctx"/>'s small-prime band into the resieve band
    /// (primes worth resieving per-candidate) and the bucket band (primes sparse enough to fill via
    /// per-block hit lists). Runs once per polynomial, before the block loop.
    /// </summary>
    private static (int BucketStart, int ResieveStart) ResolvePrimeBands(in PolynomialSieveContext ctx)
    {
        var fb = ctx.Fb;
        var bucketPrimeCutoff = ctx.Parameters.EffectiveBucketLargePrimeCutoff;
        var bucketStart = ctx.SpCount;
        if (bucketPrimeCutoff > 0)
        {
            while (bucketStart < fb.Count && fb.Primes[bucketStart] < bucketPrimeCutoff)
                bucketStart++;
        }
        else
        {
            bucketStart = fb.Count;
        }

        var resieveStart = bucketStart;
        var resievePrimeCutoff = ctx.Parameters.EffectiveResieveLargePrimeCutoff;
        if (resievePrimeCutoff > 0)
        {
            resieveStart = ctx.SpCount;
            while (resieveStart < bucketStart && fb.Primes[resieveStart] < resievePrimeCutoff)
                resieveStart++;
        }
        Debug.Assert(ctx.SpCount <= resieveStart && resieveStart <= bucketStart && bucketStart <= fb.Count);

        return (bucketStart, resieveStart);
    }

    /// <summary>
    /// The minimum log-credit threshold (in byte units) that any position within
    /// <c>[blockStart, blockEnd)</c> could need to survive, derived from the smallest |Q(x)| the
    /// block can produce. Used as a cheap block-wide lower bound before the per-candidate threshold.
    /// </summary>
    internal static byte MinThresholdByte(
        double ad, double bd, double cd, long m, int blockStart, int blockEnd,
        double byteRescale, double logScale, double largePrimeLogAllowance, int errorMargin)
    {
        var xBlockStart = (double)(blockStart - m);
        var xBlockEnd = (double)(blockEnd - 1 - m);
        var qLeft = ad * xBlockStart * xBlockStart + 2.0 * bd * xBlockStart + cd;
        var qRight = ad * xBlockEnd * xBlockEnd + 2.0 * bd * xBlockEnd + cd;
        var estLeft = Math.Max(1.0, Math.Abs(qLeft));
        var estRight = Math.Max(1.0, Math.Abs(qRight));
        var minEst = CrossesZero(qLeft, qRight) ? 1.0 : Math.Min(estLeft, estRight);

        if (ad != 0.0)
        {
            var xVertex = -bd / ad;
            if (xVertex >= xBlockStart && xVertex <= xBlockEnd)
            {
                var qVertex = ad * xVertex * xVertex + 2.0 * bd * xVertex + cd;
                if (CrossesZero(qLeft, qVertex) || CrossesZero(qVertex, qRight))
                {
                    minEst = 1.0;
                }
                else
                {
                    minEst = Math.Min(minEst, Math.Max(1.0, Math.Abs(qVertex)));
                }
            }
        }
        // Scale the minimum threshold to byte units; floor for a conservative lower bound.
        return (byte)Math.Max(0.0, Math.Floor(byteRescale *
            SievingEngine.CandidateThreshold(logScale, minEst, largePrimeLogAllowance, errorMargin)));
    }

    private static bool CrossesZero(double left, double right)
        => left == 0.0 || right == 0.0 || (left < 0.0) != (right < 0.0);

    internal static byte InclusiveVectorGate(byte minThreshold)
    {
        if (minThreshold == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minThreshold),
                "A zero threshold requires an unconditional scan.");
        }

        return (byte)(minThreshold - 1);
    }

    internal static bool CrossesZeroInRange(
        double ad, double bd, double cd, long m, int rangeStart, int rangeEnd)
    {
        var xStart = (double)(rangeStart - m);
        var xEnd = (double)(rangeEnd - 1 - m);
        var qLeft = ad * xStart * xStart + 2.0 * bd * xStart + cd;
        var qRight = ad * xEnd * xEnd + 2.0 * bd * xEnd + cd;
        if (CrossesZero(qLeft, qRight))
        {
            return true;
        }

        if (ad == 0.0)
        {
            return false;
        }

        var xVertex = -bd / ad;
        if (xVertex < xStart || xVertex > xEnd)
        {
            return false;
        }

        var qVertex = ad * xVertex * xVertex + 2.0 * bd * xVertex + cd;
        return CrossesZero(qLeft, qVertex) || CrossesZero(qVertex, qRight);
    }

    /// <summary>
    /// Computes, for the current polynomial, every prime's first sieve-hit position in
    /// <c>[0, fullBlockLength)</c> for both roots (<paramref name="pos1"/>/<paramref name="pos2"/>)
    /// and the matching root residues (<paramref name="root1Res"/>/<paramref name="root2Res"/>).
    /// Gray-code neighbours in an A-family update the saved residues by one precomputed delta
    /// instead of a full <see cref="BigInteger"/> mod per prime.
    /// </summary>
    private static void ComputeRootPositions(
        FactorBaseData fb, PolynomialCandidate poly, bool[] isAPrime, long[] invA,
        PolynomialFamilyMember familyMember, int[][]? grayRootDeltas, long m, int fullBlockLength,
        int[] pos1, int[] pos2, int[] root1Res, int[] root2Res)
    {
        var flipIndex = familyMember.FlipIndex.GetValueOrDefault();
        var canIncrementRoots = familyMember.FlipIndex.HasValue
            && grayRootDeltas is not null
            && root1Res.Length >= fb.Count;

        for (var i = 0; i < fb.Count; i++)
        {
            var p = fb.Primes[i]; // long

            if (isAPrime[i])
            {
                if (p == 2)
                {
                    // A is built from odd factor-base primes, but keep this defensive path
                    // as a sentinel because 2B is not invertible modulo 2.
                    pos1[i] = fullBlockLength;
                    pos2[i] = fullBlockLength;
                    root1Res[i] = fullBlockLength;
                    root2Res[i] = -1;
                    continue;
                }

                // If q | A, then V(x) = A*x^2 + 2B*x + C has the single root
                // x == -C * (2B)^-1 (mod q). Sieve it so the candidate receives
                // log credit and trial division can remove any q-adic V(x) factor.
                var bModP = (long)IntegerMath.Mod(poly.B, p);
                var cModP = (long)IntegerMath.Mod(poly.C, p);
                var invTwoB = (long)IntegerMath.ModInverse(IntegerMath.Mod(2L * bModP, p), p);
                var x = (int)Mod(-cModP * invTwoB, p);
                var first = (int)((x + m) % p);
                pos1[i] = first;
                pos2[i] = fullBlockLength;
                root1Res[i] = first;
                root2Res[i] = -1;
                continue;
            }

            if (p == 2)
            {
                // Only one root for p = 2; derive it from poly.C parity.
                var root = (int)(long)IntegerMath.Mod(poly.C, 2L);
                pos1[i] = (int)((root + m) % 2);
                pos2[i] = fullBlockLength; // no second root
                root1Res[i] = pos1[i];
                root2Res[i] = -1;
                continue;
            }

            int first1;
            int first2;
            if (canIncrementRoots && root2Res[i] >= 0)
            {
                var delta = grayRootDeltas![flipIndex][i];
                var adjustment = familyMember.FlipDirection > 0 ? -delta : delta;
                adjustment -= familyMember.NormalizationCorrection;
                first1 = (int)Mod(root1Res[i] + adjustment, p);
                first2 = (int)Mod(root2Res[i] + adjustment, p);
            }
            else
            {
                var bModP = (long)IntegerMath.Mod(poly.B, p);
                var x1 = (int)Mod((fb.Root1[i] - bModP) * invA[i], p);
                var x2 = (int)Mod((fb.Root2[i] - bModP) * invA[i], p);
                first1 = (int)((x1 + m) % p);
                first2 = (int)((x2 + m) % p);
            }

            // first1/first2 are sieve-index residues: (x + m) mod p.  The
            // direct branch shifts from polynomial-x space; the incremental
            // branch already updates the saved shifted residues.
            pos1[i] = first1;
            pos2[i] = (first1 != first2) ? first2 : fullBlockLength;
            root1Res[i] = pos1[i];
            root2Res[i] = (pos2[i] == fullBlockLength) ? -1 : pos2[i];
        }
    }

    /// <summary>
    /// Scatters every band prime's two-root hits across <c>[0, fullBlockLength)</c> into the
    /// per-block <paramref name="buckets"/>, so each block later replays only the hits that land
    /// within it. Runs once per polynomial.
    /// </summary>
    private static void ScatterBandPrimeHits(
        FactorBaseData fb, int bucketStart, int fullBlockLength, int blockSize,
        byte[] byteLogP, int[] pos1, int[] pos2, LargePrimeBuckets buckets)
    {
        // Block sizes are powers of two in every tuned profile; split bucket/offset
        // with shift/mask there and fall back to division otherwise.
        var blockShift = BitOperations.IsPow2(blockSize) ? BitOperations.TrailingZeroCount(blockSize) : -1;
        var blockMask = blockSize - 1;

        for (var i = bucketStart; i < fb.Count; i++)
        {
            var p = (int)fb.Primes[i];
            var logp = byteLogP[i];

            var j1 = pos1[i];
            while (j1 < fullBlockLength)
            {
                int bucket, offset;
                if (blockShift >= 0) { bucket = j1 >> blockShift; offset = j1 & blockMask; }
                else { bucket = j1 / blockSize; offset = j1 - bucket * blockSize; }
                buckets.Add(new(bucket), new(offset), new(i), new(logp));
                j1 += p;
            }

            var j2 = pos2[i];
            while (j2 < fullBlockLength)
            {
                int bucket, offset;
                if (blockShift >= 0) { bucket = j2 >> blockShift; offset = j2 & blockMask; }
                else { bucket = j2 / blockSize; offset = j2 - bucket * blockSize; }
                buckets.Add(new(bucket), new(offset), new(i), new(logp));
                j2 += p;
            }
        }
    }

    /// <summary>
    /// For this block's candidates, collects known prime hits from the resieve band and/or the
    /// large-prime buckets, so trial division can skip re-deriving them. Returns <c>null</c> when
    /// neither source applies (no candidates, or both bands are disabled for this polynomial).
    /// </summary>
    private List<int>[]? CollectKnownPrimeHits(
        FactorBaseData fb, bool[] isAPrime, int[] pos1, int[] pos2, int[] root2Res,
        LargePrimeBuckets? largePrimeBuckets, int blockStart, int blockSize,
        int resieveStart, int bucketStart)
    {
        if (_blockCandidates.Count == 0 || (resieveStart >= bucketStart && largePrimeBuckets is null))
        {
            return null;
        }

        var knownPrimeHits = _candidates.PrepareCandidatePrimeHits(_blockCandidates.Count);
        // Candidate offsets are ascending because the scan walks the block in order.
        var offsets = _candidates.PrepareCandidateOffsets(_blockCandidates);
        if (resieveStart < bucketStart)
        {
            CandidatePrimeResieve.Collect(fb, blockStart, resieveStart, bucketStart,
                isAPrime, pos1, pos2, root2Res, offsets, knownPrimeHits);
        }

        if (largePrimeBuckets is not null)
        {
            largePrimeBuckets.CollectCandidateHits(
                new BucketIndex(blockStart / blockSize), offsets, knownPrimeHits);
        }

        return knownPrimeHits;
    }
}

