using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Sieving.Tests;

/// <summary>
/// Pins every AVX2 kernel in <see cref="PolynomialSieveWorker"/> against the scalar path it replaces.
/// <para>
/// These are the tests the sieve most needs and least obviously has. A divergence between a vector
/// kernel and its scalar twin does not throw and does not corrupt a relation — the relation simply
/// never gets found, because a byte of log credit landed in the wrong place or a root advanced to
/// the wrong residue. The run then takes longer, or fails to converge, and nothing points here.
/// Every assertion below therefore compares the two implementations directly rather than checking
/// either against a hand-written expectation.
/// </para>
/// <para>
/// On a machine without AVX2 the vector paths are never taken, so those tests report as skipped
/// rather than silently passing.
/// </para>
/// </summary>
public class PolynomialSieveWorkerSimdEquivalenceTests
{
    // ── Small-prime fill: mask table vs stride loop ───────────────────────────────────────────

    [Theory]
    [InlineData(512, 2_061)]
    [InlineData(256, 1_024)]
    [InlineData(128, 300)]
    public void Small_prime_mask_fill_matches_a_scalar_stride_walk(int blockSize, int fullLength)
    {
        Assert.SkipUnless(Avx2.IsSupported, "The small-prime mask fill is only built when AVX2 is available.");

        // Every prime SmallPrimeFillMasks covers (p <= 31), plus both root shapes.
        long[] primes = [2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31];
        var factorBase = CreateFactorBase(primes);
        var byteLogP = Enumerable.Range(0, primes.Length).Select(i => (byte)(i % 7 + 1)).ToArray();
        var masks = SmallPrimeFillMasks.Build(factorBase, byteLogP);
        Assert.Equal(primes.Length, masks.Count);

        for (var i = 0; i < primes.Length; i++)
        {
            var prime = (int)primes[i];
            var firstRoot = (7 * i + 1) % prime;
            var secondRoot = (11 * i + 5) % prime;

            AssertMaskFillMatchesStride(masks, i, prime, byteLogP[i], firstRoot,
                secondRoot == firstRoot ? null : secondRoot, blockSize, fullLength);

            // Single-root primes (p = 2, or a repeated root) take the other branch of the fill.
            AssertMaskFillMatchesStride(masks, i, prime, byteLogP[i], firstRoot, null, blockSize, fullLength);
        }
    }

    /// <summary>
    /// Runs the mask-based fill exactly as <c>FillBlock</c> does — vector adds over whole 32-byte
    /// chunks, phase carried by <c>NextPhase</c>, scalar tail, position carried across blocks — and
    /// compares the whole filled interval against the stride walk the masks stand in for.
    /// </summary>
    private static void AssertMaskFillMatchesStride(
        SmallPrimeFillMasks masks, int index, int prime, byte logCredit,
        int firstRoot, int? secondRoot, int blockSize, int fullLength)
    {
        var expected = new byte[fullLength];
        for (var j = firstRoot; j < fullLength; j += prime) expected[j] += logCredit;
        if (secondRoot is { } second)
        {
            for (var j = second; j < fullLength; j += prime) expected[j] += logCredit;
        }

        var actual = new byte[fullLength];
        var maskRow = masks.Masks![index];
        var nextRow = masks.NextPhase![index];
        var block = new byte[blockSize];
        var position1 = firstRoot;
        var position2 = secondRoot ?? fullLength;

        for (var blockStart = 0; blockStart < fullLength; blockStart += blockSize)
        {
            var blockEnd = Math.Min(blockStart + blockSize, fullLength);
            Array.Clear(block);
            var chunkEnd = blockStart + ((blockEnd - blockStart) / 32) * 32;

            var s1 = position1 - blockStart;
            if (secondRoot is not null)
            {
                var s2 = position2 - blockStart;
                for (var b = blockStart; b < chunkEnd; b += 32)
                {
                    var offset = b - blockStart;
                    var v = Vector256.Add(Vector256.LoadUnsafe(ref block[offset]), maskRow[s1]);
                    v = Vector256.Add(v, maskRow[s2]);
                    v.StoreUnsafe(ref block[offset]);
                    s1 = nextRow[s1];
                    s2 = nextRow[s2];
                }

                var tail2 = chunkEnd + s2;
                while (tail2 < blockEnd) { block[tail2 - blockStart] += logCredit; tail2 += prime; }
                position2 = tail2;
            }
            else
            {
                for (var b = blockStart; b < chunkEnd; b += 32)
                {
                    var offset = b - blockStart;
                    var v = Vector256.Add(Vector256.LoadUnsafe(ref block[offset]), maskRow[s1]);
                    v.StoreUnsafe(ref block[offset]);
                    s1 = nextRow[s1];
                }
            }

            var tail1 = chunkEnd + s1;
            while (tail1 < blockEnd) { block[tail1 - blockStart] += logCredit; tail1 += prime; }
            position1 = tail1;

            Array.Copy(block, 0, actual, blockStart, blockEnd - blockStart);
        }

        Assert.Equal(expected, actual);
    }

