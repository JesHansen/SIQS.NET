namespace LinearAlgebra;

/// <summary>Small 64x64 matrix helpers over GF(2), represented as 64 row words.</summary>
public static class Gf2Matrix64
{
    private const int ApplyToBlockVectorByteTableThreshold = 64;

    public static ulong[] Identity
    {
        get
        {
            var identity = new ulong[64];
            for (var i = 0; i < identity.Length; i++)
            {
                identity[i] = 1UL << i;
            }

            return identity;
        }
    }

    public static ulong[] Multiply(ReadOnlySpan<ulong> left, ReadOnlySpan<ulong> right)
    {
        Require64(left, nameof(left));
        Require64(right, nameof(right));

        var result = new ulong[64];
        for (var i = 0; i < 64; i++)
        {
            result[i] = MultiplyRow(left[i], right);
        }

        return result;
    }

    public static ulong MultiplyRow(ulong row, ReadOnlySpan<ulong> matrix)
    {
        Require64(matrix, nameof(matrix));

        ulong accum = 0;
        while (row != 0)
        {
            var bit = System.Numerics.BitOperations.TrailingZeroCount(row);
            accum ^= matrix[bit];
            row &= row - 1;
        }

        return accum;
    }

    public static ulong[] Transpose(ReadOnlySpan<ulong> matrix)
    {
        Require64(matrix, nameof(matrix));

        var result = new ulong[64];
        for (var row = 0; row < 64; row++)
        {
            var word = matrix[row];
            while (word != 0)
            {
                var col = System.Numerics.BitOperations.TrailingZeroCount(word);
                result[col] |= 1UL << row;
                word &= word - 1;
            }
        }

        return result;
    }

    public static int FindNonsingularSubmatrix(
        ReadOnlySpan<ulong> matrix,
        IReadOnlyList<int> previousColumns,
        int previousDimension,
        Span<int> selectedColumns,
        Span<ulong> inverse)
    {
        Require64(matrix, nameof(matrix));
        if (previousDimension < 0 || previousDimension > previousColumns.Count || previousDimension > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(previousDimension));
        }

        if (selectedColumns.Length < 64)
        {
            throw new ArgumentException("Selected column output must have at least 64 entries.", nameof(selectedColumns));
        }

        if (inverse.Length < 64)
        {
            throw new ArgumentException("Inverse output must have at least 64 entries.", nameof(inverse));
        }

        Span<ulong> left = stackalloc ulong[64];
        Span<ulong> right = stackalloc ulong[64];
        Span<int> cols = stackalloc int[64];
        for (var i = 0; i < 64; i++)
        {
            left[i] = matrix[i];
            right[i] = 1UL << i;
        }

        ulong previousMask = 0;
        for (var i = 0; i < previousDimension; i++)
        {
            var col = previousColumns[i];
            if ((uint)col >= 64)
            {
                throw new ArgumentOutOfRangeException(nameof(previousColumns), $"Column {col} is out of range [0, 64).");
            }

            cols[63 - i] = col;
            previousMask |= 1UL << col;
        }

        for (int i = 0, j = 0; i < 64; i++)
        {
            if ((previousMask & (1UL << i)) == 0)
            {
                cols[j++] = i;
            }
        }

        var dimension = 0;
        for (var i = 0; i < 64; i++)
        {
            var mask = 1UL << cols[i];
            var rowIndex = cols[i];

            var pivot = -1;
            for (var j = i; j < 64; j++)
            {
                if ((left[cols[j]] & mask) != 0)
                {
                    pivot = j;
                    break;
                }
            }

            if (pivot >= 0)
            {
                SwapRows(left, rowIndex, cols[pivot]);
                SwapRows(right, rowIndex, cols[pivot]);

                for (var j = 0; j < 64; j++)
                {
                    var otherRow = cols[j];
                    if (otherRow != rowIndex && (left[otherRow] & mask) != 0)
                    {
                        left[otherRow] ^= left[rowIndex];
                        right[otherRow] ^= right[rowIndex];
                    }
                }

                selectedColumns[dimension++] = cols[i];
                continue;
            }

            pivot = -1;
            for (var j = i; j < 64; j++)
            {
                if ((right[cols[j]] & mask) != 0)
                {
                    pivot = j;
                    break;
                }
            }

            if (pivot < 0)
            {
                return 0;
            }

            SwapRows(left, rowIndex, cols[pivot]);
            SwapRows(right, rowIndex, cols[pivot]);

            for (var j = 0; j < 64; j++)
            {
                var otherRow = cols[j];
                if (otherRow != rowIndex && (right[otherRow] & mask) != 0)
                {
                    left[otherRow] ^= left[rowIndex];
                    right[otherRow] ^= right[rowIndex];
                }
            }

            left[rowIndex] = 0;
            right[rowIndex] = 0;
        }

