using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Sieving;

internal readonly record struct BucketIndex(int Value);
internal readonly record struct SieveOffset(int Value);
internal readonly record struct FactorBasePrimeIndex(int Value);
internal readonly record struct SieveLogCredit(byte Value);
internal readonly record struct LargePrimeBucketCount(int Value);
internal readonly record struct LargePrimeBucketCapacity(int Value);
internal readonly record struct SieveBlockSize(int Value);
internal readonly record struct BucketSlabSize(long Bytes);

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

    public void CollectCandidateHits(BucketIndex bucket, ReadOnlySpan<int> candidateOffsets, List<int>[] primeHits)
    {
        if (candidateOffsets.IsEmpty) return;
        var bloom = 0UL;
        foreach (var offset in candidateOffsets) bloom |= 1UL << (offset & 63);
        var baseIndex = bucket.Value * Layout.Capacity.Value;
        for (var local = 0; local < _counts[bucket.Value]; local++)
        {
            var offset = (int)(_packedHits[baseIndex + local] >> 8);
            var match = (bloom & (1UL << (offset & 63))) == 0 ? -1 : candidateOffsets.BinarySearch(offset);
            if (match >= 0) primeHits[match].Add(_primeIndexes[baseIndex + local]);
        }

        if (_overflow?[bucket.Value] is { } overflowHits)
        {
            foreach (var hit in overflowHits)
            {
                var match = candidateOffsets.BinarySearch(hit.Offset.Value);
                if (match >= 0) primeHits[match].Add(hit.PrimeIndex.Value);
            }
        }
    }

    public IReadOnlyList<OverflowBucketHit>? OverflowAt(BucketIndex bucket) => _overflow?[bucket.Value];
}