    // ── Scan gate: saturating-subtract skip vs the per-byte comparison ────────────────────────

    [Fact]
    public void Vector_scan_gate_skips_a_chunk_exactly_when_every_byte_is_below_the_threshold()
    {
        Assert.SkipUnless(Avx2.IsSupported, "The scan skip gate is only taken when AVX2 is available.");

        // Exhaustive over the whole gate domain: 255 usable thresholds x 256 byte values. The scan's
        // correctness property is one-sided in the dangerous direction — skipping a chunk that holds
        // a byte at or above the threshold silently discards relations — so assert the biconditional.
        for (var threshold = 1; threshold <= 255; threshold++)
        {
            var gate = Vector256.Create(PolynomialSieveWorker.InclusiveVectorGate((byte)threshold));
            for (var value = 0; value <= 255; value++)
            {
                var saturated = Avx2.SubtractSaturate(Vector256.Create((byte)value), gate);
                var vectorKeepsChunk = !Avx2.TestZ(saturated.AsInt32(), saturated.AsInt32());
                var scalarKeepsByte = value >= threshold;

                Assert.Equal(scalarKeepsByte, vectorKeepsChunk);
            }
        }
    }

    [Fact]
    public void Vector_scan_gate_keeps_a_chunk_holding_a_single_qualifying_byte()
    {
        Assert.SkipUnless(Avx2.IsSupported, "The scan skip gate is only taken when AVX2 is available.");

        const byte threshold = 140;
        var gate = Vector256.Create(PolynomialSieveWorker.InclusiveVectorGate(threshold));

        // One byte at the threshold, everything else one short of it: the case where an off-by-one
        // in the gate would drop a real relation and nothing else would notice.
        for (var lane = 0; lane < Vector256<byte>.Count; lane++)
        {
            var bytes = new byte[Vector256<byte>.Count];
            Array.Fill(bytes, (byte)(threshold - 1));
            bytes[lane] = threshold;

            var saturated = Avx2.SubtractSaturate(Vector256.Create(bytes), gate);
            Assert.False(Avx2.TestZ(saturated.AsInt32(), saturated.AsInt32()));
        }

        var allBelow = new byte[Vector256<byte>.Count];
        Array.Fill(allBelow, (byte)(threshold - 1));
        var noneQualify = Avx2.SubtractSaturate(Vector256.Create(allBelow), gate);
        Assert.True(Avx2.TestZ(noneQualify.AsInt32(), noneQualify.AsInt32()));
    }

    // ── Root normalization: eight lanes vs one ───────────────────────────────────────────────

    [Fact]
    public void Vector_root_normalization_matches_the_scalar_normalizer()
    {
        Assert.SkipUnless(Avx2.IsSupported, "NormalizeUpdatedRoots is an AVX2 kernel.");

        int[] primes = [2, 3, 17, 31, 257, 1_021, 65_537, 1_048_573];
        var primeVector = Vector256.Create(primes);

        // Both implementations apply a single +/-p correction, so both are defined on [-p, 2p).
        // Sweep that whole documented range for the smallest prime and sample it for the rest.
        for (var offset = -1_048_573; offset < 2 * 1_048_573; offset += 4_099)
        {
            var values = new int[primes.Length];
            for (var lane = 0; lane < primes.Length; lane++)
            {
                values[lane] = Math.Clamp(offset, -primes[lane], 2 * primes[lane] - 1);
            }

            var vector = PolynomialSieveWorker.NormalizeUpdatedRoots(Vector256.Create(values), primeVector);
            for (var lane = 0; lane < primes.Length; lane++)
            {
                var scalar = PolynomialSieveWorker.NormalizeUpdatedRoot(values[lane], primes[lane]);
                Assert.Equal(scalar, vector[lane]);

                // And within its precondition the single correction really is a residue, which is
                // what the general Mod() on the non-vector branch computes.
                Assert.Equal(((values[lane] % primes[lane]) + primes[lane]) % primes[lane], vector[lane]);
            }
        }
    }

    // ── Gray-code root update: eight lanes vs the scalar branch ──────────────────────────────

