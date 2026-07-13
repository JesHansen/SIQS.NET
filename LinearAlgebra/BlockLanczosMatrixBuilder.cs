namespace LinearAlgebra;

/// <summary>
/// Builds the immutable <see cref="BlockLanczosMatrixStorage"/> from filtered relation rows: selects
/// the dense post-Lanczos parity columns, validates each row's columns, maps the remaining columns to
/// sparse indices, and constructs the CSR row offsets / column indices and per-row dense masks. This
/// is the construction/validation half of the matrix; the runtime multiply kernels live in
/// <see cref="BlockLanczosSparseMatrix"/>.
/// </summary>
internal static class BlockLanczosMatrixBuilder
{
    public static BlockLanczosMatrixStorage Build(
        IReadOnlyList<RelationRow> rows,
        int columnCount,
        BlockLanczosOptions options)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(options);
        if (columnCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(columnCount), columnCount, "Column count must be non-negative.");
        }

        var rowCount = rows.Count;
        var denseParityColumns = SelectDenseParityColumns(rows, columnCount, options);
        var denseIndexByOriginalColumn = new Dictionary<int, int>(denseParityColumns.Length);
        for (var i = 0; i < denseParityColumns.Length; i++)
        {
            denseIndexByOriginalColumn.Add(denseParityColumns[i], i);
        }

        var sparseIndexByOriginalColumn = new int[columnCount];
        Array.Fill(sparseIndexByOriginalColumn, -1);
        var sparseColumnCount = 0;
        for (var col = 0; col < columnCount; col++)
        {
            if (!denseIndexByOriginalColumn.ContainsKey(col))
            {
                sparseIndexByOriginalColumn[col] = sparseColumnCount++;
            }
        }

        var rowOffsets = new int[rowCount + 1];
        var denseRowMasks = new ulong[rowCount];
        for (var r = 0; r < rowCount; r++)
        {
            var sparseCount = 0;
            var previous = -1;
            foreach (var col in rows[r].Columns)
            {
                ValidateColumn(rows, columnCount, col, previous);
                previous = col;
                if (denseIndexByOriginalColumn.TryGetValue(col, out var denseIndex))
                {
                    denseRowMasks[r] |= 1UL << denseIndex;
                }
                else
                {
                    sparseCount++;
                }
            }

            rowOffsets[r + 1] = checked(rowOffsets[r] + sparseCount);
        }

        var columnIndices = new int[rowOffsets[rowCount]];
        for (var r = 0; r < rowCount; r++)
        {
            var next = rowOffsets[r];
            var previous = -1;
            foreach (var col in rows[r].Columns)
            {
                ValidateColumn(rows, columnCount, col, previous);
                previous = col;
                var sparse = sparseIndexByOriginalColumn[col];
                if (sparse >= 0)
                {
                    columnIndices[next++] = sparse;
                }
            }
        }

        return new BlockLanczosMatrixStorage(
            rowCount,
            columnCount,
            sparseColumnCount,
            columnIndices,
            rowOffsets,
            denseParityColumns,
            denseRowMasks);
    }

    private static int[] SelectDenseParityColumns(
        IReadOnlyList<RelationRow> rows,
        int columnCount,
        BlockLanczosOptions options)
    {
        if (options.PostLanczosRows == 0 ||
            Math.Min(rows.Count, columnCount) < options.MinPostLanczosDimension ||
            columnCount == 0)
        {
            return Array.Empty<int>();
        }

        var weights = new int[columnCount];
        foreach (var row in rows)
        {
            var previous = -1;
            foreach (var col in row.Columns)
            {
                ValidateColumn(rows, columnCount, col, previous);
                previous = col;
                weights[col]++;
            }
        }

        return Enumerable.Range(0, columnCount)
            .OrderByDescending(col => weights[col])
            .ThenBy(col => col)
            .Take(Math.Min(options.PostLanczosRows, columnCount))
            .OrderBy(col => col)
            .ToArray();
    }

    private static void ValidateColumn(
        IReadOnlyList<RelationRow> rows,
        int columnCount,
        int col,
        int previous)
    {
        if (col < 0 || col >= columnCount)
        {
            throw new ArgumentOutOfRangeException(nameof(rows), $"Column {col} is out of range [0, {columnCount}).");
        }

        if (col <= previous)
        {
            throw new ArgumentException("Columns in each relation row must be strictly ascending.", nameof(rows));
        }
    }
}
