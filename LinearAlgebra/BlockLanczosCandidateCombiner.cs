namespace LinearAlgebra;

internal static class BlockLanczosCandidateCombiner
{
    public static ulong[] Combine(
        int columnCount,
        int rowCount,
        IReadOnlyList<ulong> x,
        IReadOnlyList<ulong> v,
        IReadOnlyList<ulong> ax,
        IReadOnlyList<ulong> av,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (x.Count != columnCount || v.Count != columnCount)
        {
            throw new ArgumentException("Candidate vectors must match the Lanczos column count.");
        }

        if (ax.Count != rowCount || av.Count != rowCount)
        {
            throw new ArgumentException("Product vectors must match the Lanczos row count.");
        }

        var candidateWords = (columnCount + 63) / 64;
        var productWords = (rowCount + 63) / 64;
        var candidates = new ulong[128][];
        var products = new ulong[128][];
        for (var i = 0; i < 128; i++)
        {
            candidates[i] = new ulong[candidateWords];
            products[i] = new ulong[productWords];
        }

        TransposeBlock(columnCount, x, candidates, 0);
        TransposeBlock(columnCount, v, candidates, 64);
        TransposeBlock(rowCount, ax, products, 0);
        TransposeBlock(rowCount, av, products, 64);

        var pivotRow = 0;
        for (var bit = 0; bit < rowCount && pivotRow < 128; bit++)
        {
            if ((bit & 0xfff) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            var word = bit >> 6;
            var mask = 1UL << (bit & 63);
            var selected = -1;
            for (var row = pivotRow; row < 128; row++)
            {
                if ((products[row][word] & mask) != 0)
                {
                    selected = row;
                    break;
                }
            }

            if (selected < 0)
            {
                continue;
            }

            (products[pivotRow], products[selected]) = (products[selected], products[pivotRow]);
            (candidates[pivotRow], candidates[selected]) = (candidates[selected], candidates[pivotRow]);

            for (var row = pivotRow + 1; row < 128; row++)
            {
                if ((products[row][word] & mask) == 0)
                {
                    continue;
                }

                Xor(products[row], products[pivotRow]);
                Xor(candidates[row], candidates[pivotRow]);
            }

            pivotRow++;
        }

        var nullRows = 128 - pivotRow;
        if (nullRows <= 0)
        {
            return new ulong[columnCount];
        }

        var outputRows = Math.Min(64, nullRows);
        var output = new ulong[columnCount];
        for (var relation = 0; relation < columnCount; relation++)
        {
            if ((relation & 0xfff) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            var word = relation >> 6;
            var mask = 1UL << (relation & 63);
            ulong packed = 0;
            for (var row = 0; row < outputRows; row++)
            {
                if ((candidates[pivotRow + row][word] & mask) != 0)
                {
                    packed |= 1UL << row;
                }
            }

            output[relation] = packed;
        }

        return output;
    }

    private static void TransposeBlock(int length, IReadOnlyList<ulong> block, ulong[][] outputRows, int outputOffset)
    {
        for (var i = 0; i < length; i++)
        {
            var wordIndex = i >> 6;
            var mask = 1UL << (i & 63);
            var word = block[i];
            while (word != 0)
            {
                var bit = System.Numerics.BitOperations.TrailingZeroCount(word);
                outputRows[outputOffset + bit][wordIndex] |= mask;
                word &= word - 1;
            }
        }
    }

    private static void Xor(ulong[] target, ulong[] source)
    {
        for (var i = 0; i < target.Length; i++)
        {
            target[i] ^= source[i];
        }
    }
}
