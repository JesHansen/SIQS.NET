using System.Numerics;
using Filtering;
using SIQS.Contracts;
using SIQS.Contracts.Files;

namespace Filtering.Tests;

public class FilteringEngineTests
{
    private static FactorBaseDocument FactorBase(int factorBaseCount)
    {
        var entries = new List<FactorBaseEntry>();
        for (var i = 0; i < factorBaseCount; i++)
        {
            entries.Add(new FactorBaseEntry(i + 1, i + 2, 0, 0, 1));
        }

        // N large enough that the TFromId t values never collide modulo N (duplicate detection
        // canonicalizes t as min(t, N - t)).
        return new FactorBaseDocument(new FactorBaseMetadata(1000003, 1, 1000003, 50, 10.0), entries);
    }

    // Distinct relations must carry distinct t values: filtering identifies duplicates by the
    // congruence (t, exponents, large primes), so a shared constant t would collapse fixtures.
    private static BigInteger TFromId(string id) => 3 + 2 * int.Parse(id.TrimStart('R', 'F'));

    private static RawRelationRecord Full(string id, params int[] parityColumns)
    {
        var exponents = parityColumns.ToDictionary(c => c, _ => 1);
        var sign = exponents.GetValueOrDefault(0) % 2 != 0 ? -1 : 1;
        return new RawRelationRecord(id, RelationKind.Full, "P00000000", 1, 0, -1000, 1, TFromId(id), sign,
            exponents, parityColumns.OrderBy(c => c).ToArray(), null);
    }

    private static RawRelationRecord Partial(string id, long largePrime, Dictionary<int, int> exponents)
    {
        var parity = exponents.Where(kv => (kv.Value & 1) == 1).Select(kv => kv.Key).OrderBy(c => c).ToArray();
        var sign = exponents.GetValueOrDefault(0) % 2 != 0 ? -1 : 1;
        return new RawRelationRecord(id, RelationKind.Partial, "P00000000", 1, 0, -1000, 1, TFromId(id), sign,
            exponents, parity, largePrime);
    }

    private static RawRelationRecord Partial2(string id, long left, long right, Dictionary<int, int> exponents)
    {
        var parity = exponents.Where(kv => (kv.Value & 1) == 1).Select(kv => kv.Key).OrderBy(c => c).ToArray();
        var sign = exponents.GetValueOrDefault(0) % 2 != 0 ? -1 : 1;
        return new RawRelationRecord(id, RelationKind.Partial, "P00000000", 1, 0, -1000, 1, TFromId(id), sign,
            exponents, parity, null)
        {
            LargePrimes = new BigInteger[] { left, right },
        };
    }

    private static FilteringResult Run(FactorBaseDocument fb, RawRelationRecord[] full, RawRelationRecord[] partials)
        => FilteringEngine.Run(fb, full, partials, new FilteringOptions(EnableTwoMerge: false));

    [Fact]
    public void Combines_matching_partials_and_squares_away_large_prime()
    {
        var fb = FactorBase(5);
        // R1: q=101, exponents make parity {1,3}; R2: q=101, parity {3,4}. Combined parity {1,4}.
        var r1 = Partial("R00000000", 101, new Dictionary<int, int> { [1] = 1, [3] = 1 });
        var r2 = Partial("R00000001", 101, new Dictionary<int, int> { [3] = 1, [4] = 1 });
        // A companion full relation sharing columns 1 and 4 keeps the combined relation from being
        // singleton-pruned, so we can inspect it.
        var companion = Full("R00000002", 1, 4);

        var result = Run(fb, new[] { companion }, new[] { r1, r2 });

        var combined = result.Relations.Relations.Single(r => r.Kind == RelationKind.CombinedPartial);
        Assert.Equal(new[] { 1, 4 }, combined.ParityColumns);
        Assert.Equal(new BigInteger(101), combined.LargePrime);
        Assert.Equal(new[] { "R00000000", "R00000001" }, combined.SourceRelationIds);
        // q must NOT become a matrix column.
        Assert.All(result.Matrix, row => Assert.DoesNotContain(101, row.Columns));
    }

