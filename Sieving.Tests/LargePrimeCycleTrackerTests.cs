using System.Numerics;
using Filtering;
using SIQS.Contracts;
using SIQS.Contracts.Files;

namespace Sieving.Tests;

public class LargePrimeCycleTrackerTests
{
    [Fact]
    public void Counts_one_large_prime_pair_as_cycle()
    {
        var tracker = new LargePrimeCycleTracker();

        Assert.False(tracker.Add(new BigInteger[] { 101 }, Array.Empty<int>()).ClosedCycle);
        var result = tracker.Add(new BigInteger[] { 101 }, Array.Empty<int>());

        Assert.True(result.ClosedCycle);
        Assert.True(result.CombinedParityIsZero);
        Assert.Equal(1, tracker.Cycles);
    }

    [Fact]
    public void Counts_two_large_prime_graph_cycle()
    {
        var tracker = new LargePrimeCycleTracker();

        Assert.False(tracker.Add(new BigInteger[] { 101, 103 }, new[] { 1 }).ClosedCycle);
        Assert.False(tracker.Add(new BigInteger[] { 103, 107 }, new[] { 2 }).ClosedCycle);
        var result = tracker.Add(new BigInteger[] { 107, 101 }, new[] { 1, 2 });

        Assert.True(result.ClosedCycle);
        Assert.True(result.CombinedParityIsZero);
        Assert.Equal(1, tracker.Cycles);
    }

    [Fact]
    public void Nonempty_closing_parity_can_combine_to_zero_parity_cycle()
    {
        var tracker = new LargePrimeCycleTracker();

        Assert.False(tracker.Add(new BigInteger[] { 101 }, new[] { 1, 3 }).ClosedCycle);
        var result = tracker.Add(new BigInteger[] { 101 }, new[] { 1, 3 });

        Assert.True(result.ClosedCycle);
        Assert.True(result.CombinedParityIsZero);
    }

    [Fact]
    public void Empty_closing_parity_can_combine_to_usable_cycle()
    {
        var tracker = new LargePrimeCycleTracker();

        Assert.False(tracker.Add(new BigInteger[] { 101 }, new[] { 1, 3 }).ClosedCycle);
        var result = tracker.Add(new BigInteger[] { 101 }, Array.Empty<int>());

        Assert.True(result.ClosedCycle);
        Assert.False(result.CombinedParityIsZero);
    }

    [Fact]
    public void Sign_column_contributes_to_cycle_fingerprint()
    {
        var tracker = new LargePrimeCycleTracker();

        Assert.False(tracker.Add(new BigInteger[] { 101 }, new[] { 0 }).ClosedCycle);
        var result = tracker.Add(new BigInteger[] { 101 }, Array.Empty<int>());

        Assert.True(result.ClosedCycle);
        Assert.False(result.CombinedParityIsZero);
    }

    [Fact]
    public void Path_compression_preserves_root_relative_fingerprints()
    {
        var tracker = new LargePrimeCycleTracker();

        Assert.False(tracker.Add(new BigInteger[] { 101, 100 }, new[] { 1 }).ClosedCycle);
        Assert.False(tracker.Add(new BigInteger[] { 102, 101 }, new[] { 2 }).ClosedCycle);
        Assert.False(tracker.Add(new BigInteger[] { 103, 102 }, new[] { 3 }).ClosedCycle);

        var firstClosure = tracker.Add(new BigInteger[] { 100, 103 }, new[] { 1, 2, 3 });
        var secondClosure = tracker.Add(new BigInteger[] { 100, 103 }, new[] { 1, 2, 3 });

        Assert.True(firstClosure.ClosedCycle);
        Assert.True(firstClosure.CombinedParityIsZero);
        Assert.True(secondClosure.ClosedCycle);
        Assert.True(secondClosure.CombinedParityIsZero);
    }

