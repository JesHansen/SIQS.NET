using SIQS.Contracts;
using SIQS.Contracts.Files;

namespace Filtering;

/// <summary>Running counts reported by filtering.</summary>
public sealed class FilteringCounters
{
    public int RawFull { get; set; }
    public int RawPartials { get; set; }
    public int CombinedPartials { get; set; }
    public int RejectedCycles { get; set; }
    public int SurplusRowsTrimmed { get; set; }
    public int DuplicatesRemoved { get; set; }
    public int RedundantColumnsMerged { get; set; }
    public int SingletonPruned { get; set; }
    public int RowsBeforePruning { get; set; }
    public int ColumnsBeforePruning { get; set; }
    public int RowsRemoved { get; set; }
    public int ColumnsRemoved { get; set; }
    public int FinalRows { get; set; }
    public int MatrixColumns { get; set; }
    public int ZeroRows { get; set; }
    public int NonZeroRows { get; set; }
    public int NonZeroRowSurplus { get; set; }
    public int TargetNonZeroSurplus { get; set; }
    public int MaxCycleLength { get; set; }
    public long TotalCycleLength { get; set; }
    public int MaxRowWeightBeforeTrim { get; set; }
    public int MaxRowWeightAfterTrim { get; set; }
    public long TotalRowWeightBeforeTrim { get; set; }
    public long TotalRowWeightAfterTrim { get; set; }
    public double AverageRowWeightBeforeTrim { get; set; }
    public double AverageRowWeightAfterTrim { get; set; }
    public int P50RowWeightBeforeTrim { get; set; }
    public int P50RowWeightAfterTrim { get; set; }
    public int P90RowWeightBeforeTrim { get; set; }
    public int P90RowWeightAfterTrim { get; set; }
    public int P99RowWeightBeforeTrim { get; set; }
    public int P99RowWeightAfterTrim { get; set; }
}

/// <summary>The full output of filtering: the three artifact payloads plus counters.</summary>
public sealed record FilteringResult(
    FilteredRelationsDocument Relations,
    IReadOnlyList<SparseMatrixRowRecord> Matrix,
    MatrixMetadata Meta,
    FilteringCounters Counters);