        for (var i = 0; i < 64; i++)
        {
            inverse[i] = right[i];
        }

        return dimension;
    }

    public static void ApplyToBlockVector(ReadOnlySpan<ulong> vector, ReadOnlySpan<ulong> matrix, Span<ulong> accumulator)
    {
        Require64(matrix, nameof(matrix));
        if (accumulator.Length != vector.Length)
        {
            throw new ArgumentException("Accumulator length must match vector length.", nameof(accumulator));
        }

        if (vector.Length < ApplyToBlockVectorByteTableThreshold)
        {
            ApplyToBlockVectorByBits(vector, matrix, accumulator);
            return;
        }

        Span<ulong> table = stackalloc ulong[8 * 256];
        BuildByteTables(matrix, table);
        for (var i = 0; i < vector.Length; i++)
        {
            var word = vector[i];
            accumulator[i] ^=
                table[(0 << 8) | (byte)word] ^
                table[(1 << 8) | (byte)(word >> 8)] ^
                table[(2 << 8) | (byte)(word >> 16)] ^
                table[(3 << 8) | (byte)(word >> 24)] ^
                table[(4 << 8) | (byte)(word >> 32)] ^
                table[(5 << 8) | (byte)(word >> 40)] ^
                table[(6 << 8) | (byte)(word >> 48)] ^
                table[(7 << 8) | (byte)(word >> 56)];
        }
    }

    private static void ApplyToBlockVectorByBits(
        ReadOnlySpan<ulong> vector,
        ReadOnlySpan<ulong> matrix,
        Span<ulong> accumulator)
    {
        for (var i = 0; i < vector.Length; i++)
        {
            accumulator[i] ^= MultiplyRow(vector[i], matrix);
        }
    }

    private static void BuildByteTables(ReadOnlySpan<ulong> matrix, Span<ulong> table)
    {
        for (var chunk = 0; chunk < 8; chunk++)
        {
            var tableOffset = chunk << 8;
            var matrixOffset = chunk << 3;
            table[tableOffset] = 0;
            for (var value = 1; value < 256; value++)
            {
                var bit = System.Numerics.BitOperations.TrailingZeroCount((uint)value);
                table[tableOffset + value] = table[tableOffset + (value & (value - 1))] ^ matrix[matrixOffset + bit];
            }
        }
    }

    public static ulong[] TransposeMultiply(ReadOnlySpan<ulong> left, ReadOnlySpan<ulong> right)
    {
        if (left.Length != right.Length)
        {
            throw new ArgumentException("Input vectors must have the same length.", nameof(right));
        }

        var result = new ulong[64];
        for (var i = 0; i < left.Length; i++)
        {
            var word = left[i];
            while (word != 0)
            {
                var bit = System.Numerics.BitOperations.TrailingZeroCount(word);
                result[bit] ^= right[i];
                word &= word - 1;
            }
        }

        return result;
    }

    public static bool IsZero(ReadOnlySpan<ulong> matrix)
    {
        foreach (var row in matrix)
        {
            if (row != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static void SwapRows(Span<ulong> rows, int left, int right)
    {
        if (left == right)
        {
            return;
        }

        (rows[left], rows[right]) = (rows[right], rows[left]);
    }

    private static void Require64(ReadOnlySpan<ulong> matrix, string parameterName)
    {
        if (matrix.Length != 64)
        {
            throw new ArgumentException("Matrix must contain exactly 64 row words.", parameterName);
        }
    }
}
