using SIQS.Contracts;
using SIQS.Contracts.Files;

namespace Filtering;

/// <summary>
/// Assembles the filtering output from the surviving candidates: the filtered relations document, the
/// compacted sparse GF(2) matrix (redundant columns merged), and the matrix metadata.
/// </summary>
internal static class FilteredResultBuilder
{
    public static FilteringResult Build(
        FactorBaseMetadata meta, int factorBaseCount, List<Candidate> survivors, FilteringCounters counters)
    {
        var columnMap = BuildMatrixColumnMap(survivors, counters);
        var relations = new List<FilteredRelationRecord>(survivors.Count);
        var matrix = new List<SparseMatrixRowRecord>(survivors.Count);

        for (var i = 0; i < survivors.Count; i++)
        {
            var id = $"F{i:D8}";
            var c = survivors[i];
            var sign = c.ExponentAtColumnZero() % 2 != 0 ? -1 : 1;
            relations.Add(new FilteredRelationRecord(
                id, c.Kind, c.SourceIds, c.T, sign, c.ExponentMap(), c.Parity, c.LargePrime)
            {
                LargePrimes = c.LargePrimes,
            });
            matrix.Add(new SparseMatrixRowRecord(i, id, RemapColumns(c.Parity, columnMap)));
        }

        counters.FinalRows = survivors.Count;
        counters.MatrixColumns = columnMap.Count;
        counters.RowsRemoved = Math.Max(0, counters.RowsBeforePruning - counters.FinalRows);
        counters.ColumnsRemoved = Math.Max(0, counters.ColumnsBeforePruning - counters.MatrixColumns);
        counters.ZeroRows = matrix.Count(r => r.Columns.Count == 0);
        counters.NonZeroRows = matrix.Count - counters.ZeroRows;
        counters.NonZeroRowSurplus = counters.NonZeroRows - counters.MatrixColumns;

        var relationsDoc = new FilteredRelationsDocument(meta.TargetN, meta.Multiplier, meta.ScaledN, relations);
        var matrixMeta = new MatrixMetadata(
            meta.TargetN, meta.Multiplier, meta.ScaledN,
            RowCount: survivors.Count,
            ColumnCount: columnMap.Count,
            FactorBaseCount: factorBaseCount,
            SignColumn: columnMap.GetValueOrDefault(0, -1),
            MatrixFile: "filtered_matrix.txt",
            RelationsFile: "relations_filtered.txt");

        return new FilteringResult(relationsDoc, matrix, matrixMeta, counters);
    }

    private static Dictionary<int, int> BuildMatrixColumnMap(List<Candidate> survivors, FilteringCounters counters)
    {
        // Columns with identical row incidence are the same GF(2) constraint stated twice (they
        // arise when overlapping partial cycles share a source partial, planting its rare primes
        // in exactly the same rows). Keeping both inflates the matrix's left null space, and each
        // such isotropic direction robs Block Lanczos of one column of dependency yield, so all
        // but the lowest column of each identical group are dropped from the solver matrix. The
        // relations keep their full parity data; dropping a duplicated constraint does not change
        // the dependency solution set.
        var signatures = new Dictionary<int, (ulong H1, ulong H2, int Weight)>();
        for (var row = 0; row < survivors.Count; row++)
        {
            foreach (var column in survivors[row].Parity)
            {
                var (h1, h2, weight) = signatures.GetValueOrDefault(column, (0xcbf29ce484222325UL, 0x9E3779B97F4A7C15UL, 0));
                h1 = (h1 ^ (uint)row) * 0x100000001B3UL;
                h2 = (h2 ^ (uint)row) * 0xD1B54A32D192ED03UL;
                signatures[column] = (h1, h2, weight + 1);
            }
        }

        var keptBySignature = new Dictionary<(ulong, ulong, int), int>(signatures.Count);
        var redundant = new HashSet<int>();
        foreach (var column in signatures.Keys.OrderBy(c => c))
        {
            if (!keptBySignature.TryAdd(signatures[column], column))
            {
                redundant.Add(column);
            }
        }

        counters.RedundantColumnsMerged = redundant.Count;

        var activeColumns = signatures.Keys
            .Where(c => !redundant.Contains(c))
            .OrderBy(c => c)
            .ToArray();

        var map = new Dictionary<int, int>(activeColumns.Length);
        for (var i = 0; i < activeColumns.Length; i++)
        {
            map.Add(activeColumns[i], i);
        }

        return map;
    }

    private static int[] RemapColumns(IReadOnlyList<int> columns, IReadOnlyDictionary<int, int> columnMap)
    {
        var remapped = new List<int>(columns.Count);
        foreach (var column in columns)
        {
            if (columnMap.TryGetValue(column, out var mapped))
            {
                remapped.Add(mapped);
            }
        }

        return remapped.ToArray();
    }
}
