using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Sieving;

internal readonly record struct BucketIndex(int Value);
internal readonly record struct SieveOffset(int Value);
internal readonly record struct FactorBasePrimeIndex(int Value);
internal readonly record struct SieveLogCredit(byte Value);
internal readonly record struct LargePrimeBucketCount(int Value);
internal readonly record struct LargePrimeBucketCapacity(int Value);
internal readonly record struct SieveBlockSize(int Value);
internal readonly record struct BucketSlabSize(long Bytes);
internal readonly record struct CandidateMajorBucketStats(
    long VectorGroups, long MatchingMasks, long DecodedPrimeHits);

/// <summary>The packed worker-local layout for large-prime sieve hits.</summary>
internal readonly record struct LargePrimeBucketLayout(
    LargePrimeBucketCount BucketCount,
    LargePrimeBucketCapacity Capacity,
    SieveBlockSize BlockSize)
{
    public BucketSlabSize SlabSize => new((long)BucketCount.Value * Capacity.Value * sizeof(uint) * 2);
}

/// <summary>A spill hit that did not fit in the fixed-capacity packed bucket slab.</summary>
internal readonly record struct OverflowBucketHit(SieveOffset Offset, FactorBasePrimeIndex PrimeIndex, SieveLogCredit LogCredit);

/// <summary>Worker-local large-prime buckets with encapsulated packed storage.</summary>
internal sealed class LargePrimeBuckets
{
    private readonly bool _assertOnOverflow;
    private readonly int[] _counts;
    private readonly uint[] _packedHits;
    private readonly int[] _primeIndexes;
    private List<OverflowBucketHit>[]? _overflow;

    public LargePrimeBuckets(LargePrimeBucketLayout layout, bool assertOnOverflow = true)
    {
        Layout = layout;
        _assertOnOverflow = assertOnOverflow;
        _counts = new int[layout.BucketCount.Value];
        _packedHits = new uint[checked(layout.BucketCount.Value * layout.Capacity.Value)];
        _primeIndexes = new int[_packedHits.Length];
    }

    public LargePrimeBucketLayout Layout { get; }
    public long OverflowHitCount { get; private set; }
    public int MaximumHitCount
    {
        get
        {
            var maximum = 0;
            for (var bucket = 0; bucket < _counts.Length; bucket++)
            {
                var overflow = _overflow?[bucket]?.Count ?? 0;
                maximum = Math.Max(maximum, _counts[bucket] + overflow);
            }
            return maximum;
        }
    }
    public BucketSlabSize SlabSize => Layout.SlabSize;

    public void Clear()
    {
        Array.Clear(_counts, 0, Layout.BucketCount.Value);
        OverflowHitCount = 0;
        foreach (var spill in _overflow ?? []) spill?.Clear();
    }

    public void Add(BucketIndex bucket, SieveOffset offset, FactorBasePrimeIndex primeIndex, SieveLogCredit logCredit)
    {
        var count = _counts[bucket.Value];
        if (count < Layout.Capacity.Value)
        {
            var index = bucket.Value * Layout.Capacity.Value + count;
            _packedHits[index] = ((uint)offset.Value << 8) | logCredit.Value;
            _primeIndexes[index] = primeIndex.Value;
            _counts[bucket.Value] = count + 1;
            return;
        }

        (_overflow ??= new List<OverflowBucketHit>[Layout.BucketCount.Value])[bucket.Value] ??= [];
        _overflow[bucket.Value]!.Add(new(offset, primeIndex, logCredit));
        OverflowHitCount++;
        if (_assertOnOverflow) Debug.Assert(false, "Large-prime bucket overflow; increase bucket capacity margin.");
    }

    public void PrepareBlock(BucketIndex bucket, ref byte sieve0)
    {
        var baseIndex = bucket.Value * Layout.Capacity.Value;
        for (var local = 0; local < _counts[bucket.Value]; local++)
        {
            var packed = _packedHits[baseIndex + local];
            Unsafe.Add(ref sieve0, (int)(packed >> 8)) += (byte)packed;
        }

        if (_overflow?[bucket.Value] is { } overflowHits)
        {
            foreach (var hit in overflowHits) Unsafe.Add(ref sieve0, hit.Offset.Value) += hit.LogCredit.Value;
        }
    }