    [Fact]
    public void Fingerprinted_tracker_matches_exact_reference_for_random_edges()
    {
        var tracker = new LargePrimeCycleTracker();
        var reference = new ExactReferenceTracker();
        var random = new Random(20260707);

        var currentRoot = 10_000L;
        for (var i = 0; i < 2_048; i++)
        {
            var nextRoot = currentRoot + 1;
            ApplyAndAssert(
                tracker,
                reference,
                new BigInteger[] { nextRoot, currentRoot },
                RandomParity(random));
            currentRoot = nextRoot;
        }

        for (var i = 0; i < 100_000; i++)
        {
            BigInteger[] largePrimes;
            if (i % 257 == 0)
            {
                largePrimes = new BigInteger[] { 10_000, currentRoot };
            }
            else if (random.Next(5) == 0)
            {
                largePrimes = new BigInteger[] { 101 + random.Next(160) };
            }
            else
            {
                var left = 101 + random.Next(160);
                var right = 101 + random.Next(160);
                if (right == left)
                {
                    right = 101 + ((right - 100) % 160);
                }

                largePrimes = new BigInteger[] { left, right };
            }

            ApplyAndAssert(tracker, reference, largePrimes, RandomParity(random));
        }

        Assert.Equal(reference.Cycles, tracker.Cycles);
    }

    [Fact]
    public void Tracker_usable_cycles_match_filtering_nonzero_combined_partials()
    {
        var fulls = new[]
        {
            Full("R00000100", 1, 3),
            Full("R00000101", 6),
        };
        var partials = new[]
        {
            Partial("R00000000", [101], [1, 3]),
            Partial("R00000001", [101], [1, 3]),       // zero combined cycle
            Partial("R00000002", [103], [1, 3]),
            Partial("R00000003", [103], []),           // usable combined cycle
            Partial("R00000004", [211, 223], [2]),
            Partial("R00000005", [223, 227], [5]),
            Partial("R00000006", [227, 211], [2, 5, 6]), // usable 2LP cycle
            Partial("R00000007", [307, 311], [4]),
            Partial("R00000008", [311, 313], [7]),
            Partial("R00000009", [313, 307], [4, 7]), // zero 2LP cycle
        };

        var tracker = new LargePrimeCycleTracker();
        var trackerCycles = 0;
        var trackerUsableCycles = 0;
        foreach (var partial in partials)
        {
            var result = tracker.Add(partial.LargePrimes, partial.ParityColumns);
            if (result.ClosedCycle)
            {
                trackerCycles++;
                if (!result.CombinedParityIsZero)
                {
                    trackerUsableCycles++;
                }
            }
        }

        var filtering = FilteringEngine.Run(FactorBase(8), fulls, partials);
        var filteringUsableCycles = filtering.Relations.Relations.Count(r =>
            r.Kind == RelationKind.CombinedPartial && r.ParityColumns.Count > 0);

        Assert.Equal(4, trackerCycles);
        Assert.Equal(2, trackerUsableCycles);
        Assert.Equal(trackerCycles, filtering.Counters.CombinedPartials);
        Assert.Equal(trackerUsableCycles, filteringUsableCycles);
    }

    [Fact]
    public void Ignores_invalid_large_prime_counts()
    {
        var tracker = new LargePrimeCycleTracker();

        Assert.False(tracker.Add(Array.Empty<BigInteger>(), Array.Empty<int>()).ClosedCycle);
        Assert.False(tracker.Add(new BigInteger[] { 101, 103, 107 }, Array.Empty<int>()).ClosedCycle);
        Assert.Equal(0, tracker.Cycles);
    }

