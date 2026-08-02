using System.Numerics;
using SIQS.Contracts;

namespace Filtering;

/// <summary>The active-column count and non-empty row count of a candidate set's parity matrix.</summary>
internal readonly record struct MatrixShape(int Columns, int NonZeroRows);

/// <summary>
/// Reduces a candidate set toward a solvable matrix: removes congruence duplicates, prunes singleton
/// columns, trims the heaviest surplus rows, and records the row-weight telemetry.
/// </summary>
internal static class CandidateReducer
{
    private readonly record struct TwoMergeProposal(
        int LeftRow,
        int RightRow,
        int MergedWeight,
        int Savings,
        int Column);

    public static List<Candidate> RemoveDuplicates(List<Candidate> ordered, FilteringCounters counters)
    {
        var seen = new HashSet<(ulong, ulong)>();
        var result = new List<Candidate>();
        foreach (var candidate in ordered)
        {
            if (seen.Add(candidate.DuplicateFingerprint))
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

        // CSR layout of the column→rows incidence: a counting pass, a prefix sum, and a fill pass
        // into one flat array. Columns are already validated to lie in [0, factorBaseCount].
        var columnCounts = new int[factorBaseCount + 1];
        for (var r = 0; r < candidates.Count; r++)
        {
            foreach (var col in candidates[r].Parity)
            {
                columnCounts[col]++;
            }
        }

        var offsets = new int[columnCounts.Length + 1];
        for (var col = 0; col < columnCounts.Length; col++)
        {
            offsets[col + 1] = offsets[col] + columnCounts[col];
        }

        var rowsByColumn = new int[offsets[^1]];
        var cursors = new int[columnCounts.Length];
        Array.Copy(offsets, cursors, cursors.Length);
        for (var r = 0; r < candidates.Count; r++)
        {
            foreach (var col in candidates[r].Parity)
            {
                rowsByColumn[cursors[col]++] = r;
            }
        }

        // Reset to per-column scan cursors: rows never reactivate, so each column's list is scanned
        // at most once in total across all pops.
        Array.Copy(offsets, cursors, cursors.Length);

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
            if (columnCounts[col] != 1)
            {
                continue;
            }

            var row = FindActiveRow(rowsByColumn, ref cursors[col], offsets[col + 1], active);
            if (row < 0)
            {
                columnCounts[col] = 0;
                continue;
            }

            active[row] = false;
            counters.SingletonPruned++;

            foreach (var c in candidates[row].Parity)
            {
                if (columnCounts[c] > 0)
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

    /// <summary>
    /// Performs structured Gaussian elimination on weight-2 columns. The two incident rows are
    /// replaced by their XOR, optionally subject to a fill budget relative to the lighter row.
    /// Arithmetic payloads are combined lazily when the final artifacts are built, preserving
    /// square-root provenance without forcing spilled payloads back into memory during reduction.
    /// </summary>
    public static List<Candidate> MergeWeightTwoColumns(
        List<Candidate> candidates,
        int factorBaseCount,
        BigInteger scaledN,
        int? slack,
        FilteringCounters counters)
    {
        if (slack is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(slack), "Two-merge slack cannot be negative.");
        }

        while (candidates.Count > 1)
        {
            var counts = new int[factorBaseCount + 1];
            var firstRows = new int[counts.Length];
            var secondRows = new int[counts.Length];
            Array.Fill(firstRows, -1);
            Array.Fill(secondRows, -1);

            for (var row = 0; row < candidates.Count; row++)
            {
                foreach (var column in candidates[row].Parity)
                {
                    if (counts[column] == 0)
                    {
                        firstRows[column] = row;
                    }
                    else if (counts[column] == 1)
                    {
                        secondRows[column] = row;
                    }

                    counts[column]++;
                }
            }

            var seenPairs = new HashSet<long>();
            var proposals = new List<TwoMergeProposal>();
            for (var column = 0; column < counts.Length; column++)
            {
                if (counts[column] != 2)
                {
                    continue;
                }

                var leftRow = Math.Min(firstRows[column], secondRows[column]);
                var rightRow = Math.Max(firstRows[column], secondRows[column]);
                var pair = ((long)leftRow << 32) | (uint)rightRow;
                if (!seenPairs.Add(pair))
                {
                    continue;
                }

                var leftWeight = candidates[leftRow].Parity.Count;
                var rightWeight = candidates[rightRow].Parity.Count;
                var mergedWeight = SymmetricDifferenceCount(
                    candidates[leftRow].Parity.Span,
                    candidates[rightRow].Parity.Span);
                var maximumAcceptedWeight = slack is { } maximumIncrease
                    ? checked(Math.Min(leftWeight, rightWeight) + maximumIncrease)
                    : int.MaxValue;
                if (mergedWeight <= maximumAcceptedWeight)
                {
                    proposals.Add(new TwoMergeProposal(
                        leftRow,
                        rightRow,
                        mergedWeight,
                        leftWeight + rightWeight - mergedWeight,
                        column));
                }
            }

            proposals.Sort(static (left, right) =>
            {
                var bySavings = right.Savings.CompareTo(left.Savings);
                if (bySavings != 0)
                {
                    return bySavings;
                }

                var byWeight = left.MergedWeight.CompareTo(right.MergedWeight);
                return byWeight != 0 ? byWeight : left.Column.CompareTo(right.Column);
            });

            var claimed = new bool[candidates.Count];
            var replacements = new Candidate?[candidates.Count];
            var mergedThisPass = 0;
            foreach (var proposal in proposals)
            {
                if (claimed[proposal.LeftRow] || claimed[proposal.RightRow])
                {
                    continue;
                }

                claimed[proposal.LeftRow] = true;
                claimed[proposal.RightRow] = true;
                var parity = SymmetricDifference(
                    candidates[proposal.LeftRow].Parity.Span,
                    candidates[proposal.RightRow].Parity.Span);
                replacements[proposal.LeftRow] = Candidate.Merged(
                    candidates[proposal.LeftRow],
                    candidates[proposal.RightRow],
                    parity,
                    scaledN);
                mergedThisPass++;
            }

            if (mergedThisPass == 0)
            {
                return candidates;
            }

            counters.TwoMerges += mergedThisPass;
            var next = new List<Candidate>(candidates.Count - mergedThisPass);
            for (var row = 0; row < candidates.Count; row++)
            {
                if (replacements[row] is { } replacement)
                {
                    next.Add(replacement);
                }
                else if (!claimed[row])
                {
                    next.Add(candidates[row]);
                }
            }

            candidates = next;
        }

        return candidates;
    }

    private static int SymmetricDifferenceCount(ReadOnlySpan<int> left, ReadOnlySpan<int> right)
    {
        var common = 0;
        var leftIndex = 0;
        var rightIndex = 0;
        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            if (left[leftIndex] < right[rightIndex])
            {
                leftIndex++;
            }
            else if (right[rightIndex] < left[leftIndex])
            {
                rightIndex++;
            }
            else
            {
                common++;
                leftIndex++;
                rightIndex++;
            }
        }

        return left.Length + right.Length - 2 * common;
    }

    private static ParityColumnSet SymmetricDifference(ReadOnlySpan<int> left, ReadOnlySpan<int> right)
    {
        var columns = new int[left.Length + right.Length];
        var leftIndex = 0;
        var rightIndex = 0;
        var count = 0;
        while (leftIndex < left.Length || rightIndex < right.Length)
        {
            if (rightIndex >= right.Length || (leftIndex < left.Length && left[leftIndex] < right[rightIndex]))
            {
                columns[count++] = left[leftIndex++];
            }
            else if (leftIndex >= left.Length || right[rightIndex] < left[leftIndex])
            {
                columns[count++] = right[rightIndex++];
            }
            else
            {
                leftIndex++;
                rightIndex++;
            }
        }

        return ParityColumnSet.FromOwned(columns[..count]);
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

    private static int FindActiveRow(int[] rowsByColumn, ref int cursor, int end, bool[] active)
    {
        while (cursor < end)
        {
            var row = rowsByColumn[cursor];
            if (active[row])
            {
                return row;
            }

            cursor++;
        }

        return -1;
    }
}