    public int CollectCandidateHits(
        BucketIndex bucket, ReadOnlySpan<int> candidateOffsets, List<int>[] primeHits)
    {
        if (candidateOffsets.IsEmpty) return 0;
        var decodedPrimeHits = 0;
        var bloom = 0UL;
        foreach (var offset in candidateOffsets) bloom |= 1UL << (offset & 63);
        var baseIndex = bucket.Value * Layout.Capacity.Value;
        for (var local = 0; local < _counts[bucket.Value]; local++)
        {
            var offset = (int)(_packedHits[baseIndex + local] >> 8);
            var match = (bloom & (1UL << (offset & 63))) == 0 ? -1 : candidateOffsets.BinarySearch(offset);
            if (match >= 0)
            {
                primeHits[match].Add(_primeIndexes[baseIndex + local]);
                decodedPrimeHits++;
            }
        }

        if (_overflow?[bucket.Value] is { } overflowHits)
        {
            foreach (var hit in overflowHits)
            {
                var match = candidateOffsets.BinarySearch(hit.Offset.Value);
                if (match >= 0)
                {
                    primeHits[match].Add(hit.PrimeIndex.Value);
                    decodedPrimeHits++;
                }
            }
        }
        return decodedPrimeHits;
    }

    public int CollectCandidateHitsWithOffsetMap(
        BucketIndex bucket, ReadOnlySpan<int> offsetToCandidate, List<int>[] primeHits)
    {
        var decodedPrimeHits = 0;
        var baseIndex = bucket.Value * Layout.Capacity.Value;
        for (var local = 0; local < _counts[bucket.Value]; local++)
        {
            var offset = (int)(_packedHits[baseIndex + local] >> 8);
            var candidateIndex = offsetToCandidate[offset];
            if (candidateIndex >= 0)
            {
                primeHits[candidateIndex].Add(_primeIndexes[baseIndex + local]);
                decodedPrimeHits++;
            }
        }

        if (_overflow?[bucket.Value] is { } overflowHits)
            foreach (var hit in overflowHits)
            {
                var candidateIndex = offsetToCandidate[hit.Offset.Value];
                if (candidateIndex >= 0)
                {
                    primeHits[candidateIndex].Add(hit.PrimeIndex.Value);
                    decodedPrimeHits++;
                }
            }
        return decodedPrimeHits;
    }

    public CandidateMajorBucketStats CollectCandidateHitsCandidateMajorVector(
        BucketIndex bucket, ReadOnlySpan<int> candidateOffsets, List<int>[] primeHits)
    {
        if (candidateOffsets.IsEmpty) return default;
        var baseIndex = bucket.Value * Layout.Capacity.Value;
        var count = _counts[bucket.Value];
        var vectorEnd = count / 8 * 8;
        long vectorGroups = 0;
        long matchingMasks = 0;
        long decodedPrimeHits = 0;
        ref var packed0 = ref MemoryMarshal.GetArrayDataReference(_packedHits);

        for (var candidateIndex = 0; candidateIndex < candidateOffsets.Length; candidateIndex++)
        {
            var local = 0;
            if (Avx2.IsSupported)
            {
                var candidate = Vector256.Create(candidateOffsets[candidateIndex]);
                for (; local < vectorEnd; local += 8)
                {
                    vectorGroups++;
                    var packed = Vector256.LoadUnsafe(
                        ref Unsafe.Add(ref packed0, baseIndex + local));
                    var offsets = Avx2.ShiftRightLogical(packed, 8).AsInt32();
                    var mask = Avx.MoveMask(Avx2.CompareEqual(offsets, candidate).AsSingle());
                    if (mask != 0) matchingMasks++;
                    while (mask != 0)
                    {
                        var lane = System.Numerics.BitOperations.TrailingZeroCount((uint)mask);
                        primeHits[candidateIndex].Add(_primeIndexes[baseIndex + local + lane]);
                        decodedPrimeHits++;
                        mask &= mask - 1;
                    }
                }
            }

            for (; local < count; local++)
                if ((int)(_packedHits[baseIndex + local] >> 8) == candidateOffsets[candidateIndex])
                {
                    primeHits[candidateIndex].Add(_primeIndexes[baseIndex + local]);
                    decodedPrimeHits++;
                }

            if (_overflow?[bucket.Value] is { } overflowHits)
                foreach (var hit in overflowHits)
                    if (hit.Offset.Value == candidateOffsets[candidateIndex])
                    {
                        primeHits[candidateIndex].Add(hit.PrimeIndex.Value);
                        decodedPrimeHits++;
                    }
        }

        return new(vectorGroups, matchingMasks, decodedPrimeHits);
    }

    public IReadOnlyList<OverflowBucketHit>? OverflowAt(BucketIndex bucket) => _overflow?[bucket.Value];

    public int HitCount(BucketIndex bucket)
        => _counts[bucket.Value] + (_overflow?[bucket.Value]?.Count ?? 0);
}