    [Fact]
    public void Discards_unpaired_partial()
    {
        var fb = FactorBase(5);
        var r1 = Partial("R00000000", 101, new Dictionary<int, int> { [1] = 1, [3] = 1 });
        var result = Run(fb, Array.Empty<RawRelationRecord>(), new[] { r1 });
        Assert.Empty(result.Relations.Relations);
    }

    [Fact]
    public void Accepts_single_pass_relation_enumerables()
    {
        var fb = FactorBase(5);
        var fulls = SinglePass(new[] { Full("R00000002", 1, 4) });
        var partials = SinglePass(new[]
        {
            Partial("R00000000", 101, new Dictionary<int, int> { [1] = 1, [3] = 1 }),
            Partial("R00000001", 101, new Dictionary<int, int> { [3] = 1, [4] = 1 }),
        });

        var result = FilteringEngine.Run(fb, fulls, partials);

        Assert.Contains(result.Relations.Relations, r => r.Kind == RelationKind.CombinedPartial);
        Assert.Equal(1, result.Counters.RawFull);
        Assert.Equal(2, result.Counters.RawPartials);
    }

    [Fact]
    public void Combines_two_large_prime_cycle()
    {
        var fb = FactorBase(5);
        var r1 = Partial2("R00000000", 101, 103, new Dictionary<int, int> { [1] = 1 });
        var r2 = Partial2("R00000001", 103, 107, new Dictionary<int, int> { [2] = 1 });
        var r3 = Partial2("R00000002", 107, 101, new Dictionary<int, int> { [1] = 1, [2] = 1 });

        var result = Run(fb, new[] { Full("R00000003") }, new[] { r1, r2, r3 });

        var combined = result.Relations.Relations.Single(r => r.Kind == RelationKind.CombinedPartial);
        Assert.Equal(new[] { "R00000000", "R00000001", "R00000002" }, combined.SourceRelationIds);
        Assert.Empty(combined.ParityColumns);
        Assert.Equal(new BigInteger[] { 101, 103, 107 }, combined.LargePrimes.OrderBy(x => x));
    }

    [Fact]
    public void Acyclic_fringe_does_not_prevent_parallel_cycle_extraction()
    {
        var fb = FactorBase(5);
        var partials = new[]
        {
            Partial2("R00000000", 101, 103, new Dictionary<int, int> { [1] = 1 }),
            Partial2("R00000001", 103, 107, new Dictionary<int, int> { [1] = 1 }),
            Partial2("R00000002", 109, 113, new Dictionary<int, int> { [2] = 1 }),
            Partial2("R00000003", 109, 113, new Dictionary<int, int> { [2] = 1 }),
        };

        var result = Run(fb, Array.Empty<RawRelationRecord>(), partials);

        var combined = Assert.Single(result.Relations.Relations);
        Assert.Equal(new[] { "R00000002", "R00000003" }, combined.SourceRelationIds);
    }

    [Fact]
    public void Distinct_one_large_prime_edges_do_not_form_a_cycle_through_vertex_zero()
    {
        var fb = FactorBase(5);
        var partials = new[]
        {
            Partial("R00000000", 101, new Dictionary<int, int> { [1] = 1 }),
            Partial("R00000001", 103, new Dictionary<int, int> { [2] = 1 }),
            Partial("R00000002", 107, new Dictionary<int, int> { [3] = 1 }),
        };

        var result = Run(fb, Array.Empty<RawRelationRecord>(), partials);

        Assert.Empty(result.Relations.Relations);
    }

