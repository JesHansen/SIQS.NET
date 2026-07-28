using System.Numerics;
using System.Runtime.CompilerServices;
using SIQS.Contracts;
using SIQS.Contracts.Files;

namespace Filtering.Tests;

/// <summary>
/// Covers the multi-pass, low-retention engine restructuring: file-backed sources must be
/// observationally equivalent to the in-memory path, Pass 1 must not retain parsed records,
/// and union-find must survive adversarially deep parent chains.
/// </summary>
public class MultiPassFilteringTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("siqs-filter-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static FactorBaseDocument FactorBase(int factorBaseCount)
    {
        var entries = new List<FactorBaseEntry>();
        for (var i = 0; i < factorBaseCount; i++)
        {
            entries.Add(new FactorBaseEntry(i + 1, i + 2, 0, 0, 1));
        }

        return new FactorBaseDocument(new FactorBaseMetadata(1000003, 1, 1000003, 50, 10.0), entries);
    }

    private static RawRelationsMetadata Meta() => new(
        TargetN: 1000003, Multiplier: 1, ScaledN: 1000003, FactorBaseBound: 50,
        LargePrimeBound: 4096, LargePrime2Bound: 4096);

    private static BigInteger TFromId(string id) => 3 + 2 * int.Parse(id.TrimStart('R', 'F'));

    private static RawRelationRecord Full(string id, params int[] parityColumns)
    {
        var exponents = parityColumns.ToDictionary(c => c, _ => 1);
        var sign = exponents.GetValueOrDefault(0) % 2 != 0 ? -1 : 1;
        return new RawRelationRecord(id, RelationKind.Full, "P00000000", 1, 0, -1000, 1, TFromId(id), sign,
            exponents, parityColumns.OrderBy(c => c).ToArray(), null);
    }

    private static RawRelationRecord Partial(string id, long largePrime, Dictionary<int, int> exponents)
        => Partial(id, new BigInteger[] { largePrime }, exponents);

    private static RawRelationRecord Partial2(string id, long left, long right, Dictionary<int, int> exponents)
        => Partial(id, new BigInteger[] { left, right }, exponents);

    private static RawRelationRecord Partial(string id, BigInteger[] largePrimes, Dictionary<int, int> exponents)
    {
        var parity = exponents.Where(kv => (kv.Value & 1) == 1).Select(kv => kv.Key).OrderBy(c => c).ToArray();
        var sign = exponents.GetValueOrDefault(0) % 2 != 0 ? -1 : 1;
        return new RawRelationRecord(id, RelationKind.Partial, "P00000000", 1, 0, -1000, 1, TFromId(id), sign,
            exponents, parity, null)
        {
            LargePrimes = largePrimes,
        };
    }

    private string WriteFile(string name, string format, IReadOnlyList<RawRelationRecord> records)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, RawRelationsFile.Write(new RawRelationsDocument(format, Meta(), records)));
        return path;
    }

    private static string Serialize(FilteringResult result)
        => FilteredRelationsFile.Write(result.Relations, FileFormats.FilteredRelationsV2)
            + "\n===\n" + FilteredMatrixFile.Write(result.Matrix)
            + "\n===\n" + MatrixMetaFile.Write(result.Meta);

    [Fact]
    public void File_backed_source_is_observationally_equivalent_to_in_memory()
    {
        var fb = FactorBase(8);
        var fulls = new[]
        {
            Full("R00000100", 1, 4),
            Full("R00000101", 2, 5),
            Full("R00000102", 1, 2, 4, 5),
        };
        // A mix that exercises 1LP pairs, a 2LP triangle spanning both files (re-rooting),
        // a duplicate mirror re-find, and an unpaired dangling edge.
        var partialsA = new[]
        {
            Partial("R00000000", 101, new Dictionary<int, int> { [1] = 1, [3] = 1 }),
            Partial2("R00000001", 211, 223, new Dictionary<int, int> { [2] = 1 }),
            Partial("R00000002", 101, new Dictionary<int, int> { [3] = 1, [4] = 1 }),
            Partial2("R00000003", 223, 227, new Dictionary<int, int> { [5] = 1 }),
        };
        var partialsB = new[]
        {
            Partial2("R00000004", 227, 211, new Dictionary<int, int> { [2] = 1, [5] = 1 }),
            // Same congruence as R00000000 under a different id: dropped as a duplicate.
            Partial("R00000005", 101, new Dictionary<int, int> { [1] = 1, [3] = 1 }) with { T = TFromId("R00000000") },
            Partial("R00000006", 1009, new Dictionary<int, int> { [6] = 1 }),
        };

        var inMemory = FilteringEngine.Run(fb, fulls, partialsA.Concat(partialsB));

        var fullsPath = WriteFile("relations_0000.txt", FileFormats.RawRelationsV1, fulls);
        var partialsPathA = WriteFile("partials_0000.txt", FileFormats.RawPartialsV2, partialsA);
        var partialsPathB = WriteFile("partials_0001.txt", FileFormats.RawPartialsV2, partialsB);
        var fileBacked = FilteringEngine.Run(
            fb,
            new RawRelationFileSource(new[] { fullsPath }),
            new RawRelationFileSource(new[] { partialsPathA, partialsPathB }));

        Assert.Equal(1, inMemory.Counters.DuplicatesRemoved);
        Assert.Equal(2, inMemory.Counters.CombinedPartials);
        Assert.Equal(Serialize(inMemory), Serialize(fileBacked));
    }

    [Fact]
    public void Spilling_candidates_to_disk_is_byte_identical_to_the_in_memory_path()
    {
        var fb = FactorBase(8);
        var fulls = new[]
        {
            Full("R00000100", 1, 4),
            Full("R00000101", 2, 5),
            Full("R00000102", 1, 2, 4, 5),
        };
        // The same mix the equivalence test uses: 1LP pairs, a 2LP triangle spanning files, a
        // duplicate mirror re-find, and an unpaired dangling edge — so spill exercises fulls,
        // combined partials, and deduped candidates alike.
        var partialsA = new[]
        {
            Partial("R00000000", 101, new Dictionary<int, int> { [1] = 1, [3] = 1 }),
            Partial2("R00000001", 211, 223, new Dictionary<int, int> { [2] = 1 }),
            Partial("R00000002", 101, new Dictionary<int, int> { [3] = 1, [4] = 1 }),
            Partial2("R00000003", 223, 227, new Dictionary<int, int> { [5] = 1 }),
        };
        var partialsB = new[]
        {
            Partial2("R00000004", 227, 211, new Dictionary<int, int> { [2] = 1, [5] = 1 }),
            Partial("R00000005", 101, new Dictionary<int, int> { [1] = 1, [3] = 1 }) with { T = TFromId("R00000000") },
            Partial("R00000006", 1009, new Dictionary<int, int> { [6] = 1 }),
        };
        var allPartials = partialsA.Concat(partialsB).ToArray();

        var inMemory = FilteringEngine.Run(fb, fulls, allPartials);

        var spillDir = Path.Combine(_dir, "spill");
        var spilled = FilteringEngine.Run(
            fb, fulls, allPartials, new FilteringOptions(SpillDirectory: spillDir));

        Assert.Equal(Serialize(inMemory), Serialize(spilled));
        // The scratch file is opened DeleteOnClose and disposed with the engine, so nothing lingers.
        Assert.False(Directory.Exists(spillDir) && Directory.EnumerateFileSystemEntries(spillDir).Any());
    }

    [Fact]
    public void Engine_retains_no_raw_records_after_run()
    {
        var issued = new List<WeakReference>();
        var result = RunWithTransientRecords(issued);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.NotEmpty(issued);
        Assert.NotEmpty(result.Relations.Relations);
        Assert.All(issued, weak => Assert.False(weak.IsAlive));
    }

    [Fact]
    public void Spill_mode_does_not_retain_candidate_exponent_payloads()
    {
        var spillDir = Path.Combine(_dir, "spill-retain");
        var exponents = new List<WeakReference>();
        var result = RunSpillWithTransientExponents(spillDir, exponents);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // The three rows form a singleton-free triangle, so all survive to output — yet the source's
        // exponent vectors are still collectable, because spill mode streams the payload to disk and
        // rebuilds a fresh vector per survivor rather than keeping the originals resident.
        Assert.Equal(3, exponents.Count);
        Assert.NotEmpty(result.Relations.Relations);
        Assert.All(exponents, weak => Assert.False(weak.IsAlive));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static FilteringResult RunSpillWithTransientExponents(string spillDir, List<WeakReference> exponents)
    {
        RawRelationRecord Fabricate(int i)
        {
            var record = i switch
            {
                0 => Full("R00000000", 1, 2),
                1 => Full("R00000001", 2, 3),
                2 => Full("R00000002", 1, 3),
                _ => throw new ArgumentOutOfRangeException(nameof(i)),
            };
            exponents.Add(new WeakReference(record.FactorExponents));
            return record;
        }

        var fulls = new TransientRawRelationSource(3, Fabricate, new List<WeakReference>());
        var noPartials = new TransientRawRelationSource(
            0, _ => throw new InvalidOperationException(), new List<WeakReference>());
        return FilteringEngine.Run(
            FactorBase(5), fulls, noPartials,
            new FilteringOptions(SpillDirectory: spillDir));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static FilteringResult RunWithTransientRecords(List<WeakReference> issued)
    {
        // Records are created on demand by the source and must not survive the run: the engine
        // may only keep locators (Pass 1) and compact candidate data (Pass 2).
        RawRelationRecord Fabricate(int i) => i switch
        {
            0 => Partial("R00000000", 101, new Dictionary<int, int> { [1] = 1, [3] = 1 }),
            1 => Partial("R00000001", 101, new Dictionary<int, int> { [3] = 1, [4] = 1 }),
            _ => throw new ArgumentOutOfRangeException(nameof(i)),
        };

        var partials = new TransientRawRelationSource(2, Fabricate, issued);
        var fulls = new TransientRawRelationSource(1, _ => Full("R00000002", 1, 4), issued);
        return FilteringEngine.Run(FactorBase(5), fulls, partials);
    }

    private sealed class TransientRawRelationSource : IRawRelationSource
    {
        private readonly int _count;
        private readonly Func<int, RawRelationRecord> _fabricate;
        private readonly List<WeakReference> _issued;

        public TransientRawRelationSource(int count, Func<int, RawRelationRecord> fabricate, List<WeakReference> issued)
        {
            _count = count;
            _fabricate = fabricate;
            _issued = issued;
        }

        public IEnumerable<(RawRelationLocator Locator, RawRelationRecord Record)> Enumerate()
        {
            for (var i = 0; i < _count; i++)
            {
                var record = _fabricate(i);
                _issued.Add(new WeakReference(record));
                yield return (new RawRelationLocator(0, i), record);
            }
        }

        public IEnumerable<(RawRelationLocator Locator, RawRelationRecord Record)> Materialize(
            IReadOnlyList<RawRelationLocator> ascendingLocators)
        {
            foreach (var locator in ascendingLocators)
            {
                var record = _fabricate(locator.Ordinal);
                _issued.Add(new WeakReference(record));
                yield return (locator, record);
            }
        }
    }

    [Fact]
    public void Union_find_survives_deep_parent_chains()
    {
        // 2LP partials chaining q_i -- q_(i+1) build a union-find parent chain of length N without
        // ever compressing it; the final edge forces a find from the bottom of the chain. The
        // recursive find this replaced overflowed the stack here.
        const int chainLength = 200_000;
        var partials = new List<RawRelationRecord>(chainLength + 1);
        for (var i = 0; i < chainLength; i++)
        {
            partials.Add(Partial2($"R{i:D8}", 100 + i, 101 + i, new Dictionary<int, int> { [1] = 1 }));
        }

        partials.Add(Partial2($"R{chainLength:D8}", 100, 1_000_000, new Dictionary<int, int> { [1] = 1 }));

        var result = FilteringEngine.Run(FactorBase(5), Array.Empty<RawRelationRecord>(), partials);

        Assert.Equal(chainLength + 1, result.Counters.RawPartials);
        Assert.Equal(0, result.Counters.CombinedPartials);
        Assert.Empty(result.Relations.Relations);
    }

    [Fact]
    public void Duplicate_id_detector_flags_repeats_only()
    {
        var detector = new DuplicateRelationIdDetector();
        Assert.True(detector.Add("R00000000"));
        Assert.True(detector.Add("R00000001"));
        Assert.False(detector.Add("R00000000"));
    }

    [Fact]
    public void Duplicate_id_detector_drops_identical_rows()
    {
        var detector = new DuplicateRelationIdDetector();
        var record = Full("R00000000", 1, 2);

        Assert.True(detector.ShouldForward(record));
        Assert.False(detector.ShouldForward(record));
        Assert.Equal(1, detector.DuplicateRowsDropped);
    }

    [Fact]
    public void Duplicate_id_detector_rejects_same_id_with_different_content()
    {
        var detector = new DuplicateRelationIdDetector();
        var record = Full("R00000000", 1, 2);
        var changed = record with { T = record.T + 2 };

        Assert.True(detector.ShouldForward(record));
        Assert.Throws<FormatException>(() => detector.ShouldForward(changed));
    }
}
