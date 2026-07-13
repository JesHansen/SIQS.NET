namespace Filtering;

/// <summary>The active-column count and non-empty row count of a candidate set's parity matrix.</summary>
internal readonly record struct MatrixShape(int Columns, int NonZeroRows);

/// <summary>
/// Reduces a candidate set toward a solvable matrix: removes congruence duplicates, prunes singleton
/// columns, trims the heaviest surplus rows, and records the row-weight telemetry.
/// </summary>
internal static class CandidateReducer
{
    public static List<Candidate> RemoveDuplicates(List<Candidate> ordered, System.Numerics.BigInteger scaledN, FilteringCounters counters)
    {
        var seen = new HashSet<(ulong, ulong)>();
        var result = new List<Candidate>();
        foreach (var candidate in ordered)
        {
            if (seen.Add(Fingerprint.Of(candidate.DuplicateKey(scaledN))))
            {
                result.Add(candidate);
            }
            else
            {
                counters.DuplicatesRemoved++;
            }
        }

        return result;
    }

    public static List<Candidate> PruneSingletons(List<Candidate> candidates, int factorBaseCount, FilteringCounters counters)
    {
        var active = new bool[candidates.Count];
        Array.Fill(active, true);

        var columnRows = new List<int>?[factorBaseCount + 1];
        var columnCounts = new int[columnRows.Length];
        for (var r = 0; r < candidates.Count; r++)
        {
            foreach (var col in candidates[r].Parity)
            {
                if ((uint)col >= (uint)columnRows.Length)
                {
                    Array.Resize(ref columnRows, col + 1);
                    Array.Resize(ref columnCounts, col + 1);
                }

                columnRows[col] ??= [];
                columnRows[col]!.Add(r);
                columnCounts[col]++;
            }
        }

        var queue = new Queue<int>();
        for (var col = 0; col < columnCounts.Length; col++)
        {
            if (columnCounts[col] == 1)
            {
                queue.Enqueue(col);
            }
        }

        while (queue.Count > 0)
        {
            var col = queue.Dequeue();
            if ((uint)col >= (uint)columnCounts.Length || columnCounts[col] != 1)
            {
                continue;
            }

            var row = FindActiveRow(columnRows[col], active);
            if (row < 0)
            {
                columnCounts[col] = 0;
                continue;
            }

            if (!active[row])
            {
                continue;
            }

            active[row] = false;
            counters.SingletonPruned++;

            foreach (var c in candidates[row].Parity)
            {
                if ((uint)c < (uint)columnCounts.Length && columnCounts[c] > 0)
                {
                    columnCounts[c]--;
                    if (columnCounts[c] == 1)
                    {
                        queue.Enqueue(c);
                    }
                }
            }
        }

        var survivors = new List<Candidate>();
        for (var r = 0; r < candidates.Count; r++)
        {
            if (active[r])
            {
                survivors.Add(candidates[r]);
            }
        }

        return survivors;
    }

    public static List<Candidate> TrimHeavyRows(
        List<Candidate> candidates,
        int factorBaseCount,
        FilteringOptions options,
        FilteringCounters counters)
    {
        if (candidates.Count == 0)
        {
            return candidates;
        }

        var stats = MatrixStats(candidates, factorBaseCount);
        var targetSurplus = AutomaticTargetNonZeroSurplus(stats.Columns);
        counters.TargetNonZeroSurplus = targetSurplus;
        var targetNonZeroRows = stats.Columns + targetSurplus;
        targetNonZeroRows = Math.Min(stats.NonZeroRows, targetNonZeroRows);

        var remove = new bool[candidates.Count];
        var removed = 0;
        var currentNonZeroRows = stats.NonZeroRows;
        foreach (var row in candidates
            .Select((candidate, index) => (candidate, index))
            .Where(x => x.candidate.Parity.Count > 0)
            .OrderByDescending(x => x.candidate.Parity.Count)
            .ThenByDescending(x => x.candidate.CycleLength)
            .ThenByDescending(x => x.candidate.Kind == SIQS.Contracts.RelationKind.CombinedPartial ? 1 : 0)
            .ThenBy(x => x.candidate.OrderKey, StringComparer.Ordinal))
        {
            var overSurplus = currentNonZeroRows > targetNonZeroRows;
            if (!overSurplus)
            {
                break;
            }

            remove[row.index] = true;
            removed++;
            currentNonZeroRows--;
        }

        if (removed == 0)
        {
            return candidates;
        }

        counters.SurplusRowsTrimmed += removed;
        var survivors = new List<Candidate>(candidates.Count - removed);
        for (var i = 0; i < candidates.Count; i++)
        {
            if (!remove[i])
            {
                survivors.Add(candidates[i]);
            }
        }

        return survivors;
    }