    [Fact]
    public void Cycle_length_cap_rejects_long_cycle_and_keeps_short_cycle()
    {
        var fb = FactorBase(5);
        var partials = new[]
        {
            Partial2("R00000000", 101, 103, new Dictionary<int, int> { [1] = 1 }),
            Partial2("R00000001", 103, 107, new Dictionary<int, int> { [1] = 1 }),
            Partial2("R00000002", 101, 107, new Dictionary<int, int> { [1] = 1 }),
            Partial2("R00000003", 109, 113, new Dictionary<int, int> { [2] = 1 }),
            Partial2("R00000004", 109, 113, new Dictionary<int, int> { [2] = 1 }),
        };

        var result = FilteringEngine.Run(
            fb,
            Array.Empty<RawRelationRecord>(),
            partials,
            new FilteringOptions(MaxCycleLength: 2));

        Assert.Equal(1, result.Counters.RejectedCycles);
        Assert.Equal(1, result.Counters.CombinedPartials);
        Assert.Equal(2, result.Counters.MaxCycleLength);
        var combined = Assert.Single(result.Relations.Relations);
        Assert.Equal(new[] { "R00000003", "R00000004" }, combined.SourceRelationIds);
    }

    [Fact]
    public void Validates_one_large_prime_against_lp1_bound_when_lp2_bound_is_lower()
    {
        var fb = FactorBase(5);
        var r1 = Partial("R00000000", 101, new Dictionary<int, int> { [1] = 1 });
        var r2 = Partial("R00000001", 101, new Dictionary<int, int> { [1] = 1 });

        var result = FilteringEngine.Run(
            fb,
            new[] { Full("R00000002") },
            new[] { r1, r2 },
            new FilteringOptions(LargePrimeBound: 200, LargePrime2Bound: 50));

        Assert.Contains(result.Relations.Relations, r => r.Kind == RelationKind.CombinedPartial);
    }

    [Fact]
    public void Validates_two_large_prime_records_against_lp2_bound()
    {
        var fb = FactorBase(5);
        var r1 = Partial2("R00000000", 101, 103, new Dictionary<int, int> { [1] = 1 });

        Assert.Throws<FormatException>(() =>
            FilteringEngine.Run(
                fb,
                Array.Empty<RawRelationRecord>(),
                new[] { r1 },
                new FilteringOptions(LargePrimeBound: 200, LargePrime2Bound: 50)));
    }

    [Fact]
    public void Cascading_singleton_pruning_removes_all()
    {
        var fb = FactorBase(5);
        var rows = new[] { Full("R00000000", 1, 2), Full("R00000001", 2, 3), Full("R00000002", 3, 4) };
        var result = Run(fb, rows, Array.Empty<RawRelationRecord>());
        Assert.Empty(result.Relations.Relations);
    }

    [Fact]
    public void Cycle_is_fully_retained()
    {
        var fb = FactorBase(5);
        var rows = new[] { Full("R00000000", 1, 2), Full("R00000001", 2, 3), Full("R00000002", 1, 3) };
        var result = Run(fb, rows, Array.Empty<RawRelationRecord>());
        Assert.Equal(3, result.Relations.Relations.Count);
    }

    [Fact]
    public void Empty_parity_row_is_kept_singleton_is_pruned()
    {
        var fb = FactorBase(5);
        var rows = new[] { Full("R00000000"), Full("R00000001", 1) };
        var result = Run(fb, rows, Array.Empty<RawRelationRecord>());

        var kept = Assert.Single(result.Relations.Relations);
        Assert.Equal(new[] { "R00000000" }, kept.SourceRelationIds);
        Assert.Empty(kept.ParityColumns);
    }

    [Fact]
    public void Counts_zero_rows_separately_from_effective_matrix_surplus()
    {
        var fb = FactorBase(5);
        var rows = new[] { Full("R00000000"), Full("R00000001", 1, 2), Full("R00000002", 1, 2), Full("R00000003", 2) };

        var result = Run(fb, rows, Array.Empty<RawRelationRecord>());

        Assert.Equal(4, result.Counters.FinalRows);
        Assert.Equal(2, result.Counters.MatrixColumns);
        Assert.Equal(1, result.Counters.ZeroRows);
        Assert.Equal(3, result.Counters.NonZeroRows);
        Assert.Equal(1, result.Counters.NonZeroRowSurplus);
    }