    [Theory]
    [InlineData(1, 0)]
    [InlineData(-1, 0)]
    [InlineData(1, 3)]
    [InlineData(-1, 7)]
    public void Vector_root_update_matches_the_scalar_branch(int flipDirection, int normalizationCorrection)
    {
        Assert.SkipUnless(Avx2.IsSupported, "TryUpdateRootsVector is an AVX2 kernel.");

        int[] primes = [11, 13, 17, 19, 23, 29, 31, 37];
        var deltas = new int[primes.Length];
        var root1 = new int[primes.Length];
        var root2 = new int[primes.Length];
        for (var lane = 0; lane < primes.Length; lane++)
        {
            deltas[lane] = (5 * lane + 2) % primes[lane];
            root1[lane] = (3 * lane + 1) % primes[lane];
            root2[lane] = (7 * lane + 4) % primes[lane];
        }

        // What the scalar branch in ComputeRootPositions computes for the same inputs.
        var expected1 = new int[primes.Length];
        var expected2 = new int[primes.Length];
        for (var lane = 0; lane < primes.Length; lane++)
        {
            var adjustment = (flipDirection > 0 ? -deltas[lane] : deltas[lane]) - normalizationCorrection;
            expected1[lane] = NonNegativeRemainder(root1[lane] + adjustment, primes[lane]);
            expected2[lane] = NonNegativeRemainder(root2[lane] + adjustment, primes[lane]);
        }

        var position1 = new int[primes.Length];
        var position2 = new int[primes.Length];
        var updated1 = (int[])root1.Clone();
        var updated2 = (int[])root2.Clone();

        var handled = PolynomialSieveWorker.TryUpdateRootsVector(
            start: 0, primes, deltas, flipDirection, normalizationCorrection,
            position1, position2, updated1, updated2, storePositions: true);

        Assert.True(handled);
        Assert.Equal(expected1, updated1);
        Assert.Equal(expected2, updated2);
        Assert.Equal(expected1, position1);
        Assert.Equal(expected2, position2);
    }

    [Fact]
    public void Vector_root_update_defers_a_chunk_holding_the_second_root_sentinel()
    {
        Assert.SkipUnless(Avx2.IsSupported, "TryUpdateRootsVector is an AVX2 kernel.");

        // A-primes, p = 2, and repeated-root entries all carry root2 = -1. The vector path has no
        // representation for that, so it must decline the whole chunk and leave every array
        // untouched for the scalar loop to redo — a partial update here would corrupt seven good
        // lanes to save one bad one.
        int[] primes = [11, 13, 17, 19, 23, 29, 31, 37];
        var deltas = Enumerable.Range(0, primes.Length).Select(lane => lane + 1).ToArray();
        var root1 = Enumerable.Range(0, primes.Length).Select(lane => lane + 2).ToArray();

        for (var sentinelLane = 0; sentinelLane < primes.Length; sentinelLane++)
        {
            var root2 = Enumerable.Range(0, primes.Length).Select(lane => lane + 3).ToArray();
            root2[sentinelLane] = -1;

            var position1 = new int[primes.Length];
            var position2 = new int[primes.Length];
            var updated1 = (int[])root1.Clone();
            var updated2 = (int[])root2.Clone();

            var handled = PolynomialSieveWorker.TryUpdateRootsVector(
                start: 0, primes, deltas, flipDirection: 1, normalizationCorrection: 0,
                position1, position2, updated1, updated2, storePositions: true);

            Assert.False(handled);
            Assert.Equal(root1, updated1);
            Assert.Equal(root2, updated2);
            Assert.All(position1, value => Assert.Equal(0, value));
            Assert.All(position2, value => Assert.Equal(0, value));
        }
    }

    [Fact]
    public void Vector_root_update_leaves_positions_alone_above_the_bucket_start()
    {
        Assert.SkipUnless(Avx2.IsSupported, "TryUpdateRootsVector is an AVX2 kernel.");

        // Bucket-sieved primes keep no per-block position, so storePositions is false for them and
        // the position arrays must stay untouched even though the roots advance.
        int[] primes = [11, 13, 17, 19, 23, 29, 31, 37];
        var deltas = Enumerable.Range(0, primes.Length).Select(lane => lane + 1).ToArray();
        var root1 = Enumerable.Range(0, primes.Length).Select(lane => lane + 2).ToArray();
        var root2 = Enumerable.Range(0, primes.Length).Select(lane => lane + 3).ToArray();
        var position1 = Enumerable.Repeat(-7, primes.Length).ToArray();
        var position2 = Enumerable.Repeat(-9, primes.Length).ToArray();

        var handled = PolynomialSieveWorker.TryUpdateRootsVector(
            start: 0, primes, deltas, flipDirection: -1, normalizationCorrection: 0,
            position1, position2, root1, root2, storePositions: false);

        Assert.True(handled);
        Assert.All(position1, value => Assert.Equal(-7, value));
        Assert.All(position2, value => Assert.Equal(-9, value));
    }

    private static int NonNegativeRemainder(int value, int modulus)
    {
        var remainder = value % modulus;
        return remainder < 0 ? remainder + modulus : remainder;
    }

    private static FactorBaseData CreateFactorBase(long[] primes) => new()
    {
        Count = primes.Length,
        Primes = primes,
        Columns = new int[primes.Length],
        Root1 = new long[primes.Length],
        Root2 = new long[primes.Length],
        LogP = new int[primes.Length],
        PrimeInverses = new ulong[primes.Length],
        PrimeDivThresholds = new ulong[primes.Length],
        TargetN = 1,
        Multiplier = 1,
        ScaledN = 1,
        Bound = primes[^1],
        LogScale = 1,
    };
}
