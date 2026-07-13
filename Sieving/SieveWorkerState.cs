namespace Sieving;

/// <summary>
/// Per-worker root/position arrays reused across polynomials: for each factor-base prime, the two
/// current sieve-hit positions (<see cref="Pos1"/>/<see cref="Pos2"/>) and the two root residues
/// (<see cref="Root1Res"/>/<see cref="Root2Res"/>). Arrays are grow-only so Gray-code neighbours keep
/// the previous polynomial's saved residues.
/// </summary>
internal sealed class SieveRootState
{
    private int[] _pos1 = [];
    private int[] _pos2 = [];
    private int[] _root1Res = [];
    private int[] _root2Res = [];

    public int[] Pos1 => _pos1;
    public int[] Pos2 => _pos2;
    public int[] Root1Res => _root1Res;
    public int[] Root2Res => _root2Res;

    /// <summary>Grows all four arrays to at least <paramref name="factorBaseCount"/> entries, reusing them otherwise.</summary>
    public void EnsureCapacity(int factorBaseCount)
    {
        if (_pos1.Length < factorBaseCount)
        {
            _pos1 = new int[factorBaseCount];
            _pos2 = new int[factorBaseCount];
        }

        if (_root1Res.Length < factorBaseCount)
        {
            _root1Res = new int[factorBaseCount];
            _root2Res = new int[factorBaseCount];
        }
    }
}

/// <summary>
/// Per-worker scratch for the known-prime-hit collection step: the per-candidate hit lists and the
/// ascending candidate offsets. Both are reused (grown, then cleared) across blocks.
/// </summary>
internal sealed class CandidateHitWorkspace
{
    private List<int>[] _candidatePrimeHits = [];
    private int[] _candidateOffsets = [];

    public List<int>[] PrepareCandidatePrimeHits(int candidateCount)
    {
        if (_candidatePrimeHits.Length < candidateCount)
        {
            Array.Resize(ref _candidatePrimeHits, candidateCount);
        }

        for (var i = 0; i < candidateCount; i++)
        {
            (_candidatePrimeHits[i] ??= []).Clear();
        }

        return _candidatePrimeHits;
    }

    public ReadOnlySpan<int> PrepareCandidateOffsets(List<BlockCandidate> candidates)
    {
        if (_candidateOffsets.Length < candidates.Count)
        {
            Array.Resize(ref _candidateOffsets, candidates.Count);
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            _candidateOffsets[i] = candidates[i].Offset;
        }

        return _candidateOffsets.AsSpan(0, candidates.Count);
    }
}

/// <summary>
/// Per-worker large-prime bucket allocation: the reused <see cref="LargePrimeBuckets"/> instance and
/// the cached per-(band, block-size) capacity estimate. The bucket layout is grown as needed and
/// reused across polynomials.
/// </summary>
internal sealed class LargePrimeBucketWorkspace
{
    private LargePrimeBuckets? _largePrimeBuckets;
    private int _capacityStart = -1;
    private int _capacityBlockSize;
    private int _capacity;

    public LargePrimeBuckets Ensure(int bucketCount, int bucketCapacity, int blockSize)
    {
        if (_largePrimeBuckets is null
            || _largePrimeBuckets.Layout.BucketCount.Value < bucketCount
            || _largePrimeBuckets.Layout.Capacity.Value < bucketCapacity
            || _largePrimeBuckets.Layout.BlockSize.Value < blockSize)
        {
            _largePrimeBuckets = new LargePrimeBuckets(new LargePrimeBucketLayout(new(bucketCount), new(bucketCapacity), new(blockSize)));
        }

        return _largePrimeBuckets;
    }

    public int EstimateCapacity(FactorBaseData fb, int bucketStart, int blockSize)
    {
        if (_capacityStart == bucketStart && _capacityBlockSize == blockSize)
        {
            return _capacity;
        }

        var expectedHits = 0.0;
        for (var i = bucketStart; i < fb.Count; i++)
        {
            expectedHits += 2.0 * blockSize / fb.Primes[i];
        }

        var capacity = (int)Math.Ceiling(expectedHits * 2.0);
        _capacityStart = bucketStart;
        _capacityBlockSize = blockSize;
        _capacity = Math.Max(4096, capacity);
        return _capacity;
    }
}