    [Fact]
    public void Merges_columns_with_identical_row_incidence()
    {
        var fb = FactorBase(6);
        // Columns 3 and 4 appear together in exactly the same two rows -> identical constraints.
        // Columns 1 and 2 have distinct incidence and must both survive.
        var rows = new[]
        {
            Full("R00000000", 1, 3, 4),
            Full("R00000001", 2, 3, 4),
            Full("R00000002", 1, 2),
            Full("R00000003", 1, 2),
        };

        var result = Run(fb, rows, Array.Empty<RawRelationRecord>());

        Assert.Equal(1, result.Counters.RedundantColumnsMerged);
        // Active columns 1, 2, 3, 4 minus one merged.
        Assert.Equal(3, result.Counters.MatrixColumns);
        // Every matrix row keeps one entry for the surviving column of the merged pair.
        var rowsById = result.Matrix.ToDictionary(r => r.RelationId, r => r.Columns);
        Assert.Equal(rowsById["F00000000"].Count, rowsById["F00000001"].Count);
        Assert.All(result.Matrix, r => Assert.True(r.Columns.Count >= 2));
    }

    [Fact]
    public void Two_merge_eliminates_weight_two_column_and_preserves_provenance()
    {
        var fb = FactorBase(6);
        var rows = new[]
        {
            Full("R00000000", 1, 2, 3),
            Full("R00000001", 1, 2, 4),
            Full("R00000002", 3, 4),
            Full("R00000003", 3, 4),
        };

        var result = FilteringEngine.Run(
            fb,
            rows,
            Array.Empty<RawRelationRecord>(),
            new FilteringOptions(EnableTwoMerge: true));

        Assert.Equal(1, result.Counters.TwoMerges);
        Assert.Equal(3, result.Meta.RowCount);
        var merged = Assert.Single(result.Relations.Relations,
            relation => relation.SourceRelationIds.Count == 2);
        Assert.Equal(new[] { "R00000000", "R00000001" }, merged.SourceRelationIds);
        Assert.Equal(new[] { 3, 4 }, merged.ParityColumns);
        Assert.Equal(
            SIQS.Contracts.Numerics.IntegerMath.Mod(TFromId("R00000000") * TFromId("R00000001"), fb.Metadata.ScaledN),
            merged.T);
        Assert.Equal(2, merged.Exponents.GetValueOrDefault(1));
        Assert.Equal(2, merged.Exponents.GetValueOrDefault(2));
    }

    [Fact]
    public void Two_merge_preserves_squared_away_large_prime_factors()
    {
        var fb = FactorBase(5);
        var partials = new[]
        {
            Partial("R00000000", 101, new Dictionary<int, int> { [1] = 1 }),
            Partial("R00000001", 101, new Dictionary<int, int> { [2] = 1 }),
            Partial("R00000002", 103, new Dictionary<int, int> { [1] = 1 }),
            Partial("R00000003", 103, new Dictionary<int, int> { [3] = 1 }),
        };
        var fulls = new[]
        {
            Full("R00000004", 2, 3),
            Full("R00000005", 2, 3),
        };

        var result = FilteringEngine.Run(
            fb,
            fulls,
            partials,
            new FilteringOptions(EnableTwoMerge: true));

        Assert.Equal(1, result.Counters.TwoMerges);
        var merged = Assert.Single(result.Relations.Relations,
            relation => relation.SourceRelationIds.Count == 4);
        Assert.Equal(RelationKind.CombinedPartial, merged.Kind);
        Assert.Equal(new BigInteger[] { 101, 103 }, merged.LargePrimes);
        Assert.Equal(new[] { 2, 3 }, merged.ParityColumns);
    }