    [Fact]
    public void Rejects_large_prime_vertices_outside_uint64_range()
    {
        var tracker = new LargePrimeCycleTracker();
        var tooLarge = BigInteger.One << 80;

        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.Add(new[] { tooLarge }, Array.Empty<int>()));
    }

    [Fact]
    public void Rejects_negative_parity_columns()
    {
        var tracker = new LargePrimeCycleTracker();

        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.Add(new BigInteger[] { 101 }, new[] { -1 }));
    }

    private static void ApplyAndAssert(
        LargePrimeCycleTracker tracker,
        ExactReferenceTracker reference,
        IReadOnlyList<BigInteger> largePrimes,
        IReadOnlyList<int> parityColumns)
    {
        var actual = tracker.Add(largePrimes, parityColumns);
        var expected = reference.Add(largePrimes, parityColumns);

        Assert.Equal(expected.ClosedCycle, actual.ClosedCycle);
        if (expected.ClosedCycle)
        {
            Assert.Equal(expected.CombinedParityIsZero, actual.CombinedParityIsZero);
        }
    }

    private static int[] RandomParity(Random random)
    {
        var mask = 0UL;
        var count = random.Next(6);
        for (var i = 0; i < count; i++)
        {
            mask ^= 1UL << random.Next(32);
        }

        var columns = new List<int>();
        for (var column = 0; column < 32; column++)
        {
            if ((mask & (1UL << column)) != 0)
            {
                columns.Add(column);
            }
        }

        return columns.ToArray();
    }

    private static FactorBaseDocument FactorBase(int factorBaseCount)
    {
        var entries = new List<FactorBaseEntry>();
        for (var i = 0; i < factorBaseCount; i++)
        {
            entries.Add(new FactorBaseEntry(i + 1, i + 2, 0, 0, 1));
        }

        return new FactorBaseDocument(new FactorBaseMetadata(1_000_003, 1, 1_000_003, 50, 10.0), entries);
    }

    private static RawRelationRecord Full(string id, params int[] parityColumns)
    {
        var exponents = parityColumns.ToDictionary(c => c, _ => 1);
        var sign = exponents.GetValueOrDefault(0) % 2 != 0 ? -1 : 1;
        return new RawRelationRecord(
            id, RelationKind.Full, "P00000000", 1, 0, -1_000, 1, TFromId(id), sign,
            exponents, parityColumns.OrderBy(c => c).ToArray(), null);
    }

    private static RawRelationRecord Partial(string id, BigInteger[] largePrimes, int[] parityColumns)
    {
        var exponents = parityColumns.ToDictionary(c => c, _ => 1);
        var sign = exponents.GetValueOrDefault(0) % 2 != 0 ? -1 : 1;
        return new RawRelationRecord(
            id, RelationKind.Partial, "P00000000", 1, 0, -1_000, 1, TFromId(id), sign,
            exponents, parityColumns.OrderBy(c => c).ToArray(), null)
        {
            LargePrimes = largePrimes,
        };
    }

    private static BigInteger TFromId(string id)
        => 3 + 2 * int.Parse(id.TrimStart('R', 'F'));

    private readonly record struct ExactCycleResult(bool ClosedCycle, bool CombinedParityIsZero);

    private sealed class ExactReferenceTracker
    {
        private readonly Dictionary<ulong, ulong> _parents = [];
        private readonly Dictionary<ulong, ulong> _relativeParity = [];

        public long Cycles { get; private set; }

        public ExactCycleResult Add(IReadOnlyList<BigInteger> largePrimes, IReadOnlyList<int> parityColumns)
        {
            if (largePrimes.Count is < 1 or > 2)
            {
                return default;
            }

            var u = (ulong)largePrimes[0];
            var v = largePrimes.Count == 1 ? 0UL : (ulong)largePrimes[1];
            var edgeParity = Mask(parityColumns);
            Ensure(u);
            Ensure(v);

            var left = Find(u);
            var right = Find(v);
            if (left.Root == right.Root)
            {
                Cycles++;
                return new ExactCycleResult(
                    ClosedCycle: true,
                    CombinedParityIsZero: (left.PathParity ^ right.PathParity ^ edgeParity) == 0);
            }

            _parents[right.Root] = left.Root;
            _relativeParity[right.Root] = left.PathParity ^ edgeParity ^ right.PathParity;
            return default;
        }

        private void Ensure(ulong vertex)
        {
            if (!_parents.ContainsKey(vertex))
            {
                _parents[vertex] = vertex;
                _relativeParity[vertex] = 0;
            }
        }

        private (ulong Root, ulong PathParity) Find(ulong vertex)
        {
            var root = vertex;
            var pathParity = 0UL;
            while (_parents[root] != root)
            {
                pathParity ^= _relativeParity[root];
                root = _parents[root];
            }

            return (root, pathParity);
        }

        private static ulong Mask(IReadOnlyList<int> parityColumns)
        {
            var mask = 0UL;
            foreach (var column in parityColumns)
            {
                if (column is < 0 or >= 64)
                {
                    throw new ArgumentOutOfRangeException(nameof(parityColumns), column, "Test parity columns must be in [0, 63].");
                }

                mask ^= 1UL << column;
            }

            return mask;
        }
    }
}
