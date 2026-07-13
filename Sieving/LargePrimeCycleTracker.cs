using System.Numerics;

namespace Sieving;

internal sealed class LargePrimeCycleTracker
{
    private const ulong SplitMix64Increment = 0x9E3779B97F4A7C15UL;

    private readonly Dictionary<ulong, ulong> _parents = [];
    private readonly Dictionary<ulong, ulong> _relativeFingerprints = [];
    private long _cycles;

    public long Cycles => _cycles;

    public LargePrimeCycleAddResult Add(IReadOnlyList<BigInteger> largePrimes, IReadOnlyList<int> parityColumns)
    {
        if (largePrimes.Count is < 1 or > 2)
        {
            return default;
        }

        var u = ToUInt64Vertex(largePrimes[0]);
        var v = largePrimes.Count == 1 ? 0UL : ToUInt64Vertex(largePrimes[1]);
        var edgeFingerprint = Fingerprint(parityColumns);
        Ensure(u);
        Ensure(v);

        var left = Find(u);
        var right = Find(v);
        if (left.Root == right.Root)
        {
            _cycles++;
            var cycleFingerprint = left.PathFingerprint ^ right.PathFingerprint ^ edgeFingerprint;
            return new LargePrimeCycleAddResult(ClosedCycle: true, CombinedParityIsZero: cycleFingerprint == 0);
        }

        _parents[right.Root] = left.Root;
        _relativeFingerprints[right.Root] = left.PathFingerprint ^ edgeFingerprint ^ right.PathFingerprint;
        return default;
    }

    private static ulong ToUInt64Vertex(BigInteger value)
    {
        if (value.Sign < 0 || value > ulong.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Large-prime graph vertices must fit in UInt64.");
        }

        return (ulong)value;
    }

    private void Ensure(ulong vertex)
    {
        if (!_parents.ContainsKey(vertex))
        {
            _parents[vertex] = vertex;
            _relativeFingerprints[vertex] = 0;
        }
    }

    private (ulong Root, ulong PathFingerprint) Find(ulong vertex)
    {
        // Iterative two-pass find with full path compression: recursion can overflow the stack on
        // the multi-million-vertex chains large runs produce. The second pass must rewrite the
        // root-relative fingerprint together with the parent pointer.
        var root = vertex;
        var pathFingerprint = 0UL;
        while (_parents[root] != root)
        {
            pathFingerprint ^= _relativeFingerprints[root];
            root = _parents[root];
        }

        var remainingFingerprint = pathFingerprint;
        while (_parents[vertex] != root)
        {
            var next = _parents[vertex];
            var oldRelativeFingerprint = _relativeFingerprints[vertex];
            _parents[vertex] = root;
            _relativeFingerprints[vertex] = remainingFingerprint;
            remainingFingerprint ^= oldRelativeFingerprint;
            vertex = next;
        }

        return (root, pathFingerprint);
    }

    private static ulong Fingerprint(IReadOnlyList<int> parityColumns)
    {
        var fingerprint = 0UL;
        foreach (var column in parityColumns)
        {
            if (column < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(parityColumns), column, "Parity columns must be non-negative.");
            }

            fingerprint ^= SplitMix64((ulong)column);
        }

        return fingerprint;
    }

    private static ulong SplitMix64(ulong value)
    {
        unchecked
        {
            var z = value + SplitMix64Increment;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}

internal readonly record struct LargePrimeCycleAddResult(bool ClosedCycle, bool CombinedParityIsZero);