    [Fact]
    public void Two_merge_can_be_disabled()
    {
        var fb = FactorBase(6);
        var rows = new[]
        {
            Full("R00000000", 1, 2, 3),
            Full("R00000001", 1, 2, 4),
            Full("R00000002", 3, 4),
            Full("R00000003", 3, 4),
        };

        var result = FilteringEngine.Run(
            fb,
            rows,
            Array.Empty<RawRelationRecord>(),
            new FilteringOptions(EnableTwoMerge: false));

        Assert.Equal(0, result.Counters.TwoMerges);
        Assert.Equal(4, result.Meta.RowCount);
    }

    [Fact]
    public void Two_merge_is_enabled_by_default()
    {
        var fb = FactorBase(6);
        var rows = new[]
        {
            Full("R00000000", 1, 2, 3),
            Full("R00000001", 1, 2, 4),
            Full("R00000002", 3, 4),
            Full("R00000003", 3, 4),
        };

        var result = FilteringEngine.Run(fb, rows, Array.Empty<RawRelationRecord>());

        Assert.Equal(1, result.Counters.TwoMerges);
        Assert.Equal(3, result.Meta.RowCount);
    }

    [Fact]
    public void Reports_columns_before_and_after_singleton_pruning()
    {
        var fb = FactorBase(5);
        var rows = new[] { Full("R00000000", 1, 2), Full("R00000001", 1, 2), Full("R00000002", 3) };

        var result = Run(fb, rows, Array.Empty<RawRelationRecord>());

        Assert.Equal(3, result.Counters.RowsBeforePruning);
        Assert.Equal(3, result.Counters.ColumnsBeforePruning);
        Assert.Equal(1, result.Counters.RowsRemoved);
        // Column 3 is singleton-pruned; columns 1 and 2 then have identical incidence and merge.
        Assert.Equal(2, result.Counters.ColumnsRemoved);
        Assert.Equal(2, result.Counters.FinalRows);
        Assert.Equal(1, result.Counters.MatrixColumns);
    }

    [Fact]
    public void Removes_exact_duplicates()
    {
        var fb = FactorBase(5);
        // Two identical full relations (same source, t, exponents, parity). Keep one.
        var a = Full("R00000000", 1, 2);
        var b = Full("R00000000", 1, 2);
        // Add a third distinct relation so the surviving pair is not singleton-pruned.
        var c = Full("R00000001", 1, 2);
        var result = Run(fb, new[] { a, b, c }, Array.Empty<RawRelationRecord>());
        // a and b are exact duplicates -> one removed; a/c share columns 1,2 so neither is a singleton.
        Assert.Equal(2, result.Relations.Relations.Count);
    }

    [Fact]
    public void Matrix_meta_has_expected_dimensions()
    {
        var fb = FactorBase(5);
        var rows = new[] { Full("R00000000", 2, 5), Full("R00000001", 2, 5), Full("R00000002", 2) };
        var result = Run(fb, rows, Array.Empty<RawRelationRecord>());

        Assert.Equal(2, result.Meta.ColumnCount);       // active matrix columns only
        Assert.Equal(5, result.Meta.FactorBaseCount);
        Assert.Equal(-1, result.Meta.SignColumn);
        Assert.Equal(result.Relations.Relations.Count, result.Meta.RowCount);
        Assert.Equal(result.Matrix.Count, result.Meta.RowCount);
        Assert.Equal(new[] { 2, 5 }, result.Relations.Relations[0].ParityColumns);
        Assert.Equal(new[] { 0, 1 }, result.Matrix[0].Columns);
        Assert.Equal(new[] { 0 }, result.Matrix[2].Columns);
    }