    private static int AutomaticTargetNonZeroSurplus(int columns)
    {
        if (columns <= 0)
        {
            return 0;
        }

        return Math.Clamp((int)Math.Ceiling(columns * 0.008), 16, 4096);
    }

    public static void RecordPrePruningTelemetry(List<Candidate> candidates, int factorBaseCount, FilteringCounters counters)
    {
        var stats = MatrixStats(candidates, factorBaseCount);
        counters.RowsBeforePruning = candidates.Count;
        counters.ColumnsBeforePruning = stats.Columns;
    }

    private static MatrixShape MatrixStats(List<Candidate> candidates, int factorBaseCount)
    {
        var activeColumns = new bool[factorBaseCount + 1];
        var columns = 0;
        var nonZeroRows = 0;

        foreach (var candidate in candidates)
        {
            if (candidate.Parity.Count > 0)
            {
                nonZeroRows++;
            }

            foreach (var col in candidate.Parity)
            {
                if (!activeColumns[col])
                {
                    activeColumns[col] = true;
                    columns++;
                }
            }
        }

        return new MatrixShape(columns, nonZeroRows);
    }

    public static void RecordRowWeightTelemetry(List<Candidate> candidates, bool beforeTrim, FilteringCounters counters)
    {
        var weights = candidates
            .Select(candidate => candidate.Parity.Count)
            .Order()
            .ToArray();
        var max = weights.Length == 0 ? 0 : weights[^1];
        var total = weights.Sum(weight => (long)weight);
        var average = weights.Length == 0 ? 0.0 : total / (double)weights.Length;
        var p50 = PercentileNearestRank(weights, 0.50);
        var p90 = PercentileNearestRank(weights, 0.90);
        var p99 = PercentileNearestRank(weights, 0.99);

        if (beforeTrim)
        {
            counters.MaxRowWeightBeforeTrim = max;
            counters.TotalRowWeightBeforeTrim = total;
            counters.AverageRowWeightBeforeTrim = average;
            counters.P50RowWeightBeforeTrim = p50;
            counters.P90RowWeightBeforeTrim = p90;
            counters.P99RowWeightBeforeTrim = p99;
        }
        else
        {
            counters.MaxRowWeightAfterTrim = max;
            counters.TotalRowWeightAfterTrim = total;
            counters.AverageRowWeightAfterTrim = average;
            counters.P50RowWeightAfterTrim = p50;
            counters.P90RowWeightAfterTrim = p90;
            counters.P99RowWeightAfterTrim = p99;
        }
    }

    private static int PercentileNearestRank(IReadOnlyList<int> sortedWeights, double percentile)
    {
        if (sortedWeights.Count == 0)
        {
            return 0;
        }

        var rank = (int)Math.Ceiling(percentile * sortedWeights.Count);
        return sortedWeights[Math.Clamp(rank - 1, 0, sortedWeights.Count - 1)];
    }

    private static int FindActiveRow(List<int>? rows, bool[] active)
    {
        if (rows is null)
        {
            return -1;
        }

        foreach (var row in rows)
        {
            if (active[row])
            {
                return row;
            }
        }

        return -1;
    }
}
