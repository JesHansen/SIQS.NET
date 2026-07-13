using System.Numerics;
using System.Security.Cryptography;

namespace CompositeGenerator;

/// <summary>Uniform sampling for native and arbitrary-precision inclusive ranges.</summary>
internal sealed class SecureBigIntegerSampler
{
    public int NextInt32(int minimum, int exclusiveMaximum)
        => RandomNumberGenerator.GetInt32(minimum, exclusiveMaximum);

    public BigInteger Next(BigIntegerRange range)
        => range.Minimum + NextBelow(range.Maximum - range.Minimum + BigInteger.One);

    public BigInteger NextBelow(BigInteger exclusiveUpperBound)
    {
        if (exclusiveUpperBound <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveUpperBound));
        }

        var bytes = exclusiveUpperBound.ToByteArray(isUnsigned: true, isBigEndian: true);
        var excessBits = bytes.Length * 8 - (int)(exclusiveUpperBound - BigInteger.One).GetBitLength();
        while (true)
        {
            RandomNumberGenerator.Fill(bytes);
            if (excessBits > 0)
            {
                bytes[0] &= (byte)(0xFF >> excessBits);
            }

            var candidate = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
            if (candidate < exclusiveUpperBound)
            {
                return candidate;
            }
        }
    }
}