    [Fact]
    public void Default_filtering_trims_large_surplus_to_automatic_target()
    {
        var fb = FactorBase(600);
        // Each row omits one column so no two columns have identical incidence.
        var rows = Enumerable.Range(0, 1200)
            .Select(i => Full($"R{i:D8}", Enumerable.Range(1, 600).Where(c => c != 1 + (i % 600)).ToArray()))
            .ToArray();

        var result = FilteringEngine.Run(fb, rows, Array.Empty<RawRelationRecord>());

        Assert.Equal(584, result.Counters.SurplusRowsTrimmed);
        Assert.Equal(616, result.Counters.NonZeroRows);
        Assert.Equal(600, result.Counters.MatrixColumns);
        Assert.Equal(16, result.Counters.TargetNonZeroSurplus);
        Assert.Equal(16, result.Counters.NonZeroRowSurplus);
        Assert.Equal(599, result.Counters.P50RowWeightBeforeTrim);
        Assert.Equal(599, result.Counters.P90RowWeightAfterTrim);
        Assert.Equal(599.0, result.Counters.AverageRowWeightAfterTrim, precision: 3);
    }

    [Fact]
    public void Automatic_trimming_leaves_small_surplus_below_target()
    {
        var fb = FactorBase(5);
        var rows = new[]
        {
            Full("R00000000", 1, 2, 3, 4),
            Full("R00000001", 1, 2, 3, 4),
            Full("R00000002", 1, 2, 3),
            Full("R00000003", 1, 2, 4),
            Full("R00000004", 1, 2),
            Full("R00000005", 1, 2),
            Full("R00000006", 1),
            Full("R00000007", 1),
            Full("R00000008", 1),
        };

        var result = FilteringEngine.Run(fb, rows, Array.Empty<RawRelationRecord>());

        Assert.Equal(0, result.Counters.SurplusRowsTrimmed);
        Assert.Equal(9, result.Counters.NonZeroRows);
        Assert.Equal(4, result.Counters.MatrixColumns);
        Assert.Equal(5, result.Counters.NonZeroRowSurplus);
    }

    [Fact]
    public void Sign_column_is_remapped_when_active()
    {
        var fb = FactorBase(5);
        var rows = new[] { Full("R00000000", 0, 3), Full("R00000001", 0, 3), Full("R00000002", 0) };
        var result = Run(fb, rows, Array.Empty<RawRelationRecord>());

        Assert.Equal(2, result.Meta.ColumnCount);
        Assert.Equal(0, result.Meta.SignColumn);
        Assert.Equal(new[] { 0, 3 }, result.Relations.Relations[0].ParityColumns);
        Assert.Equal(new[] { 0, 1 }, result.Matrix[0].Columns);
        Assert.Equal(new[] { 0 }, result.Matrix[2].Columns);
    }

    [Fact]
    public void Assigns_contiguous_filtered_ids_and_row_ids()
    {
        var fb = FactorBase(5);
        var rows = new[] { Full("R00000000", 1, 2), Full("R00000001", 1, 2) };
        var result = Run(fb, rows, Array.Empty<RawRelationRecord>());

        Assert.Equal("F00000000", result.Relations.Relations[0].RelationId);
        Assert.Equal("F00000001", result.Relations.Relations[1].RelationId);
        Assert.Equal(0, result.Matrix[0].RowId);
        Assert.Equal(1, result.Matrix[1].RowId);
        Assert.Equal("F00000000", result.Matrix[0].RelationId);
    }

    [Fact]
    public void Rejects_parity_that_disagrees_with_exponents()
    {
        var fb = FactorBase(5);
        var bad = new RawRelationRecord("R00000000", RelationKind.Full, "P0", 1, 0, -1, 1, 1, 1,
            new Dictionary<int, int> { [1] = 2 }, new[] { 1 }, null); // exponent even but parity claims col 1
        Assert.Throws<FormatException>(() => Run(fb, new[] { bad }, Array.Empty<RawRelationRecord>()));
    }

    private static IEnumerable<RawRelationRecord> SinglePass(IReadOnlyList<RawRelationRecord> records)
    {
        var consumed = false;
        return Iterator();

        IEnumerable<RawRelationRecord> Iterator()
        {
            if (consumed)
            {
                throw new InvalidOperationException("Sequence was enumerated more than once.");
            }

            consumed = true;
            foreach (var record in records)
            {
                yield return record;
            }
        }
    }
}
