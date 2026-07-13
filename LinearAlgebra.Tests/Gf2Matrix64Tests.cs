using LinearAlgebra;

namespace LinearAlgebra.Tests;

public class Gf2Matrix64Tests
{
    [Fact]
    public void Multiply_by_identity_returns_original_matrix()
    {
        var matrix = new ulong[64];
        for (var i = 0; i < matrix.Length; i++)
        {
            matrix[i] = 1UL << ((i * 17) & 63);
        }

        var product = Gf2Matrix64.Multiply(matrix, Gf2Matrix64.Identity);

        Assert.Equal(matrix, product);
    }

    [Fact]
    public void Transpose_flips_rows_and_columns()
    {
        var matrix = new ulong[64];
        matrix[3] = 1UL << 9;
        matrix[63] = 1UL << 0;

        var transposed = Gf2Matrix64.Transpose(matrix);

        Assert.Equal(1UL << 3, transposed[9]);
        Assert.Equal(1UL << 63, transposed[0]);
        Assert.Equal(matrix, Gf2Matrix64.Transpose(transposed));
    }

    [Fact]
    public void Finds_inverse_for_identity_submatrix()
    {
        var selected = new int[64];
        var inverse = new ulong[64];

        var dim = Gf2Matrix64.FindNonsingularSubmatrix(
            Gf2Matrix64.Identity, Array.Empty<int>(), 0, selected, inverse);

        Assert.Equal(64, dim);
        Assert.Equal(Enumerable.Range(0, 64).ToArray(), selected);
        Assert.Equal(Gf2Matrix64.Identity, inverse);
    }

    [Fact]
    public void Finds_rank_of_singular_matrix_and_returns_generalized_inverse_state()
    {
        var matrix = Gf2Matrix64.Identity.ToArray();
        matrix[9] = 0;
        var selected = new int[64];
        var inverse = new ulong[64];

        var dim = Gf2Matrix64.FindNonsingularSubmatrix(
            matrix, Array.Empty<int>(), 0, selected, inverse);

        Assert.Equal(63, dim);
        Assert.DoesNotContain(9, selected.Take(dim));
    }

    [Fact]
    public void ApplyToBlockVector_xors_matrix_products_into_accumulator()
    {
        var vector = new ulong[] { 0b0011, 0b0101, 0b1000 };
        var matrix = new ulong[64];
        matrix[0] = 0b1010;
        matrix[1] = 0b0101;
        matrix[2] = 0b1111;
        matrix[3] = 0b0001;
        var accumulator = new ulong[] { 0x100, 0x200, 0x400 };

        Gf2Matrix64.ApplyToBlockVector(vector, matrix, accumulator);

        Assert.Equal(0x100UL ^ Gf2Matrix64.MultiplyRow(vector[0], matrix), accumulator[0]);
        Assert.Equal(0x200UL ^ Gf2Matrix64.MultiplyRow(vector[1], matrix), accumulator[1]);
        Assert.Equal(0x400UL ^ Gf2Matrix64.MultiplyRow(vector[2], matrix), accumulator[2]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(10_000)]
    public void ApplyToBlockVector_matches_MultiplyRow_reference(int vectorLength)
    {
        var random = new Random(0x4A17 + vectorLength);
        var matrix = RandomWords(random, 64);
        var vector = RandomWords(random, vectorLength);
        var initial = RandomWords(random, vectorLength);
        var expected = initial.ToArray();
        var actual = initial.ToArray();

        for (var i = 0; i < vector.Length; i++)
        {
            expected[i] ^= Gf2Matrix64.MultiplyRow(vector[i], matrix);
        }

        Gf2Matrix64.ApplyToBlockVector(vector, matrix, actual);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(ulong.MaxValue)]
    public void ApplyToBlockVector_handles_uniform_matrices(ulong rowValue)
    {
        var matrix = Enumerable.Repeat(rowValue, 64).ToArray();
        var vector = new ulong[128];
        var initial = new ulong[vector.Length];
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (ulong)i * 0x9E3779B97F4A7C15UL;
            initial[i] = 0xA5A5A5A5A5A5A5A5UL ^ (ulong)i;
        }

        var expected = initial.ToArray();
        var actual = initial.ToArray();
        for (var i = 0; i < vector.Length; i++)
        {
            expected[i] ^= Gf2Matrix64.MultiplyRow(vector[i], matrix);
        }

        Gf2Matrix64.ApplyToBlockVector(vector, matrix, actual);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TransposeMultiply_matches_naive_outer_product()
    {
        var left = new ulong[] { 0b0011, 0b0101, 0b1000 };
        var right = new ulong[] { 0b1010, 0b0110, 0b1111 };

        var actual = Gf2Matrix64.TransposeMultiply(left, right);
        var expected = new ulong[64];
        for (var i = 0; i < left.Length; i++)
        {
            for (var row = 0; row < 64; row++)
            {
                if ((left[i] & (1UL << row)) == 0)
                {
                    continue;
                }

                for (var col = 0; col < 64; col++)
                {
                    if ((right[i] & (1UL << col)) != 0)
                    {
                        expected[row] ^= 1UL << col;
                    }
                }
            }
        }

        Assert.Equal(expected, actual);
    }

    private static ulong[] RandomWords(Random random, int count)
    {
        var values = new ulong[count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = ((ulong)random.NextInt64() << 1) ^ (ulong)random.Next(2);
        }

        return values;
    }
}
