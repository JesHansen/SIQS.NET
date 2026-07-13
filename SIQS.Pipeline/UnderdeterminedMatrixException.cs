namespace SIQS.Pipeline;

/// <summary>Raised when the filtered matrix has too few nonzero rows to run linear algebra.</summary>
public sealed class UnderdeterminedMatrixException : InvalidOperationException
{
    public UnderdeterminedMatrixException(int nonZeroRows, int columnCount)
        : base($"Matrix has only {nonZeroRows} nonzero rows for {columnCount} columns; sieve more relations.")
    {
        NonZeroRows = nonZeroRows;
        ColumnCount = columnCount;
        Deficit = Math.Max(0, columnCount - nonZeroRows);
    }

    public int NonZeroRows { get; }
    public int ColumnCount { get; }
    public int Deficit { get; }
}
